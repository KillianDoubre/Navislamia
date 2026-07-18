using System;

namespace Navislamia.Game.Services;

public enum MonsterAiAction
{
    Idle,
    Acquire,
    Chase,
    Attack,
    Drop
}

/// <summary>
/// The pure decisions behind monster AI, ported from the reference server's
/// <c>Monster::AI_processAttack</c> and simplified to a single enemy. This is the tested core;
/// <c>MonsterAiService</c> is the I/O shell that reads world state and sends packets around it.
/// </summary>
public static class MonsterAiRules
{
    /// <summary>The reference multiplies chase and cast ranges by 12 to reach world units.</summary>
    public const int RangeScale = 12;

    public static int ScaledChaseRange(int chaseRange) => RangeScale * Math.Max(0, chaseRange);

    public static int ScaledVisibleRange(int visibleRange) => RangeScale * Math.Max(0, visibleRange);

    /// <summary>Test damage: one hundredth of the player's max HP, never below 1.</summary>
    public static int PlayerDamage(int playerMaxHp) => Math.Max(1, playerMaxHp / 100);

    public static float Distance(float ax, float ay, float bx, float by) =>
        CombatRange.Distance(ax, ay, bx, by);

    /// <param name="hasTarget">Whether the monster already holds an aggro target.</param>
    /// <param name="isAggressive">The monster's <c>FirstAttack</c> flag.</param>
    /// <param name="streamedToPlayer">Whether the target player currently sees the monster; a monster
    /// may never act on a player it is not streamed to.</param>
    /// <param name="cooldownElapsed">Whether the attack cooldown has passed.</param>
    /// <param name="attackReach">The real per-monster melee reach from <see cref="CombatRange"/>.</param>
    public static MonsterAiAction Decide(
        bool hasTarget,
        bool isAggressive,
        bool streamedToPlayer,
        bool cooldownElapsed,
        float monsterX, float monsterY,
        float homeX, float homeY,
        float playerX, float playerY,
        int visibleRange,
        int chaseRange,
        float attackReach)
    {
        if (!hasTarget)
        {
            if (isAggressive && streamedToPlayer
                && Distance(monsterX, monsterY, playerX, playerY) <= ScaledVisibleRange(visibleRange))
            {
                return MonsterAiAction.Acquire;
            }

            return MonsterAiAction.Idle;
        }

        if (!streamedToPlayer
            || Distance(homeX, homeY, monsterX, monsterY) > ScaledChaseRange(chaseRange))
        {
            return MonsterAiAction.Drop;
        }

        if (Distance(monsterX, monsterY, playerX, playerY) <= attackReach)
        {
            return cooldownElapsed ? MonsterAiAction.Attack : MonsterAiAction.Idle;
        }

        return MonsterAiAction.Chase;
    }

    /// <summary>
    /// A step toward the player that stops short of stacking on them, mirroring the reference's habit
    /// of walking to just inside attack range rather than onto the target.
    /// </summary>
    public static (float X, float Y) ChaseStep(float monsterX, float monsterY, float playerX,
        float playerY, float attackReach)
    {
        var distance = Distance(monsterX, monsterY, playerX, playerY);
        var stop = attackReach * 0.8f;
        if (distance <= stop)
        {
            return (monsterX, monsterY);
        }

        var travel = (distance - stop) / distance;
        return (
            monsterX + (playerX - monsterX) * travel,
            monsterY + (playerY - monsterY) * travel);
    }
}
