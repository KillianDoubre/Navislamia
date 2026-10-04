using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Network.Packets.Upload;
using Serilog;

namespace Navislamia.Game.Services.Guilds;

public sealed partial class GuildService
{
    private sealed record PendingUpload(long Character, string Name, int Password, DateTime Expires);
    private readonly Dictionary<(long Guild, bool Banner), PendingUpload> _pendingUploads = new();
    private bool PrepareUpload(GameClient client, CharacterEntity member, GuildEntity guild, bool banner, List<Action> notifications)
    {
        foreach (var key in _pendingUploads.Where(e => e.Value.Expires <= Now).Select(e => e.Key).ToArray()) _pendingUploads.Remove(key);
        var permission = banner ? GuildPermissions.AdvertiseManagement : GuildPermissions.UpdateGuildIcon;
        if (!GuildRules.Permitted(guild, member, permission) || _uploads?.IsReady() != true || _uploads.Send is null
            || member.Id > int.MaxValue || member.AccountId > int.MaxValue || guild.Id > int.MaxValue
            || _pendingUploads.Values.Any(p => p.Character == member.Id)
            || _pendingUploads.TryGetValue((guild.Id, banner), out var previous) && previous.Expires > Now) return false;
        var password = RandomNumberGenerator.GetInt32(1, 0x00ffffff);
        member.OtpValue = password; member.OtpVerifiedAt = Now;
        var pending = new PendingUpload(member.Id, member.CharacterName, password, Now.AddMinutes(5));
        notifications.Add(() =>
        {
            _pendingUploads[(guild.Id, banner)] = pending;
            _uploads.Send(GuildUploadPackets.BuildRequest((int)member.Id, (int)member.AccountId, (int)guild.Id, password, banner));
            client.Connection.Send(GameGuildPackets.BuildUploadWindow(banner, (int)member.Id, (int)member.AccountId, password, _server.Name));
        });
        return true;
    }
    public async Task<bool> CompleteUploadAsync(GuildUploadPackets.Upload upload)
    {
        if (upload is null) return false;
        // Apply the same validation even when called outside the receive loop.
        if (upload.GuildId <= 0 || upload.FileSize <= 0 || string.IsNullOrWhiteSpace(upload.Filename)
            || upload.Filename.Length > 255 || upload.Filename.Contains("..")
            || upload.Filename.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.'))) return false;
        await _gate.WaitAsync();
        try
        {
            var key = ((long)upload.GuildId, upload.Banner);
            if (!_pendingUploads.TryGetValue(key, out var pending) || pending.Expires <= Now) return false;
            return await _characters.RunAsync(pending.Name, async () =>
            {
                await using var db = new TelecasterContext(_options);
                var member = await db.Characters.SingleOrDefaultAsync(c => c.Id == pending.Character && c.CharacterName == pending.Name);
                var guild = await db.Guilds.SingleOrDefaultAsync(g => g.Id == upload.GuildId);
                if (member is null || guild is null || member.GuildId != guild.Id || member.OtpValue != pending.Password
                    || !GuildRules.Permitted(guild, member, upload.Banner ? GuildPermissions.AdvertiseManagement : GuildPermissions.UpdateGuildIcon))
                    return false;
                if (upload.Banner) { guild.Banner = upload.Filename; guild.BannerSize = upload.FileSize; }
                else { guild.Icon = upload.Filename; guild.IconSize = upload.FileSize; }
                member.OtpValue = 0; member.OtpVerifiedAt = null;
                await db.SaveChangesAsync();
                _pendingUploads.Remove(key);
                var line = MediaLine(guild, upload.Banner);
                foreach (var viewer in _players.Clients) Send(viewer, line);
                return true;
            });
        }
        catch (Exception ex) { Log.Error(ex, "Could not persist upload for guild {Guild}", upload.GuildId); return false; }
        finally { _gate.Release(); }
    }
    private string MediaLine(GuildEntity guild, bool banner)
    {
        var file = banner ? guild.Banner : guild.Icon;
        var prefix = banner ? _server.GuildBannerUrl : _server.GuildIconUrl;
        var safeFile = !string.IsNullOrEmpty(file) && !file.Contains("..")
            && file.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
        var url = safeFile && Uri.TryCreate(prefix, UriKind.Absolute, out var baseUri)
            && baseUri.Scheme is "http" or "https" ? new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/" + file).AbsoluteUri : "";
        return $"{(banner ? "GBANNER" : "GICON")}|{guild.Id}|{guild.Name}|{url}|{(banner ? guild.BannerSize : guild.IconSize)}";
    }
    private async Task<bool> PublicGuildCommandAsync(TelecasterContext db, GameClient client, string command, string argument)
    {
        if (command is "gicon" or "gbanner")
        {
            if (!long.TryParse(argument, out var id)) return false;
            var guild = await db.Guilds.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id);
            if (guild is null) return false;
            Send(client, MediaLine(guild, command == "gbanner")); return true;
        }
        var rows = command == "granking"
            ? await db.Guilds.AsNoTracking().OrderByDescending(g => g.DonationPoint).ThenBy(g => g.Id).Take(10).ToArrayAsync()
            : await db.Guilds.AsNoTracking().Where(g => g.AdvertiseType != AdvertiseType.None && g.AdvertiseEndTime > Now)
                .OrderByDescending(g => g.AdvertiseType).ThenBy(g => g.Id).Take(30).ToArrayAsync();
        // No guild ranking packet exists in the available 7.3 protocol; normal chat keeps this usable.
        client.Connection.Send(GameChatPackets.BuildChat("@SYSTEM", 2, command == "granking" ? "Classement des guildes (dons) :" : "Annonces des guildes :"));
        for (var i = 0; i < rows.Length; i++) client.Connection.Send(GameChatPackets.BuildChat("@SYSTEM", 2,
            command == "granking" ? $"{i + 1}. {rows[i].Name} : {rows[i].DonationPoint}"
                : $"{rows[i].Name} : {rows[i].AdvertiseComment}"));
        return true;
    }
    private string AdvertiseLine(GuildEntity guild) => guild.AdvertiseEndTime > Now
        ? $"GADV|{(int)guild.AdvertiseType}|{Math.Max(0, (long)(guild.AdvertiseEndTime - Now).TotalSeconds)}|{guild.AdvertiseComment}|"
        : "GADV|0|0||";
    private bool Advertise(CharacterEntity member, GuildEntity guild, string argument, List<Action> notifications)
    {
        var args = argument.Split(' ', 3, StringSplitOptions.TrimEntries);
        if (!GuildRules.Permitted(guild, member, GuildPermissions.AdvertiseManagement) || args.Length < 2
            || !int.TryParse(args[0], out var type) || type is < 0 or > 3 || !int.TryParse(args[1], out var minutes)
            || minutes is < 0 or > 43200 || type != 0 && (minutes == 0 || args.Length != 3 || !GuildRules.ValidText(args[2], 128))) return false;
        guild.AdvertiseType = (AdvertiseType)type; guild.AdvertiseEndTime = type == 0 ? Now : Now.AddMinutes(minutes);
        guild.AdvertiseComment = type == 0 ? "" : args[2];
        notifications.Add(() => Broadcast(guild.Id, AdvertiseLine(guild)));
        return true;
    }
}
