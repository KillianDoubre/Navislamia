using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;

namespace Navislamia.Game.Services.Buffs;

/// <summary>
/// The castable skills, frozen at startup like every other catalog, so a cast never queries the database.
/// </summary>
/// <remarks>
/// Classifies each skill into a <see cref="SkillCastKind"/> once, so the cast path switches on an enum
/// rather than re-deriving effect types per request. Everything not classified is simply not castable.
/// </remarks>
public class BuffCatalog : IBuffCatalog
{
    public const int MagicSingleDamage = 231;
    public const int AddState = 301;
    public const int AddRegionState = 302;
    public const int AddHp = 501;
    public const int AddHpMp = 505;
    public const int ToggleAura = 701;
    public const int ToggleDifferentialAura = 702;
    public const int PhysicalSingleDamage = 30001;

    public static readonly int[] CastableEffectTypes =
    {
        MagicSingleDamage, AddState, AddRegionState, AddHp, AddHpMp, ToggleAura, ToggleDifferentialAura,
        PhysicalSingleDamage
    };

    /// <summary>
    /// The <c>SkillTarget</c> values whose target set contains the caster: <c>Target</c> (1),
    /// <c>RegionWith</c> (2), <c>SelfWithSummon</c> (45) and <c>PartyWithSummon</c> (51) — solo, a party
    /// is just the caster. <c>RegionWithout</c> (3) explicitly excludes the caster, and <c>Summon</c> (31)
    /// and <c>PartySummon</c> (32) target a summon, which nothing models; applying those to the caster
    /// would buff the wrong unit.
    /// </summary>
    public static readonly int[] SupportedTargets =
    {
        (int)SkillTarget.Target,
        (int)SkillTarget.RegionWith,
        (int)SkillTarget.SelfWithSummon,
        (int)SkillTarget.PartyWithSummon
    };

    private readonly ILogger _logger = Log.ForContext<BuffCatalog>();
    private readonly FrozenDictionary<int, CastableBuffFields> _skills;
    private readonly FrozenDictionary<SkillCastKind, int> _countByKind;

    public BuffCatalog(ISkillResourceRepository repository)
    {
        var skills = new Dictionary<int, CastableBuffFields>();
        foreach (var row in repository.GetCastableSkills())
        {
            if (TryClassify(row, out var fields))
            {
                skills[row.SkillId] = fields;
            }
        }

        _skills = skills.ToFrozenDictionary();
        _countByKind = _skills.Values
            .GroupBy(skill => skill.Kind)
            .ToFrozenDictionary(group => group.Key, group => group.Count());

        _logger.Debug("Loaded {count} castable skills: {kinds}", _skills.Count,
            string.Join(", ", _countByKind.Select(pair => $"{pair.Value} {pair.Key}")));
    }

    public int Count => _skills.Count;

    public int CountOf(SkillCastKind kind)
    {
        return _countByKind.TryGetValue(kind, out var count) ? count : 0;
    }

    public bool TryGet(int skillId, out CastableBuffFields fields)
    {
        return _skills.TryGetValue(skillId, out fields);
    }

    public static bool TryClassify(CastableSkillRow row, out CastableBuffFields fields)
    {
        fields = default;
        if (!TryResolveKind(row, out var kind))
        {
            return false;
        }

        // A buff, an aura and a debuff ARE a state; a heal and an attack carry their effect themselves.
        if (kind is not (SkillCastKind.Heal or SkillCastKind.PhysicalAttack or SkillCastKind.MagicAttack)
            && (row.StateId is null || row.StateId == 0))
        {
            return false;
        }

        fields = new CastableBuffFields(
            row.SkillId,
            kind,
            row.StateId ?? 0,
            row.ToggleGroup,
            row.Vars,
            row.StateSecond,
            row.StateSecondPerLevel,
            row.StateLevelBase,
            row.StateLevelPerSkill,
            row.CostMp,
            row.CostMpPerSkl,
            row.DelayCast,
            row.DelayCastPerSkl,
            row.DelayCommon,
            row.DelayCooltime,
            row.DelayCooltimePerSkl,
            row.RequiredLevel);
        return true;
    }

    private static bool TryResolveKind(CastableSkillRow row, out SkillCastKind kind)
    {
        kind = default;

        if (row.EffectType is ToggleAura or ToggleDifferentialAura)
        {
            kind = SkillCastKind.Aura;
            return true;
        }

        // Single-target offensive skills. The multi-hit and region variants need more than one hit
        // record or area resolution, so they stay out.
        if (row.EffectType is PhysicalSingleDamage or MagicSingleDamage)
        {
            if (row.Target != (int)SkillTarget.Target || !row.IsHarmful)
            {
                return false;
            }

            kind = row.EffectType == PhysicalSingleDamage
                ? SkillCastKind.PhysicalAttack
                : SkillCastKind.MagicAttack;
            return true;
        }

        if (row.EffectType is AddHp or AddHpMp)
        {
            if (row.Target != (int)SkillTarget.Target)
            {
                return false;
            }

            kind = SkillCastKind.Heal;
            return true;
        }

        if (row.EffectType is not (AddState or AddRegionState))
        {
            return false;
        }

        if (row.IsHarmful)
        {
            // A debuff is aimed at one monster; a harmful region skill would need area resolution.
            if (row.Target != (int)SkillTarget.Target)
            {
                return false;
            }

            kind = SkillCastKind.Debuff;
            return true;
        }

        if (!SupportedTargets.Contains(row.Target))
        {
            return false;
        }

        kind = SkillCastKind.Buff;
        return true;
    }
}
