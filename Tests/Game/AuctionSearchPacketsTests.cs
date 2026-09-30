using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// TM_CS_AUCTION_SEARCH (1300), the auction house search request: 51 bytes, five fields, no variable
/// part. The 7.3 client writes it (SFrame.exe constructor 0x48CA20, sender 0x48DC80) and its incoming
/// dispatcher never routes 1300, so the frame is strictly client to server. The answer is a
/// TM_SC_AUCTION_SEARCH (1301) empty page — no code in this repository ever writes the auction table —
/// with the request's page_num echoed back.
/// See docs/packet-specs/1300-auction-search.md.
/// </summary>
[TestFixture]
public class AuctionSearchPacketsTests
{
    private const int RequestLength = 51;
    private const int CategoryOffset = 7;
    private const int SubCategoryOffset = 11;
    private const int KeywordOffset = 15;
    private const int KeywordLength = 31;
    private const int PageNumOffset = 46;
    private const int IsEquipableOffset = 50;

    /// <summary>
    /// The 51-byte request as the client writes it, assembled by hand — never through
    /// <see cref="GameAuctionPackets"/> — so that every offset asserted below is the client's own and
    /// not a round trip through the reader under test.
    /// </summary>
    private static byte[] ClientSearchFrame(int categoryId = 7, int subCategoryId = 3,
        string keyword = "Epic7", int pageNum = 2, byte isEquipable = 1)
    {
        var frame = new byte[RequestLength];

        // Header, literal: Length 51 (0x33 00 00 00), Id 1300 (0x14 0x05), checksum filled in last.
        frame[0] = 0x33;
        frame[4] = 0x14;
        frame[5] = 0x05;

        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(CategoryOffset, 4), categoryId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(SubCategoryOffset, 4), subCategoryId);
        Encoding.ASCII.GetBytes(keyword).AsSpan(0, Math.Min(keyword.Length, KeywordLength))
            .CopyTo(frame.AsSpan(KeywordOffset, KeywordLength));
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(PageNumOffset, 4), pageNum);
        frame[IsEquipableOffset] = isEquipable;

        frame[6] = Checksum(frame);
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

    [Test]
    public void Id_IsTheEpic73OneAndTheFamilyStaysOnTheLowBranch()
    {
        ((ushort)GamePackets.TM_CS_AUCTION_SEARCH).Should().Be(1300);

        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_AUCTION_SEARCH).Should().BeTrue(
            "an id absent from GamePackets is dropped as \"Undefined packet ID\" before any dispatch, " +
            "and one present without a dispatch arm reaches the throwing switch");

        // rzu remaps the family to 2xxx from EPIC_9_6_3 on (TS_CS_AUCTION_SEARCH.h:15-16); 7.3 stays on
        // 1300 and the 9.6.3 value must not be declared here.
        Enum.IsDefined(typeof(GamePackets), (ushort)2300).Should().BeFalse();

        // 1304 is TM_CS_AUCTION_BIDDED_LIST in 7.3 — never the 9.6.3 summon request (GamePackets.cs:74-78).
        Enum.IsDefined(typeof(GamePackets), (ushort)1304).Should().BeFalse();
    }

    [Test]
    public void Request_IsFiftyOneBytesWithTheMeasuredOffsets()
    {
        GameAuctionPackets.SearchRequestSize.Should().Be(RequestLength);
        GameAuctionPackets.KeywordSize.Should().Be(KeywordLength);

        GameAuctionPackets.SearchRequestCategoryOffset.Should().Be(CategoryOffset);
        GameAuctionPackets.SearchRequestSubCategoryOffset.Should().Be(SubCategoryOffset);
        GameAuctionPackets.SearchRequestKeywordOffset.Should().Be(KeywordOffset);
        GameAuctionPackets.SearchRequestPageNumOffset.Should().Be(PageNumOffset);
        GameAuctionPackets.SearchRequestIsEquipableOffset.Should().Be(IsEquipableOffset);

        // 7 header + 4 + 4 + 31 + 4 + 1: no padding, no overlap, and the last byte is is_equipable.
        (GameAuctionPackets.SearchRequestIsEquipableOffset + 1).Should().Be(RequestLength);
        (KeywordOffset + KeywordLength).Should().Be(PageNumOffset);
    }

    [Test]
    public void Request_PutsEveryFieldAtItsOwnOffsetOnTheClientFrame()
    {
        var frame = ClientSearchFrame(categoryId: 0x00000102, subCategoryId: -1, keyword: "Sword",
            pageNum: 0x00000009, isEquipable: 1);

        // Header, byte by byte on the frame itself.
        frame.Length.Should().Be(RequestLength);
        frame[0].Should().Be(0x33, "Length 51 is the literal of the client constructor and sender");
        frame[1].Should().Be(0x00);
        frame[2].Should().Be(0x00);
        frame[3].Should().Be(0x00);
        frame[4].Should().Be(0x14, "Id 1300 is 0x514, low byte first");
        frame[5].Should().Be(0x05);
        frame[6].Should().Be(Checksum(frame));
        frame[6].Should().Be(0x4C, "51 + 0x14 + 0x05 = 76");

        // category_id @7, little endian: 0x00000102 reads back as 258 and never as 0x02010000.
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(CategoryOffset, 4)).Should().Be(0x00000102);
        BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(CategoryOffset, 4)).Should()
            .NotBe(0x00000102, "the field is little endian on the wire");

        // sub_category_id @11: -1 (0xFF FF FF FF) is the client's "no sub-category".
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(SubCategoryOffset, 4)).Should().Be(-1);
        frame[SubCategoryOffset].Should().Be(0xFF);

        // keyword @15, 31 bytes, NUL padded.
        frame[KeywordOffset].Should().Be((byte)'S');
        Encoding.ASCII.GetString(frame, KeywordOffset, KeywordLength).TrimEnd('\0').Should().Be("Sword");
        frame[KeywordOffset + 5].Should().Be(0x00, "the pad starts right after the keyword");
        frame[PageNumOffset - 1].Should().Be(0x00, "the keyword zone ends at 45");

        // page_num @46, little endian: 9 is 09 00 00 00 and never 00 00 00 09.
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(PageNumOffset, 4)).Should().Be(9);
        frame[PageNumOffset].Should().Be(0x09);
        frame[PageNumOffset + 1].Should().Be(0x00);
        BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(PageNumOffset, 4)).Should()
            .NotBe(9, "the field is little endian on the wire");

        // is_equipable @50, the last byte of the frame.
        frame[IsEquipableOffset].Should().Be(0x01);
        (IsEquipableOffset).Should().Be(RequestLength - 1);
    }

    [Test]
    public void TryReadAuctionSearch_HandsBackEveryField()
    {
        GameAuctionPackets.TryReadAuctionSearch(ClientSearchFrame(11, 4, "Ametrine", 6, 1), out var request)
            .Should().BeTrue();

        request.CategoryId.Should().Be(11);
        request.SubCategoryId.Should().Be(4);
        request.Keyword.Should().Be("Ametrine");
        request.PageNum.Should().Be(6);
        request.IsEquipable.Should().BeTrue();
    }

    [Test]
    public void TryReadAuctionSearch_ReadsNegativeCategoryIdsAsSigned()
    {
        // The client's own table carries -1 for "no sub-category", so the field is signed and must not
        // be widened into 0xFFFFFFFF as an unsigned value.
        GameAuctionPackets.TryReadAuctionSearch(ClientSearchFrame(-1, -1), out var request).Should().BeTrue();

        request.CategoryId.Should().Be(-1);
        request.SubCategoryId.Should().Be(-1);
    }

    [Test]
    public void TryReadAuctionSearch_TreatsIsEquipableAsAFlagByte()
    {
        GameAuctionPackets.TryReadAuctionSearch(ClientSearchFrame(isEquipable: 0), out var unchecked1).Should().BeTrue();
        unchecked1.IsEquipable.Should().BeFalse();

        GameAuctionPackets.TryReadAuctionSearch(ClientSearchFrame(isEquipable: 1), out var checked1).Should().BeTrue();
        checked1.IsEquipable.Should().BeTrue();

        // Any nonzero byte is "checked": the client's own byte is a bool, never a value with a range.
        GameAuctionPackets.TryReadAuctionSearch(ClientSearchFrame(isEquipable: 0x80), out var odd).Should().BeTrue();
        odd.IsEquipable.Should().BeTrue();
    }

    [Test]
    public void TryReadAuctionSearch_StopsTheKeywordAtTheFirstNul()
    {
        // "Swo\0" followed by a stray 'd': the reader stops at the NUL and must not read through it.
        var frame = ClientSearchFrame(keyword: "Sword");
        Encoding.ASCII.GetBytes("Swo\0").CopyTo(frame, KeywordOffset);
        frame[KeywordOffset + 4].Should().Be((byte)'d', "the stray byte after the NUL is the trap");

        GameAuctionPackets.TryReadAuctionSearch(frame, out var request).Should().BeTrue();

        request.Keyword.Should().Be("Swo");
    }

    [Test]
    public void TryReadAuctionSearch_ReadsAFullZoneWithoutRequiringATerminator()
    {
        // The client's copy is a bounded strncpy of 31 bytes: a 31-character keyword leaves no NUL, and
        // the reader must not demand one (spec §3.2, §12.5).
        const string full = "0123456789012345678901234567890";
        full.Length.Should().Be(KeywordLength);

        var frame = ClientSearchFrame(keyword: full);
        frame.AsSpan(KeywordOffset, KeywordLength).ToArray().Should().NotContain((byte)0);

        GameAuctionPackets.TryReadAuctionSearch(frame, out var request).Should().BeTrue();

        request.Keyword.Should().Be(full);
    }

    [Test]
    public void TryReadAuctionSearch_ReadsAnEmptyKeyword()
    {
        GameAuctionPackets.TryReadAuctionSearch(ClientSearchFrame(keyword: string.Empty), out var request)
            .Should().BeTrue();

        request.Keyword.Should().BeEmpty();
    }

    [TestCase(0, TestName = "TryReadAuctionSearch_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadAuctionSearch_RejectsAHeaderOnlyFrame")]
    [TestCase(15, TestName = "TryReadAuctionSearch_RejectsAFrameWithoutAnyField")]
    [TestCase(50, TestName = "TryReadAuctionSearch_RejectsAFrameMissingItsLastByte")]
    public void TryReadAuctionSearch_RejectsAnyLengthBelowFiftyOne(int length)
    {
        var packet = new byte[length];
        Array.Copy(ClientSearchFrame(), packet, Math.Min(length, RequestLength));

        GameAuctionPackets.TryReadAuctionSearch(packet, out var request).Should().BeFalse();
        request.Keyword.Should().BeNull();
        request.PageNum.Should().Be(0);
    }

    [Test]
    public void TryReadAuctionSearch_ReadsAPaddedFrameAndLeavesThePaddingAlone()
    {
        // The layout is fixed but the reader is a size gate, not an equality gate: a frame longer than 51
        // bytes has already been bounded by the receive loop, and only the five known fields are read.
        var frame = ClientSearchFrame(pageNum: 4).Concat(new byte[] { 0xAA, 0xBB }).ToArray();

        GameAuctionPackets.TryReadAuctionSearch(frame, out var request).Should().BeTrue();

        request.PageNum.Should().Be(4);
        request.Keyword.Should().Be("Epic7");
    }

    [Test]
    public void SearchRequest_IsConsumedByTheReceiveLoopWithoutThrowing()
    {
        var connection = new StorageTestHarness.FrameConnection(ClientSearchFrame());
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(RequestLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
    }

    [Test]
    public void SearchRequest_AnswersAnEmptyPageEchoingPageNum()
    {
        var connection = new StorageTestHarness.FrameConnection(ClientSearchFrame(pageNum: 5));
        var client = StorageTestHarness.NewGameClient(connection);

        client.OnDataReceived(RequestLength);

        connection.Sent.Should().ContainSingle();
        var answer = connection.Sent[0];

        answer.Length.Should().Be(5139, "the client copies 5120 bytes whatever the count says");
        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_AUCTION_SEARCH);
        BinaryPrimitives.ReadUInt32LittleEndian(answer.AsSpan(0, 4)).Should().Be(5139u);
        answer[6].Should().Be(Checksum(answer));

        BinaryPrimitives.ReadInt32LittleEndian(answer.AsSpan(7, 4)).Should().Be(5, "page_num is echoed");
        BinaryPrimitives.ReadInt32LittleEndian(answer.AsSpan(11, 4)).Should().Be(0,
            "total_page_count has no established rule, so the arm writes 0");
        BinaryPrimitives.ReadInt32LittleEndian(answer.AsSpan(15, 4)).Should().Be(0);
        answer.AsSpan(19).ToArray().Should().OnlyContain(b => b == 0,
            "nothing writes the auction table, so the page is empty and the forty slots stay zero");
    }

    [Test]
    public void SearchRequest_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[6] = Checksum(keepalive);

        var frame = ClientSearchFrame().Concat(keepalive).ToArray();
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
        connection.Sent.Should().ContainSingle();
    }

    [Test]
    public void SearchRequest_MalformedFrameIsRefusedWithTheFamilyResult()
    {
        // 50 bytes is a frame the client cannot build. The refusal is a TS_SC_RESULT carrying the request
        // id — never a TM_SC_AUCTION_* error frame, which exists in no reference and in no client.
        var frame = new byte[RequestLength - 1];
        Array.Copy(ClientSearchFrame(), frame, frame.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)frame.Length);
        frame[6] = Checksum(frame);

        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "a refused frame is still consumed entirely");
        connection.Sent.Should().ContainSingle();

        var answer = connection.Sent[0];
        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_RESULT);
        answer.Length.Should().Be(7 + 8, "TS_SC_RESULT is request id, result and value, all packed");

        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(7, 2)).Should().Be(1300);
        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(9, 2))
            .Should().Be((ushort)ResultCode.InvalidArgument);
        BinaryPrimitives.ReadInt32LittleEndian(answer.AsSpan(11, 4)).Should().Be(0);
    }
}
