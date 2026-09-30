using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

/// <summary>
/// One reserved line of a booth trade: which declared item of the booth it draws on, the item of the
/// giver's bag that changes hands, how many units, and the unit price the owner declared.
/// </summary>
public readonly record struct BoothTradeLine(int BoothIndex, uint ItemHandle, long Count, long UnitPrice)
{
    public long Price => Count * UnitPrice;
}

/// <summary>The verdict of a reservation, with the lines it took and what they cost in all.</summary>
public sealed record BoothReservation(ResultCode Result, IReadOnlyList<BoothTradeLine> Lines, long Total)
{
    public static BoothReservation Refused(ResultCode result) =>
        new(result, Array.Empty<BoothTradeLine>(), 0);
}

/// <summary>
/// The trade rules of the player booth (docs/packet-specs/705-buy-from-booth.md §5). No reference
/// implements a booth trade — NGemity declares the packets and handles none — so these rules are
/// Navislamia's, each one stated where the client or a reference constrains it:
/// <list type="bullet">
/// <item><description><b>type 1 sells, type 2 buys</b>: the 7.3 client makes 1 the default of its creation
/// window (<c>setne al</c> / <c>inc al</c>, <c>SFrame.exe 0x48D5FB</c>) and keeps <c>type == 1</c> as the
/// flag its 703 window reads (<c>0x673347-0x67334F</c>), the one 710 names <c>is_sell</c>;</description></item>
/// <item><description>the declared gold is a <b>unit price</b>, the declared count the units on offer (a
/// sell booth) or wanted (a buy booth);</description></item>
/// <item><description>the reservation takes the units off the booth <b>before</b> any gold or item moves,
/// so two customers can never buy the same unit, and gives them back if the trade fails;</description></item>
/// <item><description>gold is capped at NGemity's <c>MAX_GOLD_FOR_INVENTORY</c>
/// (<c>ItemTemplate.hpp:4</c>): 100 000 000 000.</description></item>
/// </list>
/// Every function is pure: <c>ConnectionInfo.UpdateBooth</c> runs them under the booth lock.
/// </summary>
public static class BoothTradeRules
{
    public const byte SellBooth = 1;
    public const byte BuyBooth = 2;

    /// <summary>NGemity <c>MAX_GOLD_FOR_INVENTORY</c> (<c>Entities/Item/ItemTemplate.hpp:4</c>).</summary>
    public const long MaxGold = 100_000_000_000;

    /// <summary>
    /// A <c>TM_CS_BUY_FROM_BOOTH</c> (705) against a sell booth: each asked item must be one the booth
    /// declares (same handle, same code), asked once, for 1 up to the units still on offer.
    /// </summary>
    public static (StartBoothRequest Next, BoothReservation Result) ReservePurchase(StartBoothRequest booth,
        IReadOnlyList<BoothBuyLine> asked)
    {
        if (booth is null)
        {
            return (null, BoothReservation.Refused(ResultCode.NotExist));
        }

        if (booth.Type != SellBooth)
        {
            return (booth, BoothReservation.Refused(ResultCode.NotActable));
        }

        if (asked is null || asked.Count == 0
            || asked.Select(line => line.ItemHandle).Distinct().Count() != asked.Count)
        {
            return (booth, BoothReservation.Refused(ResultCode.InvalidArgument));
        }

        var items = booth.Items.ToArray();
        var lines = new List<BoothTradeLine>(asked.Count);
        long total = 0;

        foreach (var line in asked)
        {
            var index = Array.FindIndex(items, item => item.ItemHandle == line.ItemHandle);
            if (index < 0 || items[index].Count <= 0)
            {
                return (booth, BoothReservation.Refused(ResultCode.NotExist));
            }

            if (line.Count < 1 || line.Count > items[index].Count)
            {
                return (booth, BoothReservation.Refused(ResultCode.NotEnoughItem));
            }

            if (!TryAddPrice(ref total, line.Count, items[index].Gold))
            {
                return (booth, BoothReservation.Refused(ResultCode.TooMuchMoney));
            }

            lines.Add(new BoothTradeLine(index, line.ItemHandle, line.Count, items[index].Gold));
            items[index] = items[index] with { Count = items[index].Count - (int)line.Count };
        }

        return (booth with { Items = items }, new BoothReservation(ResultCode.Success, lines, total));
    }

    /// <summary>
    /// A <c>TM_CS_SELL_TO_BOOTH</c> (706) against a buy booth: the offered item's code must be one the
    /// booth wants, with at least <paramref name="count"/> units still wanted. <paramref name="codeOf"/>
    /// gives the code of each declared sample item of the owner's bag, by handle.
    /// </summary>
    public static (StartBoothRequest Next, BoothReservation Result) ReserveSale(StartBoothRequest booth,
        uint offeredHandle, int offeredCode, long count, IReadOnlyDictionary<uint, int> codeOf)
    {
        if (booth is null)
        {
            return (null, BoothReservation.Refused(ResultCode.NotExist));
        }

        if (booth.Type != BuyBooth)
        {
            return (booth, BoothReservation.Refused(ResultCode.NotActable));
        }

        if (count < 1)
        {
            return (booth, BoothReservation.Refused(ResultCode.InvalidArgument));
        }

        var items = booth.Items.ToArray();
        var index = Array.FindIndex(items, item =>
            codeOf.TryGetValue(item.ItemHandle, out var code) && code == offeredCode && item.Count > 0);
        if (index < 0)
        {
            return (booth, BoothReservation.Refused(ResultCode.NotExist));
        }

        if (count > items[index].Count)
        {
            return (booth, BoothReservation.Refused(ResultCode.LimitMax));
        }

        long total = 0;
        if (!TryAddPrice(ref total, count, items[index].Gold))
        {
            return (booth, BoothReservation.Refused(ResultCode.TooMuchMoney));
        }

        items[index] = items[index] with { Count = items[index].Count - (int)count };
        var line = new BoothTradeLine(index, offeredHandle, count, items[index].Gold);
        return (booth with { Items = items }, new BoothReservation(ResultCode.Success, new[] { line }, total));
    }

    /// <summary>
    /// Gives reserved units back to the booth after a trade that failed. A booth closed or replaced since
    /// the reservation is left as it is: its units went with it.
    /// </summary>
    public static StartBoothRequest Release(StartBoothRequest booth, StartBoothRequest reserved,
        IReadOnlyList<BoothTradeLine> lines)
    {
        if (booth is null || reserved is null || !IsSameBooth(booth, reserved))
        {
            return booth;
        }

        var items = booth.Items.ToArray();
        foreach (var line in lines)
        {
            if (line.BoothIndex < items.Length)
            {
                items[line.BoothIndex] = items[line.BoothIndex] with
                {
                    Count = items[line.BoothIndex].Count + (int)line.Count
                };
            }
        }

        return booth with { Items = items };
    }

    /// <summary>
    /// Whether two states are the same booth declaration: a reservation only rewrites the item counts, so
    /// the name buffer the <c>700</c> was read into travels unchanged from one state to the next, while a
    /// new <c>700</c> reads a new one.
    /// </summary>
    public static bool IsSameBooth(StartBoothRequest a, StartBoothRequest b) => ReferenceEquals(a.Name, b.Name);

    /// <summary>Whether every declared item is sold (or bought) out: the booth then closes.</summary>
    public static bool IsExhausted(StartBoothRequest booth) =>
        booth is not null && booth.Items.All(item => item.Count <= 0);

    private static bool TryAddPrice(ref long total, long count, long unitPrice)
    {
        if (unitPrice < 0)
        {
            return false;
        }

        try
        {
            total = checked(total + checked(count * unitPrice));
            return total <= MaxGold;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
