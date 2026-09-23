using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Navislamia.Game.Services.Rates;

/// <summary>
/// One running <c>/rate</c> event: the rate types it multiplies, by how much, and until when (UTC wall
/// clock, so it survives a restart). Its type set shrinks when a later event or a reset takes a type over.
/// </summary>
public sealed class RateEvent
{
    public List<RateType> Types { get; set; } = new();
    public double Multiplier { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public string StartedBy { get; set; } = string.Empty;
    public bool ReminderSent { get; set; }

    public string Label => $"{RateTypes.Label(Types)} x{RateEventBook.FormatMultiplier(Multiplier)}";
}

/// <summary>
/// The running rate events and every decision about them, kept pure — no clock, file or socket — so the
/// rules are testable: a new event on a type <b>replaces</b> the one already running on it (x2 then x3 is
/// x3, not x6), an event always has an end, and an expired event stops counting at its end time even if
/// nothing has ticked yet. Not thread-safe: <c>RateService</c> owns the lock.
/// </summary>
public sealed class RateEventBook
{
    private readonly List<RateEvent> _events = new();

    public IReadOnlyList<RateEvent> Events => _events;

    /// <summary>The event multiplier of a type at <paramref name="nowUtc"/>, 1 when none is running.</summary>
    public double Multiplier(RateType type, DateTime nowUtc)
    {
        foreach (var rateEvent in _events)
        {
            if (rateEvent.EndsAtUtc > nowUtc && rateEvent.Types.Contains(type))
            {
                return rateEvent.Multiplier;
            }
        }

        return 1;
    }

    /// <summary>The event running on a type at <paramref name="nowUtc"/>, or null.</summary>
    public RateEvent Find(RateType type, DateTime nowUtc) =>
        _events.FirstOrDefault(rateEvent => rateEvent.EndsAtUtc > nowUtc && rateEvent.Types.Contains(type));

    /// <summary>Starts an event, taking its types away from any event already running on them.</summary>
    public RateEvent Start(IReadOnlyCollection<RateType> types, double multiplier, DateTime nowUtc,
        TimeSpan duration, string startedBy)
    {
        Detach(types);

        var rateEvent = new RateEvent
        {
            Types = RateTypes.All.Where(types.Contains).ToList(),
            Multiplier = multiplier,
            StartedAtUtc = nowUtc,
            EndsAtUtc = nowUtc + duration,
            StartedBy = startedBy ?? string.Empty
        };

        _events.Add(rateEvent);
        return rateEvent;
    }

    /// <summary>
    /// Ends the given types early. Returns one announcement per event that lost a type, naming only the
    /// types it lost: resetting <c>exp</c> during an <c>all</c> event leaves the other rates running.
    /// </summary>
    public IReadOnlyList<string> Reset(IReadOnlyCollection<RateType> types, DateTime nowUtc)
    {
        var notices = new List<string>();
        foreach (var rateEvent in _events.ToList())
        {
            var lost = rateEvent.Types.Where(types.Contains).ToList();
            if (lost.Count == 0)
            {
                continue;
            }

            if (rateEvent.EndsAtUtc > nowUtc)
            {
                notices.Add($"The {RateTypes.Label(lost)} x{FormatMultiplier(rateEvent.Multiplier)} event has been ended.");
            }

            rateEvent.Types.RemoveAll(lost.Contains);
            if (rateEvent.Types.Count == 0)
            {
                _events.Remove(rateEvent);
            }
        }

        return notices;
    }

    /// <summary>
    /// Advances the book to <paramref name="nowUtc"/>: removes the finished events and announces their end,
    /// and announces a reminder <paramref name="reminder"/> before the end of an event long enough to need
    /// one. Returns true in <paramref name="changed"/> when the book must be saved again.
    /// </summary>
    public IReadOnlyList<string> Tick(DateTime nowUtc, TimeSpan reminder, out bool changed)
    {
        changed = false;
        var notices = new List<string>();

        foreach (var rateEvent in _events.ToList())
        {
            if (rateEvent.EndsAtUtc <= nowUtc)
            {
                _events.Remove(rateEvent);
                notices.Add($"The {rateEvent.Label} event is over.");
                changed = true;
                continue;
            }

            // An event shorter than the reminder window would announce its reminder at its own start.
            if (!rateEvent.ReminderSent && reminder > TimeSpan.Zero &&
                rateEvent.EndsAtUtc - rateEvent.StartedAtUtc > reminder &&
                rateEvent.EndsAtUtc - nowUtc <= reminder)
            {
                rateEvent.ReminderSent = true;
                notices.Add($"The {rateEvent.Label} event ends in {FormatDuration(rateEvent.EndsAtUtc - nowUtc)}.");
                changed = true;
            }
        }

        return notices;
    }

    /// <summary>
    /// Loads saved events, dropping the ones that ended while the server was down and any entry a hand
    /// edit made meaningless (no type, a non-finite or negative multiplier). Returns how many were kept.
    /// </summary>
    public int Load(IEnumerable<RateEvent> saved, DateTime nowUtc)
    {
        _events.Clear();
        foreach (var rateEvent in saved ?? Enumerable.Empty<RateEvent>())
        {
            if (rateEvent?.Types is not { Count: > 0 } || rateEvent.EndsAtUtc <= nowUtc ||
                !double.IsFinite(rateEvent.Multiplier) || rateEvent.Multiplier < 0)
            {
                continue;
            }

            rateEvent.Types = RateTypes.All.Where(rateEvent.Types.Contains).ToList();
            Detach(rateEvent.Types);
            _events.Add(rateEvent);
        }

        return _events.Count;
    }

    private void Detach(IEnumerable<RateType> types)
    {
        var taken = types.ToHashSet();
        foreach (var rateEvent in _events.ToList())
        {
            rateEvent.Types.RemoveAll(taken.Contains);
            if (rateEvent.Types.Count == 0)
            {
                _events.Remove(rateEvent);
            }
        }
    }

    public static string FormatMultiplier(double multiplier) =>
        multiplier.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A compact duration for the announcements: <c>2d 3h</c>, <c>1h 30min</c>, <c>45min</c>, <c>30s</c>.</summary>
    public static string FormatDuration(TimeSpan duration)
    {
        // Rounded up to the second, so a reminder ticking at 4:59.6 still reads "5min".
        var total = (long)Math.Ceiling(Math.Max(0, duration.TotalSeconds));
        var days = total / 86_400;
        var hours = total % 86_400 / 3_600;
        var minutes = total % 3_600 / 60;
        var seconds = total % 60;

        var parts = new List<string>();
        if (days > 0) parts.Add($"{days}d");
        if (hours > 0) parts.Add($"{hours}h");
        if (minutes > 0) parts.Add($"{minutes}min");
        if (seconds > 0 && days == 0 && hours == 0) parts.Add($"{seconds}s");
        return parts.Count == 0 ? "0s" : string.Join(" ", parts);
    }
}
