using System;
using System.Linq;

namespace Navislamia.Game.Services.Progression;

public readonly record struct BonusReward(long Exp, long Jp, int Stamina);

public static class MonsterRewardBonuses
{
    // DungeonLoader uses the seamless cell and g_fMapLength (16384 world units).
    public static bool InDungeon(float x, float y) => float.IsFinite(x) && float.IsFinite(y)
        && ProgressionResources.Official.DungeonCells.Any(c => x >= c.X * 16384f && x < (c.X + 1) * 16384f
            && y >= c.Y * 16384f && y < (c.Y + 1) * 16384f);

    public static double StaminaRatio(int level)
    {
        level = Math.Clamp(level, 1, 300);
        return (int)(level * 2.4 + Math.Pow(level, 1.46) + level * level * .1 + 2) * .00055;
    }

    public static BonusReward Apply(long exp, long jp, int stamina, int level, decimal staminaRate,
        bool inDungeon, decimal dungeonRate, bool staminaSave = false)
    {
        exp = Math.Max(0, exp); jp = Math.Max(0, jp); stamina = Math.Max(0, stamina);
        var cost = exp / StaminaRatio(level);
        var bonus = inDungeon ? Math.Clamp(dungeonRate, 0, 1000) : 0;
        if (staminaSave || stamina >= cost) bonus += Math.Clamp(staminaRate, 0, 1000);
        var remaining = staminaSave ? stamina : Math.Max(0, stamina - (int)Math.Min(int.MaxValue, cost));
        long Add(long value) => (long)Math.Min(long.MaxValue, Math.Floor(value * (1 + bonus)));
        return new BonusReward(Add(exp), Add(jp), remaining);
    }
}
