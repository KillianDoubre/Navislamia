using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Party;
using Navislamia.Game.Services.Props;
using Serilog;

namespace Navislamia.Game.Services.Dungeons;

public interface IDungeonService
{
    ResultCode Check(GameClient client, PropAction action);
    Task<ResultCode> ExecuteAsync(GameClient client, PropAction action);
    Task SweepAsync() => Task.CompletedTask;

    /// <summary>A Vulcanus floor gate (<c>enter_other_indun</c>): the floor window, on the gate's handle.</summary>
    void ShowFloorWindow(GameClient client, uint gateHandle, PropAction action) { }
}

/// <summary>All NPC and prop entrance actions share these server-side rules.</summary>
public sealed class DungeonService : IDungeonService
{
    private readonly DungeonCatalog _catalog;
    private readonly DungeonRooms _rooms;
    private readonly IWarpService _warp;
    private readonly IPartyService _parties;
    private readonly IDungeonGuildRepository _guilds;
    private readonly Guilds.IGuildService _communities;
    private readonly ICharacterService _characters;
    private readonly MonsterWorldState _monsters;
    private readonly DungeonOptions _options;
    private readonly TimeZoneInfo _zone;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _entrances = new(1, 1);
    private readonly VulcanusScenario _vulcanus;

    public DungeonService(DungeonCatalog catalog, DungeonRooms rooms, IWarpService warp, IPartyService parties,
        IDungeonGuildRepository guilds, ICharacterService characters, IOptions<DungeonOptions> options,
        MonsterWorldState monsters = null, TimeProvider time = null, Guilds.IGuildService communities = null,
        ILevelingService leveling = null, IDynamicFieldProps props = null, IFieldPropService fieldProps = null,
        DungeonEvents events = null, Random random = null)
    {
        _vulcanus = new VulcanusScenario(catalog, rooms, monsters, props, fieldProps, leveling, random);
        events?.Attach(_vulcanus.OnMonsterKilled);
        _communities = communities;
        _catalog = catalog; _rooms = rooms; _warp = warp; _parties = parties; _guilds = guilds;
        _characters = characters; _monsters = monsters; _options = options.Value;
        _zone = TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZone);
        _time = time ?? TimeProvider.System;
    }

    public static bool Handles(PropActionKind kind) => kind is PropActionKind.EnterDungeon or PropActionKind.ExitDungeon
        or PropActionKind.EnterInstanceDungeon or PropActionKind.EnterSecretDungeon or PropActionKind.EnterOwnedSecretDungeon
        or PropActionKind.BeginDungeonRaid or PropActionKind.EnterSiegeDungeon or PropActionKind.ExitInstanceDungeon
        or PropActionKind.EnterOtherInstanceDungeon or PropActionKind.WarpInstanceFloor;

    public void ShowFloorWindow(GameClient client, uint gateHandle, PropAction action) =>
        _vulcanus.ShowFloorWindow(client, gateHandle, action);

    private InstanceType Type(GameClient client, PropAction action) => _catalog.Types.FirstOrDefault(t =>
        t.DungeonId == action.DungeonId && (action.Type < 0 || t.Type == action.Type) && t.Allows(client.ConnectionInfo.CharacterLevel));

    public ResultCode Check(GameClient client, PropAction action)
    {
        var info = client.ConnectionInfo;
        if (info.CharacterHandle == 0 || info.CharacterHp <= 0) return ResultCode.NotActable;
        if (!Handles(action.Kind)) return ResultCode.NotActable;
        if (action.Kind is PropActionKind.ExitDungeon)
            return _catalog.Exits.ContainsKey(action.DungeonId) ? ResultCode.Success : ResultCode.NotExist;
        if (action.Kind is PropActionKind.ExitInstanceDungeon) return ResultCode.Success;
        // The floor gates and their window act inside the instance the player is visiting.
        if (action.Kind is PropActionKind.EnterOtherInstanceDungeon or PropActionKind.WarpInstanceFloor)
            return _rooms.RoomOf(client) is { Key.Kind: DungeonRoomKind.Instance } room && room.Key.DungeonId == action.DungeonId
                ? ResultCode.Success : ResultCode.NotActable;
        if (_options.ClosedDungeons.Contains(action.DungeonId)) return ResultCode.AccessDenied;
        if (action.Kind is PropActionKind.EnterOwnedSecretDungeon)
            return info.GuildId > 0 ? ResultCode.Success : ResultCode.AccessDenied;
        if (action.Kind is PropActionKind.EnterInstanceDungeon)
        {
            if (info.Layer != 0 || action.Type < -1) return ResultCode.NotActable;
            if (!_catalog.Instances.ContainsKey(action.DungeonId)) return ResultCode.NotExist;
            if (Type(client, action) is null) return ResultCode.AccessDenied;
            // Vulcanus is solo: ETC_dungeon_prop.lua enter_vulcanus/partycheck.
            if (action.DungeonId == 20000 && _parties.DungeonParty(client) is not null) return ResultCode.NotActable;
            return ResultCode.Success;
        }
        if (!_catalog.Dungeons.TryGetValue(action.DungeonId, out var dungeon)) return ResultCode.NotExist;
        if (info.CharacterLevel < Math.Max(1, dungeon.Level - 40)) return ResultCode.AccessDenied;
        if (action.Kind is PropActionKind.EnterSecretDungeon && !_catalog.Secrets.Contains(action.DungeonId)) return ResultCode.NotExist;
        if (action.Kind is PropActionKind.BeginDungeonRaid or PropActionKind.EnterSiegeDungeon)
        {
            if (dungeon.Kind != 0 || info.GuildId is not > 0) return ResultCode.AccessDenied;
            if (!DungeonRules.IsOpen(_time.GetUtcNow(), _zone,
                action.Kind == PropActionKind.BeginDungeonRaid ? dungeon.RaidOpen : dungeon.SiegeOpen,
                action.Kind == PropActionKind.BeginDungeonRaid ? dungeon.RaidClose : dungeon.SiegeClose))
                return ResultCode.NotActable;
            var party = _parties.DungeonParty(client);
            if (party is null || party.Online.Any(m => Effective(m.ConnectionInfo.GuildId) != Effective(info.GuildId))) return ResultCode.AccessDenied;
            if (action.Kind == PropActionKind.BeginDungeonRaid && party.Leader != info.CharacterHandle) return ResultCode.AccessDenied;
        }
        return ResultCode.Success;
    }

    public async Task<ResultCode> ExecuteAsync(GameClient client, PropAction action)
    {
        var info = client.ConnectionInfo;
        var stamp = (info.CharacterHandle, info.CharacterName, info.X, info.Y, info.Layer, info.NpcDialogRevision, info.GuildId);
        bool Current() => stamp == (info.CharacterHandle, info.CharacterName, info.X, info.Y, info.Layer, info.NpcDialogRevision, info.GuildId);
        await _entrances.WaitAsync();
        try
        {
            if (!Current()) return ResultCode.NotActable;
            var check = Check(client, action);
            if (check != ResultCode.Success) return check;
            if (action.Kind == PropActionKind.ExitInstanceDungeon)
            {
                if (!_rooms.TryReturn(client, out var x, out var y, out var layer)) return ResultCode.NotActable;
                _warp.Warp(client, x, y, layer);
                return ResultCode.Success;
            }
            if (action.Kind == PropActionKind.WarpInstanceFloor)
            {
                return await _vulcanus.WarpFloorAsync(client, action.X,
                    (itemId, count) => ConsumeAsync(client, itemId, count), _warp);
            }
            if (action.Kind == PropActionKind.EnterOtherInstanceDungeon) return ResultCode.NotActable;
            if (action.Kind == PropActionKind.ExitDungeon)
            {
                var exit = _catalog.Exits[action.DungeonId];
                _warp.Warp(client, exit[0], exit[1], 0);
                return ResultCode.Success;
            }
            if (action.Kind == PropActionKind.EnterOwnedSecretDungeon)
            {
                var owner = await _guilds.OwnedDungeonAsync(info.GuildId.Value);
                if (!Current()) return ResultCode.NotActable;
                var secret = DungeonRules.SecretForOwner(owner);
                action = new PropAction(PropActionKind.EnterSecretDungeon, 0, 0, secret);
                check = Check(client, action);
                if (check != ResultCode.Success) return check;
            }
            if (action.Kind == PropActionKind.EnterInstanceDungeon)
                return await EnterInstance(client, action, Current);

            var dungeon = _catalog.Dungeons[action.DungeonId];
            if (action.Kind is PropActionKind.BeginDungeonRaid or PropActionKind.EnterSiegeDungeon)
            {
                var guild = Effective(info.GuildId);
                var siege = action.Kind == PropActionKind.EnterSiegeDungeon;
                if (_communities is not null)
                {
                    var authorized = await _communities.AuthorizeDungeonAsync(client, dungeon.Id, siege);
                    if (authorized != ResultCode.Success) return authorized;
                    if (!Current()) return ResultCode.NotActable;
                }
                var ownership = await _guilds.GetAsync(dungeon.Id);
                if (!Current()) return ResultCode.NotActable;
                // Membership, leadership and the clock may have changed while reading the database.
                check = Check(client, action);
                if (check != ResultCode.Success) return check;
                if (action.Kind == PropActionKind.BeginDungeonRaid && ownership.OwnerGuild == guild) return ResultCode.AccessDenied;
                if (action.Kind == PropActionKind.EnterSiegeDungeon && ownership.OwnerGuild != guild && ownership.ChallengerGuild != guild)
                    return ResultCode.AccessDenied;
                var x = siege ? ownership.OwnerGuild == guild ? dungeon.DefenceX : dungeon.SiegeX : dungeon.X;
                var y = siege ? ownership.OwnerGuild == guild ? dungeon.DefenceY : dungeon.SiegeY : dungeon.Y;
                var key = new DungeonRoomKey(siege ? DungeonRoomKind.Siege : DungeonRoomKind.Raid, dungeon.Id, siege ? 0 : guild);
                var room = _rooms.Find(key) ?? _rooms.Create(key, -1, x, y, siege && _communities is not null ? SiegeSpawns(dungeon) : RaidSpawns(dungeon));
                if (room is null) return ResultCode.NotActable;
                if (_communities is not null)
                {
                    var entered = siege ? await _communities.SiegeEnteredAsync(client, room) : await _communities.RaidStartedAsync(client, room);
                    if (!entered || !Current()) { _rooms.DiscardEmpty(room); return ResultCode.NotActable; }
                }
                if (!_rooms.Join(client, room)) return ResultCode.NotActable;
                _warp.Warp(client, x, y, room.Layer);
                return ResultCode.Success;
            }
            // Public and secret dungeons stay public. A member of a running guild raid rejoins its private layer.
            var raid = info.GuildId > 0 && _parties.DungeonParty(client) is not null
                ? _rooms.Find(new DungeonRoomKey(DungeonRoomKind.Raid, dungeon.Id, Effective(info.GuildId))) : null;
            if (raid is not null && action.Kind == PropActionKind.EnterDungeon)
            {
                if (!DungeonRules.IsOpen(_time.GetUtcNow(), _zone, dungeon.RaidOpen, dungeon.RaidClose)) return ResultCode.NotActable;
                var party = _parties.DungeonParty(client);
                if (party.Online.Any(m => Effective(m.ConnectionInfo.GuildId) != Effective(info.GuildId))) return ResultCode.AccessDenied;
                if (_communities is not null && await _communities.AuthorizeDungeonAsync(client, dungeon.Id, false, false) != ResultCode.Success) return ResultCode.AccessDenied;
                if (!Current()) return ResultCode.NotActable;
                if (!_rooms.Join(client, raid)) return ResultCode.NotActable;
            }
            _warp.Warp(client, dungeon.X, dungeon.Y, raid?.Layer ?? (byte)0);
            return ResultCode.Success;
        }
        catch (Exception error)
        {
            Log.Error(error, "Dungeon entrance failed for {Character}", info.CharacterName);
            return ResultCode.NotActable;
        }
        finally { _entrances.Release(); }
    }

    private async Task<ResultCode> EnterInstance(GameClient client, PropAction action, Func<bool> current)
    {
        var info = client.ConnectionInfo;
        var entryName = info.CharacterName;
        var party = _parties.DungeonParty(client);
        var owner = party?.Id ?? -(long)info.CharacterHandle;
        var type = Type(client, action);
        var key = new DungeonRoomKey(DungeonRoomKind.Instance, action.DungeonId, owner);
        var room = _rooms.Find(key);
        if (room is not null && room.Type != type.Type) return ResultCode.NotActable;
        if (room is not null && _rooms.IsMember(client, room)) return ResultCode.NotActable;
        var created = false;
        var paid = false;
        var entered = false;
        try
        {
            if (room is null)
            {
                if (!_rooms.HasCapacity) return ResultCode.NotActable;
                var destination = _catalog.Instances[action.DungeonId];
                var spawns = _catalog.Respawns.Where(r => r.DungeonId == action.DungeonId && r.Type == type.Type && r.Controlled == 0)
                    .Select(r => new MonsterSpawnPoint { MonsterId = r.MonsterId, Count = r.Count,
                        X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2,
                        Radius = Math.Max(0, Math.Min(r.Right - r.Left, r.Bottom - r.Top) / 2), RespawnSeconds = r.Period,
                        Group = r.Group });
                room = _rooms.Create(key, type.Type, destination.X, destination.Y, spawns);
                if (room is null) return ResultCode.NotActable;
                created = true;
                _vulcanus.OnCreate(room);
                if (type.ItemCount > 0)
                {
                    var items = await _characters.GetCarriedItemsAsync(info.CharacterName);
                    if (!current()) return ResultCode.NotActable;
                    if (Check(client, action) != ResultCode.Success || (_parties.DungeonParty(client)?.Id ?? -(long)info.CharacterHandle) != owner)
                        return ResultCode.NotActable;
                    var needed = (long)type.ItemCount;
                    var consumed = new List<CraftConsumption>();
                    foreach (var item in items.Where(i => i.ItemResourceId == type.ItemId && (int)i.WearInfo == -1).OrderBy(i => i.Id))
                    {
                        var count = Math.Min(needed, item.Amount);
                        if (count <= 0) continue;
                        consumed.Add(new CraftConsumption((uint)item.Id, count)
                        {
                            ExpectedMaterial = new MixMaterial(type.ItemId, 0, 0, 0, -1,
                                item.Level, item.Enhance, (int)item.Flag, count)
                        });
                        needed -= count;
                        if (needed == 0) break;
                    }
                    if (needed > 0) return ResultCode.AccessDenied;
                    var commit = await _characters.ApplyCraftAsync(info.CharacterName, consumed, null);
                    if (commit.Outcome != CraftCommitOutcome.Success) return ResultCode.AccessDenied;
                    paid = true;
                    if (!current() || Check(client, action) != ResultCode.Success
                        || (_parties.DungeonParty(client)?.Id ?? -(long)info.CharacterHandle) != owner)
                    {
                        return ResultCode.NotActable;
                    }
                    foreach (var item in commit.Consumed)
                        client.Connection.Send(item.Remaining == 0 ? GameCharacterPackets.BuildDestroyItem(item.Handle)
                            : GameCharacterPackets.BuildUpdateItemCount(item.Handle, item.Remaining));
                }
            }
            if (!_rooms.Join(client, room)) return ResultCode.NotActable;
            var entry = _catalog.Instances[action.DungeonId];
            _warp.Warp(client, entry.X, entry.Y, room.Layer);
            entered = true;
            return ResultCode.Success;
        }
        finally
        {
            if (!entered)
            {
                if (room is not null && _rooms.IsMember(client, room)) _rooms.OnWorldExit(client);
                if (created) _rooms.DiscardEmpty(room);
                if (paid) await _characters.AddItemAsync(entryName, type.ItemId, type.ItemCount);
            }
        }
    }

    /// <summary>
    /// <c>find_item</c> then <c>delete_item</c> for a key: <paramref name="count"/> units of <paramref name="itemId"/> from
    /// the bag, over several stacks if need be, in one save; the stack updates follow. False when the bag holds too few.
    /// </summary>
    private async Task<bool> ConsumeAsync(GameClient client, int itemId, int count)
    {
        var info = client.ConnectionInfo;
        var items = await _characters.GetCarriedItemsAsync(info.CharacterName);
        var needed = (long)count;
        var consumed = new List<CraftConsumption>();
        foreach (var item in items.Where(i => i.ItemResourceId == itemId && (int)i.WearInfo == -1).OrderBy(i => i.Id))
        {
            var take = Math.Min(needed, item.Amount);
            if (take <= 0) continue;
            consumed.Add(new CraftConsumption((uint)item.Id, take)
            {
                ExpectedMaterial = new MixMaterial(itemId, 0, 0, 0, -1, item.Level, item.Enhance, (int)item.Flag, take)
            });
            needed -= take;
            if (needed == 0) break;
        }

        if (needed > 0) return false;
        var commit = await _characters.ApplyCraftAsync(info.CharacterName, consumed, null);
        if (commit.Outcome != CraftCommitOutcome.Success) return false;
        foreach (var item in commit.Consumed)
            client.Connection.Send(item.Remaining == 0 ? GameCharacterPackets.BuildDestroyItem(item.Handle)
                : GameCharacterPackets.BuildUpdateItemCount(item.Handle, item.Remaining));
        return true;
    }

    private long Effective(long? guild) => _communities?.EffectiveGuild(guild) ?? guild ?? 0;
    private IEnumerable<MonsterSpawnPoint> SiegeSpawns(DungeonDefinition dungeon) => new[]
    {
        new MonsterSpawnPoint { MonsterId = dungeon.Connector, Count = 1, X = dungeon.ConnectorX, Y = dungeon.ConnectorY, RespawnSeconds = 0 },
        new MonsterSpawnPoint { MonsterId = dungeon.Core, Count = 1, X = dungeon.CoreX, Y = dungeon.CoreY, RespawnSeconds = 0 }
    };
    private IEnumerable<MonsterSpawnPoint> RaidSpawns(DungeonDefinition dungeon)
    {
        var spawns =
        (_monsters?.WithinRange(dungeon.CellX * 16128 + 8064, dungeon.CellY * 16128 + 8064, 11405)
            ?? Array.Empty<MonsterInstance>()).Where(m => m.Layer == 0 && (int)(m.X / 16128) == dungeon.CellX && (int)(m.Y / 16128) == dungeon.CellY)
        .Select(m => new MonsterSpawnPoint { MonsterId = m.MonsterId, Count = 1, X = (int)m.X, Y = (int)m.Y, RespawnSeconds = 0 }).ToList();
        if (_communities is not null)
            foreach (var boss in new[] { dungeon.Boss1, dungeon.Boss2 }.Where(id => id != 0).Distinct())
                if (spawns.All(p => p.MonsterId != boss)) spawns.Add(new MonsterSpawnPoint { MonsterId = boss, Count = 1, X = dungeon.X, Y = dungeon.Y, RespawnSeconds = 0 });
        return spawns;
    }

    public async Task SweepAsync()
    {
        await _entrances.WaitAsync();
        try
        {
            foreach (var (client, room) in _rooms.Visits())
            {
                var info = client.ConnectionInfo;
                var party = _parties.DungeonParty(client);
                var allowed = !room.Ended && info.Layer == room.Layer && room.Contains(info.X, info.Y)
                    && !_options.ClosedDungeons.Contains(room.Key.DungeonId);
                if (room.Key.Kind == DungeonRoomKind.Instance)
                    allowed &= (party?.Id ?? -(long)info.CharacterHandle) == room.Key.Owner;
                else
                {
                    var d = _catalog.Dungeons[room.Key.DungeonId];
                    var siege = room.Key.Kind == DungeonRoomKind.Siege;
                    allowed &= info.GuildId > 0 && party is not null
                        && party.Online.All(m => Effective(m.ConnectionInfo.GuildId) == Effective(info.GuildId))
                        && DungeonRules.IsOpen(_time.GetUtcNow(), _zone,
                            siege ? d.SiegeOpen : d.RaidOpen, siege ? d.SiegeClose : d.RaidClose);
                    if (!siege) allowed &= Effective(info.GuildId) == room.Key.Owner;
                    else if (allowed)
                    {
                        var state = await _guilds.GetAsync(d.Id);
                        allowed &= state.OwnerGuild == Effective(info.GuildId) || state.ChallengerGuild == Effective(info.GuildId);
                    }
                }
                if (!allowed && _rooms.IsMember(client, room) && _rooms.TryReturn(client, out var x, out var y, out var layer))
                    _warp.Warp(client, x, y, layer);
            }
        }
        finally { _entrances.Release(); }
    }
}
