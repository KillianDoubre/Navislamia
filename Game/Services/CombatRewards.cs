using System;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.Services.Rates;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services;

/// <summary>The loaded resource's rewards, also carried by replacement resources and scripted spawns.</summary>
public readonly record struct MonsterRewardProfile(int Exp, int Jp, int GoldChance, int GoldMin, int GoldMax,
    int ChaosChance, int ChaosMin, int ChaosMax)
{
    public static MonsterRewardProfile From(MonsterResourceEntity row, bool secondary = false) => new(
        secondary ? row.Exp2 : row.Exp, secondary ? row.Jp2 : row.Jp, row.GoldDropPercentage,
        secondary ? row.GoldMin2 : row.GoldMin, secondary ? row.GoldMax2 : row.GoldMax,
        row.ChaosDropPercentage, secondary ? row.ChaosMin2 : row.ChaosMin,
        secondary ? row.ChaosMax2 : row.ChaosMax);
}

public readonly record struct MonsterKillReward(long Exp, long Jp, long Gold, int Chaos);

public static class CombatRewards
{
    /// <summary>GameRule::MAX_GOLD_DROP, applied after drawing the amount.</summary>
    public const long MaxGoldDrop = 1_000_000;

    /// <param name="lootFactor">The level-gap malus of <see cref="MonsterRewardRules.LootFactor"/>, on both chances.</param>
    public static MonsterKillReward Roll(MonsterRewardProfile profile, IRateService rates, ICombatRandom random,
        double lootFactor = 1)
    {
        var loot = double.IsFinite(lootFactor) ? Math.Clamp(lootFactor, 0, 1) : 0;
        return new(
            rates.Scale(Math.Max(0, profile.Exp), RateType.Exp), rates.Scale(Math.Max(0, profile.Jp), RateType.Jp),
            Math.Min(MaxGoldDrop, RollAmount(profile.GoldChance, profile.GoldMin, profile.GoldMax,
                rates.Get(RateType.Gold) * loot, random)),
            RollAmount(profile.ChaosChance, profile.ChaosMin, profile.ChaosMax, rates.Get(RateType.ChaosDrop) * loot,
                random));
    }

    public static int RollAmount(int chance, int min, int max, double rate, ICombatRandom random)
    {
        var low = Math.Max(0, Math.Min(min, max));
        var high = Math.Max(0, Math.Max(min, max));
        if (chance <= 0 || high == 0 || RateMath.Sanitize(rate) == 0
            || Math.Min(100, chance * rate) <= random.Next(100)) return 0;
        if (low == high) return low;
        var span = (long)high - low + 1;
        // The only span above int.MaxValue is 0..int.MaxValue; compose a uniform 31-bit draw.
        var draw = span <= int.MaxValue ? random.Next((int)span)
            : ((long)random.Next(65536) << 15) + random.Next(32768);
        return (int)(low + draw);
    }

    public static long Share(long total, int count, int index) =>
        total / count + (index < total % count ? 1 : 0);

    public static long AddProgress(long current, long amount) =>
        Math.Max(0, current) + Math.Min(Math.Max(0, amount), long.MaxValue - Math.Max(0, current));

    public static int ChaosCapacity(float maximum) =>
        !float.IsFinite(maximum) || maximum <= 0 ? 0 : (int)Math.Min(int.MaxValue, Math.Floor(maximum));
}
