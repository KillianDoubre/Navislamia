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

    /// <summary>
    /// The experience a character loses when a monster kills it, <c>StructPlayer::GetDeadEXPPenalty</c>
    /// (<c>0x1400c0de0</c>): <c>GetNeedExp(level) x (0.15 / (level - 1) + 0.0005)</c>, twice that on a PK
    /// server (0.3 and 0.001). <c>GetNeedExp(level)</c> is the threshold to advance from the level, the same
    /// row <see cref="Resolve"/> reads. Level 1 loses nothing: the official division by <c>level - 1</c> has
    /// no meaning there. docs/packet-specs/socle-perte-experience.md.
    /// </summary>
    public static long DeathPenalty(long[] cumulativeExp, int maxLevel, int level, bool pkServer = false)
    {
        if (cumulativeExp == null || level <= 1)
        {
            return 0;
        }

        var row = System.Math.Min(System.Math.Min(level, maxLevel), cumulativeExp.Length - 1);
        var need = cumulativeExp[row];
        if (need <= 0 || need == long.MaxValue)
        {
            return 0;
        }

        var ratio = pkServer ? 0.3 / (level - 1) + 0.001 : 0.15 / (level - 1) + 0.0005;
        return (long)(need * ratio);
    }
}
