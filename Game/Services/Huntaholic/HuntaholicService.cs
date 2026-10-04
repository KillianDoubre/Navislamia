using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Party;
using Serilog;

namespace Navislamia.Game.Services.Huntaholic;

public interface IHuntaholicService
{
    void InstanceList(GameClient client, int page);
    void CreateInstance(GameClient client, string name, int maxMember, string password);
    void JoinInstance(GameClient client, int instanceNo, string password);
    void LeaveInstance(GameClient client);
    void LeaveLobby(GameClient client);
    void BeginHunting(GameClient client);
    void InstanceGameEnter(GameClient client, int instanceGameType);
    void InstanceGameExit(GameClient client);

    /// <summary>The 4253 answer's <c>bearroad_ranking</c>: the player's rank by HuntaHolic points among the online players.</summary>
    uint Ranking(GameClient client) => 0;

    /// <summary>
    /// <c>DB_Login</c>: a character saved in a hunt comes back in the lobby, on its level's lobby layer; one whose level
    /// no longer has a lobby goes to its return point (<paramref name="townX"/>, <paramref name="townY"/>).
    /// </summary>
    (float X, float Y, byte Layer) PlaceAtLogin(int level, float x, float y, byte layer,
        float townX = HuntaholicDefaults.TownX, float townY = HuntaholicDefaults.TownY) => (x, y, layer);

    /// <summary><c>StructPlayer::onLogout</c>: a hunt is quit (retired), a lobby room left.</summary>
    void OnWorldExit(GameClient client);

    bool HandlesDialog(string function);
    Task SelectDialogAsync(GameClient client, string function, string trigger);
}

/// <summary>
/// HuntaHolic (Bear Road), the official <c>HuntaholicManager</c> and its <c>GameMessage</c> handlers
/// (docs/packet-specs/socle-huntaholic.md): the lobby and its rooms, the hunt on a layer of its own, the score,
/// the rewards in points, experience and items, the daily entries, and the way in and out (4250/4251, the NPC).
/// </summary>
public sealed class HuntaholicService : IHuntaholicService, IHuntaholicEventListener
{
    /// <summary><c>CHAT_HUNTAHOLIC_SYSTEM</c>, the type of the end notice line.</summary>
    public const byte HuntaholicChatType = 160;

    public const int WarpSkill = 64818;
    public const int ExitSkill = 64827;

    private readonly ILogger _logger = Log.ForContext<HuntaholicService>();
    private readonly IHuntaholicCatalog _catalog;
    private readonly IPartyService _parties;
    private readonly IWarpService _warp;
    private readonly MonsterWorldState _monsters;
    private readonly IMonsterSpawnService _monsterSpawn;
    private readonly ISkillCastService _casts;
    private readonly ICharacterService _characters;
    private readonly ILevelingService _leveling;
    private readonly IPlayerVisibilityService _players;
    private readonly Func<uint> _clock;
    private readonly Func<DateTime> _localNow;
    private readonly Random _random;
    private readonly object _lock = new();
    private readonly List<Room> _rooms = new();
    private readonly Dictionary<long, Room> _roomOfMonster = new();

    public HuntaholicService(IHuntaholicCatalog catalog, IPartyService parties, IWarpService warp,
        MonsterWorldState monsters, IMonsterSpawnService monsterSpawn, ISkillCastService casts,
        ICharacterService characters, IPlayerVisibilityService players, HuntaholicEvents events = null,
        ILevelingService leveling = null, Func<uint> clock = null, Func<DateTime> localNow = null,
        Random random = null, bool runTicks = true)
    {
        _catalog = catalog;
        _parties = parties;
        _warp = warp;
        _monsters = monsters;
        _monsterSpawn = monsterSpawn;
        _casts = casts;
        _characters = characters;
        _players = players;
        _leveling = leveling;
        _clock = clock ?? (() => ServerClock.Now);
        _localNow = localNow ?? (() => DateTime.Now);
        _random = random ?? Random.Shared;
        events?.Attach(this);
        if (runTicks && _catalog.All.Count > 0)
        {
            _ = RunAsync();
        }
    }

    // ---- 4000-4011 ------------------------------------------------------------------------------------------

    /// <summary><c>onHuntaholicInstanceList</c>: the rooms of the player's level that have not pressed start.</summary>
    public void InstanceList(GameClient client, int page)
    {
        const ushort id = (ushort)GamePackets.TM_CS_HUNTAHOLIC_INSTANCE_LIST;
        var info = client.ConnectionInfo;
        var huntaholicId = _catalog.GetHuntaholicId(info.X, info.Y);
        if (huntaholicId == 0)
        {
            client.SendResult(id, (ushort)ResultCode.AccessDenied);
            return;
        }

        if (page < 0)
        {
            client.SendResult(id, (ushort)ResultCode.InvalidArgument);
            return;
        }

        lock (_lock)
        {
            // GetUsedInstanceCount counts every room of the level, started or not; GetInstanceList skips the started.
            var ofLevel = _rooms.Where(r => r.Base.Id == huntaholicId && HuntaholicRules.IsProperLevel(r.Tier, info.CharacterLevel)).ToList();
            var totalPage = HuntaholicRules.TotalPages(ofLevel.Count);
            if (totalPage != 0 && page > totalPage)
            {
                client.SendResult(id, (ushort)ResultCode.InvalidArgument);
                return;
            }

            var infos = totalPage == 0 || ofLevel.Count <= HuntaholicRules.InstancesPerPage * (page - 1)
                ? new List<HuntaholicInstanceInfo>()
                : ofLevel.Where(r => r.BeginTime == 0)
                    .Skip(HuntaholicRules.InstancesPerPage * Math.Max(0, page - 1))
                    .Take(HuntaholicRules.InstancesPerPage).Select(Info).ToList();
            client.Connection.Send(GameHuntaholicServerPackets.BuildInstanceList(huntaholicId, page, totalPage, infos));
        }
    }

    /// <summary><c>onHuntaholicCreateInstance</c> then <c>CreateInstanceDungeon</c>, in their order of refusals.</summary>
    public void CreateInstance(GameClient client, string name, int maxMember, string password)
    {
        const ushort id = (ushort)GamePackets.TM_CS_HUNTAHOLIC_CREATE_INSTANCE;
        var info = client.ConnectionInfo;
        var huntaholicId = _catalog.GetHuntaholicId(info.X, info.Y);
        if (huntaholicId == 0)
        {
            client.SendResult(id, (ushort)ResultCode.AccessDenied);
            return;
        }

        client.SendResult(id, (ushort)Create(client, huntaholicId, name, maxMember, password), huntaholicId);
    }

    private ResultCode Create(GameClient client, int huntaholicId, string name, int maxMember, string password)
    {
        var info = client.ConnectionInfo;
        if (info.HuntaholicEnterCount < 1) return ResultCode.NotEnoughBullet;
        if (!_catalog.IsLobby(info.X, info.Y)) return ResultCode.AccessDenied;
        if (_parties.PartyIdOf(client) != 0) return ResultCode.NotActable;
        if (!HuntaholicRules.IsValidName(name)) return ResultCode.InvalidText;
        if (!HuntaholicRules.IsValidMaxMember(maxMember)) return ResultCode.InvalidArgument;
        if (!_catalog.TryGet(huntaholicId, out var huntaholic)) return ResultCode.NotExist;

        lock (_lock)
        {
            if (_rooms.Count(r => r.Base.Id == huntaholicId) >= HuntaholicRules.MaxInstanceCount) return ResultCode.LimitMax;
            var tier = HuntaholicRules.ProperTier(huntaholic, info.CharacterLevel);
            if (tier is null) return ResultCode.NotActable;

            var partyId = _parties.CreateHuntaholicParty(client, name);
            if (partyId == 0) return ResultCode.AlreadyExist;

            client.SendTimeSync();
            var room = new Room(huntaholic, tier, NewInstanceNo(huntaholicId), partyId, (byte)maxMember,
                password ?? string.Empty);
            _rooms.Add(room);
            BroadcastInfo(room);
            _logger.Debug("{clientTag} created HuntaHolic room {no} ({name}) for tier {tier}", client.ClientTag,
                room.InstanceNo, name, tier.Id);
            return ResultCode.Success;
        }
    }

    /// <summary><c>onHuntaholicJoinInstance</c> then <c>JoinInstanceDungeon</c>/<c>joinInstanceDungeon</c>.</summary>
    public void JoinInstance(GameClient client, int instanceNo, string password)
    {
        const ushort id = (ushort)GamePackets.TM_CS_HUNTAHOLIC_JOIN_INSTANCE;
        var info = client.ConnectionInfo;
        var huntaholicId = _catalog.GetHuntaholicId(info.X, info.Y);
        if (huntaholicId == 0)
        {
            client.SendResult(id, (ushort)ResultCode.AccessDenied);
            return;
        }

        client.SendResult(id, (ushort)Join(client, huntaholicId, instanceNo, password ?? string.Empty), huntaholicId);
    }

    private ResultCode Join(GameClient client, int huntaholicId, int instanceNo, string password)
    {
        var info = client.ConnectionInfo;
        if (info.HuntaholicEnterCount < 1) return ResultCode.NotEnoughBullet;
        if (!_catalog.IsLobby(info.X, info.Y)) return ResultCode.AccessDenied;
        if (_parties.PartyIdOf(client) != 0) return ResultCode.AlreadyExist;

        lock (_lock)
        {
            var room = _rooms.FirstOrDefault(r => r.Base.Id == huntaholicId && r.InstanceNo == instanceNo);
            if (room is null) return ResultCode.NotExist;
            if (room.BeginTime != 0) return ResultCode.CoolTime;
            if (_parties.PartyMemberCount(room.PartyId) >= room.MaxMembers) return ResultCode.LimitMax;
            if (!HuntaholicRules.IsProperLevel(room.Tier, info.CharacterLevel)) return ResultCode.LimitTarget;
            if (room.Password.Length > 0 && !string.Equals(room.Password, password, StringComparison.Ordinal))
                return ResultCode.InvalidPassword;
            if (!_parties.JoinHuntaholicParty(room.PartyId, client)) return ResultCode.Unknown;

            BroadcastInfo(room);
            client.SendTimeSync();
            return ResultCode.Success;
        }
    }

    /// <summary><c>onHuntaholicLeaveInstance</c>: leave the room in the lobby, quit the hunt in the dungeon.</summary>
    public void LeaveInstance(GameClient client)
    {
        const ushort id = (ushort)GamePackets.TM_CS_HUNTAHOLIC_LEAVE_INSTANCE;
        var info = client.ConnectionInfo;
        var huntaholicId = _catalog.GetHuntaholicId(info.X, info.Y);
        if (huntaholicId == 0)
        {
            client.SendResult(id, (ushort)ResultCode.AccessDenied);
            return;
        }

        var result = _catalog.IsLobby(info.X, info.Y)
            ? LeaveRoom(client)
            : QuitHunting(client, true, HuntingResult.Unknown, warpToLobby: true);
        client.SendResult(id, (ushort)result, huntaholicId);
    }

    /// <summary><c>LeaveInstanceDungeon</c>: a started room cannot be left (CoolTime); the last one out deletes it.</summary>
    private ResultCode LeaveRoom(GameClient client)
    {
        var partyId = _parties.PartyIdOf(client);
        if (partyId == 0) return ResultCode.NotExist;
        lock (_lock)
        {
            var room = _rooms.FirstOrDefault(r => r.PartyId == partyId);
            if (room is null) return ResultCode.NotExist;
            if (room.Begin) return ResultCode.CoolTime;
            _parties.LeaveHuntaholicParty(client);
            if (_parties.PartyMemberCount(room.PartyId) == 0)
            {
                _rooms.Remove(room);
            }
            else
            {
                BroadcastInfo(room);
            }

            return ResultCode.Success;
        }
    }

    /// <summary><c>onHuntaholicLeaveLobby</c>: out of the lobby, to where the player came from.</summary>
    public void LeaveLobby(GameClient client)
    {
        const ushort id = (ushort)GamePackets.TM_CS_HUNTAHOLIC_LEAVE_LOBBY;
        var info = client.ConnectionInfo;
        if (!_catalog.IsLobby(info.X, info.Y))
        {
            client.SendResult(id, (ushort)ResultCode.AccessDenied);
            return;
        }

        if (_parties.PartyIdOf(client) != 0)
        {
            client.SendResult(id, (ushort)ResultCode.NotActable);
            return;
        }

        WarpOut(client);
        client.SendResult(id, (ushort)ResultCode.Success);
    }

    /// <summary>
    /// <c>onHuntaholicBeginHunting</c> then <c>BeginHunting</c>: the leader, a room not started, every member in the
    /// lobby with an entry left; each member pays an entry and gets the 10 s countdown (4012). The official handler
    /// sends no result for the refusals of <c>BeginHunting</c> itself, only for the two checks before it.
    /// </summary>
    public void BeginHunting(GameClient client)
    {
        const ushort id = (ushort)GamePackets.TM_CS_HUNTAHOLIC_BEGIN_HUNTING;
        var info = client.ConnectionInfo;
        if (_catalog.GetHuntaholicId(info.X, info.Y) == 0)
        {
            client.SendResult(id, (ushort)ResultCode.AccessDenied);
            return;
        }

        if (info.HuntaholicEnterCount < 1)
        {
            client.SendResult(id, (ushort)ResultCode.NotEnoughBullet);
            return;
        }

        if (!_catalog.IsLobby(info.X, info.Y)) return;
        var partyId = _parties.PartyIdOf(client);
        if (partyId == 0) return;

        lock (_lock)
        {
            var room = _rooms.FirstOrDefault(r => r.PartyId == partyId);
            if (room is null || !_parties.IsPartyLeader(partyId, client) || room.BeginTime != 0) return;

            var members = _parties.OnlineMembers(partyId);
            if (members.Count != _parties.PartyMemberCount(partyId)
                || members.Any(m => !_catalog.IsLobby(m.ConnectionInfo.X, m.ConnectionInfo.Y)
                                    || m.ConnectionInfo.HuntaholicEnterCount < 1))
            {
                return;
            }

            foreach (var member in members)
            {
                SendEnterCount(member, member.ConnectionInfo.AddHuntaholicEnterCount(-1));
                member.SendTimeSync();
                member.Connection.Send(GameHuntaholicServerPackets.BuildBeginCountdown());
            }

            room.BeginTime = Math.Max(1u, unchecked(_clock() + HuntaholicRules.BeginCountdownTicks));
        }
    }

    // ---- 4250 / 4251 and the NPC -----------------------------------------------------------------------------

    /// <summary>
    /// <c>onInstanceGameEnter</c>: type 0 (<c>HUNTAHOLIC_BEAR_ROAD</c>) leaves the party and casts the lobby warp
    /// (64818, 8 s); the deathmatch types 1/2 do not exist here. The checks before the cast are the official ones.
    /// </summary>
    public void InstanceGameEnter(GameClient client, int instanceGameType)
    {
        const ushort id = (ushort)GamePackets.TM_CS_INSTANCE_GAME_ENTER;
        var info = client.ConnectionInfo;
        ResultCode result;
        if (info.CharacterHandle == 0) result = ResultCode.NotExist;
        else if (_catalog.GetHuntaholicId(info.X, info.Y) != 0) result = ResultCode.NotActableInHuntaholic;
        else if (instanceGameType != 0) result = ResultCode.InvalidArgument;
        else
        {
            _parties.LeaveForInstanceGame(client);
            result = _parties.PartyIdOf(client) != 0 ? ResultCode.Unknown
                : _casts.CastInstanceGameSkill(client, WarpSkill) == ResultCode.Success ? ResultCode.Success
                : ResultCode.AccessDenied;
        }

        client.SendResult(id, (ushort)result);
    }

    /// <summary><c>onInstanceGameExit</c>: the exit spell (64827, 8 s), which takes the player back out.</summary>
    public void InstanceGameExit(GameClient client)
    {
        const ushort id = (ushort)GamePackets.TM_CS_INSTANCE_GAME_EXIT;
        var result = client.ConnectionInfo.CharacterHandle == 0 ? ResultCode.NotExist
            : _casts.CastInstanceGameSkill(client, ExitSkill) == ResultCode.Success ? ResultCode.Success
            : ResultCode.AccessDenied;
        client.SendResult(id, (ushort)result);
    }

    public ResultCode OnCheckInstanceSkill(GameClient client, int skillId) =>
        skillId == WarpSkill ? CheckLobbyEnterable(client, HuntaholicRules.BearRoadId)
        : skillId == ExitSkill ? ResultCode.Success
        : ResultCode.NotActable;

    public ResultCode OnFireInstanceSkill(GameClient client, int skillId)
    {
        if (skillId == ExitSkill)
        {
            WarpOut(client);
            return ResultCode.Success;
        }

        if (skillId != WarpSkill) return ResultCode.NotActable;
        var result = CheckLobbyEnterable(client, HuntaholicRules.BearRoadId);
        if (result == ResultCode.Success) WarpToLobby(client, HuntaholicRules.BearRoadId);
        return result;
    }

    /// <summary><c>StructSkill::IsHuntaholicLobbyEnterableOwner</c>.</summary>
    private ResultCode CheckLobbyEnterable(GameClient client, int huntaholicId)
    {
        var info = client.ConnectionInfo;
        if (_catalog.GetHuntaholicId(info.X, info.Y) != 0) return ResultCode.NotActableInHuntaholic;
        if (info.PkMode || info.TurnOnPkAt != 0) return ResultCode.PKLimit;
        if (!_catalog.TryGet(huntaholicId, out var huntaholic) || huntaholic.LobbyX == 0 || huntaholic.LobbyY == 0)
            return ResultCode.NotExist;
        if (HuntaholicRules.ProperLobbyLayer(huntaholic, info.CharacterLevel) == HuntaholicRules.UnusableLobbyLayer)
            return ResultCode.NotEnoughLevel;
        return ResultCode.Success;
    }

    /// <summary>
    /// <c>PendWarpToHuntaholicLobby</c>: the HP and MP are kept (<c>StoreCurrentStatesOnEnterInstanceGame(true)</c>),
    /// then the lobby on the tier's layer. The entry position it keeps too (<c>hx</c>/<c>hy</c>) is never read for
    /// HuntaHolic: leaving it always goes to the return point (<see cref="WarpOut"/>).
    /// </summary>
    private void WarpToLobby(GameClient client, int huntaholicId)
    {
        var info = client.ConnectionInfo;
        if (!_catalog.TryGet(huntaholicId, out var huntaholic)) return;
        info.HuntaholicEnterHp = info.CharacterHp;
        info.HuntaholicEnterMp = info.CharacterMp;
        _warp.Warp(client, huntaholic.LobbyX, huntaholic.LobbyY,
            HuntaholicRules.ProperLobbyLayer(huntaholic, info.CharacterLevel));
    }

    /// <summary>
    /// <c>GetPositionOnEnterInstanceGame</c>: leaving HuntaHolic goes to <c>GetLastTownPosition</c>, the return point
    /// (<c>rx</c>/<c>ry</c>, docs/packet-specs/socle-point-de-retour.md), on layer 0.
    /// </summary>
    private void WarpOut(GameClient client)
    {
        var info = client.ConnectionInfo;
        var (x, y) = info.RespawnX > 0 && info.RespawnY > 0
            ? (info.RespawnX, info.RespawnY)
            : (HuntaholicDefaults.TownX, HuntaholicDefaults.TownY);
        _warp.Warp(client, x, y, 0);
    }

    public bool HandlesDialog(string function) => function is "go_to_huntaholic" or "hunterholic_jpbox_sell";

    /// <summary>
    /// <c>NPC_huntaholic.lua</c>: <c>go_to_huntaholic</c> (1 000 gold, <c>warp_to_huntaholic_lobby</c>, a page per
    /// refusal) and <c>hunterholic_jpbox_sell(grade)</c> (20 000/90 000/200 000 JP for the boxes 1100703/02/01).
    /// </summary>
    public async Task SelectDialogAsync(GameClient client, string function, string trigger)
    {
        var info = client.ConnectionInfo;
        if (function == "go_to_huntaholic")
        {
            if (info.CharacterGold < HuntaholicRules.LobbyWarpFee)
            {
                ShowPage(client, "@90996942");
                return;
            }

            var code = LobbyWarpLuaResult(client, HuntaholicRules.BearRoadId);
            if (code != 0)
            {
                ShowPage(client, $"@{90996944 + code}");
                return;
            }

            ClearDialog(info);
            if (!info.TryDebitGold(HuntaholicRules.LobbyWarpFee))
            {
                ShowPage(client, "@90996942");
                return;
            }

            client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(info.CharacterGold, info.CharacterChaos));
            WarpToLobby(client, HuntaholicRules.BearRoadId);
            return;
        }

        var grade = ReadIntArgument(trigger);
        var (cost, item) = grade switch
        {
            1 => (20_000L, 1100703),
            2 => (90_000L, 1100702),
            3 => (200_000L, 1100701),
            _ => (0L, 0)
        };
        if (item == 0) return;

        lock (info.ProgressLock)
        {
            if (info.CharacterJp < cost)
            {
                Notice(client, "@91000273");
                return;
            }

            info.CharacterJp -= cost;
        }

        client.Connection.Send(GameCharacterPackets.BuildExpUpdate(info.CharacterHandle, info.CharacterExp, info.CharacterJp));
        try
        {
            var added = await _characters.AddItemAsync(info.CharacterName, item, 1);
            if (added is null) throw new InvalidOperationException("no item added");
            foreach (var packet in GameCharacterPackets.BuildInventory(new[] { added })) client.Connection.Send(packet);
            Notice(client, "@91000272");
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not give the JP box {item} to {clientTag}: the JP is given back", item, client.ClientTag);
            lock (info.ProgressLock) info.CharacterJp += cost;
            client.Connection.Send(GameCharacterPackets.BuildExpUpdate(info.CharacterHandle, info.CharacterExp, info.CharacterJp));
        }
    }

    /// <summary>
    /// The result codes <c>go_to_huntaholic</c> reads from <c>warp_to_huntaholic_lobby</c>, matched to the pages
    /// @90996945..51: 1 invalid, 4 in a party, 5 PK on, 6 unknown HuntaHolic, 7 no tier for the level.
    /// </summary>
    private int LobbyWarpLuaResult(GameClient client, int huntaholicId)
    {
        if (_parties.PartyIdOf(client) != 0) return 4;
        return CheckLobbyEnterable(client, huntaholicId) switch
        {
            ResultCode.Success => 0,
            ResultCode.PKLimit => 5,
            ResultCode.NotExist => 6,
            ResultCode.NotEnoughLevel => 7,
            _ => 1
        };
    }

    // ---- the hunt -------------------------------------------------------------------------------------------

    /// <summary><c>beginHunting</c>: every respawn entry spawns on the room's layer, then the members go in.</summary>
    private void StartHunt(Room room)
    {
        room.Begin = true;
        foreach (var respawn in room.Tier.Respawns)
        {
            for (var i = 0; i < respawn.Count; i++)
            {
                SpawnFor(room, respawn);
            }
        }

        var begin = _clock();
        foreach (var member in _parties.OnlineMembers(room.PartyId))
        {
            var (x, y) = ScatterAround(room.Base.DungeonX, room.Base.DungeonY);
            _warp.Warp(member, x, y, room.InstanceNo);
            member.Connection.Send(GameHuntaholicServerPackets.BuildBeginHunting(
                unchecked(begin + member.ConnectionInfo.ClientClockOffset)));
        }
    }

    private bool SpawnFor(Room room, HuntaholicRespawnRow respawn)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            float x = _random.Next(respawn.Left, respawn.Right + 1);
            float y = _random.Next(respawn.Top, respawn.Bottom + 1);
            if (_monsters.IsBlockedPoint(x, y)) continue;
            if (_monsters.SpawnInstanceMonster(respawn.MonsterId, x, y, room.InstanceNo, respawn.IsWandering) is not { } monster)
            {
                _logger.Warning("Unknown monster {monster} in HuntaHolic respawn {respawn}", respawn.MonsterId, respawn.Id);
                return false;
            }

            room.Monsters.Add(monster.InstanceId);
            room.RespawnOf[monster.InstanceId] = respawn;
            _roomOfMonster[monster.InstanceId] = room;
            return true;
        }

        _logger.Warning("Unable to respawn monster in HuntaHolic respawn {respawn}", respawn.Id);
        return false;
    }

    /// <summary>The <c>WarpFunctor</c>'s ±60 around the point, drawn again until the line to it is clear.</summary>
    private (float X, float Y) ScatterAround(float x, float y)
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            var tx = x + _random.Next(-60, 61);
            var ty = y + _random.Next(-60, 61);
            if (_monsters.HasLineOfSight(x, y, tx, ty)) return (tx, ty);
        }

        return (x, y);
    }

    /// <summary><c>InstanceDungeon::onMonsterDelete</c>: score, the room's 4007, the max point and the respawn.</summary>
    public void OnMonsterKilled(long instanceId, GameClient topDealer)
    {
        lock (_lock)
        {
            if (!_roomOfMonster.Remove(instanceId, out var room) || !room.Monsters.Remove(instanceId)) return;
            room.RespawnOf.Remove(instanceId, out var respawn);
            if (room.End || room.Score >= room.Base.MaxPoint) return;
            if (!_monsters.TryGetInstance(instanceId, out var monster)) return;

            var score = HuntaholicRules.MonsterScore(monster.MonsterType);
            if (score == 0) return;

            if (topDealer is not null)
            {
                var tag = room.ScoreOf(topDealer.ConnectionInfo.CharacterHandle);
                tag.Kills++;
                tag.Score += score;
            }

            room.KillCount++;
            room.Score += score;
            var maxReached = room.Score >= room.Base.MaxPoint;
            var members = _parties.OnlineMembers(room.PartyId);
            if (maxReached)
            {
                room.Score = room.Base.MaxPoint;
                ClearMonsters(room, members);
                foreach (var member in members)
                    member.Connection.Send(GameHuntaholicServerPackets.BuildMaxPointAchieved());
            }

            foreach (var member in members)
            {
                var kills = room.Scores.TryGetValue(member.ConnectionInfo.CharacterHandle, out var own) ? own.Kills : 0;
                member.Connection.Send(GameHuntaholicServerPackets.BuildUpdateScore(kills, room.Score));
            }

            if (!maxReached && respawn is not null)
            {
                room.PendingRespawns.Add((respawn, unchecked(_clock() + (uint)(respawn.PeriodSeconds * 100))));
            }
        }
    }

    private void ClearMonsters(Room room, IReadOnlyList<GameClient> members)
    {
        room.PendingRespawns.Clear();
        _monsters.RemoveInstanceMonsters(room.Monsters);
        foreach (var id in room.Monsters) _roomOfMonster.Remove(id);
        room.Monsters.Clear();
        room.RespawnOf.Clear();
        foreach (var member in members)
            if (member.ConnectionInfo.Layer == room.InstanceNo) _monsterSpawn.Sync(member);
    }

    /// <summary>
    /// <c>QuitHunting</c> / <c>quitHunting</c>: out of the room's party, the reward or the penalty, the 4006, then the
    /// lobby on the tier's layer (unless the player is warping elsewhere); the last one out ends the hunt.
    /// </summary>
    private ResultCode QuitHunting(GameClient client, bool reward, HuntingResult result, bool warpToLobby)
    {
        var info = client.ConnectionInfo;
        var partyId = _parties.PartyIdOf(client);
        if (partyId == 0) return ResultCode.AccessDenied;

        lock (_lock)
        {
            var room = _rooms.FirstOrDefault(r => r.PartyId == partyId);
            if (room is null)
            {
                // A party left over from a room long gone: leave it, and at least back to the lobby.
                _parties.LeaveHuntaholicParty(client);
                if (warpToLobby && _catalog.IsDungeon(info.X, info.Y)) WarpToTierLobby(client, _catalog.All.FirstOrDefault());
                return ResultCode.Success;
            }

            if (result == HuntingResult.Unknown)
            {
                result = HuntaholicRules.ResolveResult(info.CharacterHp <= 0, room.Score, room.Base.ObjectivePoint, reward);
            }

            _parties.LeaveHuntaholicParty(client);
            QuitCore(room, client, reward, result);
            if (warpToLobby) WarpToTierLobby(client, room.Base, room.Tier.Id);

            if (_parties.PartyMemberCount(room.PartyId) == 0)
            {
                EndHunt(room);
            }

            return ResultCode.Success;
        }
    }

    private void QuitCore(Room room, GameClient client, bool reward, HuntingResult result)
    {
        var info = client.ConnectionInfo;
        if (!reward)
        {
            return;
        }

        var success = result == HuntingResult.Success;
        var gain = HuntaholicRules.GainPoint(room.Tier.PointAdvantage, room.Score, success);
        if (success)
        {
            SendPoints(client, info.AddHuntaholicPoint(gain));
            lock (info.ProgressLock)
            {
                info.CharacterExp += room.Tier.RewardExp;
                info.CharacterJp += room.Tier.RewardJp;
            }

            _leveling?.ApplyExperience(client);
            client.Connection.Send(GameCharacterPackets.BuildExpUpdate(info.CharacterHandle, info.CharacterExp, info.CharacterJp));
            GiveItem(client, room.Tier.SuccessItemId, room.Tier.SuccessItemCount);
        }
        else if (result is HuntingResult.FailedByDeath or HuntingResult.FailedByGameTimeLimit)
        {
            GiveItem(client, room.Tier.FailItemId, room.Tier.FailItemCount);
        }
        else
        {
            SendEnterCount(client, info.AddHuntaholicEnterCount(-1));
            _casts.ApplyState(client, HuntaholicRules.MoveSpeedSlowdownState, 1, HuntaholicRules.QuittingPenaltyTicks);
        }

        var own = room.Scores.TryGetValue(info.CharacterHandle, out var tag) ? tag : new ScoreTag();
        client.Connection.Send(GameHuntaholicServerPackets.BuildHuntingScore(room.Base.Id, own.Kills, own.Score,
            room.KillCount, room.Score, room.Tier.PointAdvantage, 1.0, gain, (byte)result));
    }

    /// <summary><c>endHunting</c>: no monster left, the party destroyed, each member rewarded and sent to the lobby.</summary>
    private void EndHunt(Room room)
    {
        if (room.End && !_rooms.Contains(room)) return;
        room.End = true;
        _rooms.Remove(room);
        var members = _parties.OnlineMembers(room.PartyId);
        ClearMonsters(room, Array.Empty<GameClient>());
        _parties.DestroyHuntaholicParty(room.PartyId);

        var result = room.Score < room.Base.ObjectivePoint ? HuntingResult.FailedByDeath : HuntingResult.Success;
        foreach (var member in members)
        {
            QuitCore(room, member, true, member.ConnectionInfo.CharacterHp <= 0 ? HuntingResult.FailedByDeath : result);
            WarpToTierLobby(member, room.Base, room.Tier.Id);
        }
    }

    private void WarpToTierLobby(GameClient client, HuntaholicRow huntaholic, int? tierLayer = null)
    {
        if (huntaholic is null) return;
        var layer = tierLayer is { } fixedLayer
            ? (byte)fixedLayer
            : HuntaholicRules.ProperLobbyLayer(huntaholic, client.ConnectionInfo.CharacterLevel);
        if (layer == HuntaholicRules.UnusableLobbyLayer)
        {
            WarpOut(client);
            return;
        }

        var (x, y) = ScatterAround(huntaholic.LobbyX, huntaholic.LobbyY);
        _warp.Warp(client, x, y, layer);
    }

    // ---- world hooks ------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>StructPlayer::ProcessWarp</c>: leaving the HuntaHolic area leaves its lobby room, or quits the hunt without
    /// reward but with the penalty (an entry and the slowdown), and the 4005 result closes the room window.
    /// </summary>
    public void OnBeforeWarp(GameClient client, float x, float y)
    {
        var info = client.ConnectionInfo;
        var from = _catalog.GetHuntaholicId(info.X, info.Y);
        if (from == 0 || _catalog.GetHuntaholicId(x, y) == from) return;

        // ProcessWarp, before the room: RemoveAllStateByQuittingHuntaholic, then RestoreStatesOnLeaveInstanceGame(true).
        _casts.RemoveStatesWithTimeFlag(client, DataAccess.Entities.Enums.StateTimeType.EraseOnQuitHuntaholic);
        RestoreEntryVitals(client);

        if (_parties.PartyIdOf(client) == 0) return;

        if (_catalog.IsLobby(info.X, info.Y))
        {
            LeaveRoomForced(client);
        }
        else
        {
            QuitHunting(client, false, HuntingResult.NoReward, warpToLobby: false);
            SendEnterCount(client, info.AddHuntaholicEnterCount(-1));
            _casts.ApplyState(client, HuntaholicRules.MoveSpeedSlowdownState, 1, HuntaholicRules.QuittingPenaltyTicks);
        }

        client.SendResult((ushort)GamePackets.TM_CS_HUNTAHOLIC_LEAVE_INSTANCE, (ushort)ResultCode.Success);
    }

    /// <summary>
    /// <c>RestoreStatesOnLeaveInstanceGame(true)</c>: the HP and MP kept at the entry come back, then are forgotten.
    /// The HP is bounded by the maximum, as <c>SetHP</c> is; the MP needs no bound here, since a maximum only grows
    /// with the level and the states that raised it were taken at the entry's value.
    /// </summary>
    private void RestoreEntryVitals(GameClient client)
    {
        var info = client.ConnectionInfo;
        if (info.HuntaholicEnterHp < 0 || info.HuntaholicEnterMp < 0) return;

        var (hp, mp) = HuntaholicRules.EntryVitals(info.HuntaholicEnterHp, info.HuntaholicEnterMp, info.CharacterMaxHp);
        info.HuntaholicEnterHp = info.HuntaholicEnterMp = -1;
        info.CharacterHp = hp;
        info.CharacterMp = mp;
        client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", hp));
        client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "mp", mp));
    }

    /// <summary>A room member leaving the lobby while the room started counts as quitting it (forced party leave).</summary>
    private void LeaveRoomForced(GameClient client)
    {
        if (LeaveRoom(client) == ResultCode.CoolTime)
        {
            QuitHunting(client, false, HuntingResult.NoReward, warpToLobby: false);
        }
    }

    /// <summary>
    /// <c>StructPlayer::onLogout</c>: in the dungeon the hunt is quit with its reward or penalty (dead: failed by
    /// death, otherwise retired), in the lobby the room is left. The character keeps its position; the next login
    /// brings it back to the lobby (<see cref="PlaceAtLogin"/>).
    /// </summary>
    public void OnWorldExit(GameClient client)
    {
        var info = client.ConnectionInfo;
        if (_catalog.GetHuntaholicId(info.X, info.Y) == 0 || _parties.PartyIdOf(client) == 0) return;
        try
        {
            if (_catalog.IsDungeon(info.X, info.Y))
            {
                QuitHunting(client, true, info.CharacterHp <= 0 ? HuntingResult.FailedByDeath : HuntingResult.Retired,
                    warpToLobby: false);
            }
            else
            {
                LeaveRoomForced(client);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not take {clientTag} out of HuntaHolic", client.ClientTag);
        }
    }

    public (float X, float Y, byte Layer) PlaceAtLogin(int level, float x, float y, byte layer,
        float townX = HuntaholicDefaults.TownX, float townY = HuntaholicDefaults.TownY)
    {
        var huntaholicId = _catalog.GetHuntaholicId(x, y);
        if (huntaholicId == 0 || !_catalog.TryGet(huntaholicId, out var huntaholic)) return (x, y, layer);
        var lobbyLayer = HuntaholicRules.ProperLobbyLayer(huntaholic, level);
        if (lobbyLayer == HuntaholicRules.UnusableLobbyLayer)
            return (townX, townY, 0);
        return _catalog.IsDungeon(x, y) ? (huntaholic.LobbyX, huntaholic.LobbyY, lobbyLayer) : (x, y, lobbyLayer);
    }

    /// <summary>
    /// The resurrection inside HuntaHolic (<c>onResurrection</c>): in the dungeon the hunt is failed by death (the
    /// reward and 4006, back to the lobby), in the lobby back to its entry; the HP refilled in both cases.
    /// </summary>
    public bool OnTryResurrect(GameClient client)
    {
        var info = client.ConnectionInfo;
        var huntaholicId = _catalog.GetHuntaholicId(info.X, info.Y);
        if (huntaholicId == 0 || !_catalog.TryGet(huntaholicId, out var huntaholic)) return false;

        var dungeon = _catalog.IsDungeon(info.X, info.Y);
        if (dungeon && _parties.PartyIdOf(client) != 0)
        {
            QuitHunting(client, true, HuntingResult.FailedByDeath, warpToLobby: false);
        }

        info.CharacterHp = info.CharacterMaxHp > 0 ? info.CharacterMaxHp : 1;
        WarpToTierLobby(client, huntaholic);
        client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", info.CharacterHp));
        return true;
    }

    public void OnShowLobbyWindow(GameClient client, uint contactHandle)
    {
        client.Connection.Send(GameNpcDialogPackets.BuildDialog(contactHandle, "Huntaholic", string.Empty,
            Array.Empty<NpcDialogMenuEntry>(), HuntaholicDefaults.LobbyDialogType));
    }

    // ---- tick -----------------------------------------------------------------------------------------------

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                Process(_clock());
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "HuntaHolic tick failed");
            }
        }
    }

    /// <summary><c>HuntaholicManager::onProcess</c>, plus the daily entries of every online player.</summary>
    public void Process(uint now)
    {
        RefillEntries();
        lock (_lock)
        {
            foreach (var room in _rooms.ToArray())
            {
                if (!room.Begin)
                {
                    if (_parties.PartyMemberCount(room.PartyId) == 0)
                    {
                        _rooms.Remove(room);
                        continue;
                    }

                    if (room.BeginTime != 0 && unchecked((int)(now - room.BeginTime)) >= 0)
                    {
                        StartHunt(room);
                    }

                    continue;
                }

                var endTick = unchecked(room.BeginTime + (uint)room.Base.HuntingPeriodSeconds * 100);
                if (room.End || unchecked((int)(now - endTick)) > 0 || _parties.PartyMemberCount(room.PartyId) == 0)
                {
                    EndHunt(room);
                    continue;
                }

                if (!room.EndNoticed && unchecked((int)(now - (endTick - HuntaholicRules.EndNoticeTicks))) > 0)
                {
                    room.EndNoticed = true;
                    foreach (var member in _parties.OnlineMembers(room.PartyId))
                        member.Connection.Send(GameChatPackets.BuildChat("@HUNTAHOLIC", HuntaholicChatType,
                            $"@1111\v#@min@#\v{HuntaholicRules.EndNoticeTicks / 6000}"));
                }

                var spawned = false;
                foreach (var pending in room.PendingRespawns.ToArray())
                {
                    if (unchecked((int)(now - pending.Due)) < 0) continue;
                    room.PendingRespawns.Remove(pending);
                    spawned |= SpawnFor(room, pending.Respawn);
                }

                if (spawned)
                {
                    foreach (var member in _parties.OnlineMembers(room.PartyId))
                        if (member.ConnectionInfo.Layer == room.InstanceNo) _monsterSpawn.Sync(member);
                }
            }
        }
    }

    /// <summary><c>StructPlayer::onProcess</c>: the entries come back to 12 at 06:00 local time.</summary>
    private void RefillEntries()
    {
        if (_players?.Registry is null) return;
        var now = _localNow();
        foreach (var client in _players.Registry.Clients)
        {
            var info = client.ConnectionInfo;
            if (info.CharacterHandle == 0 || now < info.NextHuntaholicRefill) continue;
            info.HuntaholicEnterCount = HuntaholicEntryRefill.EntriesPerDay;
            info.NextHuntaholicRefill = HuntaholicEntryRefill.NextRefill(now);
            SendEnterCount(client, info.HuntaholicEnterCount);
        }
    }

    public uint Ranking(GameClient client)
    {
        if (_players?.Registry is null) return 0;
        var mine = client.ConnectionInfo.HuntaholicPoint;
        if (mine <= 0) return 0;
        return (uint)(1 + _players.Registry.Clients.Count(c => c.ConnectionInfo.CharacterHandle != 0
            && c.ConnectionInfo.HuntaholicPoint > mine));
    }

    // ---- helpers --------------------------------------------------------------------------------------------

    private byte NewInstanceNo(int huntaholicId)
    {
        byte no = 1;
        while (no < 0xFF && _rooms.Any(r => r.Base.Id == huntaholicId && r.InstanceNo == no)) no++;
        return no;
    }

    private HuntaholicInstanceInfo Info(Room room) => new(room.InstanceNo, _parties.PartyName(room.PartyId),
        (byte)_parties.PartyMemberCount(room.PartyId), room.MaxMembers, room.Password.Length > 0);

    private void BroadcastInfo(Room room)
    {
        var frame = GameHuntaholicServerPackets.BuildInstanceInfo(Info(room));
        foreach (var member in _parties.OnlineMembers(room.PartyId)) member.Connection.Send(frame);
    }

    private void GiveItem(GameClient client, int itemId, int count)
    {
        if (itemId <= 0 || count <= 0) return;
        _ = GiveItemAsync(client, itemId, count);
    }

    private async Task GiveItemAsync(GameClient client, int itemId, int count)
    {
        try
        {
            var item = await _characters.AddItemAsync(client.ConnectionInfo.CharacterName, itemId, count);
            if (item is null) return;
            foreach (var packet in GameCharacterPackets.BuildInventory(new[] { item })) client.Connection.Send(packet);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not give the HuntaHolic reward {item} to {clientTag}", itemId, client.ClientTag);
        }
    }

    private static void SendPoints(GameClient client, int points) =>
        client.Connection.Send(GameStatPackets.BuildProperty(client.ConnectionInfo.CharacterHandle, "huntaholicpoint", points));

    private static void SendEnterCount(GameClient client, int count) =>
        client.Connection.Send(GameStatPackets.BuildProperty(client.ConnectionInfo.CharacterHandle, "huntaholic_ent", count));

    private static void Notice(GameClient client, string text) =>
        client.Connection.Send(GameChatPackets.BuildChat("@SCRIPT", (byte)ChatType.System, text));

    private static void ShowPage(GameClient client, string text)
    {
        var info = client.ConnectionInfo;
        uint handle;
        lock (info.NpcVisibilityLock)
        {
            handle = info.NpcDialogHandle;
            info.NpcDialogTriggers.Clear();
            info.NpcDialogRevision++;
        }

        client.Connection.Send(GameNpcDialogPackets.BuildDialog(handle, "@90996937", text,
            new[] { new NpcDialogMenuEntry { Label = "@90010002", Trigger = string.Empty } }));
    }

    private static void ClearDialog(ConnectionInfo info)
    {
        lock (info.NpcVisibilityLock) info.ClearNpcDialog();
    }

    private static int ReadIntArgument(string trigger)
    {
        var open = trigger.IndexOf('(');
        var close = trigger.IndexOf(')', open + 1);
        return open >= 0 && close > open && int.TryParse(trigger[(open + 1)..close].Trim(), out var value) ? value : 0;
    }

    private sealed class ScoreTag
    {
        public int Kills;
        public int Score;
    }

    private sealed class Room
    {
        public Room(HuntaholicRow huntaholic, HuntaholicTierRow tier, byte instanceNo, int partyId, byte maxMembers,
            string password)
        {
            Base = huntaholic;
            Tier = tier;
            InstanceNo = instanceNo;
            PartyId = partyId;
            MaxMembers = maxMembers;
            Password = password;
        }

        public HuntaholicRow Base { get; }
        public HuntaholicTierRow Tier { get; }
        public byte InstanceNo { get; }
        public int PartyId { get; }
        public byte MaxMembers { get; }
        public string Password { get; }
        public bool Begin;
        public uint BeginTime;
        public bool EndNoticed;
        public bool End;
        public int KillCount;
        public int Score;
        public readonly Dictionary<uint, ScoreTag> Scores = new();
        public readonly HashSet<long> Monsters = new();
        public readonly Dictionary<long, HuntaholicRespawnRow> RespawnOf = new();
        public readonly List<(HuntaholicRespawnRow Respawn, uint Due)> PendingRespawns = new();

        public ScoreTag ScoreOf(uint handle)
        {
            if (!Scores.TryGetValue(handle, out var tag)) Scores[handle] = tag = new ScoreTag();
            return tag;
        }
    }
}

/// <summary>The values HuntaHolic needs that no table here provides.</summary>
public static class HuntaholicDefaults
{
    /// <summary>The town used when no return point is known (none is missing once a character has entered the world).</summary>
    public const float TownX = 153161;
    public const float TownY = 80223;

    /// <summary><c>TS_SC_DIALOG::TYPE_HUNTAHOLIC_LOBBY</c>, what <c>show_huntaholic_lobby_window</c> opens.</summary>
    public const int LobbyDialogType = 5;
}
