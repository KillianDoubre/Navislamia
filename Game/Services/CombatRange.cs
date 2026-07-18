using System;

namespace Navislamia.Game.Services;

/// <summary>
/// The melee reach both directions of combat share, so a player hits a monster and the monster hits
/// the player at the same distance. Player attacks used to have no range gate at all — they landed
/// anywhere in the 540-unit view — while monster attacks gated at a flat placeholder, which is the
/// asymmetry that read as an inconsistent attack range.
/// </summary>
/// <remarks>
/// This is now the reference's real value, ported from <c>Unit::GetRealAttackRange</c> and
/// <c>Object::GetUnitSize</c>: the effective reach between two units is the attacker's weapon reach
/// plus both body radii. A unit's size is <c>size × 12 × scale</c>; the reach is
/// <c>(12 × attack_range) / 100 + (attackerUnitSize + targetUnitSize) × 0.5</c>. The body-size term
/// dominates, so a small monster reaches ~12 units and a huge one hundreds — big monsters really do
/// hit from farther. The player has no modelled weapon range or size, so it uses the default unit size
/// (<c>1 × 12 × 1 = 12</c>) and the monster's weapon term; the same reach gates both directions, which
/// keeps them symmetric.
/// </remarks>
public static class CombatRange
{
    private const float UnitSizeScale = 12f;
    private const float WeaponRangeScale = 12f / 100f;

    /// <summary>A unit with no modelled size resolves to <c>1 × 12 × 1</c>, like the reference default.</summary>
    public const float PlayerUnitSize = UnitSizeScale;

    public static float UnitSize(float size, float scale) => size * UnitSizeScale * scale;

    /// <summary>
    /// The reach between a monster (its weapon range and body size) and the player. A tiny floor keeps
    /// a monster with zero size from resolving to a degenerate reach.
    /// </summary>
    public static float MeleeReach(float monsterAttackRange, float monsterSize, float monsterScale)
    {
        var weapon = monsterAttackRange * WeaponRangeScale;
        var bodies = (UnitSize(monsterSize, monsterScale) + PlayerUnitSize) * 0.5f;
        return MathF.Max(weapon + bodies, PlayerUnitSize);
    }

    public static float Distance(float ax, float ay, float bx, float by)
    {
        var dx = ax - bx;
        var dy = ay - by;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public static bool InReach(float ax, float ay, float bx, float by, float reach)
    {
        return Distance(ax, ay, bx, by) <= reach;
    }
}
