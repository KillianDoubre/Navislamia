namespace Navislamia.Game.Services.Stats;

public readonly record struct StatBaseStats(
    int StatId,
    float Strength,
    float Vitality,
    float Dexterity,
    float Agility,
    float Intelligence,
    float Wisdom,
    float Luck);

public sealed class JobStatBonus
{
    public JobStatBonus((StatTarget Target, float[] PerLevel, float Default)[] entries)
    {
        Entries = entries;
    }

    public (StatTarget Target, float[] PerLevel, float Default)[] Entries { get; }
}

public interface IStatCatalog
{
    bool TryGetBaseStats(int job, out StatBaseStats stats);

    bool TryGetJobBonus(int job, out JobStatBonus bonus);
}
