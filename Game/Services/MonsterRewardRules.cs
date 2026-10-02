using System;

namespace Navislamia.Game.Services;

/// <summary>
/// The rules the official 7.3 server applies to a monster's death rewards
/// (docs/packet-specs/socle-recompenses-monstres.md §5, §7). Everything here is pure: the arithmetic of a
/// kill is testable without a world, a client or a database.
/// </summary>
/// <remarks>
/// Measured in <c>CaptainHerlockServer.exe</c> 2012-11 (PDB addresses):
/// <list type="bullet">
/// <item>experience and JP lose <c>0.05</c> per level of gap when the beneficiary outlevels the monster
/// (<c>StructPlayer::AddExp</c>, <c>0x1400b73c0</c>, coefficient <c>0x140408458</c>), truncating;</item>
/// <item>gold, chaos and items lose <c>max(1 - 0.2 x (gap - 10), 0)</c> beyond ten levels
/// (<c>StructMonster::onDead</c>, <c>0x1400baa8d</c>, coefficient <c>0x140405fc8</c>);</item>
/// <item>experience and chaos are only granted within 500 units of the corpse (<c>0x1400b75ff</c>,
/// <c>0x1400b6cb8</c>, constant <c>500.0f</c> in <c>0x14040476c</c>);</item>
/// <item>the percentage columns are <b>per cent</b>: each chance is compared with <c>rand % 100</c>
/// (<c>procDropGold</c> <c>0x1400b3b14</c>, <c>procDropChaos</c> <c>0x1400b7e5e</c>).</item>
/// </list>
/// The rolling itself is deliberately left to the caller: the official draws once per monster and then
/// hands the amount to the party, which is the split this server already does.
/// </remarks>
public static class MonsterRewardRules
{
    /// <summary>How far from the corpse experience and chaos are still granted.</summary>
    public const float RewardRange = 500f;

    /// <summary>The experience and JP malus per level of gap above the monster's level.</summary>
    public const double ExperienceLevelGapStep = 0.05;

    /// <summary>The loot malus per level of gap beyond <see cref="LootLevelGapStart"/>.</summary>
    public const double LootLevelGapStep = 0.2;

    /// <summary>The level gap from which the loot malus starts.</summary>
    public const int LootLevelGapStart = 10;

    /// <summary>The <c>rand % 100</c> the official compares a per-cent chance with.</summary>
    public const int PercentageRollBound = 100;

    /// <summary>
    /// Whether a beneficiary at <paramref name="x"/>, <paramref name="y"/> is close enough to the corpse to
    /// be granted experience and chaos.
    /// </summary>
    public static bool WithinRewardRange(float x, float y, float monsterX, float monsterY)
    {
        var dx = x - monsterX;
        var dy = y - monsterY;
        return dx * dx + dy * dy <= RewardRange * RewardRange;
    }

    /// <summary>
    /// <paramref name="amount"/> × <c>1 - 0.05 × (beneficiary level - monster level)</c>: the beneficiary
    /// only, and only downwards, so an under-levelled player keeps the whole amount. The result truncates,
    /// like the <c>float</c> to <c>int</c> conversion of the reference, and never goes below zero.
    /// </summary>
    public static long ScaleForLevelGap(long amount, int monsterLevel, int beneficiaryLevel)
    {
        if (amount <= 0)
        {
            return 0;
        }

        var gap = beneficiaryLevel - monsterLevel;
        if (gap <= 0)
        {
            return amount;
        }

        var factor = 1 - ExperienceLevelGapStep * gap;
        return factor <= 0 ? 0 : (long)Math.Floor(amount * factor);
    }

    /// <summary>
    /// <c>max(1 - 0.2 × (gap - 10), 0)</c>, where the gap is the highest level of the rewarded party minus
    /// the monster's: one factor, applied to the gold, the chaos and the loot. It is 1 up to ten levels.
    /// </summary>
    public static double LootFactor(int monsterLevel, int highestBeneficiaryLevel)
    {
        var gap = highestBeneficiaryLevel - monsterLevel;
        if (gap <= LootLevelGapStart)
        {
            return 1;
        }

        return Math.Max(1 - LootLevelGapStep * (gap - LootLevelGapStart), 0);
    }

    /// <summary>
    /// Whether a chance passes its per-cent test. A chance of 100 or more always passes (the roll is at most
    /// 99); a chance of 0 or below, or a value that is not a number, never does.
    /// </summary>
    public static bool PassesChance(double chance, int roll) =>
        double.IsFinite(chance) && chance > 0 && roll < chance;

    /// <summary>The per-cent chance test with the roll drawn from <paramref name="random"/>.</summary>
    public static bool RollsChance(double chance, ICombatRandom random) =>
        random is not null && PassesChance(chance, random.Next(PercentageRollBound));

    /// <summary>
    /// The official <c>irand(min, max)</c>: a uniform amount in <c>[min, max]</c>, inclusive, with the
    /// bounds tolerated in either order. A non-positive upper bound gives nothing.
    /// </summary>
    public static long RollAmount(int min, int max, ICombatRandom random)
    {
        if (random is null)
        {
            return 0;
        }

        if (max < min)
        {
            (min, max) = (max, min);
        }

        if (max <= 0)
        {
            return 0;
        }

        if (min < 0)
        {
            min = 0;
        }

        var span = (long)max - min + 1;
        return min + (span > int.MaxValue ? 0 : random.Next((int)span));
    }
}
