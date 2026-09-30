using System;
using System.Buffers.Binary;
using System.Linq;
using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// TM_CS_AUCTION_BIDDED_LIST (1304), the request for the announcements the character has bid on:
/// 11 bytes, header plus a single int32 page_num, no variable part. The 7.3 client writes it
/// (SFrame.exe constructor-and-sender 0x48DDA0, reached from the single stub 0x49E387) and its incoming
/// dispatcher never routes 1304, so the frame is strictly client to server. It is the same code as the
/// TM_CS_AUCTION_SELLING_LIST (1302) frame with another Id — two opcodes, two windows, two lots.
/// The answer is a TM_SC_AUCTION_BIDDED_LIST (1305) empty page — no code in this repository ever writes
/// the auction table — echoing the request's page_num.
/// See docs/packet-specs/1304-auction-bidded-list.md.
/// </summary>
[TestFixture]
public class AuctionBiddedListPacketsTests
{
    private const int RequestLength = 11;
    private const int IdOffset = 4;
    private const int ChecksumOffset = 6;
    private const int PageNumOffset = 7;

    private const int ResponseLength = 3899;
    private const int ResponseTableOffset = 19;
    private const int ResponseEntrySize = 97;
    private const int ResponseSlots = 40;

    /// <summary>
    /// The 11-byte request as the client writes it, assembled by hand — never through
    /// <see cref="GameAuctionPackets"/> — so that every offset asserted below is the client's own and
    /// not a round trip through the reader under test. The client zeroes the whole frame before writing
    /// it, so the four bytes of page_num are the only payload.
    /// </summary>
    private static byte[] ClientBiddedListFrame(int pageNum = 1)
    {
        var frame = new byte[RequestLength];

        // Header, literal: Length 11 (0x0B 00 00 00), Id 1304 (0x18 0x05), checksum filled in last.
        frame[0] = 0x0B;
        frame[IdOffset] = 0x18;
        frame[IdOffset + 1] = 0x05;

        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(PageNumOffset, 4), pageNum);

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
    public void Id_IsTheEpic73OneAndTheSummonTrapStaysClosed()
    {
        ((ushort)GamePackets.TM_CS_AUCTION_BIDDED_LIST).Should().Be(1304);

        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_AUCTION_BIDDED_LIST).Should().BeTrue(
            "an id absent from GamePackets is dropped as \"Undefined packet ID\" before any dispatch, " +
            "and one present without a dispatch arm reaches the throwing switch");

        // The response of this very request is already declared by the merged socle: 1304 is the request,
        // 1305 the answer, and this lot must not have moved either of them.
        ((ushort)GamePackets.TM_SC_AUCTION_BIDDED_LIST).Should().Be(1305);

        // The dated trap of the sheet (§4.5): rzu gives TS_CS_SUMMON the id 1304 from EPIC_9_6_3 on, but
        // in 7.3 1304 is this auction request and the summon request stays 304.
        ((ushort)GamePackets.TM_CS_SUMMON).Should().Be(304);
        Enum.IsDefined(typeof(GamePackets), (ushort)304).Should().BeTrue();

        // rzu remaps the family to 2xxx from EPIC_9_6_3 on (TS_CS_AUCTION_BIDDED_LIST.h:10-12); 7.3 stays
        // on 1304 and the 9.6.3 value (2304) must not be declared here.
        Enum.IsDefined(typeof(GamePackets), (ushort)2304).Should().BeFalse();
    }

    [Test]
    public void Request_IsElevenBytesWithTheMeasuredOffsets()
    {
        GameAuctionPackets.BiddedListRequestSize.Should().Be(RequestLength);
        GameAuctionPackets.BiddedListRequestPageNumOffset.Should().Be(PageNumOffset);

        // The header is the repository's own seven bytes and the request adds exactly four: no padding,
        // no second field, and page_num is the last thing in the frame.
        GameAuctionPackets.BiddedListRequestPageNumOffset.Should().Be(7);
        (GameAuctionPackets.BiddedListRequestPageNumOffset + 4).Should().Be(RequestLength);
    }

    [Test]
    public void Request_PutsEveryFieldAtItsOwnOffsetOnTheClientFrame()
    {
        var frame = ClientBiddedListFrame(pageNum: 0x00000009);

        // Header, byte by byte on the frame itself.
        frame.Length.Should().Be(RequestLength);
        frame[0].Should().Be(0x0B, "Length 11 is the literal of the client constructor-and-sender");
        frame[1].Should().Be(0x00);
        frame[2].Should().Be(0x00);
        frame[3].Should().Be(0x00);
        frame[IdOffset].Should().Be(0x18, "Id 1304 is 0x518, low byte first");
        frame[IdOffset + 1].Should().Be(0x05);
        frame[ChecksumOffset].Should().Be(Checksum(frame));
        frame[ChecksumOffset].Should().Be(0x28, "11 + 0x18 + 0x05 = 40");

        // page_num is the whole payload: the four bytes [7, 10] are it, and nothing else follows.
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(PageNumOffset, 4)).Should().Be(9);
        frame[PageNumOffset].Should().Be(0x09, "little endian: 9 is 09 00 00 00 and never 00 00 00 09");
        frame[PageNumOffset + 1].Should().Be(0x00);
        frame[PageNumOffset + 2].Should().Be(0x00);
        frame[PageNumOffset + 3].Should().Be(0x00);
        BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(PageNumOffset, 4)).Should()
            .NotBe(9, "the field is little endian on the wire");

        (PageNumOffset + 4).Should().Be(RequestLength, "page_num ends exactly at the end of the frame");
    }

    [Test]
    public void Request_DiffersFromTheSellingListFrameOnlyByTheId()
    {
        // The sheet measures that 1302 and 1304 are built by the same client code with another Id only
        // (§3.3): the two frames are byte for byte identical except the two Id bytes and, consequently,
        // the checksum.
        var bidded = ClientBiddedListFrame(pageNum: 4);
        var selling = ClientBiddedListFrame(pageNum: 4);
        selling[IdOffset] = 0x16;
        selling[IdOffset + 1] = 0x05;
        WriteChecksum(selling);

        var differing = Enumerable.Range(0, RequestLength).Where(i => bidded[i] != selling[i]).ToArray();

        // The high byte of the two ids is 0x05 for both (0x0516 / 0x0518), so only the low id byte and the
        // checksum it feeds differ.
        differing.Should().Equal(new[] { IdOffset, ChecksumOffset },
            "only Id and the checksum it feeds may differ between the 1302 and 1304 frames");
        BinaryPrimitives.ReadInt32LittleEndian(bidded.AsSpan(PageNumOffset, 4))
            .Should().Be(BinaryPrimitives.ReadInt32LittleEndian(selling.AsSpan(PageNumOffset, 4)));
    }

    [Test]
    public void TryReadAuctionBiddedList_HandsBackThePageNum()
    {
        GameAuctionPackets.TryReadAuctionBiddedList(ClientBiddedListFrame(pageNum: 6), out var pageNum)
            .Should().BeTrue();

        pageNum.Should().Be(6);
    }

    [Test]
    public void TryReadAuctionBiddedList_ReadsTheFirstPageTheClientWrites()
    {
        // One of the client's two emission sites writes page_num = 1 in hard: the opening of the tab. The
        // page is therefore one-based, and 1 is echoed as 1, never as 0.
        GameAuctionPackets.TryReadAuctionBiddedList(ClientBiddedListFrame(pageNum: 1), out var pageNum)
            .Should().BeTrue();

        pageNum.Should().Be(1);
    }

    [Test]
    public void TryReadAuctionBiddedList_ReadsThePageNumAsSigned()
    {
        // The field is an int32 in rzu (TS_CS_AUCTION_BIDDED_LIST.h:7-8): a negative page must come back
        // negative instead of being widened into a huge unsigned value. What the server should answer then
        // is not established (spec §7.6), so only the reading is asserted here.
        GameAuctionPackets.TryReadAuctionBiddedList(ClientBiddedListFrame(pageNum: -1), out var pageNum)
            .Should().BeTrue();

        pageNum.Should().Be(-1);
    }

    [TestCase(0, TestName = "TryReadAuctionBiddedList_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadAuctionBiddedList_RejectsAHeaderOnlyFrame")]
    [TestCase(10, TestName = "TryReadAuctionBiddedList_RejectsAFrameMissingItsLastByte")]
    public void TryReadAuctionBiddedList_RejectsAnyLengthBelowEleven(int length)
    {
        var packet = new byte[length];
        Array.Copy(ClientBiddedListFrame(), packet, Math.Min(length, RequestLength));

        GameAuctionPackets.TryReadAuctionBiddedList(packet, out var pageNum).Should().BeFalse();
        pageNum.Should().Be(0, "the refusal leaves no partially read page behind");
    }

    [Test]
    public void TryReadAuctionBiddedList_ReadsAPaddedFrameAndLeavesThePaddingAlone()
    {
        // The reader is a size gate, not an equality gate: the receive loop has already bounded the frame,
        // and only the four known bytes are read. The sheet leaves "refuse Length > 11 or only < 11" to
        // Killian (spec §7.11, q12), so the arm accepts the longer frame like 1300 and 1302 do.
        var frame = ClientBiddedListFrame(pageNum: 4).Concat(new byte[] { 0xAA, 0xBB }).ToArray();

        GameAuctionPackets.TryReadAuctionBiddedList(frame, out var pageNum).Should().BeTrue();

        pageNum.Should().Be(4);
    }

    [Test]
    public void BiddedListRequest_IsConsumedByTheReceiveLoopWithoutThrowing()
    {
        var connection = new StorageTestHarness.FrameConnection(ClientBiddedListFrame());
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(RequestLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
    }

    [Test]
    public void BiddedListRequest_AnswersAnEmptyPageEchoingPageNum()
    {
        var connection = new StorageTestHarness.FrameConnection(ClientBiddedListFrame(pageNum: 5));
        var client = StorageTestHarness.NewGameClient(connection);

        client.OnDataReceived(RequestLength);

        connection.Sent.Should().ContainSingle();
        var answer = connection.Sent[0];

        // The 7.3 client copies 3880 bytes of table without ever reading auction_info_count, so the answer
        // is always 7 + 12 + 40 × 97 and no shorter frame may be produced.
        answer.Length.Should().Be(ResponseLength);
        BinaryPrimitives.ReadUInt32LittleEndian(answer.AsSpan(0, 4)).Should().Be((uint)ResponseLength);
        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(IdOffset, 2))
            .Should().Be((ushort)GamePackets.TM_SC_AUCTION_BIDDED_LIST);
        // The two list responses are the same shape for the client — same handler code, another target list
        // (spec §3.6): answering 1303 here would fill the sales window instead of the bids one.
        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(IdOffset, 2)).Should().NotBe(1303);
        answer[ChecksumOffset].Should().Be(Checksum(answer));

        BinaryPrimitives.ReadInt32LittleEndian(answer.AsSpan(PageNumOffset, 4)).Should().Be(5, "page_num is echoed");
        BinaryPrimitives.ReadInt32LittleEndian(answer.AsSpan(11, 4)).Should().Be(0,
            "total_page_count has no established rule, so the arm writes 0 (spec §7.3, §8 q4)");
        BinaryPrimitives.ReadInt32LittleEndian(answer.AsSpan(15, 4)).Should().Be(0, "no announcement is served");

        // The table is fully written: its last slot ends exactly at the end of the frame, and nothing
        // writes an announcement — no code in the repository reads or writes TelecasterContext.Auctions.
        (ResponseTableOffset + ResponseSlots * ResponseEntrySize).Should().Be(ResponseLength);
        answer.AsSpan(ResponseTableOffset).ToArray().Should().OnlyContain(b => b == 0,
            "the page is empty and the forty slots stay zero");
    }

    [Test]
    public void BiddedListRequest_AnswersThePageTheClientAskedForAndNotAFixedOne()
    {
        // Morsure : la page de la réponse est celle de la requête. Si le bras écrivait une page constante
        // (1, comme le prescrit la trame d'ouverture de l'onglet), les deux envois ci-dessous seraient
        // identiques.
        var first = new StorageTestHarness.FrameConnection(ClientBiddedListFrame(pageNum: 1));
        StorageTestHarness.NewGameClient(first).OnDataReceived(RequestLength);

        var third = new StorageTestHarness.FrameConnection(ClientBiddedListFrame(pageNum: 3));
        StorageTestHarness.NewGameClient(third).OnDataReceived(RequestLength);

        BinaryPrimitives.ReadInt32LittleEndian(first.Sent[0].AsSpan(PageNumOffset, 4)).Should().Be(1);
        BinaryPrimitives.ReadInt32LittleEndian(third.Sent[0].AsSpan(PageNumOffset, 4)).Should().Be(3);
        first.Sent[0].AsSpan(PageNumOffset, 4).SequenceEqual(third.Sent[0].AsSpan(PageNumOffset, 4))
            .Should().BeFalse("the echoed page must follow the request");
    }

    [Test]
    public void BiddedListRequest_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[ChecksumOffset] = Checksum(keepalive);

        var frame = ClientBiddedListFrame().Concat(keepalive).ToArray();
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
        connection.Sent.Should().ContainSingle();
    }

    [Test]
    public void BiddedListRequest_MalformedFrameIsRefusedWithTheFamilyResult()
    {
        // 10 bytes is a frame the client cannot build. The refusal is a TS_SC_RESULT carrying the request
        // id — never a TM_SC_AUCTION_* error frame, which exists in no reference and in no client — and it
        // is the family's own InvalidArgument, the code the socle uses for a request the client could not
        // have sent (spec §5.2, §5.3).
        var frame = new byte[RequestLength - 1];
        Array.Copy(ClientBiddedListFrame(), frame, frame.Length);
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
        answer.Length.Should().Be(15, "TS_SC_RESULT is request id, result and value, all packed");

        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(7, 2)).Should().Be(1304);
        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(9, 2))
            .Should().Be((ushort)ResultCode.InvalidArgument);
        BinaryPrimitives.ReadInt32LittleEndian(answer.AsSpan(11, 4)).Should().Be(0);
    }
}
