using System;
using System.Collections.Generic;
using System.Globalization;
using Navislamia.Game.DataAccess.Entities.Enums;

namespace Navislamia.Game.Services.ReturnPoints;

/// <summary>A return point: where a character reappears when it comes back to town after a death.</summary>
public readonly record struct ReturnPoint(int X, int Y);

/// <summary>A <c>Binding_*</c> dialog action: the return point it sets and the line the Lua's <c>message()</c> sends.</summary>
public readonly record struct ReturnPointBinding(int X, int Y, int Spread, string Message);

/// <summary>
/// The official return point (docs/packet-specs/socle-point-de-retour.md). The server keeps it as the two script
/// flags <c>rx</c>/<c>ry</c> (<c>StructPlayer::GetLastTownPosition</c>: "the script's return coordinates are rx, ry"),
/// and every coordinate here is the Epic 7 Lua's (<c>Epic 7 Part 4/branches/Live</c> and <c>trunk</c>):
/// <c>on_first_login</c>, <c>on_login</c>, <c>on_player_level_up</c> and <c>NPC_TeleportTown</c>. A Lua
/// <c>math.random(a, b)</c> includes both ends.
/// </summary>
public static class ReturnPointRules
{
    public const string FlagX = "rx";
    public const string FlagY = "ry";

    /// <summary>The level whose reaching moves the return point to the island's camp (<c>on_player_level_up</c>).</summary>
    public const int CampLevel = 5;

    /// <summary><c>on_first_login</c>: each race starts at its own point of the Island of Trainees.</summary>
    public static ReturnPoint Start(int race) => (Race)race switch
    {
        Race.Deva => new ReturnPoint(164474, 52932),
        Race.Asura => new ReturnPoint(168356, 55399),
        _ => new ReturnPoint(164335, 49510)
    };

    /// <summary><c>on_first_login</c>: the first return point is the start, ±30 (<c>start + random(0, 60) - 30</c>).</summary>
    public static ReturnPoint FirstReturnPoint(ReturnPoint start, Random random) =>
        new(start.X + Roll(random, 60) - 30, start.Y + Roll(random, 60) - 30);

    /// <summary><c>on_login</c>: a character without <c>rx</c>/<c>ry</c> gets its race's town.</summary>
    public static ReturnPoint LoginTown(int race, Random random) => (Race)race switch
    {
        Race.Deva => new ReturnPoint(7250 + Roll(random, 100), 6959 + Roll(random, 100)),
        Race.Asura => new ReturnPoint(116542 + Roll(random, 100), 58190 + Roll(random, 100)),
        _ => new ReturnPoint(152742 + Roll(random, 100), 77401 + Roll(random, 100))
    };

    /// <summary><c>on_player_level_up</c>, <c>lv == 5</c>: the island's camp.</summary>
    public static ReturnPoint Camp(Random random) => new(172543 + Roll(random, 100), 51847 + Roll(random, 100));

    private static readonly Dictionary<string, ReturnPointBinding> Bindings = new(StringComparer.Ordinal)
    {
        ["Binding_Deva_001"] = new(6625, 6980, 100, "@90100508"),
        ["Binding_Asura_001"] = new(116799, 58205, 100, "@90200508"),
        ["Binding_Gaia_001"] = new(153513, 77203, 100, "@90400509"),
        ["Binding_Beginner_001"] = new(172185, 52095, 10, "@90300508"),
        ["Binding_Rondoh_001"] = new(135466, 104917, 100, "@90600509"),
        ["Binding_Rondoh_002"] = new(140019, 106038, 100, "@90600509"),
        ["Binding_Ancient_relic_001"] = new(152634, 151508, 100, "@90703308")
    };

    /// <summary>Whether a dialog function is one of the teleporters' <c>Binding_*</c> actions.</summary>
    public static bool TryGetBinding(string function, out ReturnPointBinding binding) =>
        Bindings.TryGetValue(function ?? string.Empty, out binding);

    /// <summary>The return point a binding sets: its base plus <c>random(0, spread)</c> on each axis.</summary>
    public static ReturnPoint Bind(ReturnPointBinding binding, Random random) =>
        new(binding.X + Roll(random, binding.Spread), binding.Y + Roll(random, binding.Spread));

    /// <summary>
    /// Reads <c>rx</c>/<c>ry</c> from the character's script flags (<c>name:value</c>). As in the reference
    /// (<c>atoi</c>, then <c>!x || !y</c>), a missing, unreadable or zero coordinate is no return point.
    /// </summary>
    public static bool TryRead(IReadOnlyList<string> flags, out ReturnPoint point)
    {
        point = default;
        if (flags is null) return false;

        int x = 0, y = 0;
        foreach (var flag in flags)
        {
            if (TryParse(flag, out var name, out var value))
            {
                if (name == FlagX) x = value;
                else if (name == FlagY) y = value;
            }
        }

        if (x == 0 || y == 0) return false;
        point = new ReturnPoint(x, y);
        return true;
    }

    /// <summary>The flags with <c>rx</c>/<c>ry</c> replaced by <paramref name="point"/>, every other flag kept in order.</summary>
    public static string[] Write(IReadOnlyList<string> flags, ReturnPoint point)
    {
        var result = new List<string>((flags?.Count ?? 0) + 2);
        if (flags is not null)
        {
            foreach (var flag in flags)
            {
                if (flag is null) continue;
                var separator = flag.IndexOf(':');
                var name = separator < 0 ? flag : flag[..separator];
                if (name != FlagX && name != FlagY) result.Add(flag);
            }
        }

        result.Add(Format(FlagX, point.X));
        result.Add(Format(FlagY, point.Y));
        return result.ToArray();
    }

    private static string Format(string name, int value) => name + ":" + value.ToString(CultureInfo.InvariantCulture);

    private static bool TryParse(string flag, out string name, out int value)
    {
        name = null;
        value = 0;
        if (string.IsNullOrEmpty(flag)) return false;
        var separator = flag.IndexOf(':');
        if (separator <= 0) return false;
        name = flag[..separator];
        return int.TryParse(flag[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Lua's <c>math.random(0, max)</c>: both ends included.</summary>
    private static int Roll(Random random, int max) => random.Next(0, max + 1);
}
