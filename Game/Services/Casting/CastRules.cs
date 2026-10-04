using System;

namespace Navislamia.Game.Services.Casting;

/// <summary>
/// The official cast checks, from <c>CaptainHerlockServer.exe</c> 2012-11 (see
/// docs/packet-specs/socle-lancer-competences.md): the range of <c>StructCreature::CastSkill</c>
/// (<c>0x140095080</c>), the landing roll of <c>STATE_SKILL_FUNCTOR::onCreature</c> (<c>0x14020b0a0</c>) and
/// the casting pushback of <c>StructSkill::onDamage</c> (<c>0x140205ba0</c>). Every function is pure.
/// </summary>
public static class CastRules
{
    /// <summary><c>cast_range</c> is in metres: 12 world units each, like every skill range.</summary>
    public const int UnitsPerMetre = 12;

    /// <summary>The slack given on a standing target.</summary>
    public const float RangeTolerance = 1.2f;

    /// <summary>The slack given on a target that is walking.</summary>
    public const float MovingRangeTolerance = 1.5f;

    /// <summary><c>cast_range = -1</c>: the skill reaches as far as the caster's weapon.</summary>
    public const int WeaponRange = -1;

    /// <summary>
    /// Whether the target is within reach. The distance is measured between the two bodies (half a unit
    /// size off each centre) and compared with <c>12 × cast_range</c>, or with the caster's real attack range
    /// (<c>12 × attack_range / 100</c>) for a range of -1, times the tolerance.
    /// </summary>
    public static bool InRange(int castRange, float casterAttackRange, float casterX, float casterY,
        float casterUnitSize, float targetX, float targetY, float targetUnitSize, bool targetMoving)
    {
        var distance = MathF.Sqrt((casterX - targetX) * (casterX - targetX) + (casterY - targetY) * (casterY - targetY))
                       - casterUnitSize * 0.5f - targetUnitSize * 0.5f;
        var tolerance = targetMoving ? MovingRangeTolerance : RangeTolerance;
        var reach = castRange == WeaponRange
            ? UnitsPerMetre * casterAttackRange / 100f
            : UnitsPerMetre * castRange;
        return distance <= reach * tolerance;
    }

    /// <summary>The state effect types whose landing is a magic accuracy roll (301, 302, 305 to 313).</summary>
    public static bool IsAccuracyRolled(int effectType) =>
        effectType is 301 or 302 or >= 305 and <= 313;

    /// <summary>
    /// The chance out of 100 that a harmful state lands. For the effect types of
    /// <see cref="IsAccuracyRolled"/>: <c>magic accuracy − target magic avoid + hit bonus + 50</c>;
    /// otherwise <c>probability_on_hit + probability_inc_by_slv × skill level</c>.
    /// </summary>
    public static int StateLandingChance(int effectType, float casterMagicAccuracy, float targetMagicAvoid,
        int hitBonus, int probabilityOnHit, int probabilityPerLevel, int skillLevel)
        => IsAccuracyRolled(effectType)
            ? (int)casterMagicAccuracy - (int)targetMagicAvoid + hitBonus + 50
            : probabilityOnHit + probabilityPerLevel * skillLevel;

    /// <summary>
    /// Whether a harmful state lands, given a roll in <c>[0, 100)</c>. The accuracy roll lands when the roll
    /// does not exceed the chance, the probability roll the same way (<c>jle</c> / <c>jge</c> in the
    /// reference).
    /// </summary>
    public static bool StateLands(int chance, int roll) => roll <= chance;

    /// <summary>The share of the maximum HP a hit has to exceed to disturb a cast.</summary>
    public const float DisturbingDamageRatio = 0.005f;

    /// <summary><c>casting_type</c>: 0 nothing disturbs the cast, 1 a hit pushes it back, 2 a hit may break it.</summary>
    public const byte PushedBack = 1;

    /// <summary>See <see cref="PushedBack"/>.</summary>
    public const byte Breakable = 2;

    /// <summary>
    /// What a hit does to a cast in progress: for <c>casting_type</c> 1 the ticks the fire is pushed back by,
    /// for type 2 the chance out of 100 that the cast breaks; 0 when the hit is too small or the skill is
    /// neither. <c>casting_level</c> 0, 1, 2 gives 20, 50, 100 (pushback) and 20, 50, 100 (chance; any other
    /// level gives 0 for type 2 and 100 for type 1). The factor is the reference's, integer divisions
    /// included: <c>(1 + 0.4 × 10 × (damage / maxHp)) × (100 / casting speed)</c>.
    /// </summary>
    public static int DamageDisturbance(byte castingType, byte castingLevel, int damage, int maxHp,
        int castingSpeed)
    {
        if (castingType is not (PushedBack or Breakable) || maxHp <= 0
            || damage <= maxHp * DisturbingDamageRatio)
        {
            return 0;
        }

        var speed = castingSpeed == 0 ? 100 : castingSpeed;
        var factor = (1f + 0.4f * (10 * (damage / maxHp))) * (100 / speed);
        var basis = castingType == PushedBack
            ? castingLevel switch { 0 => 20, 1 => 50, _ => 100 }
            : castingLevel switch { 0 => 20, 1 => 50, 2 => 100, _ => 0 };
        return (int)(basis * factor);
    }

    /// <summary>
    /// The states whose arrival breaks the target's cast in <c>StructCreature::AddState</c>: six fixed ids,
    /// the effect type 104, and the effect type 82 when one of its first four values is set.
    /// </summary>
    public static bool InterruptsCasting(int stateId, int effectType, ReadOnlySpan<decimal> values)
    {
        if (stateId is 9001 or 6005 or 6006 or 6008 or 13601 or 1000006 or 1000007 || effectType == 104)
        {
            return true;
        }

        if (effectType != 82)
        {
            return false;
        }

        for (var i = 0; i < Math.Min(4, values.Length); i++)
        {
            if (values[i] != 0m)
            {
                return true;
            }
        }

        return false;
    }
}
