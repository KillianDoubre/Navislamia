using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;

namespace Navislamia.Game.Services.Stats;

public class SkillPassiveCatalog : ISkillPassiveCatalog
{
    public const int WeaponMastery = 10001;
    public const int IncreaseBaseAttribute = 10008;
    public const int IncreaseElementalResistance = 10006;
    public const int IncreaseHpMp = 10021;

    public static readonly int[] SupportedEffectTypes = { WeaponMastery, IncreaseBaseAttribute, IncreaseHpMp, IncreaseElementalResistance, 10031, 10032 };

    private static readonly FrozenDictionary<int, StatTarget[]> SlotTargets =
        new Dictionary<int, StatTarget[]>
        {
            [WeaponMastery] = new[] { StatTarget.AttackPointRight, StatTarget.AttackSpeed },
            [IncreaseBaseAttribute] = new[] { StatTarget.Defence, StatTarget.MagicDefence },
            [IncreaseHpMp] = new[] { StatTarget.MaxHp, StatTarget.MaxMp }
        }.ToFrozenDictionary();

    private readonly ILogger _logger = Log.ForContext<SkillPassiveCatalog>();
    private readonly FrozenDictionary<int, PassiveEntry> _passives;
    private readonly FrozenDictionary<int, StateEffectTemplate> _summonSp;

    public SkillPassiveCatalog(ISkillResourceRepository repository)
    {
        var passives = new Dictionary<int, PassiveEntry>();
        var summonSp = new Dictionary<int, StateEffectTemplate>();
        foreach (var passive in repository.GetStatPassives())
        {
            if (passive.EffectType is 10031 or 10032 && passive.Vars is { Length: > 5 })
                summonSp[passive.SkillId] = new StateEffectTemplate(StatTarget.MaxSp,
                    (float)passive.Vars[4], (float)passive.Vars[5], passive.EffectType == 10032);
            var templates = BuildTemplates(passive);
            if (templates.Count > 0)
            {
                passives[passive.SkillId] = new PassiveEntry(templates.ToArray(), passive.Weapons,
                    passive.WeaponNotRequired);
            }
        }

        _passives = passives.ToFrozenDictionary();
        _summonSp = summonSp.ToFrozenDictionary();
        _logger.Debug("Loaded {count} stat passive skills", _passives.Count);
    }

    public IReadOnlyList<StatEffect> ResolveSummonSp(int skillId, int skillLevel) =>
        skillLevel > 0 && _summonSp.TryGetValue(skillId, out var template)
            ? new[] { template.Resolve(skillLevel) } : Array.Empty<StatEffect>();

    public IReadOnlyList<StatEffect> Resolve(int skillId, int skillLevel, ItemType? equippedWeapon)
    {
        if (skillLevel <= 0 || !_passives.TryGetValue(skillId, out var entry))
        {
            return Array.Empty<StatEffect>();
        }

        if (!SkillWeaponGate.Allows(entry.Weapons, entry.WeaponNotRequired, equippedWeapon))
        {
            return Array.Empty<StatEffect>();
        }

        var effects = new StatEffect[entry.Templates.Length];
        for (var i = 0; i < entry.Templates.Length; i++)
        {
            effects[i] = entry.Templates[i].Resolve(skillLevel);
        }

        return effects;
    }

    public static IReadOnlyList<StateEffectTemplate> BuildTemplates(SkillPassiveFields passive)
    {
        if (passive.EffectType == IncreaseElementalResistance && passive.Vars is not null)
        {
            var resistances = new List<StateEffectTemplate>();
            for (var i = 0; i < 12 && i + 2 < passive.Vars.Length; i += 3)
            {
                var element = (int)passive.Vars[i];
                if (element is < 0 or > 6) continue;
                resistances.Add(new StateEffectTemplate((StatTarget)((int)StatTarget.NoneResistance + element),
                    (float)passive.Vars[i + 1], (float)passive.Vars[i + 2], false));
            }
            return resistances;
        }
        if (passive.Vars is null || !SlotTargets.TryGetValue(passive.EffectType, out var targets))
        {
            return Array.Empty<StateEffectTemplate>();
        }

        List<StateEffectTemplate> templates = null;
        for (var slot = 0; slot < targets.Length; slot++)
        {
            var baseIndex = slot * 2;
            if (baseIndex + 1 >= passive.Vars.Length)
            {
                break;
            }

            var amountBase = (float)passive.Vars[baseIndex];
            var perLevel = (float)passive.Vars[baseIndex + 1];
            if (amountBase == 0f && perLevel == 0f)
            {
                continue;
            }

            templates ??= new List<StateEffectTemplate>();
            templates.Add(new StateEffectTemplate(targets[slot], amountBase, perLevel, false));
        }

        return (IReadOnlyList<StateEffectTemplate>)templates ?? Array.Empty<StateEffectTemplate>();
    }

    private readonly record struct PassiveEntry(
        StateEffectTemplate[] Templates,
        SkillWeaponFlag Weapons,
        bool WeaponNotRequired);
}
