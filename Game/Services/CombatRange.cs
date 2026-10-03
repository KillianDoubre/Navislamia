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
/// <c>(12 × attack_range) / 100 + (attackerUnitSize + targetUnitSize) × 0.5</c>, where <c>attack_range</c> is
/// the column <b>times 100</b> (NGemity <c>ObjectMgr</c>: <c>attack_range = GetFloat() × 100</c>), so the weapon
/// term is <c>12 × column</c>: a 0.6 m claw reaches 7.2 units beyond the bodies, an 8 m ranged monster 96.
/// The factor 100 was missing until 2026-10-02, which made every monster a melee one
/// (docs/packet-specs/socle-mecaniques-combat.md §5). The player has no modelled weapon range or size, so it uses the default unit size
/// (<c>1 × 12 × 1 = 12</c>) and the monster's weapon term; the same reach gates both directions, which
/// keeps them symmetric.
/// </remarks>
public static class CombatRange
{
    private const float UnitSizeScale = 12f;
    /// <summary><c>attack_range</c> columns are in metres: 12 units each (×100 at load, ×12/100 in the reach).</summary>
    private const float WeaponRangeScale = 12f;

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

    /// <summary>
    /// The reach between a monster and a creature with its own body (a summon): the monster's weapon range plus both
    /// body radii, the rule <see cref="MeleeReach"/> applies with the player's default body.
    /// </summary>
    public static float InterUnitReach(float monsterAttackRange, float monsterSize, float monsterScale,
        float targetSize, float targetScale)
    {
        var weapon = monsterAttackRange * WeaponRangeScale;
        var bodies = (UnitSize(monsterSize, monsterScale) + UnitSize(targetSize, targetScale)) * 0.5f;
        return MathF.Max(weapon + bodies, PlayerUnitSize);
    }

    public static float Distance(float ax, float ay, float bx, float by)
    {
        var dx = ax - bx;
        var dy = ay - by;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// The player's own reach to a monster, as the reference's <c>processAttack</c> measures it: its weapon range
    /// (<c>AttackRange</c> stat, the weapon's <c>range</c> × 100, 50 bare-handed) plus both bodies.
    /// </summary>
    public static float PlayerReach(float attackRangeStat, float monsterSize, float monsterScale) =>
        attackRangeStat * WeaponRangeScale / 100f + (UnitSize(monsterSize, monsterScale) + PlayerUnitSize) * 0.5f;

    public static bool InReach(float ax, float ay, float bx, float by, float reach)
    {
        return Distance(ax, ay, bx, by) <= reach;
    }
}
