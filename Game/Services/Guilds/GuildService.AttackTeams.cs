using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services.Guilds;

public sealed partial class GuildService
{
    private sealed record AttackTeam(long PartyId, long EffectiveGuild, int DungeonId, int Type, long HeadParty);
    private readonly ConcurrentDictionary<long, AttackTeam> _teams = new();
    private readonly Dictionary<uint, Invitation> _teamInvitations = new();
    public async Task LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await using var db = new TelecasterContext(_options);
            await SyncAsync(db);
            _teams.Clear();
            // Official Community/PartyLoader.cpp:281-359, PartyManager.cpp:1452-1480.
            foreach (var party in _parties?.AttackParties() ?? Array.Empty<Party.DungeonParty>())
                _teams[party.Id] = new AttackTeam(party.Id, party.AttackGuild, party.DungeonId, party.Type, party.HeadParty);
        }
        finally { _gate.Release(); }
    }
    private bool TeamMatches(long party, long guild, int dungeon) => _teams.TryGetValue(party, out var team)
        && team.EffectiveGuild == guild && team.DungeonId == dungeon;
    public bool SameAttackTeam(GameClient first, GameClient second) => first.ConnectionInfo.PartyId is { } a
        && second.ConnectionInfo.PartyId is { } b && _teams.TryGetValue(a, out var one) && _teams.TryGetValue(b, out var two)
        && one.HeadParty == two.HeadParty;
    private void ClearTeams(int dungeon, long? guild = null)
    {
        foreach (var team in _teams.Values.Where(t => t.DungeonId == dungeon && (guild is null || t.EffectiveGuild == guild)).ToArray())
        { _parties?.DisbandAttackParty(team.PartyId); _teams.TryRemove(team.PartyId, out _); }
    }
    private async Task<bool> AttackTeamCommandAsync(TelecasterContext db, GameClient client, CharacterEntity member,
        GuildEntity guild, string command, string[] args, List<Action> notifications)
    {
        if (_parties is null || command is not ("rpcreate" or "rp_ginvite" or "rp_gjoin")) return false;
        var effective = await EffectiveAsync(db, guild);
        var lead = await db.Guilds.SingleOrDefaultAsync(g => g.Id == effective);
        if (lead?.DungeonId is not > 0 || !_catalog.Dungeons.TryGetValue((int)lead.DungeonId.Value, out var dungeon)) return false;
        var raid = Open(dungeon, false);
        var type = raid ? 1 : 2;
        var state = await DungeonAsync(db, dungeon.Id);
        if (!raid && (state.OwnerGuildId != effective && state.RaidGuildId != effective || _time.GetUtcNow() > Deadline(Week, dungeon.SiegeClose))) return false;
        if (command == "rpcreate")
        {
            if (args.Length != 1 || !GuildRules.ValidName(args[0]) || !GuildRules.Permitted(guild, member, GuildPermissions.AttackTeamCreate)
                || _parties.DungeonParty(client) is not null || _teams.Values.Any(t => t.EffectiveGuild == effective)) return false;
            var id = _parties.CreateAttackParty(client, args[0], effective, type);
            if (id == 0) return false;
            _teams[id] = new AttackTeam(id, effective, dungeon.Id, type, id);
            _parties.LinkAttackParty(id, dungeon.Id, id);
            return true;
        }
        if (command == "rp_ginvite")
        {
            var party = _parties.DungeonParty(client);
            if (party is null || party.Leader != member.Id || !_teams.TryGetValue(party.Id, out var team) || team.HeadParty != party.Id
                || !GuildRules.Permitted(guild, member, GuildPermissions.AttackTeamCreate) || args.Length != 1) return false;
            var targetMember = await db.Characters.SingleOrDefaultAsync(c => c.CharacterName == args[0]);
            var target = targetMember?.GuildId is > 0 ? await db.Guilds.SingleOrDefaultAsync(g => g.Id == targetMember.GuildId) : null;
            if (target is null || await EffectiveAsync(db, target) != effective
                || !_players.TryResolve((uint)targetMember.Id, out var live) || _parties.DungeonParty(live) is not null) return false;
            var invitation = new Invitation(live, client, party.Id, guild.Id, RandomNumberGenerator.GetInt32(1, int.MaxValue), _time.GetUtcNow().AddMinutes(2), (uint)member.Id, member.CharacterName);
            _teamInvitations[live.ConnectionInfo.CharacterHandle] = invitation;
            notifications.Add(() => live.Connection.Send(GameChatPackets.BuildChat("@RAID", (byte)ChatType.RaidSystem,
                $"RAID_GUILD_INVITE|{member.CharacterName}|{party.Name}|{party.Id}|{invitation.Token}|")));
            return true;
        }
        if (args.Length != 2 || !long.TryParse(args[0], out var head) || !int.TryParse(args[1], out var token)
            || !_teamInvitations.TryGetValue((uint)member.Id, out var invited) || token != invited.Token
            || !ReferenceEquals(invited.Target, client) || invited.Group != head || invited.Expires < _time.GetUtcNow()
            || !Current(invited.Inviter, invited.InviterHandle, invited.InviterName)
            || !_teams.TryGetValue(head, out var leading) || leading.EffectiveGuild != effective || leading.Type != type
            || !GuildRules.Permitted(guild, member, GuildPermissions.AttackTeamJoin) || _parties.DungeonParty(client) is not null) return false;
        var max = Math.Max(1, raid ? dungeon.RaidParties : dungeon.GuildParties);
        if (_teams.Values.Count(t => t.HeadParty == head) >= max) return false;
        var newId = _parties.CreateAttackParty(client, $"Team{head}_{guild.Id}", effective, type);
        if (newId == 0) return false;
        _teams[newId] = new AttackTeam(newId, effective, dungeon.Id, type, head);
        _parties.LinkAttackParty(newId, dungeon.Id, head);
        _teamInvitations.Remove((uint)member.Id);
        return true;
    }

    private void PruneTeams()
    {
        foreach (var team in _teams.Values)
            if (_parties?.AttackPartyExists(team.PartyId) != true) _teams.TryRemove(team.PartyId, out _);
    }
}
