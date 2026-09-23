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
    ActivateProp
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
    int RequiredLevel);

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
    int RequiredLevel);

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
}
