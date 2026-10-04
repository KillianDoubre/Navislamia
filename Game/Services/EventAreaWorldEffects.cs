using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Props;
using Serilog;

namespace Navislamia.Game.Services;

public interface IEventAreaWorldEffects
{
    void SpawnMonsters(int code, int count, float x, float y, byte layer, uint lifetimeTicks);
    void SpawnProp(int code, uint seconds, float x, float y, byte layer);
}

/// <summary>World effects of server-owned area scripts, published after their character changes commit.</summary>
public sealed class EventAreaWorldEffects : IEventAreaWorldEffects, IDisposable
{
    private readonly MonsterWorldState _world;
    private readonly IMonsterSpawnService _monsters;
    private readonly IPlayerVisibilityService _players;
    private readonly IDynamicFieldProps _props;
    private readonly IFieldPropCatalog _catalog;
    private readonly IFieldPropService _stream;
    private readonly Dictionary<long,uint> _expires = new();
    private readonly object _gate = new();
    private readonly Timer _timer;
    public EventAreaWorldEffects(MonsterWorldState world, IMonsterSpawnService monsters, IPlayerVisibilityService players,
        IDynamicFieldProps props, IFieldPropCatalog catalog, IFieldPropService stream)
    {
        _world = world; _monsters = monsters; _players = players; _props = props; _catalog = catalog; _stream = stream;
        _timer = new Timer(_ => Tick(), null, 1000, 1000);
    }
    public void SpawnMonsters(int code, int count, float x, float y, byte layer, uint lifetimeTicks)
    {
        if (count is < 1 or > 100 || !float.IsFinite(x) || !float.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(count));
        for (var i = 0; i < count; i++)
            if (_world.SpawnInstanceMonster(code, x, y, layer, true) is { } monster && lifetimeTicks > 0)
                lock (_gate) _expires[monster.InstanceId] = unchecked(ServerClock.Now + lifetimeTicks);
        foreach (var client in _players.Registry.Clients) _monsters.Sync(client);
    }
    public void SpawnProp(int code, uint seconds, float x, float y, byte layer)
    {
        if (!_catalog.TryGetTemplate(code, out var template)) return;
        _props.Add(code, x, y, layer, template, expiresAt: seconds == 0 ? 0 : unchecked(ServerClock.Now + checked(seconds * 100)));
        foreach (var client in _players.Registry.Clients) _stream.Sync(client);
    }
    private void Tick()
    {
        try
        {
            var now = ServerClock.Now; long[] ids;
            lock (_gate)
            {
                ids = _expires.Where(e => unchecked((int)(now - e.Value)) >= 0).Select(e => e.Key).ToArray();
                foreach (var id in ids) _expires.Remove(id);
            }
            if (ids.Length > 0)
            { _world.RemoveInstanceMonsters(ids); foreach (var client in _players.Registry.Clients) _monsters.Sync(client); }
            if (_props.RemoveExpired(now).Count > 0)
                foreach (var client in _players.Registry.Clients) _stream.Sync(client);
        }
        catch (Exception ex) { Log.Error(ex, "Could not expire event area world effects"); }
    }
    public void Dispose() => _timer.Dispose();
}
