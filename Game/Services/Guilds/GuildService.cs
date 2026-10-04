using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services.Dungeons;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Progression;
using Serilog;

namespace Navislamia.Game.Services.Guilds;

/// <summary>Official guild chat commands, persisted before their client notifications are published.</summary>
public sealed partial class GuildService : IGuildService
{
    private readonly DbContextOptions<TelecasterContext> _options;
    private readonly CharacterGate _characters;
    private readonly PlayerRegistry _players;
    private readonly GuildRuntime _runtime;
    private readonly DungeonCatalog _catalog;
    private readonly DungeonRooms _rooms;
    private readonly Party.IPartyService _parties;
    private readonly MonsterWorldState _world;
    private readonly IStatService _stats;
    private readonly TitleCatalog _titleCatalog;
    private readonly ITitleService _titles;
    private readonly IBannedWordsRepository _banned;
    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _zone;
    private readonly DungeonOptions _dungeonOptions;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly GuildUploadRelay _uploads;
    private readonly ServerOptions _server;
    private readonly Dictionary<(uint Target, bool Alliance), Invitation> _invitations = new();
    private readonly Dictionary<GameClient, InputWindow> _inputs = new();
    private static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        "gcreate", "gdestroy", "ginvite", "gjoin", "gkick", "gleave", "gpromote", "gpermission",
        "gpermissionset", "gpermissionname", "gnotice", "gurl", "gmemo", "ginfo", "graidsiegetip",
        "gacreate", "gainvite", "gajoin", "gakick", "galeave", "gadestroy", "garaid", "graid",
        "graidcancel", "gtax", "gwithdraw", "gdropdungeon", "rpcreate", "rp_ginvite", "rp_gjoin",
        "gicon", "gbanner", "gupdateicon", "gupdatebanner", "gadvertise", "glist", "granking", "gwindow"
    };
    private sealed record Invitation(GameClient Target, GameClient Inviter, long Group, long InviterGuild, int Token, DateTimeOffset Expires,
        uint InviterHandle, string InviterName);
    private sealed record InputWindow(uint Handle, long Revision, bool Alliance);
    private sealed class WalletTransfer { public long Gold; public int Chaos; public bool Committed; }

    public GuildService(DbContextOptions<TelecasterContext> options, CharacterGate characters,
        IPlayerVisibilityService players, GuildRuntime runtime, GuildCombatEvents events,
        DungeonCatalog catalog, DungeonRooms rooms, IOptions<DungeonOptions> dungeonOptions,
        MonsterWorldState world = null, IStatService stats = null, TitleCatalog titleCatalog = null,
        ITitleService titles = null, IBannedWordsRepository banned = null, TimeProvider time = null, Party.IPartyService parties = null,
        GuildUploadRelay uploads = null, IOptions<ServerOptions> server = null)
    {
        // The guild units open their own transactions around session effects: no retrying strategy (TelecasterOptions).
        _options = TelecasterOptions.WithoutRetry(options); _characters = characters; _players = players.Registry; _runtime = runtime;
        _parties = parties;
        _uploads = uploads; _server = server?.Value ?? new ServerOptions();
        _catalog = catalog; _rooms = rooms; _world = world; _stats = stats; _titleCatalog = titleCatalog ?? new TitleCatalog();
        _titles = titles; _banned = banned; _time = time ?? TimeProvider.System; _dungeonOptions = dungeonOptions.Value;
        _zone = TimeZoneInfo.FindSystemTimeZoneById(_dungeonOptions.TimeZone);
        events.Killed = OnMonsterKilledAsync;
    }

    public long EffectiveGuild(long? guildId) => _runtime.Effective(guildId);
    private DateTime Now => _time.GetUtcNow().UtcDateTime;
    private long UnixNow => _time.GetUtcNow().ToUnixTimeSeconds();
    private bool Current(GameClient client, uint handle, string name) => client.ConnectionInfo.CharacterHandle == handle
        && client.ConnectionInfo.CharacterName == name && _players.TryResolve(handle, out var live) && ReferenceEquals(live, client);
    public bool TryHandleCommand(GameClient client, string message)
    {
        var command = message.TrimStart('/').Split(' ', 2)[0];
        if (!Commands.Contains(command)) return false;
        _ = ExecuteCommandAsync(client, message);
        return true;
    }

    public async Task<bool> ExecuteCommandAsync(GameClient client, string message)
    {
        var info = client.ConnectionInfo;
        var handle = info.CharacterHandle; var name = info.CharacterName;
        var parts = message.TrimStart('/').Split(' ', 2, StringSplitOptions.TrimEntries);
        var command = parts[0].ToLowerInvariant(); var argument = parts.Length > 1 ? parts[1] : string.Empty;
        if (!Commands.Contains(command)) return false;
        await _gate.WaitAsync();
        try
        {
            if (!Current(client, handle, name)) return false;
            PruneTeams();
            return await _characters.RunAsync(name, async () =>
            {
                var transfer = new WalletTransfer();
                try
                {
                await using var db = new TelecasterContext(_options);
                await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync() : null;
                var member = await db.Characters.SingleOrDefaultAsync(c => c.Id == handle && c.CharacterName == name);
                if (member is null || !Current(client, handle, name)) return false;
                var guild = member.GuildId is > 0 ? await db.Guilds.SingleOrDefaultAsync(g => g.Id == member.GuildId) : null;
                var notifications = new List<Action>();
                var result = await CommandAsync(db, client, member, guild, command, argument, notifications, transfer);
                if (!result) { Send(client, "ERROR_INVALID_COMMAND"); return false; }
                await db.SaveChangesAsync();
                if (transaction is not null) await transaction.CommitAsync();
                transfer.Committed = true;
                await SyncAsync(db);
                foreach (var notify in notifications) notify();
                return true;
                }
                finally
                {
                    if (!transfer.Committed)
                    {
                        if (transfer.Gold > 0) info.TryDebitGold(transfer.Gold);
                        if (transfer.Chaos > 0) lock (info.ProgressLock) info.CharacterChaos -= transfer.Chaos;
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Guild command {Command} failed for {Character}", command, name);
            Send(client, "ERROR_FAILED"); return false;
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> CommandAsync(TelecasterContext db, GameClient client, CharacterEntity member, GuildEntity guild,
        string command, string argument, List<Action> notifications, WalletTransfer transfer)
    {
        if (command is "gcreate" or "gacreate")
            return PrepareCreation(client, command == "gacreate", argument);
        if (command is "gicon" or "gbanner" or "glist" or "granking")
            return await PublicGuildCommandAsync(db, client, command, argument);
        if (guild is null && command != "gjoin") return false;
        bool Permit(GuildPermissions permission) => GuildRules.Permitted(guild, member, permission);
        var args = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (command)
        {
            case "gwindow":
                notifications.Add(() => client.Connection.Send(GameGuildPackets.BuildWindow(GamePackets.TM_SC_OPEN_GUILD_WINDOW))); return true;
            case "gupdateicon": case "gupdatebanner":
                return PrepareUpload(client, member, guild, command == "gupdatebanner", notifications);
            case "gadvertise":
                return Advertise(member, guild, argument, notifications);
            case "ginfo": case "graidsiegetip":
                await SendInfoAsync(db, client, guild); return true;
            case "ginvite":
            {
                if (!Permit(GuildPermissions.MemberInvite) || await db.Characters.CountAsync(c => c.GuildId == guild.Id) >= GuildRules.MaxMembers) return false;
                var target = await db.Characters.SingleOrDefaultAsync(c => c.CharacterName == argument);
                if (target is null || target.GuildId is > 0 || target.GuildBlockTime > Now
                    || !_players.TryResolve((uint)target.Id, out var live)) return false;
                var invitation = Invite(live, client, guild.Id, false, guild.Id);
                notifications.Add(() => Send(live, $"INVITE|{member.CharacterName}|{guild.Name}|{guild.Id}|{invitation.Token}|"));
                return true;
            }
            case "gjoin":
            {
                if (member.GuildId is > 0 || member.GuildBlockTime > Now || !ReadInvitation(client, false, args, out var invitation)) return false;
                var destination = await db.Guilds.SingleOrDefaultAsync(g => g.Id == invitation.Group);
                var inviter = await db.Characters.SingleOrDefaultAsync(c => c.Id == invitation.Inviter.ConnectionInfo.CharacterHandle);
                if (destination is null || inviter is null || !GuildRules.Permitted(destination, inviter, GuildPermissions.MemberInvite)
                    || await db.Characters.CountAsync(c => c.GuildId == destination.Id) >= GuildRules.MaxMembers) return false;
                member.GuildId = destination.Id; member.GuildPermission = 1; member.GuildMemo = string.Empty;
                _invitations.Remove(((uint)member.Id, false));
                notifications.Add(() => { Broadcast(destination.Id, $"NEW|{member.CharacterName}|"); Send(client, $"JOIN|{destination.Name}|"); });
                return true;
            }
            case "gleave": case "gkick":
            {
                var target = command == "gleave" ? member : await db.Characters.SingleOrDefaultAsync(c => c.CharacterName == argument && c.GuildId == guild.Id);
                if (target is null || target.Id == guild.LeaderId || Busy(guild.Id)
                    || command == "gkick" && (!Permit(GuildPermissions.MemberKick) || target.GuildPermission >= Rank(guild, member))) return false;
                RemoveMember(target, guild.Id);
                notifications.Add(() =>
                {
                    var message = command == "gkick" ? $"KICK|{guild.Name}|{target.CharacterName}|" : $"LEAVE|{target.CharacterName}|";
                    Broadcast(guild.Id, message); if (_players.TryResolve((uint)target.Id, out var live)) Send(live, message);
                });
                return true;
            }
            case "gpromote":
            {
                if (guild.LeaderId != member.Id || Busy(guild.Id)) return false;
                var target = await db.Characters.SingleOrDefaultAsync(c => c.CharacterName == argument && c.GuildId == guild.Id);
                if (target is null || target.Id == member.Id) return false;
                guild.LeaderId = target.Id; member.GuildPermission = 1; target.GuildPermission = 7;
                notifications.Add(() => Broadcast(guild.Id, $"PROMOTE||{target.CharacterName}")); return true;
            }
            case "gpermission":
            {
                if (args.Length != 2 || !byte.TryParse(args[1], out var rank) || rank is < 1 or > 6
                    || rank >= Rank(guild, member) || !Permit(GuildPermissions.GrantRevokePermission)) return false;
                var target = await db.Characters.SingleOrDefaultAsync(c => c.CharacterName == args[0] && c.GuildId == guild.Id);
                if (target is null || target.Id == member.Id || target.Id == guild.LeaderId || target.GuildPermission >= Rank(guild, member)) return false;
                target.GuildPermission = rank;
                notifications.Add(() => Broadcast(guild.Id, $"CHANGE_PERMISSION|{target.CharacterName}|{rank}|")); return true;
            }
            case "gpermissionset":
                if (guild.LeaderId != member.Id || args.Length != 2 || !int.TryParse(args[0], out var tier) || tier is < 1 or > 6
                    || !int.TryParse(args[1], out var flags) || flags < 0 || flags > (int)GuildRules.All) return false;
                guild.PermissionSets = Pad(guild.PermissionSets, (GuildPermissions)0); guild.PermissionSets[tier - 1] = (GuildPermissions)flags;
                return true;
            case "gpermissionname":
            {
                var split = argument.Split(' ', 2);
                if (!Permit(GuildPermissions.SetPermissionName) || split.Length != 2 || !int.TryParse(split[0], out var rank)
                    || rank is < 1 or > 6 || !GuildRules.ValidText(split[1], 16)) return false;
                guild.PermissionNames = Pad(guild.PermissionNames, string.Empty); guild.PermissionNames[rank - 1] = split[1]; return true;
            }
            case "gnotice": case "gurl":
                if (!Permit(GuildPermissions.Notice) || !GuildRules.ValidText(argument, command == "gnotice" ? 128 : 128)) return false;
                if (command == "gnotice") guild.Notice = argument; else guild.Url = argument;
                if (command == "gnotice") notifications.Add(() => Broadcast(guild.Id, $"NOTICE|{argument}"));
                return true;
            case "gmemo":
            {
                var split = argument.Split(' ', 2);
                if (!Permit(GuildPermissions.MemberMemo) || split.Length != 2 || !GuildRules.ValidText(split[1], 32)) return false;
                var target = await db.Characters.SingleOrDefaultAsync(c => c.GuildId == guild.Id && c.CharacterName == split[0]);
                if (target is null) return false;
                target.GuildMemo = split[1]; return true;
            }
            case "gdestroy":
                if (guild.LeaderId != member.Id || guild.AllianceId is > 0 || guild.DungeonId is > 0 || Busy(guild.Id)
                    || await db.Dungeons.AnyAsync(d => d.OwnerGuildId == guild.Id || d.RaidGuildId == guild.Id)) return false;
                var members = await db.Characters.Where(c => c.GuildId == guild.Id).ToArrayAsync();
                foreach (var target in members) RemoveMember(target, guild.Id);
                db.Guilds.Remove(guild);
                notifications.Add(() => { foreach (var target in members) if (_players.TryResolve((uint)target.Id, out var live)) Send(live, $"DESTROY|{guild.Name}|"); });
                return true;
            case "gainvite": case "gajoin": case "gakick": case "galeave": case "gadestroy":
                return await AllianceCommandAsync(db, client, member, guild, command, argument, args, notifications);
            default:
                return await DungeonCommandAsync(db, client, member, guild, command, args, notifications, transfer);
        }
    }

    private static byte Rank(GuildEntity guild, CharacterEntity member) => guild.LeaderId == member.Id ? (byte)7 : member.GuildPermission;
    private static T[] Pad<T>(T[] values, T empty) => Enumerable.Range(0, 6).Select(i => values is not null && values.Length > i ? values[i] : empty).ToArray();
    private void RemoveMember(CharacterEntity member, long guild)
    {
        member.PreviousGuildId = guild; member.GuildId = null; member.GuildPermission = 0;
        member.GuildBlockTime = Now + GuildRules.RejoinDelay;
        _invitations.Remove(((uint)member.Id, false)); _invitations.Remove(((uint)member.Id, true));
    }
    private Invitation Invite(GameClient target, GameClient inviter, long group, bool alliance, long inviterGuild)
    {
        var invitation = new Invitation(target, inviter, group, inviterGuild, RandomNumberGenerator.GetInt32(1, int.MaxValue), _time.GetUtcNow().AddMinutes(2),
            inviter.ConnectionInfo.CharacterHandle, inviter.ConnectionInfo.CharacterName);
        _invitations[(target.ConnectionInfo.CharacterHandle, alliance)] = invitation;
        return invitation;
    }
    private bool ReadInvitation(GameClient client, bool alliance, string[] args, out Invitation invitation)
    {
        invitation = null;
        return args.Length == 2 && long.TryParse(args[0], out var group) && int.TryParse(args[1], out var token)
            && _invitations.TryGetValue((client.ConnectionInfo.CharacterHandle, alliance), out invitation)
            && ReferenceEquals(invitation.Target, client) && invitation.Group == group && invitation.Token == token
            && invitation.Expires >= _time.GetUtcNow()
            && Current(invitation.Inviter, invitation.InviterHandle, invitation.InviterName);
    }
    public static void Send(GameClient player, string message, bool alliance = false) => player.Connection.Send(
        GameChatPackets.BuildChat(alliance ? "@ALLIANCE" : "@GUILD", (byte)(alliance ? ChatType.AllianceSystem : ChatType.GuildSystem), message));
    private void Broadcast(long guild, string message, bool alliance = false)
    {
        foreach (var client in _players.Clients)
            if (alliance ? client.ConnectionInfo.AllianceId == guild : client.ConnectionInfo.GuildId == guild) Send(client, message, alliance);
    }
    public async Task OnWorldEntryAsync(GameClient client)
    {
        await _gate.WaitAsync();
        try
        {
            await using var db = new TelecasterContext(_options);
            await SyncAsync(db);
            if (client.ConnectionInfo.GuildId is > 0)
                Broadcast(client.ConnectionInfo.GuildId.Value, $"LOGIN|{(await db.Guilds.FindAsync(client.ConnectionInfo.GuildId.Value))?.Name}|{client.ConnectionInfo.CharacterName}|");
        }
        catch (Exception ex) { Log.Error(ex, "Guild world entry failed"); }
        finally { _gate.Release(); }
    }
    public async Task OnWorldExitAsync(GameClient client)
    {
        lock (_inputs) _inputs.Remove(client);
        await _gate.WaitAsync();
        _gate.Release();
        // Invitations are bound to the exact session and expire; a new login cannot redeem them.
        if (client.ConnectionInfo.GuildId is > 0)
            Broadcast(client.ConnectionInfo.GuildId.Value, $"LOGOUT|{client.ConnectionInfo.CharacterName}|");
    }
    private async Task SyncAsync(TelecasterContext db)
    {
        var guilds = await db.Guilds.AsNoTracking().ToArrayAsync();
        var alliances = (await db.Alliances.AsNoTracking().ToArrayAsync()).ToDictionary(a => a.Id);
        var byId = guilds.ToDictionary(g => g.Id);
        foreach (var guild in guilds) _runtime.SetGuild(guild.Id,
            guild.AllianceId is > 0 && alliances.TryGetValue(guild.AllianceId.Value, out var alliance) ? alliance.LeadGuildId : guild.Id);
        var ids = _players.Clients.Select(c => (long)c.ConnectionInfo.CharacterHandle).ToArray();
        var members = (await db.Characters.AsNoTracking().Where(c => ids.Contains(c.Id)).ToArrayAsync()).ToDictionary(c => c.Id);
        foreach (var client in _players.Clients)
        {
            var info = client.ConnectionInfo;
            if (!members.TryGetValue(info.CharacterHandle, out var member) || member.CharacterName != info.CharacterName) continue;
            var previous = info.GuildId;
            var guild = member.GuildId is > 0 ? byId.GetValueOrDefault(member.GuildId.Value) : null;
            info.GuildId = guild?.Id; info.AllianceId = guild?.AllianceId;
            info.GuildPermission = guild is null ? (byte)0 : Rank(guild, member);
            if (guild is not null) await SendInfoAsync(db, client, guild);
            if (previous != info.GuildId)
            {
                // Guild changes must reach peers already in view, not only the next ENTER.
                var notification = GameChatPackets.BuildChat("@GUILD", (byte)ChatType.GuildSystem, $"GCHANGE|{info.CharacterHandle}|{info.GuildId ?? 0}");
                foreach (var viewer in _players.Clients)
                    if (ReferenceEquals(viewer, client) || viewer.ConnectionInfo.SpawnedPlayers.ContainsKey(info.CharacterHandle)) viewer.Connection.Send(notification);
            }
        }
    }
    private async Task SendInfoAsync(TelecasterContext db, GameClient client, GuildEntity guild)
    {
        var members = await db.Characters.AsNoTracking().Where(c => c.GuildId == guild.Id).OrderBy(c => c.Id).ToArrayAsync();
        var leader = members.FirstOrDefault(c => c.Id == guild.LeaderId)?.CharacterName ?? string.Empty;
        var effective = EffectiveGuild(guild.Id);
        var owned = await db.Dungeons.Where(d => d.OwnerGuildId == effective).Select(d => d.Id).FirstOrDefaultAsync();
        Send(client, $"GINFO|{guild.Id}|{guild.Name}|{leader}|{members.Length}|{guild.Notice}|{(guild.Recruiting ? 1 : 0)}|{owned}|{guild.Url}");
        Send(client, AdvertiseLine(guild));
        var names = Pad(guild.PermissionNames, string.Empty); var sets = Pad(guild.PermissionSets, (GuildPermissions)0);
        Send(client, "GPERMISSION|" + string.Concat(Enumerable.Range(0, 6).Select(i => $"{names[i]}|{(int)sets[i]}|")));
        foreach (var member in members)
        {
            var online = _players.TryResolve((uint)member.Id, out var live) && live.ConnectionInfo.CharacterName == member.CharacterName;
            var info = online ? live.ConnectionInfo : null;
            Send(client, $"GMEMBER|{(online ? member.Id : 0)}|{member.CharacterName}|{info?.CharacterLevel ?? member.Lv}|{info?.CharacterJob ?? (int)member.CurrentJob}|{Rank(guild, member)}|{Hp(info)}|{Mp(info)}|{(int)(info?.X ?? 0)}|{(int)(info?.Y ?? 0)}|{(online ? 2 : 0)}|");
            Send(client, $"MEMO|{member.CharacterName}|{member.GuildMemo}|");
        }
        if (guild.AllianceId is > 0)
        {
            var alliance = await db.Alliances.AsNoTracking().SingleOrDefaultAsync(a => a.Id == guild.AllianceId);
            if (alliance is not null)
            {
                var allies = await db.Guilds.AsNoTracking().Where(g => g.AllianceId == alliance.Id).OrderBy(g => g.Id).ToArrayAsync();
                var line = $"AINFO|{alliance.Id}|{alliance.Name}|{allies.Length}|{alliance.LeadGuildId}|";
                foreach (var ally in allies)
                {
                    var alliedLeader = await db.Characters.Where(c => c.Id == ally.LeaderId).Select(c => c.CharacterName).FirstOrDefaultAsync();
                    line += $"{ally.Id}|{ally.Name}|{alliedLeader}|{await db.Characters.CountAsync(c => c.GuildId == ally.Id)}|";
                }
                Send(client, line);
            }
        }
        await SendRaidTipAsync(db, client, guild);
    }

    private static int Hp(ConnectionInfo info) => info is null ? 0 : (int)Math.Clamp((long)info.CharacterHp * 100 / Math.Max(1, info.CharacterMaxHp), 0, 100);
    private int Mp(ConnectionInfo info)
    {
        if (info is null) return 0;
        var max = _stats?.Compute(info).Total is { } total ? (int)total.MaxMp : info.CharacterMp;
        return (int)Math.Clamp((long)info.CharacterMp * 100 / Math.Max(1, max), 0, 100);
    }
    public void OnVitalsChanged(GameClient client)
    {
        var info = client.ConnectionInfo;
        if (info.GuildId is > 0) Broadcast(info.GuildId.Value,
            $"MINFO|{info.CharacterHandle}|{info.CharacterName}|{info.CharacterLevel}|{info.CharacterJob}|{info.GuildPermission}|{Hp(info)}|{Mp(info)}|{(int)info.X}|{(int)info.Y}|2|");
    }
}
