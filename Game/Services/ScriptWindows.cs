using System;
using System.Globalization;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Props;

namespace Navislamia.Game.Services;

/// <summary>The callbacks measured in SFrame; see socle-fenetres-script.md.</summary>
public sealed record ScriptWindow(string Window, string Argument, string Trigger, uint Character,
    uint Npc, long Revision, PropAction? DungeonAction = null)
{
    public bool Matches(string reply)
    {
        // These three windows insert the argument string verbatim, not an additional difficulty.
        if (Window is "secret_dungeon_confirm_window" or "instance_dungeon_confirm_window"
            or "instance_dungeon_confirm_window2") return reply == $"{Trigger}({Argument})";
        if (Window == "dungeon_raid_confirm_window") return reply == Trigger + "()";
        // The feather copies the complete server-authored expression without adding anything.
        if (Window == "recall_feather_confirm_window") return reply == Trigger;
        if (Window != "number_input_window" || Trigger != "on_channel_set")
            return reply == Trigger || (NpcDialogService.ReadFunctionName(Trigger) == Trigger && reply == Trigger + "()");
        var prefix = Trigger + "(";
        if (!reply.StartsWith(prefix, StringComparison.Ordinal) || !reply.EndsWith(')')) return false;
        var number = reply.AsSpan(prefix.Length, reply.Length - prefix.Length - 1);
        // The edit control sends text, including leading zeroes. Never execute arbitrary Lua
        // just because it contains the authorized function (the official strstr check).
        var digits = number.Length > 0 && number[0] is '+' or '-' ? number[1..] : number;
        if (digits.Length == 0) return false;
        foreach (var digit in digits) if (!char.IsAsciiDigit(digit)) return false;
        return long.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
    }
}

public static class ScriptWindows
{
    public static bool Show(GameClient client, string window, string argument, string trigger, PropAction? action = null)
    {
        window = (window ?? string.Empty).Split('\0')[0];
        argument = (argument ?? string.Empty).Split('\0')[0];
        trigger = (trigger ?? string.Empty).Split('\0')[0].Trim();
        var frame = GameSmallPackets.ShowWindow(window, argument, trigger);
        if (frame is null) return false;
        var info = client.ConnectionInfo;
        lock (info.NpcVisibilityLock)
        {
            info.ScriptWindowTrigger = trigger;
            info.ScriptWindow = new ScriptWindow(window, argument, trigger, info.CharacterHandle,
                info.NpcDialogHandle, info.NpcDialogRevision, action);
            client.Connection.Send(frame);
        }
        return true;
    }
}
