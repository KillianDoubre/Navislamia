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
    public const int IncreaseExtensionAttribute = 10009;
    public const int AmplifyBaseAttribute = 10011;
    public const int HuntingTrainingEffect = 10013;
    public const int IncreaseSummonHpMpSp = 10031;
    public const int AmplifySummonHpMpSp = 10032;

    /// <summary>
    /// <c>StructPlayer::applyPassiveSkillEffect</c>, <c>CalculateStat.cpp:1492-1500, 1555-1570</c>: six
    /// <c>(base, per level)</c> pairs. The SP regeneration (pair 6) has no stat here.
    /// </summary>
    private static readonly StatTarget[] SummonTargets =
        { StatTarget.MaxHp, StatTarget.MaxMp, StatTarget.MaxSp, StatTarget.HpRegenPoint, StatTarget.MpRegenPoint };

    public static readonly int[] SupportedEffectTypes = { WeaponMastery, IncreaseBaseAttribute, IncreaseHpMp,
        IncreaseElementalResistance, IncreaseExtensionAttribute, AmplifyBaseAttribute, HuntingTrainingEffect,
        IncreaseSummonHpMpSp, AmplifySummonHpMpSp };

    /// <summary>
    /// <c>EF_INCREASE_EXTENSION_ATTRIBUTE</c>, <c>CalculateStat.cpp:749-765</c>: <c>var[i] × level</c> onto each target;
    /// the cool time speed is subtracted ("smaller is faster").
    /// </summary>
    private static readonly StatTarget[] ExtensionTargets =
    {
        StatTarget.HpRegenPercentage, StatTarget.HpRegenPoint, StatTarget.MpRegenPercentage, StatTarget.MpRegenPoint,
        StatTarget.BlockChance, StatTarget.BlockDefence, StatTarget.Critical, StatTarget.CriticalPower,
        StatTarget.CastingSpeed, StatTarget.CoolTimeSpeed
    };

    /// <summary>
    /// <c>EF_INCREASE_BASE_ATTRIBUTE</c> (<c>CalculateStat.cpp:681-722</c>, flat) and <c>EF_AMPLIFY_BASE_ATTRIBUTE</c>
    /// (<c>:724-747</c>, <c>m_AttributeAmplifier</c>): <c>var[i] × level</c> onto the same ten stats.
    /// </summary>
    private static readonly StatTarget[] BaseAttributeTargets =
    {
        StatTarget.AttackPointRight, StatTarget.Defence, StatTarget.MagicPoint, StatTarget.MagicDefence,
        StatTarget.AttackSpeed, StatTarget.MoveSpeed, StatTarget.AccuracyRight, StatTarget.MagicAccuracy,
        StatTarget.Avoid, StatTarget.MagicAvoid
    };

    private static readonly FrozenDictionary<int, StatTarget[]> SlotTargets =
        new Dictionary<int, StatTarget[]>
        {
            [WeaponMastery] = new[] { StatTarget.AttackPointRight, StatTarget.AttackSpeed },
            [IncreaseHpMp] = new[] { StatTarget.MaxHp, StatTarget.MaxMp }
        }.ToFrozenDictionary();

    private readonly ILogger _logger = Log.ForContext<SkillPassiveCatalog>();
    private readonly FrozenDictionary<int, PassiveEntry> _passives;
    private readonly FrozenDictionary<int, StateEffectTemplate> _summonSp;
    private readonly FrozenDictionary<int, decimal> _firstVars;
    private readonly FrozenDictionary<int, StateEffectTemplate[]> _forSummon;
    private readonly FrozenDictionary<int, decimal[]> _hunting;

    public SkillPassiveCatalog(ISkillResourceRepository repository)
    {
        var passives = new Dictionary<int, PassiveEntry>();
        var summonSp = new Dictionary<int, StateEffectTemplate>();
        var forSummon = new Dictionary<int, StateEffectTemplate[]>();
        var hunting = new Dictionary<int, decimal[]>();
        foreach (var passive in repository.GetStatPassives())
        {
            if (passive.EffectType is 10031 or 10032 && passive.Vars is { Length: > 5 })
                summonSp[passive.SkillId] = new StateEffectTemplate(StatTarget.MaxSp,
                    (float)passive.Vars[4], (float)passive.Vars[5], passive.EffectType == 10032);
            if (passive.EffectType is IncreaseSummonHpMpSp or AmplifySummonHpMpSp && SummonTemplates(passive) is { Length: > 0 } summon)
                forSummon[passive.SkillId] = summon;
            if (passive.EffectType == HuntingTrainingEffect && passive.Vars is not null)
                hunting[passive.SkillId] = passive.Vars;
            var templates = BuildTemplates(passive);
            if (templates.Count > 0)
            {
                passives[passive.SkillId] = new PassiveEntry(templates.ToArray(), passive.Weapons,
                    passive.WeaponNotRequired);
            }
        }

        _passives = passives.ToFrozenDictionary();
        _summonSp = summonSp.ToFrozenDictionary();
        _forSummon = forSummon.ToFrozenDictionary();
        _hunting = hunting.ToFrozenDictionary();
        _firstVars = (repository.GetSkillRows(new[] { Creatures.DoubleSummonRules.TechnicalCreatureControlSkill })
                      ?? Array.Empty<DataAccess.Repositories.Interfaces.CastableSkillRow>())
            .Where(row => row.Vars is { Length: > 0 })
            .ToFrozenDictionary(row => row.SkillId, row => row.Vars[0]);
        _logger.Debug("Loaded {count} stat passive skills", _passives.Count);
    }

    public decimal FirstVar(int skillId) => _firstVars.GetValueOrDefault(skillId);

    public IReadOnlyList<StatEffect> ResolveForSummon(int skillId, int skillLevel) =>
        skillLevel > 0 && _forSummon.TryGetValue(skillId, out var templates)
            ? templates.Select(t => t.Resolve(skillLevel)).ToArray() : Array.Empty<StatEffect>();

    public decimal[] HuntingTraining(int skillId) => _hunting.GetValueOrDefault(skillId);

    /// <summary>The pairs of 10031 (flat) or 10032 (amplifier) onto max HP, MP, SP and the HP/MP regeneration.</summary>
    public static StateEffectTemplate[] SummonTemplates(SkillPassiveFields passive)
    {
        if (passive.Vars is null) return Array.Empty<StateEffectTemplate>();
        var amplify = passive.EffectType == AmplifySummonHpMpSp;
        var templates = new List<StateEffectTemplate>();
        for (var slot = 0; slot < SummonTargets.Length && slot * 2 + 1 < passive.Vars.Length; slot++)
        {
            var amountBase = (float)passive.Vars[slot * 2];
            var perLevel = (float)passive.Vars[slot * 2 + 1];
            if (amountBase == 0f && perLevel == 0f) continue;
            templates.Add(new StateEffectTemplate(SummonTargets[slot], amountBase, perLevel, amplify));
        }

        return templates.ToArray();
    }

    public IReadOnlyList<StatEffect> ResolveSummonSp(int skillId, int skillLevel) =>
        skillLevel > 0 && _summonSp.TryGetValue(skillId, out var template)
            ? new[] { template.Resolve(skillLevel) } : Array.Empty<StatEffect>();

    public IReadOnlyList<StatEffect> Resolve(int skillId, int skillLevel, ItemType? equippedWeapon) =>
        Resolve(skillId, skillLevel, equippedWeapon, wearsShield: false);

    public IReadOnlyList<StatEffect> Resolve(int skillId, int skillLevel, ItemType? equippedWeapon, bool wearsShield)
    {
        if (skillLevel <= 0 || !_passives.TryGetValue(skillId, out var entry))
        {
            return Array.Empty<StatEffect>();
        }

        if (!SkillWeaponGate.Allows(entry.Weapons, entry.WeaponNotRequired, equippedWeapon, wearsShield))
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
        if (passive.EffectType is IncreaseExtensionAttribute or AmplifyBaseAttribute or IncreaseBaseAttribute
            && passive.Vars is not null)
        {
            // Ten single per-level values, not (base, per level) pairs: var[i] × level.
            var amplify = passive.EffectType == AmplifyBaseAttribute;
            var slots = passive.EffectType == IncreaseExtensionAttribute ? ExtensionTargets : BaseAttributeTargets;
            var perLevel = new List<StateEffectTemplate>();
            for (var i = 0; i < slots.Length && i < passive.Vars.Length; i++)
            {
                var value = (float)passive.Vars[i];
                if (value == 0f) continue;
                if (!amplify && slots[i] == StatTarget.CoolTimeSpeed) value = -value;
                perLevel.Add(new StateEffectTemplate(slots[i], 0f, value, amplify));
            }
            return perLevel;
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
