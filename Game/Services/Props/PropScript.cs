using System;
using System.Globalization;

namespace Navislamia.Game.Services.Props;

public enum PropActionKind
{
    None,
    CommonWarpGate,
    EnterDungeon,
    ExitDungeon,
    RunTeleport,
    OpenMarket
}

/// <summary>
/// A prop script or dialog trigger resolved to what it does. Like NPC dialog triggers, the source
/// expression is looked up rather than executed as Lua. <see cref="Name"/> carries the argument of
/// <see cref="PropActionKind.OpenMarket"/> and is null for every other kind.
/// </summary>
public readonly record struct PropAction(PropActionKind Kind, int X, int Y, int DungeonId, string Name = null)
{
    public static readonly PropAction None = new(PropActionKind.None, 0, 0, 0);

    public static PropAction Warp(int x, int y) => new(PropActionKind.CommonWarpGate, x, y, 0);

    /// <summary>
    /// An <c>open_market</c> merchant trigger. The name may be empty: most triggers of the Epic 7.3
    /// dialog catalogue are the truncated form <c>open_market(</c>, whose argument was concatenated in
    /// the client's Lua and never captured.
    /// </summary>
    public static PropAction Market(string name) =>
        new(PropActionKind.OpenMarket, 0, 0, 0, name ?? string.Empty);
}

/// <summary>
/// Parses the supported prop scripts and teleport dialog triggers. Anything else resolves to
/// <see cref="PropAction.None"/>, the same "unsupported resolves to nothing" rule the passive and
/// buff effect catalogs already follow.
/// </summary>
public static class PropScript
{
    public static PropAction Parse(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return PropAction.None;

        var open = script.IndexOf('(');
        if (open <= 0)
            return PropAction.None;

        var name = script[..open].Trim();

        // The merchant entry of an NPC dialog carries the trigger as the client's Lua built it. The Epic
        // 7.3 catalogue only kept the truncated prefix "open_market(": the name was concatenated at run
        // time and the generator does not accept it. The trigger is therefore read with or without its
        // closing parenthesis, and what sits between the parentheses is the market name — empty in the
        // truncated form, which the market service then refuses rather than guessing.
        if (name == "open_market")
            return PropAction.Market(ReadMarketName(script, open));

        var close = script.LastIndexOf(')');
        if (close < open)
            return PropAction.None;

        var arguments = Split(script[(open + 1)..close]);

        return name switch
        {
            "common_warp_gate" when arguments.Length == 2 &&
                                    TryInt(arguments[0], out var x) && TryInt(arguments[1], out var y)
                => new PropAction(PropActionKind.CommonWarpGate, x, y, 0),

            // RunTeleport's first argument is a cost, which is 0 for every dialog that uses it and
            // is not charged.
            "RunTeleport" when arguments.Length == 3 &&
                               TryInt(arguments[1], out var x) && TryInt(arguments[2], out var y)
                => new PropAction(PropActionKind.RunTeleport, x, y, 0),

            "enter_dungeon" when arguments.Length == 1 && TryInt(arguments[0], out var id)
                => new PropAction(PropActionKind.EnterDungeon, 0, 0, id),

            "exit_dungeon" when arguments.Length == 1 && TryInt(arguments[0], out var id)
                => new PropAction(PropActionKind.ExitDungeon, 0, 0, id),

            _ => PropAction.None
        };
    }

    private static string[] Split(string arguments) =>
        string.IsNullOrWhiteSpace(arguments) ? Array.Empty<string>() : arguments.Split(',');

    private static string ReadMarketName(string script, int open)
    {
        var close = script.IndexOf(')', open + 1);
        var value = close < 0 ? script[(open + 1)..] : script[(open + 1)..close];
        return value.Trim();
    }

    private static bool TryInt(string value, out int result) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
}
