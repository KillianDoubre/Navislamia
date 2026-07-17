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

    public static uint Now => unchecked((uint)(Environment.TickCount / MillisecondsPerTick));

    public static uint FromSeconds(decimal seconds)
    {
        return seconds <= 0m ? 0u : (uint)(seconds * TicksPerSecond);
    }

    public static uint FromSeconds(int seconds)
    {
        return seconds <= 0 ? 0u : (uint)seconds * TicksPerSecond;
    }
}
