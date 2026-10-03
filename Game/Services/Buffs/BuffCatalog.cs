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
    public const int AddRegionHpMp = 508;
    public const int AddRegionHp = 521;
    public const int Summon = 601;
    public const int Unsummon = 602;
    public const int Taming = 603;
    public const int ToggleAura = 701;
    public const int ToggleDifferentialAura = 702;
    public const int PhysicalSingleDamage = 30001;
    public const int Resurrection = 504;
    public const int ResurrectionWithRecover = 30501;

    /// <summary>
    /// EF_ACTIVATE_FIELD_PROP (0x251D). This is how a portal is used: the client casts the prop's
    /// activate skill at the prop's handle.
    /// </summary>
    public const int ActivateFieldProp = 9501;

    /// <summary>
    /// The three creature spells of Epic 7.3 — summon (4001), unsummon (4002) and taming (4003) — whose
    /// effect types are 601/602/603. The skill id is part of the classification key: effect type 603
    /// alone would also cover 4004, a second taming skill that the 7.3 client's skill table does not
    /// carry, and our key (the effect type) is wider than the reference's per-skill entry
    /// (docs/packet-specs/socle-apprivoisement-invocation.md §6 and §5.3).
    /// </summary>
    public const int SummonSkill = 4001;

    /// <inheritdoc cref="SummonSkill"/>
    public const int UnsummonSkill = 4002;

    /// <inheritdoc cref="SummonSkill"/>
    public const int TamingSkill = 4003;

    /// <summary>Effect 604 of the two instance game spells (<see cref="SkillCastKind.InstanceGame"/>).</summary>
    public const int InstanceGameEffect = 604;

    public const int WarpToHuntaholicLobbySkill = 64818;
    public const int InstanceGameExitSkill = 64827;

    public static readonly int[] CastableEffectTypes =
    {
        MagicSingleDamage, AddState, AddRegionState, AddHp, AddHpMp, AddRegionHpMp, AddRegionHp, ToggleAura, ToggleDifferentialAura,
        PhysicalSingleDamage, ActivateFieldProp, Summon, Unsummon, Taming, Resurrection, ResurrectionWithRecover,
        InstanceGameEffect,
        30011, 30012, 30013, 30016, 232, 241, 261, 262, 263, 271
    };

    /// <summary>
    /// Player, party, region and summon targets resolved by the support cast path.
    /// </summary>
    public static readonly int[] SupportedTargets =
    {
        (int)SkillTarget.Target,
        (int)SkillTarget.RegionWith,
        (int)SkillTarget.RegionWithout,
        (int)SkillTarget.Region,
        (int)SkillTarget.ExceptCaster,
        (int)SkillTarget.Party,
        (int)SkillTarget.Summon,
        (int)SkillTarget.PartySummon,
        (int)SkillTarget.SelfWithSummon,
        (int)SkillTarget.PartyWithSummon,
        (int)SkillTarget.Master,
        (int)SkillTarget.SelfWithMaster
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

    /// <summary><c>casting_type</c> and <c>casting_level</c> are text columns holding 0, 1 or 2.</summary>
    private static byte SmallNumber(string value) =>
        byte.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : (byte)0;

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

        // A buff, an aura and a debuff ARE a state; a heal, an attack, a prop activation and a creature
        // spell carry their effect themselves.
        if (kind is not (SkillCastKind.Heal or SkillCastKind.PhysicalAttack or SkillCastKind.MagicAttack
                or SkillCastKind.ActivateProp or SkillCastKind.Summon or SkillCastKind.Unsummon
                or SkillCastKind.Taming or SkillCastKind.Resurrection or SkillCastKind.InstanceGame)
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
            row.RequiredLevel,
            row.HitBonus,
            row.Percentage,
            row.CriticalBonus,
            row.CriticalBonusPerSkl,
            row.EffectType,
            row.Target,
            row.RequiredTarget,
            row.CastRange,
            row.ProbabilityOnHit,
            row.ProbabilityIncBySlv,
            SmallNumber(row.CastingType),
            SmallNumber(row.CastingLevel),
            // is_passive marks the cancellable skills in this data: 976 of the 977 with a cast delay carry it,
            // and StructSkill::Cancel refuses a skill without it (socle-lancer-competences.md §5).
            row.IsPassive,
            row.IsHarmful,
            row.HateMod,
            row.HateBasic,
            row.HatePerSkl, row.ValidRange, row.UseOnSelf, row.UseOnParty,
            row.UseOnNeutral, row.UseOnCharacter, row.UseOnSummon, row.ElementalType);
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

        // The prop itself decides what the cast does, so the target is a prop handle rather than one
        // of the SkillTargets, and no state is involved.
        if (row.EffectType == ActivateFieldProp)
        {
            kind = SkillCastKind.ActivateProp;
            return true;
        }

        // The three creature spells (4001/4002/4003), keyed on the skill id as well as the effect type:
        // 603 alone would also cover 4004, which the 7.3 client does not carry (fiche §6).
        if (row.SkillId is SummonSkill or UnsummonSkill or TamingSkill)
        {
            if (row.EffectType is not (Summon or Unsummon or Taming))
            {
                return false;
            }

            kind = row.EffectType switch
            {
                Summon => SkillCastKind.Summon,
                Unsummon => SkillCastKind.Unsummon,
                _ => SkillCastKind.Taming
            };
            return true;
        }

        // StructSkill::ProcSkill dispatches these two on their ids: 604 is their effect and nothing else's.
        if (row.SkillId is WarpToHuntaholicLobbySkill or InstanceGameExitSkill)
        {
            kind = SkillCastKind.InstanceGame;
            return row.EffectType == InstanceGameEffect;
        }

        // A resurrection on a character (tf_avatar): 6013, the creature scroll's skill, targets summons only.
        if (row.EffectType is Resurrection or ResurrectionWithRecover)
        {
            if (!row.UseOnCharacter)
            {
                return false;
            }

            kind = SkillCastKind.Resurrection;
            return true;
        }

        if (SkillAreaRules.IsSupportedDamage(row.EffectType) && row.EffectType is not (101 or 201 or 30001 or 231))
        {
            if (!row.IsHarmful || row.Target is not (1 or 2 or 3 or 4)) return false;
            if (!SkillAreaRules.IsArea(row.EffectType) && row.Target != 1) return false;
            kind = SkillAreaRules.IsMagical(row.EffectType)
                ? SkillCastKind.MagicAttack : SkillCastKind.PhysicalAttack;
            return true;
        }

        // Existing single-target families retain their target gate.
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

        if (row.EffectType is AddHp or AddHpMp or AddRegionHpMp or AddRegionHp)
        {
            if (row.IsHarmful || !SupportedTargets.Contains(row.Target))
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
