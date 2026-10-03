using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Dungeons;

public enum DungeonRoomKind { Instance, Raid, Siege }
public sealed record DungeonRoomKey(DungeonRoomKind Kind, int DungeonId, long Owner);
public sealed class DungeonRoom
{
    public DungeonRoomKey Key { get; init; }
    public int Type { get; init; }
    public byte Layer { get; init; }
    public int CellX { get; init; }
    public int CellY { get; init; }
    public bool KeepAlive { get; set; }
    public bool Ended { get; set; }
    public HashSet<GameClient> Members { get; } = new();
    public IReadOnlyList<long> Monsters { get; set; } = Array.Empty<long>();
    public bool Contains(float x, float y) => (int)(x / 16128) == CellX && (int)(y / 16128) == CellY;
}

/// <summary>Shared with WarpService so every departure releases a lease, including resurrection and teleports.</summary>
public sealed class DungeonRooms
{
    private readonly object _gate = new();
    private readonly Dictionary<DungeonRoomKey, DungeonRoom> _rooms = new();
    private readonly Dictionary<GameClient, (DungeonRoom Room, float X, float Y, byte Layer)> _visits = new();
    private readonly MonsterWorldState _monsters;
    private readonly IGroundItemService _items;
    public DungeonRooms(MonsterWorldState monsters = null, IGroundItemService items = null)
    { _monsters = monsters; _items = items; }

    public DungeonRoom Find(DungeonRoomKey key)
    {
        lock (_gate) return _rooms.GetValueOrDefault(key);
    }

    public DungeonRoom Create(DungeonRoomKey key, int type, int x, int y, IEnumerable<MonsterSpawnPoint> spawns)
    {
        lock (_gate)
        {
            if (_rooms.TryGetValue(key, out var existing)) return existing.Type == type ? existing : null;
            byte layer = 1;
            if (key.Kind != DungeonRoomKind.Siege)
            {
                var used = _rooms.Values.Select(r => r.Layer).ToHashSet();
                var available = Enumerable.Range(2, 126).FirstOrDefault(i => !used.Contains((byte)i));
                if (available == 0) return null;
                layer = (byte)available;
            }
            var room = new DungeonRoom { Key = key, Type = type, Layer = layer, CellX = x / 16128, CellY = y / 16128 };
            room.Monsters = _monsters?.SpawnDungeonMonsters(spawns.Select(p => new MonsterSpawnPoint
            {
                MonsterId = p.MonsterId, Count = p.Count, X = p.X, Y = p.Y, Radius = p.Radius,
                Layer = layer, IsDungeonRaidMonster = key.Kind == DungeonRoomKind.Raid, RespawnSeconds = p.RespawnSeconds
            })) ?? Array.Empty<long>();
            _rooms.Add(key, room);
            return room;
        }
    }

    public bool HasCapacity
    {
        get { lock (_gate) return _rooms.Values.Count(r => r.Layer != 1) < 126; }
    }

    public IReadOnlyList<(GameClient Client, DungeonRoom Room)> Visits()
    {
        lock (_gate) return _visits.Select(v => (v.Key, v.Value.Room)).ToArray();
    }

    public void DiscardEmpty(DungeonRoom room)
    {
        lock (_gate)
        {
            if (room.Members.Count != 0 || !_rooms.TryGetValue(room.Key, out var current) || !ReferenceEquals(current, room)) return;
            _rooms.Remove(room.Key);
            _monsters?.RemoveDungeonLayer(room.Layer, room.CellX, room.CellY);
            _items?.RemoveDungeonItems(room.Layer, room.CellX, room.CellY);
        }
    }

    public bool IsMember(GameClient client, DungeonRoom room)
    {
        lock (_gate) return _visits.TryGetValue(client, out var v) && ReferenceEquals(v.Room, room);
    }

    public bool Join(GameClient client, DungeonRoom room)
    {
        lock (_gate)
        {
            if (room.Ended || !_rooms.TryGetValue(room.Key, out var current) || !ReferenceEquals(current, room)) return false;
            if (IsMember(client, room)) return true;
            var info = client.ConnectionInfo;
            var origin = _visits.TryGetValue(client, out var v) ? (v.X, v.Y, v.Layer) : (info.X, info.Y, info.Layer);
            Release(client);
            room.Members.Add(client);
            _visits[client] = (room, origin.Item1, origin.Item2, origin.Item3);
            return true;
        }
    }

    public byte OnWarp(GameClient client, float x, float y, byte layer)
    {
        lock (_gate)
        {
            if (!_visits.TryGetValue(client, out var visit)) return layer;
            if (visit.Room.Layer == layer && visit.Room.Contains(x, y)) return layer;
            Release(client);
            return 0;
        }
    }

    public bool TryReturn(GameClient client, out float x, out float y, out byte layer)
    {
        lock (_gate)
        {
            if (_visits.TryGetValue(client, out var visit))
            {
                x = visit.X; y = visit.Y; layer = visit.Layer;
                Release(client);
                return true;
            }
            x = y = 0; layer = 0;
            return false;
        }
    }

    public void OnWorldExit(GameClient client)
    {
        if (!TryReturn(client, out var x, out var y, out var layer)) return;
        var info = client.ConnectionInfo;
        info.X = info.DestinationX = x;
        info.Y = info.DestinationY = y;
        info.Layer = layer;
    }

    private void Release(GameClient client)
    {
        if (!_visits.Remove(client, out var visit)) return;
        visit.Room.Members.Remove(client);
        if (visit.Room.Members.Count != 0 || visit.Room.KeepAlive && !visit.Room.Ended) return;
        _rooms.Remove(visit.Room.Key);
        _monsters?.RemoveDungeonLayer(visit.Room.Layer, visit.Room.CellX, visit.Room.CellY);
        _items?.RemoveDungeonItems(visit.Room.Layer, visit.Room.CellX, visit.Room.CellY);
    }

    public void Finish(DungeonRoomKey key)
    {
        lock (_gate)
        {
            if (!_rooms.TryGetValue(key, out var room)) return;
            room.Ended = true; room.KeepAlive = false;
            _monsters?.RemoveDungeonLayer(room.Layer, room.CellX, room.CellY);
            _items?.RemoveDungeonItems(room.Layer, room.CellX, room.CellY);
            if (room.Members.Count == 0) _rooms.Remove(key);
        }
    }
}
