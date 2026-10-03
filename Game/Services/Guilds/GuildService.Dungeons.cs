using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Dungeons;
using Navislamia.Game.Services.Progression;
using Serilog;

namespace Navislamia.Game.Services.Guilds;

public sealed partial class GuildService
{
    public DateTime Week
    {
        get
        {
            var local = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _zone).Date;
            var monday = local.AddDays(-((int)local.DayOfWeek + 6) % 7);
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(monday, DateTimeKind.Unspecified), _zone);
        }
    }
    private DateTimeOffset Deadline(DateTime week, int seconds)
    {
        var monday = TimeZoneInfo.ConvertTimeFromUtc(week, _zone);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(monday.AddSeconds(seconds), DateTimeKind.Unspecified), _zone));
    }
    private bool Open(DungeonDefinition dungeon, bool siege) => !_dungeonOptions.ClosedDungeons.Contains(dungeon.Id)
        && DungeonRules.IsOpen(_time.GetUtcNow(), _zone, siege ? dungeon.SiegeOpen : dungeon.RaidOpen, siege ? dungeon.SiegeClose : dungeon.RaidClose);
    private bool Busy(long guild) => _teams.Values.Any(t => t.EffectiveGuild == EffectiveGuild(guild)) || _rooms.Visits().Any(v => v.Room.Key.Kind != DungeonRoomKind.Instance
        && EffectiveGuild(v.Client.ConnectionInfo.GuildId) == EffectiveGuild(guild))
        || _players.Clients.Any(c => EffectiveGuild(c.ConnectionInfo.GuildId) == EffectiveGuild(guild) && _runtime.Siege(c.ConnectionInfo) is not null);
    private async Task<DungeonEntity> DungeonAsync(TelecasterContext db, int id)
    {
        var state = await db.Dungeons.SingleOrDefaultAsync(d => d.Id == id);
        if (state is not null) return state;
        state = new DungeonEntity { Id = id, TaxRate = 1 };
        db.Dungeons.Add(state); return state;
    }
    private async Task<bool> DungeonCommandAsync(TelecasterContext db, GameClient client, CharacterEntity member,
        GuildEntity guild, string command, string[] args, List<Action> notifications, WalletTransfer transfer)
    {
        if (command == "graid")
        {
            if (args.Length != 1 || !int.TryParse(args[0], out var id) || !_catalog.Dungeons.TryGetValue(id, out var dungeon)
                || dungeon.Kind != 0 || !Open(dungeon, false) || EffectiveGuild(guild.Id) != guild.Id
                || !GuildRules.Permitted(guild, member, GuildPermissions.RequestDungeonRaid | GuildPermissions.AttackTeamCreate)
                || guild.DungeonBlockTime > UnixNow || Busy(guild.Id) || guild.DungeonId is > 0 && guild.DungeonId != id) return false;
            var state = await DungeonAsync(db, id);
            if (state.OwnerGuildId == guild.Id || await db.Dungeons.AnyAsync(d => d.OwnerGuildId == guild.Id)) return false;
            var week = Week;
            var registration = await db.GuildRaids.SingleOrDefaultAsync(r => r.GuildId == guild.Id && r.Week == week);
            if (registration is not null && (registration.DungeonId != id || registration.WrappedUp)) return false;
            if (registration is null) db.GuildRaids.Add(new GuildRaidEntity { GuildId = guild.Id, DungeonId = id, Week = week });
            guild.DungeonId = id;
            notifications.Add(() => Send(client, $"RAID_REGISTER|{id}|")); return true;
        }
        if (command == "graidcancel")
        {
            if (!GuildRules.Permitted(guild, member, GuildPermissions.RequestDungeonRaid | GuildPermissions.AttackTeamCreate)
                || EffectiveGuild(guild.Id) != guild.Id || Busy(guild.Id)) return false;
            var registration = await db.GuildRaids.SingleOrDefaultAsync(r => r.GuildId == guild.Id && r.Week == Week);
            if (registration is null || registration.WrappedUp || await db.Dungeons.AnyAsync(d => d.OwnerGuildId == guild.Id)) return false;
            db.GuildRaids.Remove(registration); guild.DungeonId = null; return true;
        }
        if (command is "gtax" or "gwithdraw" or "gdropdungeon")
        {
            if (!GuildRules.Permitted(guild, member, GuildPermissions.DungeonManagement) || EffectiveGuild(guild.Id) != guild.Id) return false;
            var state = await db.Dungeons.SingleOrDefaultAsync(d => d.OwnerGuildId == guild.Id);
            if (state is null) return false;
            if (command == "gtax")
            {
                if (args.Length != 1 || !int.TryParse(args[0], out var rate) || rate is < 1 or > 10) return false;
                state.TaxRate = rate; return true;
            }
            if (command == "gdropdungeon")
            {
                if (guild.LeaderId != member.Id || Busy(guild.Id)
                    || await db.GuildSieges.AnyAsync(s => s.DungeonId == state.Id && s.FinishedAt == null)) return false;
                state.OwnerGuildId = null; guild.DungeonId = null;
                var effective = guild.Id;
                foreach (var ally in await db.Guilds.Where(g => g.Id == effective || guild.AllianceId != null && g.AllianceId == guild.AllianceId).ToArrayAsync())
                    ally.DungeonBlockTime = UnixNow + (long)GuildRules.RejoinDelay.TotalSeconds;
                return true;
            }
            if (args.Length != 1 || args[0] is not ("gold" or "chaos")) return false;
            if (!Current(client, (uint)member.Id, member.CharacterName)) return false;
            var info = client.ConnectionInfo;
            if (args[0] == "gold")
            {
                var amount = guild.Gold;
                if (amount <= 0 || !info.TryCreditGold(amount, GuildRules.MaxGold)) return false;
                transfer.Gold = amount;
                guild.Gold = 0; member.Gold = info.CharacterGold;
            }
            else
            {
                lock (info.ProgressLock)
                {
                    var capacity = _stats is null ? 0 : CombatRewards.ChaosCapacity(_stats.Compute(info).Total.MaxChaos);
                    var amount = Math.Min(guild.Chaos, Math.Max(0, capacity - info.CharacterChaos));
                    if (amount <= 0) return false;
                    transfer.Chaos = amount;
                    info.CharacterChaos += amount; member.Chaos = info.CharacterChaos; guild.Chaos -= amount;
                }
            }
            notifications.Add(() => client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(info.CharacterGold, info.CharacterChaos)));
            return true;
        }
        return await AttackTeamCommandAsync(db, client, member, guild, command, args, notifications);
    }

    public async Task<ResultCode> AuthorizeDungeonAsync(GameClient client, int dungeonId, bool siege, bool starting = true)
    {
        await _gate.WaitAsync();
        try
        {
            await using var db = new TelecasterContext(_options);
            if (!_catalog.Dungeons.TryGetValue(dungeonId, out var dungeon) || dungeon.Kind != 0 || !Open(dungeon, siege)) return ResultCode.NotActable;
            var info = client.ConnectionInfo;
            var member = await db.Characters.AsNoTracking().SingleOrDefaultAsync(c => c.Id == info.CharacterHandle && c.CharacterName == info.CharacterName);
            if (member?.GuildId is not > 0) return ResultCode.AccessDenied;
            var guild = await db.Guilds.AsNoTracking().SingleOrDefaultAsync(g => g.Id == member.GuildId);
            if (guild is null) return ResultCode.AccessDenied;
            var effective = await EffectiveAsync(db, guild);
            _runtime.SetGuild(guild.Id, effective);
            var party = _parties?.DungeonParty(client);
            if (party is null || !TeamMatches(party.Id, effective, dungeonId)) return ResultCode.AccessDenied;
            var ids = party.Members.Select(id => (long)id).ToArray();
            var members = await db.Characters.AsNoTracking().Where(c => ids.Contains(c.Id)).ToArrayAsync();
            if (members.Length != ids.Length) return ResultCode.AccessDenied;
            foreach (var participant in members)
            {
                var participantGuild = participant.GuildId is > 0 ? await db.Guilds.AsNoTracking().SingleOrDefaultAsync(g => g.Id == participant.GuildId) : null;
                if (participantGuild is null || await EffectiveAsync(db, participantGuild) != effective) return ResultCode.AccessDenied;
            }
            var state = await DungeonAsync(db, dungeonId);
            if (siege)
            {
                await WrapRaidsAsync(db, dungeon, Week);
                var war = await EnsureSiegeAsync(db, dungeon, state, Week);
                await db.SaveChangesAsync();
                if (war is null || effective != war.AttackerId && effective != war.DefenderId) return ResultCode.AccessDenied;
                return war?.FinishedAt is null && war is not null ? ResultCode.Success : ResultCode.NotActable;
            }
            if (state.OwnerGuildId == effective || starting && !GuildRules.Permitted(guild, member, GuildPermissions.AttackTeamCreate)) return ResultCode.AccessDenied;
            var raid = await db.GuildRaids.SingleOrDefaultAsync(r => r.GuildId == effective && r.Week == Week && r.DungeonId == dungeonId && !r.WrappedUp);
            if (raid is null || guild.DungeonBlockTime > UnixNow) return ResultCode.AccessDenied;
            if (starting && (party.Leader != info.CharacterHandle || _teams[party.Id].HeadParty != party.Id)) return ResultCode.AccessDenied;
            if (starting && raid.LastCompletedAt is { } completed && TimeZoneInfo.ConvertTimeFromUtc(completed, _zone).Date == TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _zone).Date)
                return ResultCode.NotActable;
            return ResultCode.Success;
        }
        finally { _gate.Release(); }
    }
    private static async Task<long> EffectiveAsync(TelecasterContext db, GuildEntity guild) => guild.AllianceId is > 0
        ? await db.Alliances.Where(a => a.Id == guild.AllianceId).Select(a => a.LeadGuildId).FirstOrDefaultAsync() : guild.Id;
    public async Task<bool> RaidStartedAsync(GameClient client, DungeonRoom room)
    {
        await _gate.WaitAsync();
        try
        {
            await using var db = new TelecasterContext(_options);
            var raid = await db.GuildRaids.SingleOrDefaultAsync(r => r.GuildId == room.Key.Owner && r.DungeonId == room.Key.DungeonId && r.Week == Week && !r.WrappedUp);
            if (raid is null || !Open(_catalog.Dungeons[room.Key.DungeonId], false) || room.Ended) return false;
            if (room.Members.Count == 0) { raid.StartedAt = Now; raid.Boss1Dead = false; raid.Boss2Dead = false; }
            await db.SaveChangesAsync();
            return true;
        }
        finally { _gate.Release(); }
    }
    public async Task<bool> SiegeEnteredAsync(GameClient client, DungeonRoom room)
    {
        await _gate.WaitAsync();
        try
        {
            await using var db = new TelecasterContext(_options);
            var dungeon = _catalog.Dungeons[room.Key.DungeonId];
            var state = await DungeonAsync(db, dungeon.Id);
            if (!Open(dungeon, true)) return false;
            var siege = await EnsureSiegeAsync(db, dungeon, state, Week);
            var effective = EffectiveGuild(client.ConnectionInfo.GuildId);
            if (siege is null || siege.FinishedAt is not null || effective != siege.AttackerId && effective != siege.DefenderId) return false;
            room.KeepAlive = true;
            RegisterObjectives(room, dungeon);
            var participant = await db.GuildSiegeParticipants.SingleOrDefaultAsync(p => p.SiegeId == siege.Id && p.CharacterId == client.ConnectionInfo.CharacterHandle);
            if (participant is null)
            {
                participant = new GuildSiegeParticipantEntity { SiegeId = siege.Id, CharacterId = client.ConnectionInfo.CharacterHandle, Attacker = effective == siege.AttackerId };
                db.GuildSiegeParticipants.Add(participant);
            }
            if (!participant.StartCredited)
            {
                await StoreSiegeTitleAsync(db, participant.CharacterId, TitleEvents.SiegeStart(dungeon.Id, participant.Attacker), () => participant.StartCredited = true);
            }
            await db.SaveChangesAsync();
            if (_titles is not null) _ = _titles.RefreshAsync(client);
            return true;
        }
        finally { _gate.Release(); }
    }
    private async Task<GuildSiegeEntity> EnsureSiegeAsync(TelecasterContext db, DungeonDefinition dungeon, DungeonEntity state, DateTime week)
    {
        var siege = await db.GuildSieges.SingleOrDefaultAsync(s => s.DungeonId == dungeon.Id && s.Week == week);
        if (siege is null)
        {
            if (state.RaidGuildId is not > 0 || !Open(dungeon, true)) return null;
            siege = new GuildSiegeEntity { DungeonId = dungeon.Id, Week = week, DefenderId = state.OwnerGuildId, AttackerId = state.RaidGuildId.Value };
            db.GuildSieges.Add(siege); await db.SaveChangesAsync();
        }
        if (siege.FinishedAt is null) _runtime.SetSiege(new GuildSiegeSide(dungeon.Id, siege.DefenderId, siege.AttackerId,
            state.OwnerGuildId, dungeon, Deadline(week, dungeon.SiegeClose)));
        return siege;
    }
    private void RegisterObjectives(DungeonRoom room, DungeonDefinition dungeon)
    {
        if (_world is null) return;
        foreach (var id in room.Monsters)
            if (_world.TryGetInstance(id, out var monster))
            {
                if (monster.MonsterId == dungeon.Core) _runtime.SetObjective(id, dungeon.Id, true);
                else if (monster.MonsterId == dungeon.Connector) _runtime.SetObjective(id, dungeon.Id, false);
            }
    }

    public async Task<MonsterKillReward> OnMonsterKilledAsync(GameClient killer, MonsterInstance monster, long instanceId, MonsterKillReward reward)
    {
        await _gate.WaitAsync();
        try
        {
            await using var db = new TelecasterContext(_options);
            var dungeon = _catalog.Dungeons.Values.FirstOrDefault(d => d.Kind == 0 && d.CellX == (int)(monster.X / 16128) && d.CellY == (int)(monster.Y / 16128));
            if (dungeon is null) return reward;
            if (_runtime.TryObjective(instanceId, out var objectiveDungeon, out var core))
            {
                var siege = await db.GuildSieges.SingleOrDefaultAsync(s => s.DungeonId == objectiveDungeon && s.Week == Week && s.FinishedAt == null);
                var state = await DungeonAsync(db, objectiveDungeon);
                if (siege is null || !Open(dungeon, true)) return default;
                var effective = EffectiveGuild(killer.ConnectionInfo.GuildId);
                if (core)
                {
                    // Official core-as-monster path: the core's death swaps attack and defence.
                    if (effective != siege.AttackerId && effective != siege.DefenderId || effective == state.OwnerGuildId) return default;
                    state.OwnerGuildId = effective; siege.CoreDestroyed = true;
                    await db.SaveChangesAsync();
                    _runtime.SetSiege(new GuildSiegeSide(dungeon.Id, siege.DefenderId, siege.AttackerId, effective, dungeon, Deadline(Week, dungeon.SiegeClose)));
                    if (siege.DefenderId is null) await FinishSiegeAsync(db, dungeon, siege, state);
                    else RespawnCore(dungeon);
                }
                else if (effective == state.OwnerGuildId) await FinishSiegeAsync(db, dungeon, siege, state);
                return default;
            }
            var visit = _rooms.Visits().FirstOrDefault(v => ReferenceEquals(v.Client, killer) && v.Room.Layer == monster.Layer && v.Room.Key.DungeonId == dungeon.Id);
            if (visit.Room?.Key.Kind == DungeonRoomKind.Raid)
            {
                var raid = await db.GuildRaids.SingleOrDefaultAsync(r => r.GuildId == visit.Room.Key.Owner && r.DungeonId == dungeon.Id && r.Week == Week && !r.WrappedUp);
                if (raid is not null && raid.StartedAt is not null && Open(dungeon, false))
                {
                    if (monster.MonsterId == dungeon.Boss1) raid.Boss1Dead = true;
                    if (monster.MonsterId == dungeon.Boss2) raid.Boss2Dead = true;
                    if ((dungeon.Boss1 == 0 || raid.Boss1Dead) && (dungeon.Boss2 == 0 || raid.Boss2Dead) && (dungeon.Boss1 != 0 || dungeon.Boss2 != 0))
                    {
                        var ticks = (int)Math.Clamp((_time.GetUtcNow().UtcDateTime - raid.StartedAt.Value).TotalMilliseconds / 10, 1, int.MaxValue);
                        if (raid.BestTime == 0 || ticks < raid.BestTime) raid.BestTime = ticks;
                        raid.LastCompletedAt = Now; raid.StartedAt = null;
                        await db.SaveChangesAsync();
                        foreach (var player in visit.Room.Members.ToArray())
                        {
                            var seconds = ticks / 100;
                            player.Connection.Send(GameChatPackets.BuildChat("@RAID", (byte)ChatType.RaidSystem,
                                $"RAID_RESULT|SUCC|{seconds / 3600}|{seconds / 60 % 60}|{seconds % 60}|"));
                            player.Connection.Send(GameChatPackets.BuildChat("@RAID", (byte)ChatType.RaidSystem, "RAID_END|"));
                        }
                        _rooms.Finish(visit.Room.Key);
                        ClearTeams(dungeon.Id, raid.GuildId);
                    }
                    else await db.SaveChangesAsync();
                }
            }
            // StructMonster::procDropGold/procDropChaos taxes the draw before contribution or party sharing.
            var ownership = await db.Dungeons.AsNoTracking().SingleOrDefaultAsync(d => d.Id == dungeon.Id);
            if (ownership?.OwnerGuildId is > 0)
            {
                var owner = await db.Guilds.SingleOrDefaultAsync(g => g.Id == ownership.OwnerGuildId);
                if (owner is not null)
                {
                    var gold = Math.Min(GuildRules.Tax(reward.Gold, ownership.TaxRate), long.MaxValue - Math.Max(0, owner.Gold));
                    var chaos = (int)Math.Min(GuildRules.Tax(reward.Chaos, ownership.TaxRate), int.MaxValue - Math.Max(0, owner.Chaos));
                    owner.Gold += gold; owner.Chaos += chaos;
                    await db.SaveChangesAsync();
                    reward = reward with { Gold = reward.Gold - gold, Chaos = reward.Chaos - chaos };
                }
            }
            return reward;
        }
        catch (Exception ex) { Log.Error(ex, "Guild monster event failed for {Instance}", instanceId); return reward; }
        finally { _gate.Release(); }
    }
    private void RespawnCore(DungeonDefinition dungeon)
    {
        if (_world is null) return;
        var room = _rooms.Find(new DungeonRoomKey(DungeonRoomKind.Siege, dungeon.Id, 0));
        if (room is null) return;
        var spawned = _world.SpawnDungeonMonsters(new[] { new MonsterSpawnPoint { MonsterId = dungeon.Core, Count = 1, X = dungeon.CoreX, Y = dungeon.CoreY, Layer = 1, RespawnSeconds = 0 } });
        room.Monsters = room.Monsters.Concat(spawned).ToArray();
        RegisterObjectives(room, dungeon);
    }
    private async Task WrapRaidsAsync(TelecasterContext db, DungeonDefinition dungeon, DateTime week)
    {
        if (_time.GetUtcNow() <= Deadline(week, dungeon.RaidClose)) return;
        var raids = await db.GuildRaids.Where(r => r.DungeonId == dungeon.Id && r.Week == week && !r.WrappedUp).ToArrayAsync();
        if (raids.Length == 0) return;
        var state = await DungeonAsync(db, dungeon.Id);
        var winner = raids.Where(r => r.BestTime > 0).OrderBy(r => r.BestTime).ThenBy(r => r.Id).FirstOrDefault();
        state.RaidGuildId = winner?.GuildId; state.BestRaidTime = winner?.BestTime ?? 0;
        state.LastDungeonRaidWrapUpTime = (int)Math.Min(int.MaxValue, UnixNow);
        foreach (var raid in raids)
        {
            raid.WrappedUp = true; raid.StartedAt = null;
            var guild = await db.Guilds.SingleOrDefaultAsync(g => g.Id == raid.GuildId);
            if (guild is not null && state.OwnerGuildId != guild.Id && state.RaidGuildId != guild.Id) guild.DungeonId = null;
            _rooms.Finish(new DungeonRoomKey(DungeonRoomKind.Raid, dungeon.Id, raid.GuildId));
            ClearTeams(dungeon.Id, raid.GuildId);
        }
        await db.SaveChangesAsync();
    }
    private async Task FinishSiegeAsync(TelecasterContext db, DungeonDefinition dungeon, GuildSiegeEntity siege, DungeonEntity state)
    {
        if (siege.FinishedAt is not null) return;
        var names = await db.Characters.Where(c => db.GuildSiegeParticipants.Any(p => p.SiegeId == siege.Id && p.CharacterId == c.Id && !p.EndCredited))
            .Select(c => c.CharacterName).ToArrayAsync();
        await _characters.RunManyAsync(names, async () =>
        {
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync() : null;
        siege.FinishedAt = Now; siege.WinnerId = state.OwnerGuildId;
        state.RaidGuildId = null; state.BestRaidTime = 0; state.LastDungeonSiegeFinishTime = (int)Math.Min(int.MaxValue, UnixNow);
        foreach (var guild in await db.Guilds.Where(g => g.Id == siege.AttackerId || g.Id == siege.DefenderId).ToArrayAsync())
        {
            guild.DungeonId = guild.Id == state.OwnerGuildId ? dungeon.Id : null;
            if (guild.Id != state.OwnerGuildId) guild.DungeonBlockTime = UnixNow + (long)GuildRules.RejoinDelay.TotalSeconds;
        }
        var participants = await db.GuildSiegeParticipants.Where(p => p.SiegeId == siege.Id && !p.EndCredited).ToArrayAsync();
        foreach (var participant in participants)
        {
            var success = participant.Attacker ? state.OwnerGuildId == siege.AttackerId : state.OwnerGuildId == siege.DefenderId;
            await StoreSiegeTitleAsync(db, participant.CharacterId, TitleEvents.SiegeEnd(dungeon.Id, participant.Attacker, success), () => participant.EndCredited = true, false);
        }
        await db.SaveChangesAsync();
        if (transaction is not null) await transaction.CommitAsync();
        });
        var participants = await db.GuildSiegeParticipants.Where(p => p.SiegeId == siege.Id).ToArrayAsync();
        var winnerName = state.OwnerGuildId is > 0 ? await db.Guilds.Where(g => g.Id == state.OwnerGuildId).Select(g => g.Name).FirstOrDefaultAsync() : string.Empty;
        foreach (var participant in participants)
            if (_players.TryResolve((uint)participant.CharacterId, out var live))
            {
                var success = participant.Attacker ? state.OwnerGuildId == siege.AttackerId : state.OwnerGuildId == siege.DefenderId;
                live.Connection.Send(GameChatPackets.BuildChat("@RAID", (byte)ChatType.RaidSystem,
                    $"SIEGE_RESULT|{(success ? "SUCC" : "FAIL")}|{(participant.Attacker ? "ATK" : "DEF")}|{dungeon.Id}|{winnerName}|"));
                live.Connection.Send(GameChatPackets.BuildChat("@RAID", (byte)ChatType.RaidSystem, "SIEGE_END|"));
                if (_titles is not null) _ = _titles.RefreshAsync(live);
            }
        _runtime.RemoveSiege(dungeon.Id);
        _rooms.Finish(new DungeonRoomKey(DungeonRoomKind.Siege, dungeon.Id, 0));
        ClearTeams(dungeon.Id);
    }
    private async Task StoreSiegeTitleAsync(TelecasterContext db, long characterId, Func<TitleConditionType, long?> increment, Action credited, bool takeGate = true)
    {
        var name = await db.Characters.Where(c => c.Id == characterId).Select(c => c.CharacterName).SingleAsync();
        async Task Store()
        {
            var state = await db.CharacterTitleStates.SingleOrDefaultAsync(s => s.CharacterId == characterId);
            if (state is null) { state = new CharacterTitleStateEntity { CharacterId = characterId }; db.CharacterTitleStates.Add(state); }
            var counts = state.ConditionIds.Select((id, i) => (id, count: state.ConditionCounts[i])).ToDictionary(p => p.id, p => p.count);
            foreach (var type in _titleCatalog.Types.Values)
                if (increment(type) is { } value) counts[type.Id] = type.Set ? value : CombatRewards.AddProgress(counts.GetValueOrDefault(type.Id), value);
            state.ConditionIds = counts.Keys.OrderBy(id => id).ToArray(); state.ConditionCounts = state.ConditionIds.Select(id => counts[id]).ToArray();
            credited();
            // A title refresh cannot see half a participation, or overwrite counters read before its gate.
            if (takeGate) await db.SaveChangesAsync();
        }
        if (takeGate) await _characters.RunAsync(name, Store); else await Store();
    }
    public async Task TickAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await using var db = new TelecasterContext(_options);
            PruneTeams();
            var guilds = await db.Guilds.AsNoTracking().ToArrayAsync();
            foreach (var guild in guilds) _runtime.SetGuild(guild.Id, await EffectiveAsync(db, guild));
            // Catch up missed weeks after a restart, before admitting any new weekly event.
            var weeks = (await db.GuildRaids.Where(r => !r.WrappedUp).Select(r => r.Week).Distinct().ToArrayAsync()).Append(Week).Distinct().OrderBy(w => w).ToArray();
            foreach (var week in weeks)
                foreach (var dungeon in _catalog.Dungeons.Values.Where(d => d.Kind == 0)) await WrapRaidsAsync(db, dungeon, week);
            foreach (var dungeon in _catalog.Dungeons.Values.Where(d => d.Kind == 0))
            {
                var state = await db.Dungeons.SingleOrDefaultAsync(d => d.Id == dungeon.Id);
                if (state is null) continue;
                if (Open(dungeon, true) && state.RaidGuildId is > 0) await EnsureSiegeAsync(db, dungeon, state, Week);
            }
            foreach (var siege in await db.GuildSieges.Where(s => s.FinishedAt == null).ToArrayAsync())
            {
                var dungeon = _catalog.Dungeons[(int)siege.DungeonId];
                var state = await db.Dungeons.SingleAsync(d => d.Id == dungeon.Id);
                if (_time.GetUtcNow() > Deadline(siege.Week, dungeon.SiegeClose) || _dungeonOptions.ClosedDungeons.Contains(dungeon.Id))
                    await FinishSiegeAsync(db, dungeon, siege, state);
                else
                {
                    _runtime.SetSiege(new GuildSiegeSide(dungeon.Id, siege.DefenderId, siege.AttackerId, state.OwnerGuildId, dungeon, Deadline(siege.Week, dungeon.SiegeClose)));
                    SendSiegeStatus(dungeon, siege, state);
                }
            }
            foreach (var invite in _invitations.Where(i => i.Value.Expires < _time.GetUtcNow()).Select(i => i.Key).ToArray()) _invitations.Remove(invite);
        }
        finally { _gate.Release(); }
    }
    private void SendSiegeStatus(DungeonDefinition dungeon, GuildSiegeEntity siege, DungeonEntity state)
    {
        var room = _rooms.Find(new DungeonRoomKey(DungeonRoomKind.Siege, dungeon.Id, 0));
        if (room is null) return;
        int Hp(int code)
        {
            if (_world is null) return 100;
            var id = room.Monsters.LastOrDefault(id => _world.TryGetInstance(id, out var monster) && monster.MonsterId == code && _world.IsAlive(id));
            return id != 0 && _world.TryGetInstance(id, out var instance) ? (int)((long)_world.GetHp(id) * 100 / Math.Max(1, instance.Hp)) : 0;
        }
        foreach (var client in room.Members.ToArray()) client.Connection.Send(GameChatPackets.BuildChat("@RAID", (byte)ChatType.RaidSystem,
            $"SIEGE_STATUS|{Hp(dungeon.Connector)}|{Hp(dungeon.Core)}|{(state.OwnerGuildId == EffectiveGuild(client.ConnectionInfo.GuildId) ? 1 : 0)}|"));
    }
    private async Task SendRaidTipAsync(TelecasterContext db, GameClient client, GuildEntity guild)
    {
        var effective = EffectiveGuild(guild.Id);
        var own = await db.Guilds.AsNoTracking().SingleOrDefaultAsync(g => g.Id == effective);
        if (own?.DungeonId is not > 0) { Send(client, "GRAIDSIEGETIP|0|0| | |0|0|"); return; }
        var dungeonId = (int)own.DungeonId.Value;
        var state = await db.Dungeons.AsNoTracking().SingleOrDefaultAsync(d => d.Id == dungeonId);
        var raid = await db.GuildRaids.AsNoTracking().Where(r => r.DungeonId == dungeonId && r.Week == Week && r.BestTime > 0)
            .OrderBy(r => r.BestTime).ThenBy(r => r.Id).FirstOrDefaultAsync();
        var raiding = _catalog.Dungeons.TryGetValue(dungeonId, out var definition) && Open(definition, false);
        var targetId = raiding ? raid?.GuildId : state?.OwnerGuildId == effective ? state.RaidGuildId : state?.OwnerGuildId;
        var target = targetId is > 0 ? await db.Guilds.AsNoTracking().SingleOrDefaultAsync(g => g.Id == targetId) : null;
        var leader = target is null ? null : await db.Characters.Where(c => c.Id == target.LeaderId).Select(c => c.CharacterName).FirstOrDefaultAsync();
        Send(client, $"GRAIDSIEGETIP|{(raiding ? 1 : state?.OwnerGuildId == effective ? 2 : 3)}|{(raiding ? raid?.BestTime ?? 0 : state?.BestRaidTime ?? 0)}|{target?.Name ?? " "}|{leader ?? " "}|{(target is null ? 0 : await db.Characters.CountAsync(c => c.GuildId == target.Id))}|{dungeonId}|");
    }
    private async Task SelectDungeonManagementAsync(GameClient client, uint handle, string trigger, string action, int dungeon)
    {
        var info = client.ConnectionInfo;
        lock (info.NpcVisibilityLock)
            if (info.NpcDialogHandle != handle || !info.NpcDialogTriggers.Contains(trigger)
                || !info.SpawnedNpcIdsByHandle.TryGetValue(handle, out var npc) || !_catalog.NpcDungeons.TryGetValue((int)npc, out var linked) || linked != dungeon) return;
        var command = action switch
        {
            "register" => $"/graid {dungeon}", "cancel" => "/graidcancel", "gold" => "/gwithdraw gold", "chaos" => "/gwithdraw chaos",
            "drop" => "/gdropdungeon", _ => null
        };
        if (command is not null) { await ExecuteCommandAsync(client, command); return; }
        await using var db = new TelecasterContext(_options);
        var rate = await db.Dungeons.Where(d => d.Id == dungeon).Select(d => d.TaxRate).FirstOrDefaultAsync();
        await ExecuteCommandAsync(client, $"/gtax {rate + (action == "taxup" ? 1 : -1)}");
    }
}
