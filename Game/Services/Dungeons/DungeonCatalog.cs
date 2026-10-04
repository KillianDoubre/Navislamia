using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;

namespace Navislamia.Game.Services.Dungeons;

public sealed record DungeonDefinition(int Id, int LocalFlag, int Kind, int Level, int X, int Y,
    int SiegeX, int SiegeY, int DefenceX, int DefenceY, int RaidOpen, int RaidClose,
    int SiegeOpen, int SiegeClose, int CellX, int CellY, int Boss1 = 0, int Boss2 = 0,
    int Connector = 0, int ConnectorX = 0, int ConnectorY = 0, int Core = 0, int CoreX = 0, int CoreY = 0,
    int GuildParties = 0, int MercenaryParties = 0, int RaidParties = 0);
public sealed record InstanceDefinition(int Id, int LocalFlag, int X, int Y);
public sealed record InstanceType(int DungeonId, int Type, int MinLevel, int MaxLevel, int ItemId, int ItemCount)
{
    public bool Allows(int level) => level >= MinLevel && level < MaxLevel;
}
public sealed record InstanceRespawn(int DungeonId, int Type, int Left, int Top, int Right, int Bottom,
    int MonsterId, int Count, int Period, int Controlled, int Group = 0);

/// <summary>A prop an instance poses on its layer when it is created (<c>InstanceDungeonHealingPropResource</c>).</summary>
public sealed record InstanceProp(int DungeonId, int Type, int PropId, int X, int Y, float ZOffset = 0,
    float RotateX = 0, float RotateY = 0, float RotateZ = 0, float ScaleX = 1, float ScaleY = 1, float ScaleZ = 1);

/// <summary>The <c>FieldPropResource</c> columns of the props instances pose: the skill and the script a double-click runs.</summary>
public sealed record DungeonPropTemplate(int Id, int ActivateSkillId, string Script, int MinLevel, int MaxLevel);

/// <summary><c>vulcanus_clear_reward(layer, floor)</c>: what clearing a room of a floor gives, by difficulty.</summary>
public sealed record VulcanusReward(int Difficulty, int Floor, long Exp, long Jp, long Gold);

public sealed class DungeonCatalog
{
    public Dictionary<int, DungeonDefinition> Dungeons { get; }
    public Dictionary<int, InstanceDefinition> Instances { get; }
    public InstanceType[] Types { get; }
    public InstanceRespawn[] Respawns { get; }
    public HashSet<int> Secrets { get; }
    public Dictionary<int, int> NpcDungeons { get; }
    public Dictionary<int, int[]> Exits { get; }
    public InstanceProp[] InstanceProps { get; }
    public Dictionary<int, DungeonPropTemplate> PropTemplates { get; }
    public VulcanusReward[] VulcanusRewards { get; }

    public DungeonCatalog(IOptions<DungeonOptions> options)
    {
        using var stream = typeof(DungeonCatalog).Assembly.GetManifestResourceStream("Navislamia.DungeonResources.json")
            ?? throw new InvalidOperationException("Missing embedded dungeon resources");
        var data = JsonSerializer.Deserialize<CatalogData>(stream);
        var flag = options.Value.LocalFlag;
        Dungeons = data.Dungeons.GroupBy(d => d.Id).ToDictionary(g => g.Key,
            g => g.FirstOrDefault(d => d.LocalFlag == flag)
                ?? g.FirstOrDefault(d => (d.LocalFlag & flag) != 0)
                ?? throw new InvalidOperationException($"No regional dungeon row for {g.Key}, flag {flag}"));
        Instances = data.Instances.GroupBy(d => d.Id).ToDictionary(g => g.Key,
            g => g.FirstOrDefault(d => d.LocalFlag == flag || d.LocalFlag == 0)
                ?? g.First(d => (d.LocalFlag & flag) != 0));
        Types = data.Types;
        Respawns = data.Respawns;
        Secrets = data.Secrets.ToHashSet();
        NpcDungeons = data.NpcDungeons;
        Exits = data.Exits;
        InstanceProps = data.InstanceProps ?? Array.Empty<InstanceProp>();
        PropTemplates = (data.PropTemplates ?? Array.Empty<DungeonPropTemplate>()).ToDictionary(t => t.Id);
        VulcanusRewards = data.VulcanusRewards ?? Array.Empty<VulcanusReward>();
    }

    private sealed class CatalogData
    {
        public DungeonDefinition[] Dungeons { get; set; }
        public InstanceDefinition[] Instances { get; set; }
        public InstanceType[] Types { get; set; }
        public InstanceRespawn[] Respawns { get; set; }
        public int[] Secrets { get; set; }
        public Dictionary<int, int> NpcDungeons { get; set; }
        public Dictionary<int, int[]> Exits { get; set; }
        public InstanceProp[] InstanceProps { get; set; }
        public DungeonPropTemplate[] PropTemplates { get; set; }
        public VulcanusReward[] VulcanusRewards { get; set; }
    }
}

public static class DungeonRules
{
    // Official schedules count seconds from Monday in server local time.
    public static bool IsOpen(DateTimeOffset instant, TimeZoneInfo zone, int opening, int closing)
    {
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        var seconds = ((int)local.DayOfWeek + 6) % 7 * 86400 + (int)local.TimeOfDay.TotalSeconds;
        return opening <= closing ? seconds >= opening && seconds <= closing
            : seconds >= opening || seconds <= closing;
    }

    public static int SecretForOwner(int dungeon) => dungeon switch
    {
        130300 or 130500 => 70101,
        130800 or 130900 => 120201,
        121000 => 100101,
        122000 => 90101,
        123000 => 80101,
        120700 => 110101,
        _ => 0
    };
    public static int SecretForPortal(int prop) => prop switch
    {
        120291 => 120201, 100191 => 100101, 90191 => 90101,
        80191 => 80101, 110191 => 110101, 70191 => 70101, _ => 0
    };
}
