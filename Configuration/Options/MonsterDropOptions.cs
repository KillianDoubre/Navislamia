using System.Collections.Generic;

namespace Navislamia.Configuration.Options;

public class MonsterDropEntryOptions
{
    public int ItemId { get; set; }
    public double Chance { get; set; }
    public int MinCount { get; set; }
    public int MaxCount { get; set; }
}

public class MonsterDropGroupEntryOptions
{
    public int ItemId { get; set; }
    public double Weight { get; set; }
    public int MinCount { get; set; }
    public int MaxCount { get; set; }
}

public class MonsterDropOptions
{
    public Dictionary<int, List<MonsterDropEntryOptions>> Tables { get; set; } = new();

    /// <summary>Group id (negative) -> weighted "pick one" items. A negative table entry references one.</summary>
    public Dictionary<int, List<MonsterDropGroupEntryOptions>> Groups { get; set; } = new();

    public Dictionary<int, int> Monsters { get; set; } = new();
}
