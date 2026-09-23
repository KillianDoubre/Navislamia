using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Serilog;

namespace Navislamia.Game.Services.Rates;

/// <summary>
/// The I/O shell around <see cref="RateEventBook"/>: it reads the <c>Rates</c> settings live, holds the lock
/// the combat, drop and command threads share, and saves the running events so a restart resumes them with
/// the time they had left — a maintenance restart must not cut short an announced x2 weekend.
/// </summary>
public class RateService : IRateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILogger _logger = Log.ForContext<RateService>();
    private readonly IOptionsMonitor<RatesOptions> _options;
    private readonly Func<DateTime> _clock;
    private readonly RateEventBook _book = new();
    private readonly Random _random = new();
    private readonly object _lock = new();

    public RateService(IOptionsMonitor<RatesOptions> options) : this(options, () => DateTime.UtcNow)
    {
    }

    public RateService(IOptionsMonitor<RatesOptions> options, Func<DateTime> clock)
    {
        _options = options;
        _clock = clock;
        LoadEvents();
    }

    private RatesOptions Options => _options.CurrentValue ?? new RatesOptions();

    public double Get(RateType type)
    {
        var baseRate = Base(Options, type);
        lock (_lock)
        {
            return baseRate * _book.Multiplier(type, _clock());
        }
    }

    public long Scale(long value, RateType type)
    {
        var rate = Get(type);
        lock (_random)
        {
            return RateMath.ScaleRandom(value, rate, _random);
        }
    }

    public TimeSpan MonsterRespawnDelay => TimeSpan.FromSeconds(Math.Max(0, Options.MonsterRespawnSeconds));

    public TimeSpan GroundItemLifetime => TimeSpan.FromSeconds(Math.Max(1, Options.GroundItemLifetimeSeconds));

    public double SkillJpCost => RateMath.Sanitize(Options.SkillJpCost);

    public double JobLevelJpCost => RateMath.Sanitize(Options.JobLevelJpCost);

    public double MaxEventMultiplier => RateMath.Sanitize(Options.MaxEventMultiplier);

    public IReadOnlyList<RateStatus> Status()
    {
        var options = Options;
        var now = _clock();
        lock (_lock)
        {
            return RateTypes.All.Select(type =>
            {
                var baseRate = Base(options, type);
                var running = _book.Find(type, now);
                var eventRate = running?.Multiplier ?? 1;
                return new RateStatus(type, baseRate, eventRate, baseRate * eventRate,
                    running == null ? null : running.EndsAtUtc - now);
            }).ToList();
        }
    }

    public string StartEvent(IReadOnlyCollection<RateType> types, double multiplier, TimeSpan duration,
        string startedBy)
    {
        RateEvent started;
        lock (_lock)
        {
            started = _book.Start(types, multiplier, _clock(), duration, startedBy);
            SaveEvents();
        }

        _logger.Information("GM {gm} started the {label} rate event for {duration}", startedBy, started.Label,
            RateEventBook.FormatDuration(duration));
        return $"Event: {started.Label} for {RateEventBook.FormatDuration(duration)}!";
    }

    public IReadOnlyList<string> ResetEvents(IReadOnlyCollection<RateType> types, string resetBy)
    {
        IReadOnlyList<string> notices;
        lock (_lock)
        {
            notices = _book.Reset(types, _clock());
            SaveEvents();
        }

        _logger.Information("GM {gm} reset the {types} rate events: {notices}", resetBy, RateTypes.Label(types),
            notices);
        return notices;
    }

    public IReadOnlyList<string> Tick()
    {
        var reminder = TimeSpan.FromMinutes(Math.Max(0, Options.EventReminderMinutes));
        IReadOnlyList<string> notices;
        lock (_lock)
        {
            notices = _book.Tick(_clock(), reminder, out var changed);
            if (changed)
            {
                SaveEvents();
            }
        }

        foreach (var notice in notices)
        {
            _logger.Information("Rate event: {notice}", notice);
        }

        return notices;
    }

    /// <summary>An unset <c>Jp</c> follows <c>Exp</c>, which is NGemity's single <c>EXPRate</c>.</summary>
    private static double Base(RatesOptions options, RateType type) => RateMath.Sanitize(type switch
    {
        RateType.Exp => options.Exp,
        RateType.Jp => options.Jp ?? options.Exp,
        RateType.Gold => options.Gold,
        RateType.ItemDrop => options.ItemDrop,
        RateType.CreatureCardDrop => options.CreatureCardDrop,
        _ => 1
    });

    private string StatePath => Options.EventStatePath;

    private void LoadEvents()
    {
        var path = StatePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            var saved = JsonSerializer.Deserialize<List<RateEvent>>(File.ReadAllText(path), JsonOptions);
            int kept;
            lock (_lock)
            {
                kept = _book.Load(saved, _clock());
            }

            _logger.Information("Resumed {kept} rate event(s) from {path}", kept, path);
        }
        catch (Exception exception)
        {
            // A broken file must not stop the server: it starts without events, and says so.
            _logger.Error(exception, "Could not read the rate events from {path}; starting without any", path);
        }
    }

    /// <summary>Written to a temporary file then moved, so a crash mid-write never leaves half a file.</summary>
    private void SaveEvents()
    {
        var path = StatePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_book.Events, JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not save the rate events to {path}; they will not survive a restart",
                path);
        }
    }
}
