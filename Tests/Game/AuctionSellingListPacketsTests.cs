using System;
using System.Buffers.Binary;
using System.Linq;
using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// TM_CS_AUCTION_SELLING_LIST (1302), the request for the character's own sale announcements: 11 bytes,
/// header plus a single int32 page_num, no variable part. The 7.3 client writes it (SFrame.exe
/// constructor-and-sender 0x48DD10, single caller 0x49E37D) and its incoming dispatcher never routes
/// 1302, so the frame is strictly client to server. The answer is a TM_SC_AUCTION_SELLING_LIST (1303)
/// empty page — no code in this repository ever writes the auction table — echoing the request's
/// page_num.
/// See docs/packet-specs/1302-auction-selling-list.md.
/// </summary>
[TestFixture]
public class AuctionSellingListPacketsTests
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
    private static byte[] ClientSellingListFrame(int pageNum = 1)
    {
        var frame = new byte[RequestLength];

        // Header, literal: Length 11 (0x0B 00 00 00), Id 1302 (0x16 0x05), checksum filled in last.
        frame[0] = 0x0B;
        frame[IdOffset] = 0x16;
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
    public void Id_IsTheEpic73OneAndTheFamilyStaysOnTheLowBranch()
    {
        ((ushort)GamePackets.TM_CS_AUCTION_SELLING_LIST).Should().Be(1302);

        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_AUCTION_SELLING_LIST).Should().BeTrue(
            "an id absent from GamePackets is dropped as \"Undefined packet ID\" before any dispatch, " +
            "and one present without a dispatch arm reaches the throwing switch");

        // The response of this very request is already declared by the merged socle: 1302 is the request,
        // 1303 the answer, and the branch must not have moved either.
        ((ushort)GamePackets.TM_SC_AUCTION_SELLING_LIST).Should().Be(1303);

        // rzu remaps the family to 2xxx from EPIC_9_6_3 on (TS_CS_AUCTION_SELLING_LIST.h:10-12); 7.3
        // stays on 1302 and the 9.6.3 value (2302) must not be declared here.
        Enum.IsDefined(typeof(GamePackets), (ushort)2302).Should().BeFalse();

        // 1304 is TM_CS_AUCTION_BIDDED_LIST in 7.3 — never the 9.6.3 summon request
        // (GamePackets.cs:74-77). The rest of the family may be declared, under its auction names only.
        Enum.GetName(typeof(GamePackets), (ushort)1304).Should().BeOneOf(new string[] { null, "TM_CS_AUCTION_BIDDED_LIST" });
        Enum.GetName(typeof(GamePackets), (ushort)1306).Should().BeOneOf(new string[] { null, "TM_CS_AUCTION_BID" });
    }

    [Test]
    public void Request_IsElevenBytesWithTheMeasuredOffsets()
    {
        GameAuctionPackets.SellingListRequestSize.Should().Be(RequestLength);
        GameAuctionPackets.SellingListRequestPageNumOffset.Should().Be(PageNumOffset);

        // The header is the repository's own seven bytes and the request adds exactly four: no padding,
        // no second field, and page_num is the last thing in the frame.
        GameAuctionPackets.SellingListRequestPageNumOffset.Should().Be(7);
        (GameAuctionPackets.SellingListRequestPageNumOffset + 4).Should().Be(RequestLength);
    }

    [Test]
    public void Request_PutsEveryFieldAtItsOwnOffsetOnTheClientFrame()
    {
        var frame = ClientSellingListFrame(pageNum: 0x00000009);

        // Header, byte by byte on the frame itself.
        frame.Length.Should().Be(RequestLength);
        frame[0].Should().Be(0x0B, "Length 11 is the literal of the client constructor-and-sender");
        frame[1].Should().Be(0x00);
        frame[2].Should().Be(0x00);
        frame[3].Should().Be(0x00);
        frame[IdOffset].Should().Be(0x16, "Id 1302 is 0x516, low byte first");
        frame[IdOffset + 1].Should().Be(0x05);
        frame[ChecksumOffset].Should().Be(Checksum(frame));
        frame[ChecksumOffset].Should().Be(0x26, "11 + 0x16 + 0x05 = 38");

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
    public void TryReadAuctionSellingList_HandsBackThePageNum()
    {
        GameAuctionPackets.TryReadAuctionSellingList(ClientSellingListFrame(pageNum: 6), out var pageNum)
            .Should().BeTrue();

        pageNum.Should().Be(6);
    }

    [Test]
    public void TryReadAuctionSellingList_ReadsTheFirstPageTheClientWrites()
    {
        // Two of the client's three emission sites write page_num = 1 in hard (spec §2): the opening of
        // the tab. The page is therefore one-based, and 1 is echoed as 1, never as 0.
        GameAuctionPackets.TryReadAuctionSellingList(ClientSellingListFrame(pageNum: 1), out var pageNum)
            .Should().BeTrue();

        pageNum.Should().Be(1);
    }

    [Test]
    public void TryReadAuctionSellingList_ReadsThePageNumAsSigned()
    {
        // The field is an int32 in rzu (TS_CS_AUCTION_SELLING_LIST.h:8): a negative page must come back
        // negative instead of being widened into a huge unsigned value. What the server should answer
        // then is not established (spec §7.3), so only the reading is asserted here.
        GameAuctionPackets.TryReadAuctionSellingList(ClientSellingListFrame(pageNum: -1), out var pageNum)
            .Should().BeTrue();

        pageNum.Should().Be(-1);
    }

    [TestCase(0, TestName = "TryReadAuctionSellingList_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadAuctionSellingList_RejectsAHeaderOnlyFrame")]
    [TestCase(10, TestName = "TryReadAuctionSellingList_RejectsAFrameMissingItsLastByte")]
    public void TryReadAuctionSellingList_RejectsAnyLengthBelowEleven(int length)
    {
        var packet = new byte[length];
        Array.Copy(ClientSellingListFrame(), packet, Math.Min(length, RequestLength));

        GameAuctionPackets.TryReadAuctionSellingList(packet, out var pageNum).Should().BeFalse();
        pageNum.Should().Be(0, "the refusal leaves no partially read page behind");
    }

    [Test]
    public void TryReadAuctionSellingList_ReadsAPaddedFrameAndLeavesThePaddingAlone()
    {
        // The reader is a size gate, not an equality gate: the receive loop has already bounded the
        // frame, and only the four known bytes are read. The fiche leaves "refuse Length > 11 or only
        // < 11" to Killian (spec §7.7), so the arm accepts the longer frame like 1300 does.
        var frame = ClientSellingListFrame(pageNum: 4).Concat(new byte[] { 0xAA, 0xBB }).ToArray();

        GameAuctionPackets.TryReadAuctionSellingList(frame, out var pageNum).Should().BeTrue();

        pageNum.Should().Be(4);
    }

    [Test]
    public void SellingListRequest_IsConsumedByTheReceiveLoopWithoutThrowing()
    {
        var connection = new StorageTestHarness.FrameConnection(ClientSellingListFrame());
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(RequestLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
    }

    [Test]
    public void SellingListRequest_AnswersAnEmptyPageEchoingPageNum()
    {
        var connection = new StorageTestHarness.FrameConnection(ClientSellingListFrame(pageNum: 5));
        var client = StorageTestHarness.NewGameClient(connection);

        client.OnDataReceived(RequestLength);

        connection.Sent.Should().ContainSingle();
        var answer = connection.Sent[0];

        // The 7.3 client copies 3880 bytes of table without ever reading auction_info_count, so the
        // answer is always 7 + 12 + 40 × 97 and no shorter frame may be produced.
        answer.Length.Should().Be(ResponseLength);
        BinaryPrimitives.ReadUInt32LittleEndian(answer.AsSpan(0, 4)).Should().Be((uint)ResponseLength);
        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(IdOffset, 2))
            .Should().Be((ushort)GamePackets.TM_SC_AUCTION_SELLING_LIST);
        answer[ChecksumOffset].Should().Be(Checksum(answer));

        BinaryPrimitives.ReadInt32LittleEndian(answer.AsSpan(PageNumOffset, 4)).Should().Be(5, "page_num is echoed");
        BinaryPrimitives.ReadInt32LittleEndian(answer.AsSpan(11, 4)).Should().Be(0,
            "total_page_count has no established rule, so the arm writes 0 (spec §7.2, §8 q2)");
        BinaryPrimitives.ReadInt32LittleEndian(answer.AsSpan(15, 4)).Should().Be(0, "no announcement is served");

        // The table is fully written: its last slot ends exactly at the end of the frame, and nothing
        // writes an announcement — no code in the repository reads or writes TelecasterContext.Auctions.
        (ResponseTableOffset + ResponseSlots * ResponseEntrySize).Should().Be(ResponseLength);
        answer.AsSpan(ResponseTableOffset).ToArray().Should().OnlyContain(b => b == 0,
            "the page is empty and the forty slots stay zero");
    }

    [Test]
    public void SellingListRequest_AnswersThePageTheClientAskedForAndNotAFixedOne()
    {
        // Morsure : la page de la réponse est celle de la requête. Si le bras écrivait une page
        // constante (1, comme le prescrit la trame d'ouverture), les deux envois ci-dessous seraient
        // identiques.
        var first = new StorageTestHarness.FrameConnection(ClientSellingListFrame(pageNum: 1));
        StorageTestHarness.NewGameClient(first).OnDataReceived(RequestLength);

        var third = new StorageTestHarness.FrameConnection(ClientSellingListFrame(pageNum: 3));
        StorageTestHarness.NewGameClient(third).OnDataReceived(RequestLength);

        BinaryPrimitives.ReadInt32LittleEndian(first.Sent[0].AsSpan(PageNumOffset, 4)).Should().Be(1);
        BinaryPrimitives.ReadInt32LittleEndian(third.Sent[0].AsSpan(PageNumOffset, 4)).Should().Be(3);
        first.Sent[0].AsSpan(PageNumOffset, 4).SequenceEqual(third.Sent[0].AsSpan(PageNumOffset, 4))
            .Should().BeFalse("the echoed page must follow the request");
    }

    [Test]
    public void SellingListRequest_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[ChecksumOffset] = Checksum(keepalive);

        var frame = ClientSellingListFrame().Concat(keepalive).ToArray();
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
        connection.Sent.Should().ContainSingle();
    }

    [Test]
    public void SellingListRequest_MalformedFrameIsRefusedWithTheFamilyResult()
    {
        // 10 bytes is a frame the client cannot build. The refusal is a TS_SC_RESULT carrying the
        // request id — never a TM_SC_AUCTION_* error frame, which exists in no reference and in no
        // client — and it is the family's own InvalidArgument, the code the socle uses for a request the
        // client could not have sent (spec §5.2).
        var frame = new byte[RequestLength - 1];
        Array.Copy(ClientSellingListFrame(), frame, frame.Length);
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

        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(7, 2)).Should().Be(1302);
        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(9, 2))
            .Should().Be((ushort)ResultCode.InvalidArgument);
        BinaryPrimitives.ReadInt32LittleEndian(answer.AsSpan(11, 4)).Should().Be(0);
    }
}
