using System;
using System.Globalization;

namespace Navislamia.Game.Services.Props;

public enum PropActionKind
{
    None,
    CommonWarpGate,
    EnterDungeon,
    ExitDungeon,
    RunTeleport
}

/// <summary>
/// A prop script or dialog trigger resolved to what it does. Like NPC dialog triggers, the source
/// expression is looked up rather than executed as Lua.
/// </summary>
public readonly record struct PropAction(PropActionKind Kind, int X, int Y, int DungeonId)
{
    public static readonly PropAction None = new(PropActionKind.None, 0, 0, 0);

    public static PropAction Warp(int x, int y) => new(PropActionKind.CommonWarpGate, x, y, 0);
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
        var close = script.LastIndexOf(')');
        if (open <= 0 || close < open)
            return PropAction.None;

        var name = script[..open].Trim();
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

    private static bool TryInt(string value, out int result) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
}
