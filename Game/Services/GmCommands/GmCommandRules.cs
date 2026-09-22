using System;
using System.Globalization;

namespace Navislamia.Game.Services.GmCommands;

/// <summary>
/// The pure decisions behind the GM commands: who may run what, and how each argument list is read and
/// bounded. The service is the I/O shell around these (docs/gm-commands.md).
/// </summary>
public static class GmCommandRules
{
    /// <summary>
    /// NGemity's threshold: a privileged command needs <c>GetPermission() &gt;= 100</c>
    /// (<c>AllowedCommandInfo::Run</c>). Read from <c>Characters.Permission</c>.
    /// </summary>
    public const int GmPermission = 100;

    /// <summary>
    /// The largest stack <c>/item</c> creates. No source bounds a stack in 7.3; this is a guard of this
    /// repository against a typo creating an absurd row, not a game rule.
    /// </summary>
    public const long MaxItemCount = 10_000;

    public static bool CanUse(GmCommandDefinition definition, int permission) =>
        definition != null && (!definition.Privileged || permission >= GmPermission);

    /// <summary>
    /// <c>on</c>/<c>off</c> switch of <c>/battle</c> and <c>/walk</c>. With no argument the command takes
    /// <paramref name="whenMissing"/>; any word other than the two is refused.
    /// </summary>
    public static bool TryParseSwitch(string[] args, bool whenMissing, out bool value)
    {
        value = whenMissing;
        if (args == null || args.Length == 0)
        {
            return true;
        }

        if (args.Length > 1)
        {
            return false;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "on":
                value = true;
                return true;
            case "off":
                value = false;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// <c>/warp &lt;x&gt; &lt;y&gt;</c>: two finite, non-negative world coordinates, read with the invariant
    /// culture so <c>94454.5</c> means the same on every machine. The world has no negative coordinate.
    /// </summary>
    public static bool TryParseWarp(string[] args, out float x, out float y)
    {
        x = 0;
        y = 0;
        return args is { Length: 2 } &&
               TryParseCoordinate(args[0], out x) &&
               TryParseCoordinate(args[1], out y);
    }

    /// <summary><c>/item &lt;code&gt; [count]</c>: a positive resource id, then a count in 1..MaxItemCount.</summary>
    public static bool TryParseItem(string[] args, out int code, out long count)
    {
        code = 0;
        count = 1;
        if (args == null || args.Length is < 1 or > 2)
        {
            return false;
        }

        if (!int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out code) || code <= 0)
        {
            return false;
        }

        if (args.Length == 2 &&
            (!long.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out count) ||
             count < 1 || count > MaxItemCount))
        {
            return false;
        }

        return true;
    }

    /// <summary><c>/gold &lt;amount&gt;</c>: a signed delta, so <c>/gold -500</c> takes gold back.</summary>
    public static bool TryParseGold(string[] args, out long delta)
    {
        delta = 0;
        return args is { Length: 1 } &&
               long.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out delta) &&
               delta != 0;
    }

    /// <summary>The balance after a delta, never below zero and never wrapped past <see cref="long.MaxValue"/>.</summary>
    public static long ApplyGold(long current, long delta)
    {
        if (delta > 0 && current > long.MaxValue - delta)
        {
            return long.MaxValue;
        }

        return Math.Max(0, current + delta);
    }

    /// <summary>
    /// <c>/level &lt;level&gt;</c>: a target above the current level and at most the curve's maximum. Only
    /// upward: a level-up is the one sequence this repository sends (<c>LevelingService</c>), and no
    /// packet sequence for losing a level is established.
    /// </summary>
    public static bool TryParseLevel(string[] args, int currentLevel, int maxLevel, out int target)
    {
        target = 0;
        return args is { Length: 1 } &&
               int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out target) &&
               target > currentLevel && target <= maxLevel;
    }

    /// <summary>The text <c>/position</c> answers.</summary>
    public static string FormatPosition(float x, float y, float z, byte layer) =>
        string.Create(CultureInfo.InvariantCulture, $"X: {x:0.##} Y: {y:0.##} Z: {z:0.##} Layer: {layer}");

    private static bool TryParseCoordinate(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
        float.IsFinite(value) && value >= 0;
}
