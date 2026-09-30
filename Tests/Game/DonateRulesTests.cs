using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// The judgments of <c>TM_CS_DONATE_ITEM</c> (258): the shape of the offer on its own, then what the
/// character holds. The credit side of the donation is deliberately not judged here — no moral point
/// and no conversion rate is established for Epic 7.3 (docs/packet-specs/258-donate-item.md §5.4).
/// </summary>
[TestFixture]
public class DonateRulesTests
{
    private static GameActionPackets.DonateItemRequest Offer(long gold, int jp,
        params GameActionPackets.DonateItemEntry[] items)
    {
        return new GameActionPackets.DonateItemRequest(gold, jp, items);
    }

    [Test]
    public void CheckShape_AcceptsAGoldOffer()
    {
        DonateRules.CheckShape(Offer(30_000L, 0)).Should().Be(ResultCode.Success);
    }

    [Test]
    public void CheckShape_AcceptsAJpOffer()
    {
        DonateRules.CheckShape(Offer(0L, 120)).Should().Be(ResultCode.Success);
    }

    [Test]
    public void CheckShape_AcceptsAnItemOffer()
    {
        DonateRules.CheckShape(Offer(0L, 0, new GameActionPackets.DonateItemEntry(0x80000001u, 3L)))
            .Should().Be(ResultCode.Success);
    }

    [Test]
    public void CheckShape_AcceptsGoldAndJpAndItemsInTheSameFrame()
    {
        // The client's action message carries the three at once, so a combined offer must be judged
        // as one legitimate request rather than split.
        DonateRules.CheckShape(Offer(1_000L, 5, new GameActionPackets.DonateItemEntry(0x80000001u, 1L)))
            .Should().Be(ResultCode.Success);
    }

    [Test]
    public void CheckShape_RefusesAnEntirelyEmptyOffer()
    {
        // The client emits nothing when the gold, the jp and the item list are all empty, so an empty
        // frame is not a donation the server can act on.
        DonateRules.CheckShape(Offer(0L, 0)).Should().Be(ResultCode.InvalidArgument);
    }

    [Test]
    public void CheckShape_RefusesANegativeGoldOrJp()
    {
        DonateRules.CheckShape(Offer(-1L, 0)).Should().Be(ResultCode.InvalidArgument);
        DonateRules.CheckShape(Offer(0L, -1)).Should().Be(ResultCode.InvalidArgument);
    }

    [Test]
    public void CheckShape_RefusesAnItemAskingForNoUnit()
    {
        DonateRules.CheckShape(Offer(0L, 0, new GameActionPackets.DonateItemEntry(0x80000001u, 0L)))
            .Should().Be(ResultCode.InvalidArgument);
        DonateRules.CheckShape(Offer(0L, 0, new GameActionPackets.DonateItemEntry(0x80000001u, -3L)))
            .Should().Be(ResultCode.InvalidArgument);
    }

    [Test]
    public void CheckShape_RefusesTheSameHandleTwice()
    {
        // One stack named twice is either an incoherence or an attempt to debit it twice.
        DonateRules.CheckShape(Offer(0L, 0,
                new GameActionPackets.DonateItemEntry(0x80000001u, 2L),
                new GameActionPackets.DonateItemEntry(0x80000001u, 5L)))
            .Should().Be(ResultCode.InvalidArgument);
    }

    [Test]
    public void CheckShape_AcceptsTwoDistinctHandles()
    {
        DonateRules.CheckShape(Offer(0L, 0,
                new GameActionPackets.DonateItemEntry(0x80000001u, 2L),
                new GameActionPackets.DonateItemEntry(0x80000002u, 5L)))
            .Should().Be(ResultCode.Success);
    }

    [Test]
    public void CheckShape_JudgesTheShapeEvenWhenTheAmountsAreUnaffordable()
    {
        // Shape first: a malformed frame is an argument error whatever the character holds.
        DonateRules.CheckShape(Offer(long.MaxValue, -1)).Should().Be(ResultCode.InvalidArgument);
    }

    [Test]
    public void CheckAffordable_AcceptsAnOfferWithinWhatTheCharacterHolds()
    {
        DonateRules.CheckAffordable(30_000L, 500L, Offer(30_000L, 500)).Should().Be(ResultCode.Success);
    }

    [Test]
    public void CheckAffordable_RefusesMoreGoldThanTheCharacterHolds()
    {
        DonateRules.CheckAffordable(29_999L, 500L, Offer(30_000L, 0)).Should().Be(ResultCode.NotEnoughMoney);
    }

    [Test]
    public void CheckAffordable_RefusesMoreJpThanTheCharacterHolds()
    {
        DonateRules.CheckAffordable(30_000L, 499L, Offer(0L, 500)).Should().Be(ResultCode.NotEnoughJP);
    }

    [Test]
    public void CheckAffordable_ReportsTheGoldBeforeTheJp()
    {
        // Both sides are short: the frame names gold first, so that is the refusal reported.
        DonateRules.CheckAffordable(0L, 0L, Offer(10L, 10)).Should().Be(ResultCode.NotEnoughMoney);
    }
}
