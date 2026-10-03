using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services.Guilds;

public sealed partial class GuildService
{
    public bool Contact(GameClient client, uint npcHandle, int npcId)
    {
        if (!GuildRules.Officers.Contains(npcId)) return false;
        Show(client, npcHandle, "Guilde", "Creation et gestion des guildes et des alliances.",
            ("Creer une guilde (niveau 20, 100000 or)", "show_guild_create()"),
            ("Creer une alliance", "show_alliance_create()"));
        return true;
    }
    private static NpcDialogMenuEntry Entry(string label, string trigger) => new() { Label = label, Trigger = trigger };
    // Dialog state and input windows share the visibility lock, not the async guild gate.
    private static void Show(GameClient client, uint handle, string title, string text, params (string Label, string Trigger)[] entries)
    {
        var info = client.ConnectionInfo;
        lock (info.NpcVisibilityLock)
        {
            info.ClearNpcDialog(); info.NpcDialogHandle = handle;
            var menu = entries.Select(e => Entry(e.Label, e.Trigger)).ToArray();
            foreach (var entry in menu) if (entry.Trigger.Length > 0) info.NpcDialogTriggers.Add(entry.Trigger);
            client.Connection.Send(GameNpcDialogPackets.BuildDialog(handle, title, text, menu));
        }
    }
    public bool Select(GameClient client, uint npcHandle, string trigger)
    {
        if (trigger is "show_guild_create()" or "show_alliance_create()")
        {
            var info = client.ConnectionInfo;
            lock (info.NpcVisibilityLock)
            {
                if (info.NpcDialogHandle != npcHandle || !info.NpcDialogTriggers.Contains(trigger)
                    || !info.SpawnedNpcIdsByHandle.TryGetValue(npcHandle, out var npc) || !GuildRules.Officers.Contains((int)npc)) return true;
                var alliance = trigger == "show_alliance_create()";
                lock (_inputs) _inputs[client] = new InputWindow(npcHandle, info.NpcDialogRevision, alliance);
                var packet = new byte[7]; BinaryPrimitives.WriteUInt32LittleEndian(packet, 7);
                BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4), (ushort)(alliance ? 660 : 650));
                for (var i = 0; i < 6; i++) packet[6] += packet[i];
                client.Connection.Send(packet);
            }
            return true;
        }
        var confirm = Regex.Match(trigger, "\\Aguild_confirm_(create|alliance)\\('([A-Za-z0-9 ]{1,30})'\\)\\z");
        if (confirm.Success)
        {
            _ = CreateConfirmedAsync(client, npcHandle, trigger, confirm.Groups[2].Value, confirm.Groups[1].Value == "alliance");
            return true;
        }
        // The dungeon manager's authored menu routes only exact advertised triggers into commands.
        var action = Regex.Match(trigger, @"\Aguild_dungeon_(register|cancel|taxup|taxdown|gold|chaos|drop)\(([0-9]+)\)\z");
        if (action.Success)
        {
            _ = SelectDungeonManagementAsync(client, npcHandle, trigger, action.Groups[1].Value, int.Parse(action.Groups[2].Value));
            return true;
        }
        return false;
    }
    private bool PrepareCreation(GameClient client, bool alliance, string name)
    {
        if (!GuildRules.ValidName(name) || _banned?.ContainsBannedWord(name) == true) return false;
        var info = client.ConnectionInfo;
        lock (info.NpcVisibilityLock)
        {
            InputWindow window;
            lock (_inputs) if (!_inputs.Remove(client, out window)) return false;
            if (window.Alliance != alliance || window.Handle != info.NpcDialogHandle || window.Revision != info.NpcDialogRevision
                || !info.SpawnedNpcIdsByHandle.TryGetValue(window.Handle, out var npc) || !GuildRules.Officers.Contains((int)npc)) return false;
            var trigger = $"guild_confirm_{(alliance ? "alliance" : "create")}('{name}')";
            Show(client, window.Handle, alliance ? "Alliance" : "Guilde", $"Confirmer la creation de {name}{(alliance ? "" : " pour 100000 or") } ?",
                ("Confirmer", trigger), ("Annuler", ""));
            return true;
        }
    }
    public async Task<bool> CreateConfirmedAsync(GameClient client, uint npcHandle, string trigger, string name, bool alliance)
    {
        var info = client.ConnectionInfo; var characterName = info.CharacterName; var handle = info.CharacterHandle;
        var revision = info.NpcDialogRevision;
        await _gate.WaitAsync();
        try
        {
            return await _characters.RunAsync(characterName, async () =>
            {
                bool Valid()
                {
                    lock (info.NpcVisibilityLock) return Current(client, handle, characterName) && info.NpcDialogHandle == npcHandle
                        && info.NpcDialogRevision == revision && info.NpcDialogTriggers.Contains(trigger)
                        && info.SpawnedNpcIdsByHandle.TryGetValue(npcHandle, out var npc) && GuildRules.Officers.Contains((int)npc);
                }
                if (!Valid() || trigger != $"guild_confirm_{(alliance ? "alliance" : "create")}('{name}')"
                    || !GuildRules.ValidName(name) || _banned?.ContainsBannedWord(name) == true) return false;
                await using var db = new TelecasterContext(_options);
                await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync() : null;
                var member = await db.Characters.SingleOrDefaultAsync(c => c.Id == handle && c.CharacterName == characterName);
                if (member is null || !Valid()) return false;
                var normalized = GuildRules.Normalize(name);
                if (alliance)
                {
                    var guild = member.GuildId is > 0 ? await db.Guilds.SingleOrDefaultAsync(g => g.Id == member.GuildId) : null;
                    if (guild is null || guild.LeaderId != member.Id || guild.AllianceId is > 0 || guild.DungeonId is > 0
                        || guild.AllianceBlockTime > UnixNow || Busy(guild.Id) || await db.Alliances.AnyAsync(a => a.NormalizedName == normalized)) return false;
                    var created = new AllianceEntity { Name = name, NormalizedName = normalized, LeadGuildId = guild.Id, MaxAllianceCount = 3 };
                    db.Alliances.Add(created); await db.SaveChangesAsync();
                    guild.AllianceId = created.Id;
                    await db.SaveChangesAsync();
                    if (transaction is not null) await transaction.CommitAsync();
                    lock (info.NpcVisibilityLock) info.ClearNpcDialog();
                    await SyncAsync(db); Send(client, $"CREATE|{name}|", true); return true;
                }
                if (member.GuildId is > 0 || member.GuildBlockTime > Now || info.CharacterLevel < 20 || member.Lv < 20
                    || await db.Guilds.AnyAsync(g => g.NormalizedName == normalized) || !Valid()) return false;
                if (!info.TryDebitGold(GuildRules.CreationCost)) return false;
                var committed = false;
                try
                {
                    var guild = new GuildEntity { Name = name, NormalizedName = normalized, LeaderId = member.Id,
                        PermissionNames = Enumerable.Repeat(string.Empty, 6).ToArray(), PermissionSets = new GuildPermissions[6],
                        Notice = string.Empty, Url = string.Empty, Icon = string.Empty, Banner = string.Empty,
                        AdvertiseComment = string.Empty, AdvertiseEndTime = Now };
                    db.Guilds.Add(guild); await db.SaveChangesAsync();
                    member.GuildId = guild.Id; member.GuildPermission = 7; member.GuildMemo = string.Empty;
                    member.Gold = info.CharacterGold;
                    await db.SaveChangesAsync();
                    if (transaction is not null) await transaction.CommitAsync();
                    committed = true;
                    lock (info.NpcVisibilityLock) info.ClearNpcDialog();
                    client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(info.CharacterGold, info.CharacterChaos));
                    await SyncAsync(db); Send(client, $"JOIN|{name}|"); return true;
                }
                catch { if (!committed) info.TryCreditGold(GuildRules.CreationCost, long.MaxValue); throw; }
            });
        }
        catch (Exception ex) { Log.Error(ex, "Guild creation failed for {Character}", characterName); Send(client, "ERROR_FAILED"); return false; }
        finally { _gate.Release(); }
    }
}
