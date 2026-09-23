using System;
using System.Collections.Generic;
using System.Globalization;
using Navislamia.Game.Services.Rates;

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

    /// <summary>Default duration of <c>/buff</c>, and its ceiling: one day. Both are choices of this repository.</summary>
    public const int DefaultBuffSeconds = 300;
    public const int MaxBuffSeconds = 86_400;

    /// <summary>
    /// The highest job level <c>/joblevel</c> asks for. The real ceiling is the JP curve, which caps the
    /// first job tier around 10 (<c>JobLevelCurve</c>); this only refuses an absurd target.
    /// </summary>
    public const int MaxJobLevel = 100;

    /// <summary>
    /// The longest <c>/rate</c> event: 30 days. A choice of this repository, so a typo cannot start an
    /// event that outlives everybody's memory of it.
    /// </summary>
    public static readonly TimeSpan MaxRateEventDuration = TimeSpan.FromDays(30);

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

    /// <summary>
    /// A signed, non-zero amount (<c>/jp</c>, <c>/chaos</c>). <c>/exp</c> additionally requires it to be
    /// positive: cumulative experience never goes down, since no level loss is established.
    /// </summary>
    public static bool TryParseAmount(string[] args, bool positiveOnly, out long amount)
    {
        amount = 0;
        return args is { Length: 1 } &&
               long.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out amount) &&
               amount != 0 && (!positiveOnly || amount > 0);
    }

    /// <summary>Chaos is an int32 on the wire: the balance stays in <c>0..int.MaxValue</c>.</summary>
    public static int ApplyChaos(int current, long delta) =>
        (int)Math.Min(AddClamped(Math.Max(0, current), delta), int.MaxValue);

    /// <summary><c>/joblevel &lt;level&gt;</c>: a target above the current job level, at most <see cref="MaxJobLevel"/>.</summary>
    public static bool TryParseJobLevel(string[] args, int currentJobLevel, out int target)
    {
        target = 0;
        return args is { Length: 1 } &&
               int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out target) &&
               target > currentJobLevel && target <= MaxJobLevel;
    }

    /// <summary>
    /// <c>/learn &lt;skill&gt; [level]</c>: a positive skill id, then an optional level in 1..255. A missing level
    /// is 0 here and means "the skill's maximum", which only the catalogue knows.
    /// </summary>
    public static bool TryParseLearn(string[] args, out int skillId, out byte level)
    {
        skillId = 0;
        level = 0;
        if (args == null || args.Length is < 1 or > 2)
        {
            return false;
        }

        if (!int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out skillId) || skillId <= 0)
        {
            return false;
        }

        return args.Length == 1 ||
               (byte.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out level) && level > 0);
    }

    /// <summary>
    /// <c>/buff &lt;state&gt; [level] [seconds]</c>: a positive state id, a level in 1..65535 (1 by default) and a
    /// duration in 1..<see cref="MaxBuffSeconds"/> seconds (<see cref="DefaultBuffSeconds"/> by default).
    /// </summary>
    public static bool TryParseBuff(string[] args, out int stateId, out ushort level, out int seconds)
    {
        stateId = 0;
        level = 1;
        seconds = DefaultBuffSeconds;
        if (args == null || args.Length is < 1 or > 3)
        {
            return false;
        }

        if (!int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out stateId) || stateId <= 0)
        {
            return false;
        }

        if (args.Length >= 2 &&
            (!ushort.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out level) || level == 0))
        {
            return false;
        }

        return args.Length < 3 ||
               (int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds) &&
                seconds is > 0 and <= MaxBuffSeconds);
    }

    /// <summary>
    /// <c>/rate</c>: no argument shows the rates; <c>reset [type]</c> ends the events of a type, or of all
    /// types; <c>&lt;type&gt; &lt;multiplier&gt; &lt;duration&gt;</c> starts one. The duration is required, so an
    /// event can never stay on by oversight. The multiplier is in <c>0..maxMultiplier</c> (an <c>x</c> prefix
    /// is accepted), the duration in seconds or with a <c>s</c>/<c>m</c>/<c>min</c>/<c>h</c>/<c>d</c> suffix,
    /// up to <see cref="MaxRateEventDuration"/>.
    /// </summary>
    public static bool TryParseRate(string[] args, double maxMultiplier, out RateCommandLine command)
    {
        command = default;
        if (args == null || args.Length == 0)
        {
            command = new RateCommandLine(RateCommandKind.Show, RateTypes.All, 0, TimeSpan.Zero);
            return true;
        }

        if (args[0].Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length > 2)
            {
                return false;
            }

            IReadOnlyList<RateType> resetTypes = RateTypes.All;
            if (args.Length == 2 && !RateTypes.TryParse(args[1], out resetTypes))
            {
                return false;
            }

            command = new RateCommandLine(RateCommandKind.Reset, resetTypes, 0, TimeSpan.Zero);
            return true;
        }

        if (args.Length != 3 || !RateTypes.TryParse(args[0], out var types) ||
            !TryParseMultiplier(args[1], maxMultiplier, out var multiplier) ||
            !TryParseDuration(args[2], out var duration))
        {
            return false;
        }

        command = new RateCommandLine(RateCommandKind.Start, types, multiplier, duration);
        return true;
    }

    public static bool TryParseMultiplier(string text, double maxMultiplier, out double multiplier)
    {
        multiplier = 0;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var number = text[0] is 'x' or 'X' ? text.Substring(1) : text;
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out multiplier) &&
               double.IsFinite(multiplier) && multiplier >= 0 && multiplier <= maxMultiplier;
    }

    /// <summary>A positive duration: <c>3600</c>, <c>90s</c>, <c>30m</c>, <c>30min</c>, <c>2h</c>, <c>1d</c>.</summary>
    public static bool TryParseDuration(string text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var lower = text.ToLowerInvariant();
        var (number, unit) =
            lower.EndsWith("min") ? (lower[..^3], 60L) :
            lower.EndsWith('s') ? (lower[..^1], 1L) :
            lower.EndsWith('m') ? (lower[..^1], 60L) :
            lower.EndsWith('h') ? (lower[..^1], 3_600L) :
            lower.EndsWith('d') ? (lower[..^1], 86_400L) :
            (lower, 1L);

        if (!long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0 ||
            amount > (long)MaxRateEventDuration.TotalSeconds / unit)
        {
            return false;
        }

        duration = TimeSpan.FromSeconds(amount * unit);
        return true;
    }

    /// <summary>A sum that saturates at <see cref="long.MaxValue"/> and never goes below zero.</summary>
    public static long AddClamped(long current, long delta) => ApplyGold(current, delta);

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

public enum RateCommandKind
{
    Show,
    Start,
    Reset
}

/// <summary>A parsed <c>/rate</c>: what to do, on which types, and for a start by how much and how long.</summary>
public readonly record struct RateCommandLine(RateCommandKind Kind, IReadOnlyList<RateType> Types,
    double Multiplier, TimeSpan Duration);
