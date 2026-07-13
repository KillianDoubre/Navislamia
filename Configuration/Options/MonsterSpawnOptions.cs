using System.Collections.Generic;

namespace Navislamia.Configuration.Options;

public class MonsterSpawnOptions
{
    public List<MonsterSpawnPoint> Spawns { get; set; } = new();
    public List<MonsterSpawnArea> Areas { get; set; } = new();
}

public class MonsterSpawnPoint
{
    public int MonsterId { get; set; }
    public int? ResourceId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Count { get; set; }
    public int Radius { get; set; }
}

public class MonsterSpawnArea
{
    public string Map { get; set; }
    public int SpawnGroupId { get; set; }
    public int Left { get; set; }
    public int Top { get; set; }
    public int Right { get; set; }
    public int Bottom { get; set; }
    public List<MonsterSpawnPopulation> Monsters { get; set; } = new();
}

public class MonsterSpawnPopulation
{
    public int ResourceId { get; set; }
    public int Count { get; set; }
}
