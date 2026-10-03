using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services.Dungeons;

namespace Navislamia.Game.Services.Guilds;

public sealed record GuildSiegeSide(int DungeonId, long? Defender, long Attacker, long? CurrentOwner,
    DungeonDefinition Definition, DateTimeOffset EndsAt);

/// <summary>Combat reads a snapshot; it never takes the guild database gate or depends on WarpService.</summary>
public sealed class GuildRuntime
{
    private readonly ConcurrentDictionary<long, long> _effective = new();
    private readonly ConcurrentDictionary<int, GuildSiegeSide> _sieges = new();
    private readonly ConcurrentDictionary<long, (int Dungeon, bool Core)> _objectives = new();
    private readonly TimeProvider _time;
    public GuildRuntime(TimeProvider time = null) => _time = time ?? TimeProvider.System;
    public long Effective(long? guild) => guild is > 0 ? _effective.GetValueOrDefault(guild.Value, guild.Value) : 0;
    public void SetGuild(long guild, long effective) => _effective[guild] = effective;
    public void SetSiege(GuildSiegeSide siege) => _sieges[siege.DungeonId] = siege;
    public void RemoveSiege(int dungeon)
    {
        _sieges.TryRemove(dungeon, out _);
        foreach (var item in _objectives) if (item.Value.Dungeon == dungeon) _objectives.TryRemove(item.Key, out _);
    }
    public void SetObjective(long instance, int dungeon, bool core) => _objectives[instance] = (dungeon, core);
    public bool IsObjective(long instance) => _objectives.ContainsKey(instance);
    public bool TryObjective(long instance, out int dungeon, out bool core)
    {
        if (_objectives.TryGetValue(instance, out var value)) { dungeon = value.Dungeon; core = value.Core; return true; }
        dungeon = 0; core = false; return false;
    }
    public GuildSiegeSide Siege(ConnectionInfo info)
    {
        if (info.Layer != 1) return null;
        foreach (var siege in _sieges.Values)
            if (_time.GetUtcNow() <= siege.EndsAt && siege.Definition.CellX == (int)(info.X / 16128)
                && siege.Definition.CellY == (int)(info.Y / 16128)) return siege;
        return null;
    }
    public int Side(ConnectionInfo info, GuildSiegeSide siege)
    {
        var guild = Effective(info.GuildId);
        return guild == siege.Attacker ? 1 : guild > 0 && guild == siege.Defender ? 2 : 0;
    }
    public bool WarEnemy(ConnectionInfo first, ConnectionInfo second)
    {
        var siege = Siege(first);
        return siege is not null && second.Layer == first.Layer
            && siege.Definition.CellX == (int)(second.X / 16128) && siege.Definition.CellY == (int)(second.Y / 16128)
            && Side(first, siege) != 0 && Side(second, siege) != 0 && Side(first, siege) != Side(second, siege);
    }
    public bool CanDamage(GameClient player, long instance)
    {
        if (!TryObjective(instance, out var dungeon, out var core)) return true;
        var siege = Siege(player.ConnectionInfo);
        if (siege is null || siege.DungeonId != dungeon || Side(player.ConnectionInfo, siege) == 0) return false;
        var guild = Effective(player.ConnectionInfo.GuildId);
        return core ? guild != siege.CurrentOwner : guild == siege.CurrentOwner;
    }
}

/// <summary>The kill relay breaks the combat/warp/dungeon dependency cycle.</summary>
public sealed class GuildCombatEvents
{
    public Func<GameClient, MonsterInstance, long, MonsterKillReward, Task<MonsterKillReward>> Killed { get; set; }
    public Task<MonsterKillReward> OnKilledAsync(GameClient player, MonsterInstance monster, long instance, MonsterKillReward reward) =>
        Killed?.Invoke(player, monster, instance, reward) ?? Task.FromResult(reward);
}
