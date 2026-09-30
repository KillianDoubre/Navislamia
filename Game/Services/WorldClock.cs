using System;
using System.Threading;

namespace Navislamia.Game.Services;

/// <summary>
/// The in-world clock of <c>TS_SC_GAME_TIME.game_time</c> (1101): <b>Unix time in seconds</b>, which is what
/// the official server sends (<c>SendGameTime</c> writes <c>_time64()</c>, <c>CaptainHerlockServer.exe</c>
/// <c>0x14014e560</c>) and NGemity too (<c>time(nullptr)</c>, <c>Messages.cpp:321</c>). The client turns it
/// into its day and night on its own. <see cref="OffsetSeconds"/> shifts it for every player, to test the
/// cycle without waiting (<c>/gametime</c>); it starts at 0 and is not persisted.
/// </summary>
public static class WorldClock
{
    private static long _offsetSeconds;

    public static long OffsetSeconds
    {
        get => Interlocked.Read(ref _offsetSeconds);
        set => Interlocked.Exchange(ref _offsetSeconds, value);
    }

    public static ulong GameTime(DateTimeOffset now) =>
        (ulong)Math.Max(0, now.ToUnixTimeSeconds() + OffsetSeconds);

    public static ulong GameTimeNow => GameTime(DateTimeOffset.UtcNow);
}
