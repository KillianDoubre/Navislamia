using System;

namespace Navislamia.Game.Services.Huntaholic;

/// <summary>
/// What a session holds of HuntaHolic and saves with its progress: the points (<c>huntaholicpoint</c>) and
/// the entries left today (<c>huntaholic_ent</c>). See docs/packet-specs/socle-huntaholic.md.
/// </summary>
public readonly record struct HuntaholicProgress(int Point, int EnterCount);

/// <summary>
/// The daily entry refill of the official server (<c>DB_Login.cpp:644-711</c> at login,
/// <c>StructPlayer.cpp:7077-7095</c> while online): <see cref="EntriesPerDay"/> at 06:00 local time.
/// </summary>
public static class HuntaholicEntryRefill
{
    /// <summary><c>GameRule::HUNTAHOLIC_ENTERANCE_COUNT_PER_DAY</c>.</summary>
    public const int EntriesPerDay = 12;

    /// <summary><c>GameRule::HUNTAHOLIC_MAX_OWNABLE_POINT</c>.</summary>
    public const int MaxOwnablePoint = 2_000_000_000;

    private const int RefillHour = 6;

    /// <summary>
    /// The refill owed after a logout at <paramref name="logout"/>: 06:00 that day, or the next day when the
    /// logout came after it. A reconnection within a minute compares with a minute earlier, like the
    /// official <c>tLogoutDuration</c>; no logout time at all (a character never saved) owes a refill now.
    /// </summary>
    public static DateTime RefillAfterLogout(DateTime? logout, DateTime now)
    {
        if (logout is not { } last)
        {
            return DateTime.MinValue;
        }

        if (now - last < TimeSpan.FromMinutes(1))
        {
            last = now - TimeSpan.FromMinutes(1);
        }

        var refill = last.Date.AddHours(RefillHour);
        return last > refill ? refill.AddDays(1) : refill;
    }

    /// <summary>The next refill after one done at <paramref name="now"/>: 06:00 today, or tomorrow from 06:00.</summary>
    public static DateTime NextRefill(DateTime now)
    {
        var today = now.Date.AddHours(RefillHour);
        return now.Hour >= RefillHour ? today.AddDays(1) : today;
    }
}
