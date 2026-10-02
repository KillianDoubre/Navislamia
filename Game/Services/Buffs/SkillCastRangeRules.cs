using System;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Buffs;

/// <summary>StructCreature::CastSkill: body radii, weapon range (-1), and movement tolerance.</summary>
public static class SkillCastRangeRules
{
    public static bool AppliesTo(int effect) => SkillAreaRules.IsArea(effect)
        || SkillAreaRules.IsAtOnceMultiple(effect) || SkillAreaRules.IsSequential(effect);

    public static bool InRange(int castRange, float attackRange, float distance,
        float casterSize, float targetSize, bool targetMoving)
    {
        if (castRange < -1 || !float.IsFinite(distance) || distance < 0
            || !float.IsFinite(casterSize) || !float.IsFinite(targetSize)) return false;
        var range = castRange == -1 ? attackRange * SkillAreaRules.UnitSize / 100
            : castRange * SkillAreaRules.UnitSize;
        if (!float.IsFinite(range) || range < 0) return false;
        return distance - Math.Max(0, casterSize) / 2 - Math.Max(0, targetSize) / 2
            <= range * (targetMoving ? 1.5f : 1.2f);
    }

    /// <summary>The existing ground-coordinate gate has no target body or movement tolerance.</summary>
    public static bool GroundInRange(int castRange, float attackRange, float distance)
    {
        if (castRange < -1 || !float.IsFinite(distance) || distance < 0) return false;
        var range = castRange == -1 ? attackRange * SkillAreaRules.UnitSize / 100
            : castRange * SkillAreaRules.UnitSize;
        return float.IsFinite(range) && range >= 0 && distance <= range;
    }

    public static bool IsPlayerMoving(ConnectionInfo info, uint now)
    {
        if (info.MoveStartTick == 0 || info.MoveSpeed == 0) return false;
        var distance = CombatRange.Distance(info.X, info.Y, info.DestinationX, info.DestinationY);
        var end = MonsterMovement.EndTick(info.MoveStartTick, distance, info.MoveSpeed);
        return unchecked((int)(now - info.MoveStartTick)) >= 0 && unchecked((int)(now - end)) < 0;
    }

    public static (float X, float Y) PlayerPosition(ConnectionInfo info, uint now) =>
        info.MoveStartTick == 0 ? (info.X, info.Y) : info.PositionAt(now);
}
