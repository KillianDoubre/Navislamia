using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services.GmCommands;

public partial class GmCommandService
{
    private async Task<bool> RunOfficialAsync(GameClient client, GmCommandDefinition definition,
        GmCommandLine line, IEnumerable<GameClient> everyone)
    {
        var info = client.ConnectionInfo;
        var args = line.Args;
        GameClient Find(string name) => everyone.Append(client).FirstOrDefault(c => c.ConnectionInfo.CharacterHandle != 0
            && string.Equals(c.ConnectionInfo.CharacterName, name, StringComparison.OrdinalIgnoreCase));
        switch (definition.Command)
        {
            case GmCommand.ChangeName:
                if (args.Length != 1) { Usage(client, definition); return true; }
                Task renaming;
                lock (info.NameChangeLock)
                {
                    renaming = RenameAndPublishAsync(client, args[0], info.NameChangeCompletion);
                    info.NameChangeCompletion = renaming;
                }
                await renaming;
                return true;

            case GmCommand.BlockChat:
                if (args.Length is < 1 or > 2) { Usage(client, definition); return true; }
                var blocked = Find(args[0]);
                if (blocked is null) { Reply(client, "Player not found."); return true; }
                if (args.Length == 1)
                { Reply(client, $"Chat block: {blocked.ConnectionInfo.ChatBlockRemaining(ServerClock.Now)} seconds."); return true; }
                if (!int.TryParse(args[1], out var minutes) || minutes is < 0 or > 144000)
                { Usage(client, definition); return true; }
                var blockedName = blocked.ConnectionInfo.CharacterName; var blockedHandle = blocked.ConnectionInfo.CharacterHandle;
                if (!await _characterService.SaveChatBlockTimeAsync(blockedName, minutes * 60))
                { Reply(client, "Chat block save failed."); return true; }
                if (blocked.ConnectionInfo.CharacterName != blockedName || blocked.ConnectionInfo.CharacterHandle != blockedHandle) return true;
                blocked.ConnectionInfo.ChatBlockUntil = minutes == 0 ? 0 : unchecked(ServerClock.Now + (uint)(minutes * 6000));
                Reply(client, $"Chat block: {blockedName}, {minutes} minutes.");
                return true;

            case GmCommand.CheckAutoUser:
                if (args.Length != 1) { Usage(client, definition); return true; }
                var checkedPlayer = Find(args[0]);
                Reply(client, checkedPlayer is null ? "Player not found." : $"auto_user: {(checkedPlayer.ConnectionInfo.AutoUsed ? 1 : 0)}");
                return true;

            case GmCommand.ForceWarp:
                if (args.Length == 1)
                {
                    var destination = Find(args[0]);
                    if (destination is null) { Reply(client, "Player not found."); return true; }
                    var p = destination.ConnectionInfo.PositionAt(ServerClock.Now);
                    _warpService.Warp(client, p.X, p.Y, destination.ConnectionInfo.Layer);
                }
                else if (args.Length is 2 or 3
                    && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                    && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                    && float.IsFinite(x) && float.IsFinite(y) && x >= 0 && y >= 0)
                {
                    var victim = args.Length == 2 ? client : Find(args[2]);
                    if (victim is null) { Reply(client, "Player not found."); return true; }
                    _warpService.Warp(victim, x, y);
                }
                else Usage(client, definition);
                return true;

            case GmCommand.Invisible:
                if (args.Length == 0) { Reply(client, $"Invisible : {(info.IsInvisible ? "true" : "false")}"); return true; }
                if (args.Length != 1 || args[0] is not ("1" or "2")) { Usage(client, definition); return true; }
                info.IsInvisible = args[0] == "1";
                if (info.IsInvisible) _combatService.DropAggro(client);
                client.SendActorStatus();
                return true;

            case GmCommand.Kick:
                if (args.Length != 1) { Usage(client, definition); return true; }
                var kicked = Find(args[0]);
                if (kicked is null) Reply(client, "Player not found.");
                else kicked.Connection.Disconnect(); // existing disconnect owns saving and world cleanup
                return true;

            case GmCommand.Rebirth:
                if (args.Length != 0) { Usage(client, definition); return true; }
                _resurrection?.Rebirth(client);
                return true;

            case GmCommand.Lv:
                if (args.Length != 1 || !int.TryParse(args[0], out var level) || level < 1
                    || level > _levelingService.MaxLevel) { Usage(client, definition); return true; }
                if (!_levelingService.SetLevel(client, level)) Reply(client, "Level curve unavailable.");
                return true;
            default: return false;
        }
    }
    private async Task RenameAndPublishAsync(GameClient client, string newName, Task previous)
    {
        await previous;
        var info = client.ConnectionInfo;
        var old = info.CharacterName; var handle = info.CharacterHandle;
        var renamed = await _characterService.RenameCharacterAsync(old, newName);
        if (info.CharacterHandle != handle || info.CharacterName != old) return;
        if (renamed != ResultCode.Success)
        { Reply(client, $"Name change refused: {renamed}."); return; }
        info.CharacterName = newName;
        info.CharacterList.Remove(old); info.CharacterList.Add(newName);
        client.SendToSelfAndObservers(GamePetPackets.BuildChangeName(handle, newName));
        _parties?.OnNameChanged(client);
    }
}
