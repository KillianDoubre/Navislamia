using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// Rules of <c>TM_CS_PUTON_CARD</c> (214), socketing a soul stone into an equipment socket. The
/// judgement is pure, so every guard the reference runs can be exercised without a database, a client
/// or a running server.
/// </summary>
[TestFixture]
public class CardSocketRulesTests
{
    private const long StoneResourceId = 2501;
    private const long OtherStoneResourceId = 2502;

    private static long[] Sockets(params long[] values)
    {
        return values;
    }

    // --- The retained reading (fiche §7.1, R2) -----------------------------------------------------

    [Test]
    public void ResolveTarget_ReadsThePositionAsTheWearSlotAndTheHandleAsTheCard()
    {
        var target = CardSocketRules.ResolveTarget(2, 0x80000123u);

        target.Slot.Should().Be(ItemWearType.Armor);
        target.CardHandle.Should().Be(0x80000123u);
    }

    [Test]
    public void ResolveTarget_KeepsTheSignedSlotOfAnOutOfRangePosition()
    {
        // -1 is ItemWearType.None: the service refuses it before this point, and the rules must not
        // silently turn it into slot 255 in between.
        CardSocketRules.ResolveTarget(-1, 1).Slot.Should().Be(ItemWearType.None);
        ((int)CardSocketRules.ResolveTarget(-1, 1).Slot).Should().Be(-1);
    }

    // --- Socketable item --------------------------------------------------------------------------

    [Test]
    public void IsSocketable_AcceptsOneToFourSocketsAndRefusesTheRest()
    {
        CardSocketRules.IsSocketable(0).Should().BeFalse();
        CardSocketRules.IsSocketable(1).Should().BeTrue();
        CardSocketRules.IsSocketable(3).Should().BeTrue();
        CardSocketRules.IsSocketable(4).Should().BeTrue();
        CardSocketRules.IsSocketable(5).Should().BeFalse();
        CardSocketRules.IsSocketable(-1).Should().BeFalse();
    }

    // --- Soul stone nature ------------------------------------------------------------------------

    [Test]
    public void IsSoulstone_RequiresAllThreeResourceMarkers()
    {
        CardSocketRules.IsSoulstone(ItemGroup.Soulstone, ItemType.Soulstone, ItemBaseType.Soulstone)
            .Should().BeTrue();

        CardSocketRules.IsSoulstone(ItemGroup.Soulstone, ItemType.Soulstone, ItemBaseType.Card)
            .Should().BeFalse();
        CardSocketRules.IsSoulstone(ItemGroup.Soulstone, ItemType.Etc, ItemBaseType.Soulstone)
            .Should().BeFalse();
        CardSocketRules.IsSoulstone(ItemGroup.Etc, ItemType.Soulstone, ItemBaseType.Soulstone)
            .Should().BeFalse();
    }

    // --- Replica ceiling --------------------------------------------------------------------------

    [Test]
    public void MaxReplicas_IsTwoOnAFourSocketItemAndOneBelow()
    {
        CardSocketRules.MaxReplicas(4).Should().Be(2);
        CardSocketRules.MaxReplicas(3).Should().Be(1);
        CardSocketRules.MaxReplicas(1).Should().Be(1);
    }

    [Test]
    public void Judge_AcceptsTheSecondIdenticalStoneOnAFourSocketItem()
    {
        var code = CardSocketRules.Judge(4, Sockets(StoneResourceId), true, StoneResourceId, out var index);

        code.Should().Be(ResultCode.Success);
        index.Should().Be(1);
    }

    [Test]
    public void Judge_RefusesTheThirdIdenticalStoneOnAFourSocketItem()
    {
        var sockets = Sockets(StoneResourceId, StoneResourceId);

        var code = CardSocketRules.Judge(4, sockets, true, StoneResourceId, out var index);

        code.Should().Be(ResultCode.AlreadyExist);
        index.Should().Be(-1);
    }

    [Test]
    public void Judge_RefusesTheSecondIdenticalStoneWhenTheItemIsNotFourSocketed()
    {
        var sockets = Sockets(StoneResourceId, 0, 0);

        var code = CardSocketRules.Judge(3, sockets, true, StoneResourceId, out var index);

        code.Should().Be(ResultCode.AlreadyExist);
        index.Should().Be(-1);
    }

    [Test]
    public void Judge_AcceptsADifferentStoneAlongsideAnIdenticalPair()
    {
        var sockets = Sockets(StoneResourceId, StoneResourceId);

        var code = CardSocketRules.Judge(4, sockets, true, OtherStoneResourceId, out var index);

        code.Should().Be(ResultCode.Success);
        index.Should().Be(2);
    }

    // --- Free socket ------------------------------------------------------------------------------

    [Test]
    public void FindFreeSocket_ReturnsTheFirstEmptyOneAndMinusOneWhenFull()
    {
        CardSocketRules.FindFreeSocket(4, Sockets(0, 0, 0, 0)).Should().Be(0);
        CardSocketRules.FindFreeSocket(4, Sockets(7, 0, 0, 0)).Should().Be(1);
        CardSocketRules.FindFreeSocket(4, Sockets(7, 8, 9, 0)).Should().Be(3);
        CardSocketRules.FindFreeSocket(2, Sockets(7, 8, 0, 0)).Should().Be(-1);
    }

    [Test]
    public void FindFreeSocket_StaysInsideTheItemsOwnSocketCount()
    {
        // Sockets past the item's own count are not its sockets: a one-socket item whose single socket
        // is filled has no free socket, even though the column carries four slots.
        CardSocketRules.FindFreeSocket(1, Sockets(7, 0, 0, 0)).Should().Be(-1);
    }

    [Test]
    public void Judge_RefusesAFullItemInsteadOfOverwritingASocket()
    {
        var sockets = Sockets(OtherStoneResourceId, OtherStoneResourceId, OtherStoneResourceId,
            OtherStoneResourceId);

        var code = CardSocketRules.Judge(4, sockets, true, StoneResourceId, out var index);

        // The client can socket over an occupied socket, but the frame carries no socket index: the
        // reading retained for 7.3 picks the first free socket and refuses when there is none, rather
        // than destroying a stone the player never chose.
        code.Should().Be(ResultCode.AlreadyExist);
        index.Should().Be(-1);
    }

    [Test]
    public void Judge_AcceptsAnEmptyItemAndPicksTheFirstSocket()
    {
        var code = CardSocketRules.Judge(4, Sockets(0, 0, 0, 0), true, StoneResourceId, out var index);

        code.Should().Be(ResultCode.Success);
        index.Should().Be(0);
    }

    [Test]
    public void Judge_AcceptsAnItemWhoseSocketColumnIsAbsent()
    {
        // Stored rows can carry a null or a shorter socket column; a null column is an empty item.
        var code = CardSocketRules.Judge(4, null, true, StoneResourceId, out var index);

        code.Should().Be(ResultCode.Success);
        index.Should().Be(0);
    }

    // --- Refusals ---------------------------------------------------------------------------------

    [Test]
    public void Judge_RefusesAnItemThatIsNotSocketable()
    {
        CardSocketRules.Judge(0, Sockets(0, 0, 0, 0), true, StoneResourceId, out var index)
            .Should().Be(ResultCode.AccessDenied);
        CardSocketRules.Judge(5, Sockets(0, 0, 0, 0), true, StoneResourceId, out var index2)
            .Should().Be(ResultCode.AccessDenied);

        index.Should().Be(-1);
        index2.Should().Be(-1);
    }

    [Test]
    public void Judge_RefusesACardThatIsNotASoulStone()
    {
        var code = CardSocketRules.Judge(4, Sockets(0, 0, 0, 0), false, StoneResourceId, out var index);

        code.Should().Be(ResultCode.NotActable);
        index.Should().Be(-1);
    }

    [Test]
    public void Judge_ChecksTheItemBeforeTheCard()
    {
        // An unsocketable item is refused as such even when the card is not a soul stone, in the same
        // order as the reference (socket count, then nature, then the ceiling).
        CardSocketRules.Judge(0, Sockets(0), false, StoneResourceId, out _)
            .Should().Be(ResultCode.AccessDenied);
    }

    // --- Socket reads ------------------------------------------------------------------------------

    [Test]
    public void SocketAt_ToleratesAnAbsentOrShortColumn()
    {
        CardSocketRules.SocketAt(null, 0).Should().Be(0);
        CardSocketRules.SocketAt(Sockets(7), 0).Should().Be(7);
        CardSocketRules.SocketAt(Sockets(7), 1).Should().Be(0);
        CardSocketRules.SocketAt(Sockets(7), -1).Should().Be(0);
    }

    [Test]
    public void CountSameResource_CountsOnlyTheItemsOwnSockets()
    {
        CardSocketRules.CountSameResource(4, Sockets(StoneResourceId, OtherStoneResourceId, 0, 0),
            StoneResourceId).Should().Be(1);
        CardSocketRules.CountSameResource(4, null, StoneResourceId).Should().Be(0);
        // The fourth slot holds the same resource but lies outside a two-socket item's own sockets.
        CardSocketRules.CountSameResource(2, Sockets(OtherStoneResourceId, 0, 0, StoneResourceId),
            StoneResourceId).Should().Be(0);
    }
}
