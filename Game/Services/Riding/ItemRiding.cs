using System;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services.Riding;

/// <summary>The riding state a ride item put on, and what it carries (<c>m_nRidingStateCode</c>).</summary>
public sealed record ItemRide(int StateId, RidingStateValues Values, uint ItemHandle);

/// <summary>
/// Riding by item (<c>ITEM_EFFECT_INSTANT::TOGGLE_STATE</c> on a ride item, <c>EF_RIDING</c> state), the official
/// <c>HasRidingState</c> next to <c>IsRiding</c> for a summon (docs/packet-specs/socle-monture-objet.md).
/// </summary>
public static class ItemRiding
{
    /// <summary>
    /// The location types a mount is refused in (<c>IsMountable</c>: secret and instance dungeons, battle arena,
    /// prayer hall); a dungeon proper is the dungeon cells (<c>IsInDungeon</c>).
    /// </summary>
    private static readonly short[] UnmountableLocations = { 12, 14, 15, 16 };

    /// <summary>
    /// <c>StructPlayer::IsMountable(true)</c>, the place alone: not in a dungeon, secret or instance dungeon,
    /// arena or prayer hall.
    /// </summary>
    public static bool IsMountablePlace(float x, float y, short locationType) =>
        !Progression.MonsterRewardBonuses.InDungeon(x, y) && Array.IndexOf(UnmountableLocations, locationType) < 0;

    /// <summary>
    /// The ride the character is on, null on foot. A ride whose state went (expiry, cancel, removal) is forgotten
    /// here, so no path that removes a state has to know about riding.
    /// </summary>
    public static ItemRide Current(ConnectionInfo info)
    {
        var ride = info.ItemRide;
        if (ride is null)
        {
            return null;
        }

        lock (info.BuffLock)
        {
            if (info.ActiveBuffs.Exists(buff => buff.StateId == ride.StateId))
            {
                return ride;
            }
        }

        if (ReferenceEquals(info.ItemRide, ride))
        {
            info.ItemRide = null;
        }

        return null;
    }

    /// <summary><c>HasRidingState()</c>.</summary>
    public static bool IsRiding(ConnectionInfo info) => Current(info) is not null;

    /// <summary>
    /// The speed byte of a rider (<c>GetRealRidingSpeed</c>: <c>fRidingSpeed / 7</c>), the walking one kept when the
    /// state says so and it is faster (<c>CalculateStat.cpp:332-335</c>); null on foot.
    /// </summary>
    public static byte? Speed(ConnectionInfo info, byte walkingSpeed)
    {
        var ride = Current(info);
        if (ride is null)
        {
            return null;
        }

        var riding = (byte)Math.Clamp((int)(ride.Values.Speed / 7m), 0, byte.MaxValue);
        return ride.Values.KeepFasterSpeed && walkingSpeed > riding ? walkingSpeed : riding;
    }

    /// <summary>
    /// <c>StructCreature::onDamage</c>: a hit throws the rider off when <c>rand() % 100</c> does not exceed the state's
    /// chance (<c>value_7</c> on a critical hit, then <c>value_6</c>).
    /// </summary>
    public static bool FallsOnHit(RidingStateValues values, bool critical, Func<int, int> next) =>
        (critical && next(100) <= values.UnmountOnCritical) || next(100) <= values.UnmountOnDamage;
}
