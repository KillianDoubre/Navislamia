using System;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services.Buffs;

namespace Navislamia.Game.Services.Props;

/// <summary>
/// <c>SKILL_EFFECT_TARGET_LIMIT_*</c> (<c>SkillBase.h</c>): who a prop's heal lands on.
/// </summary>
public enum PropHealTargets
{
    AnyBody = 0,
    NotEnemy = 1,
    OnlyAlly = 2,
    OnlyEnemy = 3
}

/// <summary>
/// <c>StructSkill::REGION_HEAL_BY_FIELD_PROP</c> (9502): the prop's heal, once, around the prop.
/// </summary>
public readonly record struct RegionPropHeal(int FlatHp, float HpRatio, float Range, PropHealTargets Targets);

/// <summary>
/// <c>StructSkillProp::INIT/FIRE_AREA_EFFECT_HEAL_BY_FIELD_PROP</c> (9503): a healing area on the prop's spot, firing
/// every <see cref="IntervalTicks"/> until <see cref="DurationTicks"/> is over.
/// </summary>
public readonly record struct AreaPropHeal(float HpRatio, float MpRatio, float Range, uint IntervalTicks,
    PropHealTargets Targets, uint DurationTicks);

/// <summary>
/// The two healing-prop skills of HuntaHolic, ported from the official server (2015 sources,
/// docs/packet-specs/socle-huntaholic.md §8). <c>GetVar(i)</c> is <see cref="SkillAreaRules.Var"/>, a range is in
/// metres (× <see cref="SkillAreaRules.UnitSize"/>) and a time in seconds (× 100 ticks).
/// </summary>
public static class PropHealRules
{
    /// <summary>9502: <c>var0 + var1 × lvl</c> HP plus <c>(var2 + var3 × lvl) × max HP</c>, within <c>var4</c> m, targets <c>var5</c>.</summary>
    public static RegionPropHeal Region(CastableBuffFields fields, int level) => new(
        (int)(SkillAreaRules.Var(fields, 0) + SkillAreaRules.Var(fields, 1) * level),
        SkillAreaRules.Var(fields, 2) + SkillAreaRules.Var(fields, 3) * level,
        SkillAreaRules.Var(fields, 4) * SkillAreaRules.UnitSize,
        (PropHealTargets)(int)SkillAreaRules.Var(fields, 5));

    /// <summary>
    /// 9503: <c>(var0 + var1 × lvl) × max HP</c> and <c>(var2 + var3 × lvl) × max MP</c> within <c>var4</c> m every
    /// <c>var5</c> s, targets <c>var6</c>, for <c>var7 + var8 × lvl</c> s.
    /// </summary>
    public static AreaPropHeal Area(CastableBuffFields fields, int level) => new(
        SkillAreaRules.Var(fields, 0) + SkillAreaRules.Var(fields, 1) * level,
        SkillAreaRules.Var(fields, 2) + SkillAreaRules.Var(fields, 3) * level,
        SkillAreaRules.Var(fields, 4) * SkillAreaRules.UnitSize,
        (uint)Math.Max(1, SkillAreaRules.Var(fields, 5) * 100),
        (PropHealTargets)(int)SkillAreaRules.Var(fields, 6),
        (uint)Math.Max(0, (SkillAreaRules.Var(fields, 7) + SkillAreaRules.Var(fields, 8) * level) * 100));

    /// <summary>
    /// How many times the area fires. <c>onProcess</c> fires at once (<c>m_nLastFireTime = 0</c>), then every interval,
    /// and stops only once <c>current_time &gt; m_nEndTime</c>: a fire falling exactly on the end still lands.
    /// </summary>
    public static int AreaFireCount(AreaPropHeal area) => (int)(area.DurationTicks / area.IntervalTicks) + 1;

    /// <summary><c>Heal(amount)</c>: what is really given, bounded by the missing HP.</summary>
    public static int Healed(int flat, float ratio, int current, int max) =>
        Math.Clamp((int)(flat + ratio * max), 0, Math.Max(0, max - current));

    /// <summary>
    /// The player side of <c>IsAlly</c>/<c>IsEnemy</c> for a heal: the caster and the members of its party are its
    /// allies. No player is an enemy here (PK and duels are not judged for a prop's heal), so an enemy-only heal reaches
    /// no player, and the two other limits reach every player in range.
    /// </summary>
    public static bool Reaches(PropHealTargets targets, bool isCaster, long casterPartyId, long targetPartyId) => targets switch
    {
        PropHealTargets.OnlyAlly => isCaster || (casterPartyId != 0 && casterPartyId == targetPartyId),
        PropHealTargets.OnlyEnemy => false,
        _ => true
    };
}
