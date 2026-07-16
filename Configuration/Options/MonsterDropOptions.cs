using System.Collections.Generic;

namespace Navislamia.Configuration.Options;

public class MonsterDropEntryOptions
{
    public int ItemId { get; set; }
    public double Chance { get; set; }
    public int MinCount { get; set; }
    public int MaxCount { get; set; }
}

public class MonsterDropOptions
{
    public Dictionary<int, List<MonsterDropEntryOptions>> Tables { get; set; } = new();
    public Dictionary<int, int> Monsters { get; set; } = new();
}
