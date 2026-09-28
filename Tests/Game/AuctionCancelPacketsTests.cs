using System;
using System.Buffers.Binary;
using System.Linq;
using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// TM_CS_AUCTION_CANCEL (1310), the "withdraw this announcement" request of the auction house: 11 bytes,
/// the repository's 7-byte header plus a single <c>auction_uid</c> at offset 7 — the exact shape of 1308 but
/// for the identifier, which is precisely why a wrong offset or a confused twin would take an announcement
/// off the market instead of buying it, or the other way round.
/// The 7.3 client builds and sends it (SFrame.exe construction routine 0x48DFC0, whose single caller is the
/// stub 0x49E3BB, reached by the internal key 1161 = 0x489) and never receives it: no dedicated
/// TS_SC_AUCTION_* frame exists past 1305, and the emitting window SUIAuctionRegisterWnd consumes no result
/// for 0x51E — it re-asks its own selling list (1302) instead.
/// See docs/packet-specs/1310-auction-cancel.md.
/// </summary>
[TestFixture]
public class AuctionCancelPacketsTests
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
    private static byte[] ClientCancelFrame(uint auctionUid = 42)
    {
        var frame = new byte[RequestLength];

        // Header, literal: Length 11 (0x0B 00 00 00), Id 1310 (0x1E 0x05), checksum filled in last.
        frame[LengthOffset] = 0x0B;
        frame[IdOffset] = 0x1E;
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
        ((ushort)GamePackets.TM_CS_AUCTION_CANCEL).Should().Be(1310);

        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_AUCTION_CANCEL).Should().BeTrue(
            "an id absent from GamePackets is dropped as \"Undefined packet ID\" before any dispatch, " +
            "and one present without a dispatch arm reaches the throwing switch");

        // The dated trap of the sheet (§4.1): rzu gives TS_CS_AUCTION_CANCEL the id 2310 from EPIC_9_6_3 on.
        // In 7.3 the withdrawal is 1310 — the client owns a single construction site for 0x51E — and the
        // later value must not be declared here: 2310 would be a second member for one act.
        Enum.IsDefined(typeof(GamePackets), (ushort)2310).Should().BeFalse(
            "2310 is the EPIC_9_6_3 remap of 1310 and belongs to no 7.3 frame");
        ((ushort)GamePackets.TM_CS_AUCTION_CANCEL).Should().NotBe(2310);

        // The twin trap: 1310 shares its 11-byte shape with 1308 (TM_CS_AUCTION_INSTANT_PURCHASE, an id its
        // own lot declares — not this one) and their identifiers differ by two while their payloads are
        // identical to the bit. The two ids must stay distinct: the frame's own 0x51E is the only thing that
        // tells a withdrawal from a purchase.
        ((ushort)GamePackets.TM_CS_AUCTION_CANCEL).Should().NotBe(1308);
        ((ushort)GamePackets.TM_CS_AUCTION_CANCEL).Should().NotBe(1306);

        // The answer of this act is the generic result frame, never a TM_SC_AUCTION_* one: the family's
        // declared responses stop at 1305, and 1307 is a gap no reference fills.
        ((ushort)GamePackets.TM_SC_RESULT).Should().Be(0);
        Enum.IsDefined(typeof(GamePackets), (ushort)1307).Should().BeFalse(
            "no reference declares a 1307, so the withdrawal request has no dedicated answer frame");
    }

    [Test]
    public void Request_IsElevenBytesWithTheMeasuredOffsets()
    {
        GameAuctionPackets.AuctionCancelRequestSize.Should().Be(RequestLength);
        GameAuctionPackets.AuctionCancelRequestAuctionUidOffset.Should().Be(AuctionUidOffset);

        // The header is the repository's own seven bytes and auction_uid the last four: no padding, no
        // second field, and the uid ends exactly at the end of the frame (spec §3.1).
        GameAuctionPackets.AuctionCancelRequestAuctionUidOffset.Should().Be(7);
        (GameAuctionPackets.AuctionCancelRequestSize
            - GameAuctionPackets.AuctionCancelRequestAuctionUidOffset).Should().Be(4);
    }

    [Test]
    public void Request_PutsEveryFieldAtItsOwnOffsetOnTheClientFrame()
    {
        var frame = ClientCancelFrame(auctionUid: 9);

        // Length, four bytes at [0, 3], little endian: the client's own literal 11.
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(LengthOffset, 4)).Should().Be(RequestLength);
        frame[LengthOffset].Should().Be(0x0B, "little endian: 11 is 0B 00 00 00 and never 00 00 00 0B");
        frame[1].Should().Be(0x00);
        frame[2].Should().Be(0x00);
        frame[3].Should().Be(0x00);
        BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(LengthOffset, 4)).Should()
            .NotBe(RequestLength, "the field is little endian on the wire");

        // Id, two bytes at [4, 5]: 1310 is 0x51E, low byte first. The sheet's own watchdog: 0x51E appears
        // exactly once as a code immediate in SFrame.exe (0x48DFD6), the other occurrences being division
        // constants.
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(IdOffset, 2)).Should().Be(1310);
        frame[IdOffset].Should().Be(0x1E, "Id 1310 is 0x51E, low byte first");
        frame[IdOffset + 1].Should().Be(0x05);

        // Checksum, one byte at [6]: the sum of the first six header bytes, 0x0B + 0x1E + 0x05 = 0x2E. The
        // sheet measures this constant (46) for the frame, the uid being outside the sum.
        frame[ChecksumOffset].Should().Be(Checksum(frame));
        frame[ChecksumOffset].Should().Be(0x2E, "0x0B + 0x1E + 0x05 = 46");

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
        // 3 000 000 000 does not fit in an int32: read through a signed field, or cut to two bytes, the uid
        // would come back as something else. The sheet measures one dword moved at frame+7 by the client
        // (spec §3.1), so the whole 32-bit field is the identifier.
        const uint auctionUid = 3_000_000_000u;
        var frame = ClientCancelFrame(auctionUid);

        // The client's own bytes, low byte first: 0xB2D05E00.
        frame[AuctionUidOffset].Should().Be(0x00);
        frame[AuctionUidOffset + 1].Should().Be(0x5E);
        frame[AuctionUidOffset + 2].Should().Be(0xD0);
        frame[AuctionUidOffset + 3].Should().Be(0xB2);

        GameAuctionPackets.TryReadAuctionCancel(frame, out var readUid).Should().BeTrue();

        readUid.Should().Be(auctionUid);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(AuctionUidOffset, 2)).Should().Be((ushort)0x5E00,
            "the low two bytes alone are not the uid");
    }

    [Test]
    public void TryReadAuctionCancel_HandsBackTheUid()
    {
        GameAuctionPackets.TryReadAuctionCancel(ClientCancelFrame(auctionUid: 1310), out var auctionUid)
            .Should().BeTrue();

        auctionUid.Should().Be(1310u);
    }

    [Test]
    public void TryReadAuctionCancel_ReadsTheUidUnsigned()
    {
        // This is the one frame of the family where rzu and NGemity both declare uint32_t (spec §4.3), so
        // the reader is unsigned where the 1308 twin reads a uint32 over an int32_t declaration. 0xFFFFFFFF
        // must come back as uint.MaxValue and never as -1 widened.
        GameAuctionPackets.TryReadAuctionCancel(ClientCancelFrame(auctionUid: uint.MaxValue),
            out var auctionUid).Should().BeTrue();

        auctionUid.Should().Be(uint.MaxValue);
        ((int)auctionUid).Should().Be(-1, "the same four bytes read as a signed int32 are -1");
    }

    [TestCase(0, TestName = "TryReadAuctionCancel_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadAuctionCancel_RejectsAHeaderOnlyFrame")]
    [TestCase(10, TestName = "TryReadAuctionCancel_RejectsAFrameMissingItsLastByte")]
    [TestCase(12, TestName = "TryReadAuctionCancel_RejectsAPaddedFrame")]
    public void TryReadAuctionCancel_RejectsAnyLengthOtherThanEleven(int length)
    {
        var packet = new byte[length];
        Array.Copy(ClientCancelFrame(), packet, Math.Min(length, RequestLength));

        GameAuctionPackets.TryReadAuctionCancel(packet, out var auctionUid).Should().BeFalse();
        auctionUid.Should().Be(0u, "the refusal leaves no partially read uid behind");
    }

    [Test]
    public void CancelRequest_IsConsumedByTheReceiveLoopWithoutThrowing()
    {
        var connection = new StorageTestHarness.FrameConnection(ClientCancelFrame());
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(RequestLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
    }

    [Test]
    public void CancelRequest_ExecutesNothingAndAnswersNothing()
    {
        // The sheet is explicit: the withdrawal is neither executed nor answered in this lot. No auction
        // service or repository exists, TM_CS_AUCTION_REGISTER (1309) is not declared so nothing has ever
        // been put up for auction, and the fate of the item and of the tax is a game rule Killian has not
        // settled (spec §5.4, §5.5, §7 question 4). The client expects no dedicated answer either: its
        // window consumes no result for 0x51E and re-asks its selling list (1302) instead (spec §3.4,
        // §5.3).
        var connection = new StorageTestHarness.FrameConnection(ClientCancelFrame(auctionUid: 77));
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(RequestLength);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "the frame was consumed even though nothing answers it");
        connection.Sent.Should().BeEmpty(
            "a well-formed withdrawal is a no-op until the announcement writer and the rules exist");
    }

    [Test]
    public void CancelRequest_MalformedFrameIsRefusedWithTheFamilyResult()
    {
        // 10 bytes is a frame no client can build. The refusal is a TS_SC_RESULT carrying the request id —
        // never a TM_SC_AUCTION_* error frame, which exists in no reference and in no client — and it is
        // the family's own InvalidArgument, the code the sibling lots use for a request the client could
        // not have sent (spec §5.2, §5.3).
        var frame = new byte[RequestLength - 1];
        Array.Copy(ClientCancelFrame(), frame, frame.Length);
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

        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(ResultRequestIdOffset, 2)).Should().Be(1310,
            "the result names the request it refuses, not 1308 or the result frame's own id");
        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(ResultCodeOffset, 2))
            .Should().Be((ushort)ResultCode.InvalidArgument);
        BinaryPrimitives.ReadInt32LittleEndian(answer.AsSpan(ResultValueOffset, 4)).Should().Be(0);
    }

    [Test]
    public void CancelRequest_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[ChecksumOffset] = Checksum(keepalive);

        var frame = ClientCancelFrame().Concat(keepalive).ToArray();
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
        connection.Sent.Should().BeEmpty("neither the withdrawal request nor the keepalive is answered");
    }
}
