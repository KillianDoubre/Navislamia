using System;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Navislamia.Game.Services.Weight;

/// <summary>
/// The weight rules of the official server (<c>CaptainHerlockServer.exe</c>, Epic 7 Part 4) and NGemity,
/// which agree: an item weighs its resource's <c>weight</c> times its count (<c>Item::GetWeight</c>), a worn
/// item weighs nothing (<c>Inventory::Push</c> takes it back through the weight modifier), and the carried
/// weight against the maximum slows the walk (<c>StructPlayer::GetMoveSpeed</c>). See
/// <c>docs/packet-specs/socle-poids.md</c>.
/// </summary>
public static class WeightRules
{
    /// <summary>From three quarters of the maximum the walk is halved (<c>__real@3f400000</c>).</summary>
    public const float SlowRatio = 0.75f;

    public const float SlowFactor = 0.5f;

    /// <summary>At or past the maximum the walk falls to a tenth (<c>__real@3dcccccd</c>).</summary>
    public const float OverloadFactor = 0.1f;

    /// <summary>What the bag weighs: every item not worn, resource weight times count.</summary>
    public static float Carried(IEnumerable<ItemEntity> items, Func<int, float> weightOf)
    {
        var total = 0f;
        foreach (var item in items)
        {
            if (item.WearInfo == ItemWearType.None)
            {
                total += weightOf((int)item.ItemResourceId) * item.Amount;
            }
        }

        return total;
    }

    /// <summary>
    /// Whether <paramref name="added"/> more still fits: the reference refuses when carried plus added passes
    /// the maximum (<c>Player::IsTakeable</c>, <c>onBuyItem</c>, <c>CheckTradeWeight</c>).
    /// </summary>
    public static bool Fits(float carried, float maximum, float added) => carried + added <= maximum;

    /// <summary>
    /// The walk speed a load allows: unchanged under 75 %, halved from 75 %, a tenth at or past 100 % — and a
    /// tenth for a negative ratio too, as the reference writes it.
    /// </summary>
    /// <summary>
    /// The <c>TS_SC_MOVE</c> speed of a character: its move speed slowed by the load, divided by 7
    /// (<c>StructPlayer::GetMoveSpeed</c> then <c>StructCreature::GetRealMoveSpeed</c>).
    /// </summary>
    public static byte RealMoveSpeed(float moveSpeed, float carried, float maximum)
    {
        var speed = (int)moveSpeed;
        if (!float.IsPositiveInfinity(maximum))
        {
            var ratio = maximum > 0 ? carried / maximum : float.PositiveInfinity;
            if (ratio >= 1f || ratio < 0f)
            {
                speed = (int)(speed * OverloadFactor);
            }
            else if (ratio >= SlowRatio)
            {
                speed = (int)(speed * SlowFactor);
            }
        }

        return Navislamia.Game.Services.MonsterMovement.SpeedByte(speed);
    }

    public static byte MoveSpeed(byte speed, float carried, float maximum)
    {
        if (float.IsPositiveInfinity(maximum))
        {
            return speed;
        }

        var ratio = maximum > 0 ? carried / maximum : float.PositiveInfinity;
        if (ratio >= 1f || ratio < 0f)
        {
            return (byte)(int)(speed * OverloadFactor);
        }

        return ratio >= SlowRatio ? (byte)(int)(speed * SlowFactor) : speed;
    }

    /// <summary>
    /// Whether the bag may be taken off (<c>Unit::putoffItem</c> on <c>WEAR_BAG_SLOT</c>): not while already
    /// overloaded, and not if the load would pass the maximum without the bag's own capacity.
    /// </summary>
    public static bool CanTakeOffBag(float carried, float maximum, float bagCapacity) =>
        maximum >= carried && maximum - bagCapacity >= carried;
}
