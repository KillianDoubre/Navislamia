using System;
using System.Buffers.Binary;
using System.Linq;
using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// TM_CS_AUCTION_INSTANT_PURCHASE (1308), the "buy this announcement at its fixed price" request of the
/// auction house: 11 bytes, the repository's 7-byte header plus a single <c>auction_uid</c> at offset 7 —
/// the shortest frame of the family, and one whose whole payload is a single field, so a wrong offset or a
/// wrong width is a request aimed at the wrong announcement.
/// The 7.3 client builds and sends it (SFrame.exe construction routine 0x48DEA0, whose single caller is
/// the stub 0x49E3A1, reached by the internal key 1159 = 0x487) and never receives it: it has no dedicated
/// answer frame in any reference, and the client reads a generic TM_SC_RESULT (id 0) whose
/// <c>request_msg_id</c> is 1308 — the one id its result handler 0x66DB80 reserves a case for.
/// See docs/packet-specs/1308-auction-instant-purchase.md.
/// </summary>
[TestFixture]
public class AuctionInstantPurchasePacketsTests
{
    private const int RequestLength = 11;
    private const int LengthOffset = 0;
    private const int IdOffset = 4;
    private const int ChecksumOffset = 6;
    private const int AuctionUidOffset = 7;

    private const int ResultLength = 15;
    private const int ResultRequestIdOffset = 7;
    private const int ResultCodeOffset = 9;
    private const int ResultValueOffset = 11;

    /// <summary>
    /// The 11-byte request as the client writes it, assembled by hand — never through
    /// <see cref="GameAuctionPackets"/> — so that every offset asserted below is the client's own and not a
    /// round trip through the reader under test. The client moves one <c>dword</c> read from
    /// <c>message+0x13</c> straight to <c>frame+7</c> and nothing else: no padding, no second field.
    /// </summary>
    private static byte[] ClientInstantPurchaseFrame(uint auctionUid = 42)
    {
        var frame = new byte[RequestLength];

        // Header, literal: Length 11 (0x0B 00 00 00), Id 1308 (0x1C 0x05), checksum filled in last.
        frame[LengthOffset] = 0x0B;
        frame[IdOffset] = 0x1C;
        frame[IdOffset + 1] = 0x05;

        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(AuctionUidOffset, 4), auctionUid);

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
        ((ushort)GamePackets.TM_CS_AUCTION_INSTANT_PURCHASE).Should().Be(1308);

        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_AUCTION_INSTANT_PURCHASE).Should().BeTrue(
            "an id absent from GamePackets is dropped as \"Undefined packet ID\" before any dispatch, " +
            "and one present without a dispatch arm reaches the throwing switch");

        // The dated trap of the sheet (§4): rzu gives TS_CS_AUCTION_INSTANT_PURCHASE the id 2308 from
        // EPIC_9_6_3 on; in 7.3 the buy-now request is 1308, and the later value must not be declared here
        // — 2308 would be a second member for one act, and the 7.3 client has a single construction site
        // for 0x51C.
        Enum.IsDefined(typeof(GamePackets), (ushort)2308).Should().BeFalse(
            "2308 is the EPIC_9_6_3 remap of 1308 and belongs to no 7.3 frame");
        ((ushort)GamePackets.TM_CS_AUCTION_INSTANT_PURCHASE).Should().NotBe(2308);

        // 1308 is not the later summon request either: the remap trap the enum's own comment names keeps
        // TM_CS_SUMMON at 304.
        ((ushort)GamePackets.TM_CS_SUMMON).Should().Be(304);

        // The answer of this act is the generic result frame, never a TM_SC_AUCTION_* one: the family's
        // declared responses stop at 1305 (1301/1303/1305), and the result frame carries the request id in
        // its payload. 1307 is a gap no reference fills, so it stays undeclared.
        ((ushort)GamePackets.TM_SC_RESULT).Should().Be(0);
        Enum.IsDefined(typeof(GamePackets), (ushort)1307).Should().BeFalse(
            "no reference declares a 1307, so the buy-now request has no dedicated answer frame");
    }

    [Test]
    public void Request_IsElevenBytesWithTheMeasuredOffsets()
    {
        GameAuctionPackets.InstantPurchaseRequestSize.Should().Be(RequestLength);
        GameAuctionPackets.InstantPurchaseRequestAuctionUidOffset.Should().Be(AuctionUidOffset);

        // The header is the repository's own seven bytes and auction_uid the last four: no padding, no
        // second field, and the uid ends exactly at the end of the frame (spec §3.1).
        GameAuctionPackets.InstantPurchaseRequestAuctionUidOffset.Should().Be(7);
        (GameAuctionPackets.InstantPurchaseRequestSize
            - GameAuctionPackets.InstantPurchaseRequestAuctionUidOffset).Should().Be(4);
    }

    [Test]
    public void Request_PutsEveryFieldAtItsOwnOffsetOnTheClientFrame()
    {
        var frame = ClientInstantPurchaseFrame(auctionUid: 9);

        // Length, four bytes at [0, 3], little endian: the client's own literal 11.
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(LengthOffset, 4)).Should().Be(RequestLength);
        frame[LengthOffset].Should().Be(0x0B, "little endian: 11 is 0B 00 00 00 and never 00 00 00 0B");
        frame[1].Should().Be(0x00);
        frame[2].Should().Be(0x00);
        frame[3].Should().Be(0x00);
        BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(LengthOffset, 4)).Should()
            .NotBe(RequestLength, "the field is little endian on the wire");

        // Id, two bytes at [4, 5]: 1308 is 0x51C, low byte first.
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(IdOffset, 2)).Should().Be(1308);
        frame[IdOffset].Should().Be(0x1C, "Id 1308 is 0x51C, low byte first");
        frame[IdOffset + 1].Should().Be(0x05);

        // Checksum, one byte at [6]: the sum of the first six header bytes, 11 + 0x1C + 0x05 = 44.
        frame[ChecksumOffset].Should().Be(Checksum(frame));
        frame[ChecksumOffset].Should().Be(0x2C, "11 + 0x1C + 0x05 = 44");

        // auction_uid, four bytes at [7, 10], little endian and the tail of the frame.
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(AuctionUidOffset, 4)).Should().Be(9);
        frame[AuctionUidOffset].Should().Be(0x09, "little endian: 9 is 09 00 00 00 and never 00 00 00 09");
        frame[AuctionUidOffset + 1].Should().Be(0x00);
        frame[AuctionUidOffset + 2].Should().Be(0x00);
        frame[AuctionUidOffset + 3].Should().Be(0x00);
        BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(AuctionUidOffset, 4)).Should()
            .NotBe(9, "the field is little endian on the wire");

        (AuctionUidOffset + 4).Should().Be(RequestLength, "auction_uid ends exactly at the end of the frame");
    }

    [Test]
    public void Request_CarriesTheUidInThirtyTwoBits()
    {
        // 3 000 000 000 does not fit in an int32: read through a signed field, or truncated to two bytes,
        // the uid would come back as something else. The sheet measures one dword moved at frame+7 by the
        // client (spec §3.1), so the whole 32-bit field is the identifier.
        const uint auctionUid = 3_000_000_000u;
        var frame = ClientInstantPurchaseFrame(auctionUid);

        // The client's own bytes, low byte first: 0xB2D05E00.
        frame[AuctionUidOffset].Should().Be(0x00);
        frame[AuctionUidOffset + 1].Should().Be(0x5E);
        frame[AuctionUidOffset + 2].Should().Be(0xD0);
        frame[AuctionUidOffset + 3].Should().Be(0xB2);

        GameAuctionPackets.TryReadAuctionInstantPurchase(frame, out var readUid).Should().BeTrue();

        readUid.Should().Be(auctionUid);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(AuctionUidOffset, 2)).Should().Be((ushort)0x5E00,
            "the low two bytes alone are not the uid");
    }

    [Test]
    public void TryReadAuctionInstantPurchase_HandsBackTheUid()
    {
        GameAuctionPackets.TryReadAuctionInstantPurchase(ClientInstantPurchaseFrame(auctionUid: 2718),
            out var auctionUid).Should().BeTrue();

        auctionUid.Should().Be(2718u);
    }

    [Test]
    public void TryReadAuctionInstantPurchase_ReadsTheUidUnsigned()
    {
        // The collection's own decision (socle-encheres.md §3.5, spec §4): the identifier is a uint32 where
        // rzu writes an int32_t for this frame and a uint32_t for its twin 1310. The width, not the sign, is
        // what the client sends — 0xFFFFFFFF must come back as uint.MaxValue and never as -1 widened.
        GameAuctionPackets.TryReadAuctionInstantPurchase(ClientInstantPurchaseFrame(auctionUid: uint.MaxValue),
            out var auctionUid).Should().BeTrue();

        auctionUid.Should().Be(uint.MaxValue);
        ((int)auctionUid).Should().Be(-1, "the same four bytes read as a signed int32 are -1");
    }

    [TestCase(0, TestName = "TryReadAuctionInstantPurchase_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadAuctionInstantPurchase_RejectsAHeaderOnlyFrame")]
    [TestCase(10, TestName = "TryReadAuctionInstantPurchase_RejectsAFrameMissingItsLastByte")]
    public void TryReadAuctionInstantPurchase_RejectsAnyLengthBelowEleven(int length)
    {
        var packet = new byte[length];
        Array.Copy(ClientInstantPurchaseFrame(), packet, Math.Min(length, RequestLength));

        GameAuctionPackets.TryReadAuctionInstantPurchase(packet, out var auctionUid).Should().BeFalse();
        auctionUid.Should().Be(0u, "the refusal leaves no partially read uid behind");
    }

    [Test]
    public void TryReadAuctionInstantPurchase_ReadsAPaddedFrameAndLeavesThePaddingAlone()
    {
        // The reader is a size gate, not an equality gate: the receive loop has already bounded the frame,
        // and only the four known payload bytes are read. The sheet prescribes refusing "any frame shorter
        // than eleven bytes" and nothing about longer ones (spec §5.2), so the longer frame is accepted
        // like the sibling readers do.
        var frame = ClientInstantPurchaseFrame(auctionUid: 3).Concat(new byte[] { 0xAA, 0xBB }).ToArray();

        GameAuctionPackets.TryReadAuctionInstantPurchase(frame, out var auctionUid).Should().BeTrue();

        auctionUid.Should().Be(3u);
    }

    [Test]
    public void InstantPurchaseRequest_IsConsumedByTheReceiveLoopWithoutThrowing()
    {
        var connection = new StorageTestHarness.FrameConnection(ClientInstantPurchaseFrame());
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(RequestLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
    }

    [Test]
    public void InstantPurchaseRequest_ExecutesNothingAndAnswersNothing()
    {
        // The sheet is explicit: the purchase is neither executed nor answered in this lot. Nothing in the
        // server knows the announcement auction_uid names, TM_CS_AUCTION_REGISTER (1309) is not even
        // declared, and no ResultCode for a well-formed request is established (spec §5.5, §7.1):
        // answering Success would claim a purchase that never happened, and answering a refusal would
        // invent a code. The client therefore gets no frame back at all.
        var connection = new StorageTestHarness.FrameConnection(ClientInstantPurchaseFrame(auctionUid: 77));
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(RequestLength);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "the frame was consumed even though nothing answers it");
        connection.Sent.Should().BeEmpty(
            "a well-formed buy-now request is a no-op until the announcement writer and the rules exist");
    }

    [Test]
    public void InstantPurchaseRequest_MalformedFrameIsRefusedWithTheFamilyResult()
    {
        // 10 bytes is a frame no client can build. The refusal is a TS_SC_RESULT carrying the request id —
        // never a TM_SC_AUCTION_* error frame, which exists in no reference and in no client — and it is
        // the family's own InvalidArgument, the code the sibling lots use for a request the client could
        // not have sent (spec §5.3).
        var frame = new byte[RequestLength - 1];
        Array.Copy(ClientInstantPurchaseFrame(), frame, frame.Length);
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

        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(ResultRequestIdOffset, 2)).Should().Be(1308,
            "the result names the request it refuses, not 1306 or the result frame's own id");
        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(ResultCodeOffset, 2))
            .Should().Be((ushort)ResultCode.InvalidArgument);
        BinaryPrimitives.ReadInt32LittleEndian(answer.AsSpan(ResultValueOffset, 4)).Should().Be(0);
    }

    [Test]
    public void InstantPurchaseRequest_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[ChecksumOffset] = Checksum(keepalive);

        var frame = ClientInstantPurchaseFrame().Concat(keepalive).ToArray();
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
        connection.Sent.Should().BeEmpty("neither the buy-now request nor the keepalive is answered");
    }
}
