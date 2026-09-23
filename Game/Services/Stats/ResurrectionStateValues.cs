using System;

namespace Navislamia.Game.Services.Stats;

/// <summary>
/// The four values of a <c>SEF_RESURRECTION</c> state, in NGemity's reading
/// (<c>Unit::ResurrectByState</c>, <c>Unit.cpp</c>): HP back is
/// <c>(value_0 + value_1 × level) × max HP</c> and MP back is <c>(value_2 + value_3 × level) × max MP</c>.
/// They are ratios, not percent numbers: state 13472 carries <c>0.05</c> for 5 % of the HP.
/// </summary>
public readonly record struct ResurrectionStateValues(decimal HpBase, decimal HpPerLevel, decimal MpBase,
    decimal MpPerLevel)
{
    /// <summary>Reads <c>value_0..value_3</c>; a missing value reads 0.</summary>
    public static ResurrectionStateValues From(decimal[] values)
    {
        decimal At(int index) => values != null && index < values.Length ? values[index] : 0m;
        return new ResurrectionStateValues(At(0), At(1), At(2), At(3));
    }

    public decimal HpRatio(int level) => HpBase + HpPerLevel * Math.Max(0, level);

    public decimal MpRatio(int level) => MpBase + MpPerLevel * Math.Max(0, level);
}
