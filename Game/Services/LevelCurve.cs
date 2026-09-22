namespace Navislamia.Game.Services;

public static class LevelCurve
{
    public static int Resolve(long[] cumulativeExp, int maxLevel, long exp, int currentLevel)
    {
        var level = currentLevel < 1 ? 1 : currentLevel;

        while (level < maxLevel && level < cumulativeExp.Length && exp >= cumulativeExp[level])
        {
            level++;
        }

        return level;
    }

    /// <summary>
    /// The cumulative experience a character needs to <b>be</b> <paramref name="level"/>:
    /// <c>cumulativeExp[L]</c> is the threshold to advance from <c>L</c> to <c>L + 1</c>, so level
    /// <c>N</c> starts at <c>cumulativeExp[N - 1]</c>, and level 1 at zero. False for a level outside
    /// <c>1..maxLevel</c> or whose threshold was never loaded.
    /// </summary>
    public static bool TryGetExperienceFor(long[] cumulativeExp, int maxLevel, int level, out long exp)
    {
        exp = 0;
        if (cumulativeExp == null || level < 1 || level > maxLevel)
        {
            return false;
        }

        if (level == 1)
        {
            return true;
        }

        if (level - 1 >= cumulativeExp.Length || cumulativeExp[level - 1] == long.MaxValue)
        {
            return false;
        }

        exp = cumulativeExp[level - 1];
        return true;
    }
}
