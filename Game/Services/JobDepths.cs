using System.Collections.Generic;
using System.Linq;

namespace Navislamia.Game.Services;

/// <summary>
/// <c>JobResource.job_depth</c> read as the official server reads it: a depth <b>index</b> 0..3 (a base job 0,
/// a first job 1, …), which the item and quest gates turn into a bit, <c>1 &lt;&lt; GetJobDepth()</c>
/// (<c>StructPlayer::TranslateWearPosition</c>, <c>AuctionManager.cpp:400</c>). The 9.4 export holds the index
/// (job 100 → 0, 101 → 1, 110 → 2, 120 → 3); whether the imported <c>JobResources.JobDepth</c> kept it or a bit
/// 1/2/4/8 is not established, so the encoding is read once from the whole table: a 0 or a 3 can only be an
/// index, a 4 or an 8 only a bit.
/// </summary>
public static class JobDepths
{
    public static bool AreFlags(IEnumerable<short> depths)
    {
        var values = depths.ToHashSet();
        return values.Contains(4) || values.Contains(8) || !(values.Contains(0) || values.Contains(3));
    }

    /// <summary>The depth index 0..3 of a stored value, -1 for a value of neither encoding.</summary>
    public static int ToIndex(short value, bool flags) => flags
        ? value switch { 1 => 0, 2 => 1, 4 => 2, 8 => 3, _ => -1 }
        : value is >= 0 and <= 3 ? value : -1;
}
