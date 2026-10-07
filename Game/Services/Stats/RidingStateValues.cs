namespace Navislamia.Game.Services.Stats;

/// <summary>
/// What an <c>EF_RIDING</c> (200) state carries (<c>StructPlayer::applyState</c>, <c>CalculateStat.cpp:3059-3069</c>,
/// and <c>GetUnMountProbabilityOnDamage</c>/<c>…OnCriticalDamage</c>, <c>StructPlayer.cpp:12041-12075</c>):
/// <c>value_0</c> is the riding speed, <c>value_6</c> and <c>value_7</c> the percent chance a hit and a critical hit
/// throw the rider off, and a non-zero <c>value_8</c> keeps the walking speed when it is faster.
/// </summary>
public readonly record struct RidingStateValues(decimal Speed, int UnmountOnDamage, int UnmountOnCritical,
    bool KeepFasterSpeed)
{
    /// <summary>The <c>StateResource.effect_type</c> of a riding state.</summary>
    public const int EffectType = 200;

    public static RidingStateValues From(decimal[] values)
    {
        decimal At(int index) => values != null && index < values.Length ? values[index] : 0m;
        return new RidingStateValues(At(0), (int)At(6), (int)At(7), At(8) != 0m);
    }
}
