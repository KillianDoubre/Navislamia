namespace Navislamia.Game.Services;

public static class JobLevelCurve
{
    public static int NextCost(int[] costByLevel, int currentJobLevel)
    {
        var level = currentJobLevel < 1 ? 1 : currentJobLevel;
        if (level >= costByLevel.Length)
        {
            return 0;
        }

        var cost = costByLevel[level];
        return cost > 0 ? cost : 0;
    }

    /// <inheritdoc cref="NextCost(int[], int)"/>
    public static long NextCost(long[] costByLevel, int currentJobLevel)
    {
        var level = currentJobLevel < 1 ? 1 : currentJobLevel;
        if (costByLevel is null || level >= costByLevel.Length)
        {
            return 0;
        }

        var cost = costByLevel[level];
        return cost > 0 ? cost : 0;
    }
}
