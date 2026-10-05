using System.Collections.Generic;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

public readonly record struct SkillPassiveFields(
    int SkillId,
    int EffectType,
    decimal[] Vars,
    SkillWeaponFlag Weapons,
    bool WeaponNotRequired);

public enum SkillCastKind
{
    Buff,
    Aura,
    Heal,
    Debuff,
    PhysicalAttack,
    MagicAttack,
    ActivateProp,

    /// <summary>
    /// The creature spell 4001 (<c>EF_SUMMON</c> 601): the card the cast targets becomes a summon
    /// (<c>Skill::CREATURE_SUMMON</c>). Carrying it out needs the card lookup and the summon record,
    /// neither of which exists yet.
    /// </summary>
    Summon,

    /// <summary>The creature spell 4002 (<c>EF_UNSUMMON</c> 602): the card's summon goes back to it.</summary>
    Unsummon,

    /// <summary>
    /// The creature spell 4003 (<c>EF_TAMING</c> 603): an attempt on a living monster, judged by
    /// <see cref="Navislamia.Game.Services.TamingRules"/>.
    /// </summary>
    Taming,

    /// <summary>
    /// <c>EF_RESURRECTION</c> (504) and <c>EF_RESURRECTION_WITH_RECOVER</c> (30501) on a character
    /// (<c>tf_avatar</c>): a dead player in sight comes back where they fell
    /// (docs/packet-specs/socle-mort-joueur.md §3).
    /// </summary>
    Resurrection,

    /// <summary>
    /// The instance game spells 64818 (<c>SKILL_WARP_TO_HUNTAHOLIC_LOBBY</c>) and 64827 (<c>SKILL_INSTANCE_GAME_EXIT</c>),
    /// effect 604: never learned, cast by the server on 4250/4251 (<c>CastSkill</c> in <c>onInstanceGameEnter</c>),
    /// carried out by HuntaHolic when they fire (docs/packet-specs/socle-huntaholic.md §6).
    /// </summary>
    InstanceGame,

    /// <summary>
    /// <c>EF_REGION_HEAL_BY_FIELD_PROP</c> (9502) and <c>EF_AREA_EFFECT_HEAL_BY_FIELD_PROP</c> (9503): cast at a HuntaHolic
    /// healing prop, never learned; the prop is used up, then heals around it at once (9502) or leaves a healing area
    /// (9503). docs/packet-specs/socle-huntaholic.md §8.
    /// </summary>
    PropHeal,
    Energy
}

public readonly record struct CastableBuffFields(
    int SkillId,
    SkillCastKind Kind,
    int StateId,
    int ToggleGroup,
    decimal[] Vars,
    decimal StateSecond,
    decimal StateSecondPerLevel,
    int StateLevelBase,
    decimal StateLevelPerSkill,
    int CostMp,
    int CostMpPerSkl,
    decimal DelayCast,
    decimal DelayCastPerSkl,
    decimal DelayCommon,
    decimal DelayCooltime,
    decimal DelayCooltimePerSkl,
    int RequiredLevel,
    int HitBonus = 0,
    int Percentage = 0,
    int CriticalBonus = 0,
    int CriticalBonusPerSkl = 0,
    int EffectType = 0,
    int Target = 1,
    int RequiredTarget = 1,
    int CastRange = 0,
    int ProbabilityOnHit = 0,
    int ProbabilityIncBySlv = 0,
    byte CastingType = 0,
    byte CastingLevel = 0,
    bool Cancelable = false,
    bool IsHarmful = false,
    decimal HateMod = 0m,
    int HateBasic = 0,
    decimal HatePerSkl = 0m,
    int ValidRange = 0, bool UseOnSelf = true, bool UseOnParty = true,
    bool UseOnNeutral = true, bool UseOnCharacter = true, bool UseOnSummon = true, int ElementalType = 0, decimal CostEnergy = 0m, decimal CostEnergyPerSkl = 0m);

/// <summary>The raw fields the catalog classifies into a <see cref="SkillCastKind"/>.</summary>
public readonly record struct CastableSkillRow(
    int SkillId,
    int EffectType,
    bool IsHarmful,
    int Target,
    int? StateId,
    int ToggleGroup,
    decimal[] Vars,
    decimal StateSecond,
    decimal StateSecondPerLevel,
    int StateLevelBase,
    decimal StateLevelPerSkill,
    int CostMp,
    int CostMpPerSkl,
    decimal DelayCast,
    decimal DelayCastPerSkl,
    decimal DelayCommon,
    decimal DelayCooltime,
    decimal DelayCooltimePerSkl,
    int RequiredLevel,
    int HitBonus = 0,
    int Percentage = 0,
    int CriticalBonus = 0,
    int CriticalBonusPerSkl = 0,
    int RequiredTarget = 1,
    int CastRange = 0,
    int ProbabilityOnHit = 0,
    int ProbabilityIncBySlv = 0,
    string CastingType = null,
    string CastingLevel = null,
    bool IsPassive = false,
    bool UseOnCharacter = false,
    decimal HateMod = 0m,
    int HateBasic = 0,
    decimal HatePerSkl = 0m,
    int ValidRange = 0, bool UseOnSelf = true, bool UseOnParty = true,
    bool UseOnNeutral = true, bool UseOnSummon = true, int ElementalType = 0, decimal CostEnergy = 0m, decimal CostEnergyPerSkl = 0m);

/// <summary>A resurrection skill (<c>EF_RESURRECTION</c> 504 or <c>EF_RESURRECTION_WITH_RECOVER</c> 30501).</summary>
public readonly record struct ResurrectionSkillRow(int SkillId, int EffectType, decimal[] Vars);

public interface ISkillResourceRepository
{
    IReadOnlyList<SkillPassiveFields> GetStatPassives();

    /// <summary>
    /// The resurrection skills that can target a character (<c>tf_avatar</c>, <c>UseOnCharacter</c>): the
    /// Resurrection Scroll's 6001 is one, the Creature Resurrection Scroll's 6013 (summons only) is not.
    /// </summary>
    IReadOnlyList<ResurrectionSkillRow> GetResurrectionSkills();

    IReadOnlyList<CastableSkillRow> GetCastableSkills();

    /// <summary>
    /// The rows of <paramref name="ids"/>, whatever their effect type: a monster casts skills no player
    /// learns (<c>EF_PHYSICAL_SINGLE_DAMAGE_T1</c> 101, <c>EF_MAGIC_SINGLE_DAMAGE_T1_OLD</c> 201…).
    /// </summary>
    IReadOnlyList<CastableSkillRow> GetSkillRows(IReadOnlyCollection<int> ids);

    /// <summary>The rows of the given effect types, including state and energy proc passives.</summary>
    IReadOnlyList<CastableSkillRow> GetSkillRowsByEffectType(IReadOnlyCollection<int> effectTypes) =>
        System.Array.Empty<CastableSkillRow>();
}
