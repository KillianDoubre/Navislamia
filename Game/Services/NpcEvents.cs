using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Navislamia.Configuration.Options;

namespace Navislamia.Game.Services;

/// <summary>
/// The seasonal events of the NPCs (docs/packet-specs/socle-pnj-evenements.md). The official Lua knows no date: the
/// Halloween candy entry of the town NPCs is commented "runs during the Halloween event only", and was put in the script
/// for the event and taken out after it. The catalogue kept every such entry, and the data places the Halloween pumpkin
/// spirits and the Easter dragon all year. An event's menu entries and NPCs are therefore hidden until
/// <see cref="GameRuleOptions.Events"/> opens it. The list is <c>tools/export_npc_event_menus.py</c>'s, embedded.
/// </summary>
public static class NpcEvents
{
    private static readonly Lazy<(FrozenDictionary<string, string> Menus, FrozenDictionary<long, string> Npcs)> Data =
        new(() =>
        {
            using var stream = typeof(NpcEvents).Assembly.GetManifestResourceStream("Navislamia.NpcEventMenus.json")
                ?? throw new InvalidOperationException("The embedded NPC event list is missing");
            var file = JsonSerializer.Deserialize<EventFile>(stream)!;
            return (file.Menus.ToFrozenDictionary(StringComparer.Ordinal),
                file.Npcs.ToFrozenDictionary(entry => long.Parse(entry.Key, CultureInfo.InvariantCulture), entry => entry.Value));
        });

    /// <summary>The event a menu entry's function opens, or null for an ordinary entry.</summary>
    public static string EventOfMenu(string trigger) =>
        Data.Value.Menus.TryGetValue(NpcDialogService.ReadFunctionName(trigger ?? string.Empty), out var name) ? name : null;

    /// <summary>The event an NPC belongs to (its contact is an event's), or null for an ordinary NPC.</summary>
    public static string EventOfNpc(long npcId) => Data.Value.Npcs.TryGetValue(npcId, out var name) ? name : null;

    public static bool IsMenuShown(string trigger, IReadOnlyList<GameEventWindow> windows, DateTime localNow) =>
        EventOfMenu(trigger) is not { } name || IsOpen(name, windows, localNow);

    public static bool IsNpcShown(long npcId, IReadOnlyList<GameEventWindow> windows, DateTime localNow) =>
        EventOfNpc(npcId) is not { } name || IsOpen(name, windows, localNow);

    /// <summary>
    /// Whether one of the windows of <paramref name="name"/> holds the day of <paramref name="localNow"/>; a window
    /// ending before it starts spans the new year. A window that does not read as <c>MM-dd</c> opens nothing.
    /// </summary>
    public static bool IsOpen(string name, IReadOnlyList<GameEventWindow> windows, DateTime localNow)
    {
        if (windows is null) return false;
        var today = localNow.Month * 100 + localNow.Day;
        foreach (var window in windows)
        {
            if (!string.Equals(window?.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (!TryReadDay(window.From, out var from) || !TryReadDay(window.To, out var to)) continue;
            if (from <= to ? today >= from && today <= to : today >= from || today <= to) return true;
        }

        return false;
    }

    private static bool TryReadDay(string value, out int day)
    {
        day = 0;
        // Read in a leap year, so that 02-29 is a day.
        if (!DateTime.TryParseExact("2000-" + value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            return false;
        day = date.Month * 100 + date.Day;
        return true;
    }

    private sealed class EventFile
    {
        public Dictionary<string, string> Menus { get; set; } = new();
        public Dictionary<string, string> Npcs { get; set; } = new();
    }
}
