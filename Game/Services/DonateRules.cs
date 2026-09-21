using System;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

/// <summary>
/// The judgments of <c>TM_CS_DONATE_ITEM</c> (258). The frame carries a bare value offer — gold, jp
/// and item stacks — with no recipient and no rate, so only what the server can verify is judged
/// here: the shape of the offer, then what the character actually holds. Nothing about the credit
/// side is decided in this type (fiche §5.4, §7.1).
/// </summary>
public static class DonateRules
{
    /// <summary>
    /// Judges the offer by itself, before any lookup: the amounts are unsigned in the frame's
    /// meaning (a negative gold or jp is not a gift), every item record must ask for at least one
    /// unit, no handle may appear twice — giving one stack twice is an incoherence or a double
    /// debit attempt — and an entirely empty offer is not a request at all, which is also what the
    /// client does: its frame builder emits nothing when the three fields are empty (spec §2).
    /// </summary>
    public static ResultCode CheckShape(in GameActionPackets.DonateItemRequest request)
    {
        if (request.Gold < 0 || request.Jp < 0)
        {
            return ResultCode.InvalidArgument;
        }

        var items = request.Items ?? Array.Empty<GameActionPackets.DonateItemEntry>();
        if (items.Length == 0 && request.Gold == 0 && request.Jp == 0)
        {
            return ResultCode.InvalidArgument;
        }

        for (var i = 0; i < items.Length; i++)
        {
            if (items[i].Count <= 0)
            {
                return ResultCode.InvalidArgument;
            }

            for (var j = 0; j < i; j++)
            {
                if (items[j].Handle == items[i].Handle)
                {
                    return ResultCode.InvalidArgument;
                }
            }
        }

        return ResultCode.Success;
    }

    /// <summary>
    /// Judges the offer against what the character holds. A client can name more than it owns: the
    /// bound is server side (fiche §5.3). Gold is reported before jp, the order of the frame.
    /// </summary>
    public static ResultCode CheckAffordable(long characterGold, long characterJp,
        in GameActionPackets.DonateItemRequest request)
    {
        if (request.Gold > characterGold)
        {
            return ResultCode.NotEnoughMoney;
        }

        return request.Jp > characterJp ? ResultCode.NotEnoughJP : ResultCode.Success;
    }
}
