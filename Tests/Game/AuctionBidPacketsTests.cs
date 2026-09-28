using System;
using System.Buffers.Binary;
using System.Linq;
using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// TM_CS_AUCTION_BID (1306), the bid placed on an announcement of the auction house: 19 bytes, header
/// plus int32 auction_uid and int64 price — the only 64-bit field of the family's requests, hence the only
/// one a reader can get wrong while still reading the right field. The 7.3 client writes it (SFrame.exe
/// construction routine 0x48CA70 and sender 0x48DE30, reached from the single stub 0x49E397) and never
/// receives it. It is the one act of the family with no dedicated TS_SC_AUCTION_* answer: the client reads
/// a TM_SC_RESULT (id 0) whose request_msg_id is 1306, an explicit case of its result handler 0x66DB80.
/// Nothing executes the bid and no result code is established for a well-formed frame, so the arm reads,
/// logs and answers nothing (spec §5.7, §7.1).
/// See docs/packet-specs/1306-auction-bid.md.
/// </summary>
[TestFixture]
public class AuctionBidPacketsTests
{
    private const int RequestLength = 19;
    private const int IdOffset = 4;
    private const int ChecksumOffset = 6;
    private const int AuctionUidOffset = 7;
    private const int PriceOffset = 11;

    private const int ResultLength = 15;
    private const int ResultRequestIdOffset = 7;
    private const int ResultCodeOffset = 9;
    private const int ResultValueOffset = 11;

    /// <summary>
    /// The 19-byte request as the client writes it, assembled by hand — never through
    /// <see cref="GameAuctionPackets"/> — so that every offset asserted below is the client's own and not a
    /// round trip through the reader under test. The client hands over the value its price control already
    /// holds, so both payload fields are written little endian, back to back, with no padding.
    /// </summary>
    private static byte[] ClientBidFrame(int auctionUid = 42, long price = 1_000)
    {
        var frame = new byte[RequestLength];

        // Header, literal: Length 19 (0x13 00 00 00), Id 1306 (0x1A 0x05), checksum filled in last.
        frame[0] = 0x13;
        frame[IdOffset] = 0x1A;
        frame[IdOffset + 1] = 0x05;

        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(AuctionUidOffset, 4), auctionUid);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(PriceOffset, 8), price);

        frame[ChecksumOffset] = Checksum(frame);
        return frame;
    }

    private static byte Checksum(byte[] packet)
    {
        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        return checksum;
    }

    private static void WriteChecksum(byte[] packet) => packet[ChecksumOffset] = Checksum(packet);

    [Test]
    public void Id_IsTheEpic73OneAndTheRemapStaysClosed()
    {
        ((ushort)GamePackets.TM_CS_AUCTION_BID).Should().Be(1306);

        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_AUCTION_BID).Should().BeTrue(
            "an id absent from GamePackets is dropped as \"Undefined packet ID\" before any dispatch, " +
            "and one present without a dispatch arm reaches the throwing switch");

        // The dated trap of the sheet (§4.5): rzu gives TS_CS_AUCTION_BID the id 2306 from EPIC_9_6_3 on;
        // in 7.3 the bid is 1306, and the later value must not be declared here — 2306 would be a second
        // member for one act, and the receive loop would answer to an id no 7.3 client ever sends.
        Enum.IsDefined(typeof(GamePackets), (ushort)2306).Should().BeFalse();
        ((ushort)GamePackets.TM_CS_AUCTION_BID).Should().NotBe(2306);

        // 1306 designates the auction bid in this version: the summon request it is remapped to in later
        // versions stays 304, as the enum's own comment requires.
        ((ushort)GamePackets.TM_CS_SUMMON).Should().Be(304);

        // The answer of this act is the generic result frame, never a TM_SC_AUCTION_* one: the family's
        // declared responses stop at 1305, and the result frame carries the request id in its payload.
        ((ushort)GamePackets.TM_SC_RESULT).Should().Be(0);
        Enum.IsDefined(typeof(GamePackets), (ushort)1307).Should().BeFalse(
            "no reference declares a 1307, so the bid has no dedicated answer frame");
    }

    [Test]
    public void Request_IsNineteenBytesWithTheMeasuredOffsets()
    {
        GameAuctionPackets.BidRequestSize.Should().Be(RequestLength);
        GameAuctionPackets.BidRequestAuctionUidOffset.Should().Be(AuctionUidOffset);
        GameAuctionPackets.BidRequestPriceOffset.Should().Be(PriceOffset);

        // The header is the repository's own seven bytes, auction_uid the next four and price the last
        // eight: no padding, no second field, and the price is the only 64-bit field of the family's
        // requests (spec §3.1).
        GameAuctionPackets.BidRequestAuctionUidOffset.Should().Be(7);
        (GameAuctionPackets.BidRequestPriceOffset - GameAuctionPackets.BidRequestAuctionUidOffset).Should().Be(4);
        (GameAuctionPackets.BidRequestSize - GameAuctionPackets.BidRequestPriceOffset).Should().Be(8);
    }

    [Test]
    public void Request_PutsEveryFieldAtItsOwnOffsetOnTheClientFrame()
    {
        var frame = ClientBidFrame(auctionUid: 0x0000_0009, price: 0x0000_0000_0000_0007);

        // Header, byte by byte on the frame itself.
        frame.Length.Should().Be(RequestLength);
        frame[0].Should().Be(0x13, "Length 19 is the literal of the client's construction routine");
        frame[1].Should().Be(0x00);
        frame[2].Should().Be(0x00);
        frame[3].Should().Be(0x00);
        frame[IdOffset].Should().Be(0x1A, "Id 1306 is 0x51A, low byte first");
        frame[IdOffset + 1].Should().Be(0x05);
        frame[ChecksumOffset].Should().Be(Checksum(frame));
        frame[ChecksumOffset].Should().Be(0x32, "19 + 0x1A + 0x05 = 50");

        // auction_uid, four bytes at [7, 10], little endian — the same signed int32 as the other requests.
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(AuctionUidOffset, 4)).Should().Be(9);
        frame[AuctionUidOffset].Should().Be(0x09, "little endian: 9 is 09 00 00 00 and never 00 00 00 09");
        frame[AuctionUidOffset + 1].Should().Be(0x00);
        frame[AuctionUidOffset + 2].Should().Be(0x00);
        frame[AuctionUidOffset + 3].Should().Be(0x00);
        BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(AuctionUidOffset, 4)).Should()
            .NotBe(9, "the field is little endian on the wire");

        // price, eight bytes at [11, 18], the tail of the frame.
        BinaryPrimitives.ReadInt64LittleEndian(frame.AsSpan(PriceOffset, 8)).Should().Be(7);
        frame[PriceOffset].Should().Be(0x07);
        frame[PriceOffset + 1].Should().Be(0x00);
        frame[PriceOffset + 7].Should().Be(0x00, "the eighth byte is inside the frame and belongs to price");
        BinaryPrimitives.ReadInt64BigEndian(frame.AsSpan(PriceOffset, 8)).Should()
            .NotBe(7, "the field is little endian on the wire");

        (PriceOffset + 8).Should().Be(RequestLength, "price ends exactly at the end of the frame");
    }

    [Test]
    public void Request_CarriesThePriceInSixtyFourBits()
    {
        // 5 000 000 000 is above int.MaxValue: read as an int32, the field would come back truncated (or
        // negative). The sheet measures a 64-bit field (spec §3.1) filled by a client whose price control
        // holds a 64-bit integer (spec §2).
        const long price = 5_000_000_000L;
        var frame = ClientBidFrame(auctionUid: 1, price: price);

        // The client's own bytes, low byte first: 0x12A05F200.
        frame[PriceOffset].Should().Be(0x00);
        frame[PriceOffset + 1].Should().Be(0xF2);
        frame[PriceOffset + 2].Should().Be(0x05);
        frame[PriceOffset + 3].Should().Be(0x2A);
        frame[PriceOffset + 4].Should().Be(0x01);
        frame[PriceOffset + 5].Should().Be(0x00);
        frame[PriceOffset + 6].Should().Be(0x00);
        frame[PriceOffset + 7].Should().Be(0x00);

        GameAuctionPackets.TryReadAuctionBid(frame, out var auctionUid, out var readPrice).Should().BeTrue();

        auctionUid.Should().Be(1);
        readPrice.Should().Be(price);
        ((long)BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(PriceOffset, 4))).Should().NotBe(price,
            "the low four bytes alone are not the price");
    }

    [Test]
    public void TryReadAuctionBid_HandsBackBothFields()
    {
        GameAuctionPackets.TryReadAuctionBid(ClientBidFrame(auctionUid: 2718, price: 999_999_999L),
            out var auctionUid, out var price).Should().BeTrue();

        auctionUid.Should().Be(2718);
        price.Should().Be(999_999_999L);
    }

    [Test]
    public void TryReadAuctionBid_ReadsAuctionUidAsSigned()
    {
        // The field is an int32 in rzu (TS_CS_AUCTION_BID.h): a negative uid must come back negative
        // instead of being widened into a huge unsigned value. What the server should answer then is not
        // established (spec §7.2), so only the reading is asserted here.
        GameAuctionPackets.TryReadAuctionBid(ClientBidFrame(auctionUid: -1), out var auctionUid, out _)
            .Should().BeTrue();

        auctionUid.Should().Be(-1);
    }

    [TestCase(0, TestName = "TryReadAuctionBid_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadAuctionBid_RejectsAHeaderOnlyFrame")]
    [TestCase(11, TestName = "TryReadAuctionBid_RejectsAFrameStoppingAtThePrice")]
    [TestCase(18, TestName = "TryReadAuctionBid_RejectsAFrameMissingItsLastByte")]
    public void TryReadAuctionBid_RejectsAnyLengthBelowNineteen(int length)
    {
        var packet = new byte[length];
        Array.Copy(ClientBidFrame(), packet, Math.Min(length, RequestLength));

        GameAuctionPackets.TryReadAuctionBid(packet, out var auctionUid, out var price).Should().BeFalse();
        auctionUid.Should().Be(0, "the refusal leaves no partially read uid behind");
        price.Should().Be(0, "the refusal leaves no partially read price behind");
    }

    [Test]
    public void TryReadAuctionBid_ReadsAPaddedFrameAndLeavesThePaddingAlone()
    {
        // The reader is a size gate, not an equality gate: the receive loop has already bounded the frame,
        // and only the twelve known payload bytes are read. The sheet leaves "refuse Length > 19 or only
        // < 19" to Killian (spec §7.5), so the arm accepts the longer frame like 1300, 1302 and 1304 do.
        var frame = ClientBidFrame(auctionUid: 3, price: 12_345L).Concat(new byte[] { 0xAA, 0xBB }).ToArray();

        GameAuctionPackets.TryReadAuctionBid(frame, out var auctionUid, out var price).Should().BeTrue();

        auctionUid.Should().Be(3);
        price.Should().Be(12_345L);
    }

    [Test]
    public void AuctionBidRequest_IsConsumedByTheReceiveLoopWithoutThrowing()
    {
        var connection = new StorageTestHarness.FrameConnection(ClientBidFrame());
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(RequestLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
    }

    [Test]
    public void AuctionBidRequest_ExecutesNothingAndAnswersNothing()
    {
        // The sheet is explicit: the bid is neither executed nor answered in this lot. Nothing in the
        // server knows the announcement auction_uid targets, and no ResultCode for a well-formed bid is
        // established (spec §5.7, §7.1): answering Success would claim a bid that never happened, and
        // answering a refusal would invent a code. The client therefore gets no frame back at all.
        var connection = new StorageTestHarness.FrameConnection(ClientBidFrame(auctionUid: 77, price: 5_000L));
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(RequestLength);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "the frame was consumed even though nothing answers it");
        connection.Sent.Should().BeEmpty(
            "a well-formed bid is a no-op until the rules and the announcement writer exist");
    }

    [Test]
    public void AuctionBidRequest_MalformedFrameIsRefusedWithTheFamilyResult()
    {
        // 18 bytes is a frame no client can build. The refusal is a TS_SC_RESULT carrying the request id —
        // never a TM_SC_AUCTION_* error frame, which exists in no reference and in no client — and it is
        // the family's own InvalidArgument, the code the three sibling lots use for a request the client
        // could not have sent (spec §5.7, §13).
        var frame = new byte[RequestLength - 1];
        Array.Copy(ClientBidFrame(), frame, frame.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)frame.Length);
        WriteChecksum(frame);

        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "a refused frame is still consumed entirely");
        connection.Sent.Should().ContainSingle();

        var answer = connection.Sent[0];
        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(IdOffset, 2))
            .Should().Be((ushort)GamePackets.TM_SC_RESULT);
        answer.Length.Should().Be(ResultLength, "TS_SC_RESULT is request id, result and value, all packed");

        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(ResultRequestIdOffset, 2)).Should().Be(1306,
            "the result names the request it refuses, not 1305 or the result frame's own id");
        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(ResultCodeOffset, 2))
            .Should().Be((ushort)ResultCode.InvalidArgument);
        BinaryPrimitives.ReadInt32LittleEndian(answer.AsSpan(ResultValueOffset, 4)).Should().Be(0);
    }

    [Test]
    public void AuctionBidRequest_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[ChecksumOffset] = Checksum(keepalive);

        var frame = ClientBidFrame().Concat(keepalive).ToArray();
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
        connection.Sent.Should().BeEmpty("neither the bid nor the keepalive is answered");
    }
}
