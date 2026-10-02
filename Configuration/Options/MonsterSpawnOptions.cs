using System.Collections.Generic;

namespace Navislamia.Configuration.Options;

public class MonsterSpawnOptions
{
    /// <summary>Official game.change_monster_drop_set: select Exp2/Jp2 and the alternate money/chaos bounds.</summary>
    public bool UseSecondaryRewards { get; set; }
    public List<MonsterSpawnPoint> Spawns { get; set; } = new();
    public List<MonsterSpawnArea> Areas { get; set; } = new();
}

public class MonsterSpawnPoint
{
    public byte Layer { get; set; }
    public bool IsDungeonRaidMonster { get; set; }
    public int MonsterId { get; set; }
    public int? ResourceId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Count { get; set; }
    public int Radius { get; set; }
}

public class MonsterSpawnArea
{
    public byte Layer { get; set; }
    public bool IsDungeonRaidMonster { get; set; }
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
