using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Guilds;

public sealed partial class GuildService
{
    private async Task<bool> AllianceCommandAsync(TelecasterContext db, GameClient client, CharacterEntity member,
        GuildEntity guild, string command, string argument, string[] args, List<Action> notifications)
    {
        var alliance = guild.AllianceId is > 0 ? await db.Alliances.SingleOrDefaultAsync(a => a.Id == guild.AllianceId) : null;
        if (command == "gajoin")
        {
            if (alliance is not null || !GuildRules.Permitted(guild, member, GuildPermissions.AllianceJoin)
                || guild.AllianceBlockTime > UnixNow || guild.DungeonBlockTime > UnixNow || guild.DungeonId is > 0
                || Busy(guild.Id) || !ReadInvitation(client, true, args, out var invitation)) return false;
            var destination = await db.Alliances.SingleOrDefaultAsync(a => a.Id == invitation.Group);
            var inviter = await db.Characters.SingleOrDefaultAsync(c => c.Id == invitation.Inviter.ConnectionInfo.CharacterHandle);
            var leadingGuild = destination is null ? null : await db.Guilds.SingleOrDefaultAsync(g => g.Id == destination.LeadGuildId);
            if (destination is null || leadingGuild is null || inviter is null
                || invitation.InviterGuild != leadingGuild.Id || !GuildRules.Permitted(leadingGuild, inviter, GuildPermissions.AllianceManagement)
                || leadingGuild.DungeonId is > 0 || Busy(leadingGuild.Id)
                || await db.Guilds.CountAsync(g => g.AllianceId == destination.Id) >= destination.MaxAllianceCount) return false;
            guild.AllianceId = destination.Id;
            _invitations.Remove(((uint)member.Id, true));
            notifications.Add(() => Broadcast(destination.Id, $"JOIN|{destination.Name}|{guild.Name}|", true));
            return true;
        }
        if (alliance is null) return false;
        var lead = await db.Guilds.SingleOrDefaultAsync(g => g.Id == alliance.LeadGuildId);
        if (lead is null || lead.DungeonId is > 0 || Busy(lead.Id)) return false;
        if (command == "gainvite")
        {
            if (guild.Id != alliance.LeadGuildId || !GuildRules.Permitted(guild, member, GuildPermissions.AllianceManagement)
                || await db.Guilds.CountAsync(g => g.AllianceId == alliance.Id) >= alliance.MaxAllianceCount) return false;
            var normalized = GuildRules.Normalize(argument);
            var target = await db.Guilds.SingleOrDefaultAsync(g => g.NormalizedName == normalized);
            if (target is null || target.AllianceId is > 0 || target.DungeonId is > 0 || target.AllianceBlockTime > UnixNow
                || target.DungeonBlockTime > UnixNow || Busy(target.Id) || target.LeaderId is null
                || !_players.TryResolve((uint)target.LeaderId.Value, out var live)) return false;
            var invitation = Invite(live, client, alliance.Id, true, guild.Id);
            notifications.Add(() => Send(live, $"INVITE|{member.CharacterName}|{alliance.Name}|{alliance.Id}|{invitation.Token}|", true));
            return true;
        }
        if (command == "gadestroy")
        {
            if (guild.Id != alliance.LeadGuildId || guild.LeaderId != member.Id
                || await db.Guilds.CountAsync(g => g.AllianceId == alliance.Id) != 1) return false;
            guild.AllianceId = null; guild.AllianceBlockTime = UnixNow + (long)GuildRules.RejoinDelay.TotalSeconds;
            db.Alliances.Remove(alliance);
            notifications.Add(() => Broadcast(guild.Id, $"DESTROY|{alliance.Name}|", false));
            return true;
        }
        GuildEntity departing;
        if (command == "galeave")
        {
            if (guild.Id == alliance.LeadGuildId || !GuildRules.Permitted(guild, member, GuildPermissions.AllianceLeave)) return false;
            departing = guild;
        }
        else
        {
            if (command != "gakick" || guild.Id != alliance.LeadGuildId
                || !GuildRules.Permitted(guild, member, GuildPermissions.AllianceManagement)) return false;
            var normalized = GuildRules.Normalize(argument);
            departing = await db.Guilds.SingleOrDefaultAsync(g => g.AllianceId == alliance.Id && g.NormalizedName == normalized);
            if (departing is null || departing.Id == alliance.LeadGuildId) return false;
        }
        if (Busy(departing.Id)) return false;
        departing.AllianceId = null; departing.AllianceBlockTime = UnixNow + (long)GuildRules.RejoinDelay.TotalSeconds;
        notifications.Add(() =>
        {
            var message = $"{(command == "gakick" ? "KICK" : "LEAVE")}|{alliance.Name}|{departing.Name}|";
            Broadcast(alliance.Id, message, true);
            foreach (var live in _players.Clients) if (live.ConnectionInfo.GuildId == departing.Id) Send(live, message, true);
        });
        return true;
    }
}
