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
    public const int IncreaseHpMp = 10021;

    public static readonly int[] SupportedEffectTypes = { WeaponMastery, IncreaseBaseAttribute, IncreaseHpMp };

    private static readonly FrozenDictionary<int, StatTarget[]> SlotTargets =
        new Dictionary<int, StatTarget[]>
        {
            [WeaponMastery] = new[] { StatTarget.AttackPointRight, StatTarget.AttackSpeed },
            [IncreaseBaseAttribute] = new[] { StatTarget.Defence, StatTarget.MagicDefence },
            [IncreaseHpMp] = new[] { StatTarget.MaxHp, StatTarget.MaxMp }
        }.ToFrozenDictionary();

    private readonly ILogger _logger = Log.ForContext<SkillPassiveCatalog>();
    private readonly FrozenDictionary<int, PassiveEntry> _passives;

    public SkillPassiveCatalog(ISkillResourceRepository repository)
    {
        var passives = new Dictionary<int, PassiveEntry>();
        foreach (var passive in repository.GetStatPassives())
        {
            var templates = BuildTemplates(passive);
            if (templates.Count > 0)
            {
                passives[passive.SkillId] = new PassiveEntry(templates.ToArray(), passive.Weapons,
                    passive.WeaponNotRequired);
            }
        }

        _passives = passives.ToFrozenDictionary();
        _logger.Debug("Loaded {count} stat passive skills", _passives.Count);
    }

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
