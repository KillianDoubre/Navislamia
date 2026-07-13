namespace Navislamia.Game.Services;

public static class CombatRewards
{
    private const long ExpBase = 10;
    private const long ExpPerLevel = 5;
    private const long JpBase = 5;
    private const long JpPerLevel = 2;
    private const long GoldBase = 5;
    private const long GoldPerLevel = 3;

    public static (long Exp, long Jp, long Gold) Compute(int monsterLevel)
    {
        var level = monsterLevel > 0 ? monsterLevel : 1;
        return (ExpBase + level * ExpPerLevel, JpBase + level * JpPerLevel, GoldBase + level * GoldPerLevel);
    }
}
