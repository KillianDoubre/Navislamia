using System;

namespace Navislamia.Game.Services;

/// <summary>
/// The <c>ar_time_t</c> clock. Every time field on the wire is expressed in these ticks.
/// </summary>
/// <remarks>
/// One tick is 10 ms, not 1 ms. Confirmed by rzgame (<c>typedef ar_time_t rztime_t; // unit [10ms]</c>),
/// by the reference emulator (<c>GetArTime() = ms / 10</c>), and by the client itself: the shipped
/// <c>ITEM_ARRANGE_COOL_TIME = 3000</c> greys the sort button for a measured 30 seconds.
/// </remarks>
public static class ServerClock
{
    public const uint TicksPerSecond = 100;
    private const int MillisecondsPerTick = 10;

    /// <remarks>
    /// Derived from the 64-bit tick count. <c>Environment.TickCount</c> is an <c>int</c> that turns
    /// negative after 24.9 days of <b>machine</b> uptime, and dividing it before the cast made the clock
    /// jump by ~49.7 days at that instant: every <c>(int)(now - end)</c> comparison then read the past as
    /// the future, so buffs stopped expiring and cooldowns locked. A 64-bit count divided then truncated
    /// to 32 bits wraps cleanly every 497 days, which the unchecked comparisons already handle.
    /// </remarks>
    public static uint Now => unchecked((uint)(Environment.TickCount64 / MillisecondsPerTick));

    public static uint FromSeconds(decimal seconds)
    {
        return seconds <= 0m ? 0u : (uint)(seconds * TicksPerSecond);
    }

    public static uint FromSeconds(int seconds)
    {
        return seconds <= 0 ? 0u : (uint)seconds * TicksPerSecond;
    }
}
