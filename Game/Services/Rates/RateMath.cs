using System;

namespace Navislamia.Game.Services.Rates;

/// <summary>How a rate turns an integer amount into another integer amount.</summary>
public static class RateMath
{
    /// <summary>
    /// <paramref name="value"/> × <paramref name="rate"/>, rounded at random so a fractional rate is exact
    /// on average: 7 × 1.5 = 10.5 gives 10 or 11 with one chance in two each.
    /// </summary>
    /// <remarks>
    /// This is what NGemity's <c>GameRule::GetIntValueByRandomInt64</c> is written to do, but its test
    /// <c>(rand % 100) / 100.0 + v &gt;= v</c> is always true, so it always truncates. The intent is taken
    /// here, not the defect. A non-positive value or rate gives 0; the result saturates at
    /// <see cref="long.MaxValue"/>.
    /// </remarks>
    public static long ScaleRandom(long value, double rate, Random random)
    {
        if (value <= 0 || !(rate > 0))
        {
            return 0;
        }

        var scaled = value * rate;
        if (scaled >= long.MaxValue)
        {
            return long.MaxValue;
        }

        var whole = Math.Floor(scaled);
        var fraction = scaled - whole;
        var result = (long)whole;
        return fraction > 0 && random.NextDouble() < fraction ? result + 1 : result;
    }

    /// <summary>
    /// A cost × <paramref name="rate"/>, rounded up so a reduced cost is never free by accident: only a rate
    /// of 0 makes it 0. Rounding at random would show the player a different price on each attempt.
    /// </summary>
    public static long ScaleCost(long cost, double rate)
    {
        if (cost <= 0 || !(rate > 0))
        {
            return 0;
        }

        var scaled = Math.Ceiling(cost * rate);
        return scaled >= long.MaxValue ? long.MaxValue : (long)scaled;
    }

    /// <summary>A configured rate read defensively: NaN, infinity or a negative number count as 0.</summary>
    public static double Sanitize(double rate) => double.IsFinite(rate) && rate > 0 ? rate : 0;
}
