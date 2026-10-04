using System;
using Navislamia.Configuration.Options;

namespace Navislamia.Game.Services.Auction;

public static class AutoAuctionSchedule
{
    /// <summary>One registration per resource and elapsed period; downtime never creates a backlog.</summary>
    public static bool IsDue(AutoAuctionRow row, DateTime? lastRegistered, DateTime now)
    {
        if (row.Id <= 0 || row.Price <= 0 || row.ItemCode <= 0 || row.EnrollmentTime > now
            || (row.Repeat && row.RepeatDays <= 0)) return false;
        if (lastRegistered is null) return true;
        if (!row.Repeat) return false;
        var period = TimeSpan.FromDays(row.RepeatDays).Ticks;
        var next = row.EnrollmentTime.Ticks + ((lastRegistered.Value.Ticks - row.EnrollmentTime.Ticks) / period + 1) * period;
        return now.Ticks >= next;
    }
}
