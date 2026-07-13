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
}
