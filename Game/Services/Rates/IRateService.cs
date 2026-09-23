using System;
using System.Collections.Generic;

namespace Navislamia.Game.Services.Rates;

/// <summary>What <c>/rate</c> and <c>/rates</c> show for one type.</summary>
public readonly record struct RateStatus(RateType Type, double Base, double Event, double Effective,
    TimeSpan? Remaining);

/// <summary>
/// The server rates: the configured base (the <c>Rates</c> section, re-read on every call so an edit of the
/// settings applies live) multiplied by the running <c>/rate</c> event, if any. See docs/gm-commands.md.
/// </summary>
public interface IRateService
{
    /// <summary>Base × event for a type.</summary>
    double Get(RateType type);

    /// <summary><paramref name="value"/> × the rate of <paramref name="type"/>, rounded at random (<see cref="RateMath.ScaleRandom"/>).</summary>
    long Scale(long value, RateType type);

    TimeSpan MonsterRespawnDelay { get; }

    TimeSpan GroundItemLifetime { get; }

    double SkillJpCost { get; }

    double JobLevelJpCost { get; }

    double MaxEventMultiplier { get; }

    IReadOnlyList<RateStatus> Status();

    /// <summary>Starts (or replaces) an event and returns its start announcement.</summary>
    string StartEvent(IReadOnlyCollection<RateType> types, double multiplier, TimeSpan duration, string startedBy);

    /// <summary>Ends the given types early and returns the announcements, empty when nothing was running.</summary>
    IReadOnlyList<string> ResetEvents(IReadOnlyCollection<RateType> types, string resetBy);

    /// <summary>Removes finished events and returns the end and reminder announcements now due.</summary>
    IReadOnlyList<string> Tick();
}
