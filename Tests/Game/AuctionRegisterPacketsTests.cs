using System;
using System.Buffers.Binary;
using System.Linq;
using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// TM_CS_AUCTION_REGISTER (1309), the registration request of the auction house: 32 bytes, the widest and
/// the only two-price frame of the family — the 7-byte header, <c>item_handle</c> (uint32) at 7,
/// <c>item_count</c> (int32) at 11, <c>start_price</c> (int64) at 15, <c>instant_purchase_price</c>
/// (int64) at 23 and <c>duration_type</c> (uint8) at 31, which is the last byte.
/// The 7.3 client builds and sends it from the register button of SUIAuctionRegisterWnd (constructor
/// 0x48DF30, single caller the stub 0x49E3AE, internal key 1160 = 0x488) and never receives it: it has no
/// dedicated answer frame in any reference, and the client reads a generic TM_SC_RESULT (id 0) whose
/// <c>request_msg_id</c> is 1309.
/// See docs/packet-specs/1309-auction-register.md.
/// </summary>
[TestFixture]
public class AuctionRegisterPacketsTests
{
    private const int RequestLength = 32;
    private const int LengthOffset = 0;
    private const int IdOffset = 4;
    private const int ChecksumOffset = 6;
    private const int ItemHandleOffset = 7;
    private const int ItemCountOffset = 11;
    private const int StartPriceOffset = 15;
    private const int InstantPurchasePriceOffset = 23;
    private const int DurationTypeOffset = 31;

    private const int ResultLength = 15;
    private const int ResultRequestIdOffset = 7;
    private const int ResultCodeOffset = 9;
    private const int ResultValueOffset = 11;

    /// <summary>
    /// The 32-byte request as the client writes it, assembled by hand — never through
    /// <see cref="GameAuctionPackets"/> — so that every offset asserted below is the client's own and not a
    /// round trip through the reader under test. The client copies the message's fields one after the
    /// other with no hole on the wire (spec §3.1, §3.2): its internal message reserves four bytes between
    /// <c>item_count</c> and <c>start_price</c> and the sender skips them.
    /// </summary>
    private static byte[] ClientRegisterFrame(uint itemHandle = 1_000, int itemCount = 2,
        long startPrice = 1_000_000, long instantPurchasePrice = 3_000_000_000, byte durationType = 2)
    {
        var frame = new byte[RequestLength];

        // Header, literal: Length 32 (0x20 00 00 00), Id 1309 (0x1D 0x05), checksum filled in last.
        frame[LengthOffset] = 0x20;
        frame[IdOffset] = 0x1D;
        frame[IdOffset + 1] = 0x05;

        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(ItemHandleOffset, 4), itemHandle);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(ItemCountOffset, 4), itemCount);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(StartPriceOffset, 8), startPrice);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(InstantPurchasePriceOffset, 8),
            instantPurchasePrice);
        frame[DurationTypeOffset] = durationType;

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
        ((ushort)GamePackets.TM_CS_AUCTION_REGISTER).Should().Be(1309);

        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_AUCTION_REGISTER).Should().BeTrue(
            "an id absent from GamePackets is dropped as \"Undefined packet ID\" before any dispatch, " +
            "and one present without a dispatch arm reaches the throwing switch");

        // The dated trap of the sheet (§4): rzu gives TS_CS_AUCTION_REGISTER the id 2309 from EPIC_9_6_3
        // on; in 7.3 the registration request is 1309 and 2309 belongs to no frame of this client, which
        // owns a single construction site for 0x51D (spec §3.2).
        Enum.IsDefined(typeof(GamePackets), (ushort)2309).Should().BeFalse(
            "2309 is the EPIC_9_6_3 remap of 1309 and must not be declared here");
        ((ushort)GamePackets.TM_CS_AUCTION_REGISTER).Should().NotBe(2309);

        // 1309 is not TM_CS_SUMMON either: the family's remaps kept TM_CS_SUMMON at 304 (spec §4).
        ((ushort)GamePackets.TM_CS_SUMMON).Should().Be(304);

        // The answer of this act is the generic result frame, never a TM_SC_AUCTION_* one: the family's
        // declared responses stop at 1305 (1301/1303/1305), and 1307 is the documented gap no reference
        // fills, so it stays undeclared.
        ((ushort)GamePackets.TM_SC_RESULT).Should().Be(0);
        Enum.IsDefined(typeof(GamePackets), (ushort)1307).Should().BeFalse(
            "no reference declares a 1307, so the registration request has no dedicated answer frame");
    }

    [Test]
    public void Request_IsThirtyTwoBytesWithTheMeasuredOffsets()
    {
        GameAuctionPackets.AuctionRegisterRequestSize.Should().Be(RequestLength);
        GameAuctionPackets.AuctionRegisterRequestItemHandleOffset.Should().Be(ItemHandleOffset);
        GameAuctionPackets.AuctionRegisterRequestItemCountOffset.Should().Be(ItemCountOffset);
        GameAuctionPackets.AuctionRegisterRequestStartPriceOffset.Should().Be(StartPriceOffset);
        GameAuctionPackets.AuctionRegisterRequestInstantPurchasePriceOffset.Should()
            .Be(InstantPurchasePriceOffset);
        GameAuctionPackets.AuctionRegisterRequestDurationTypeOffset.Should().Be(DurationTypeOffset);

        GameAuctionPackets.AuctionRegisterRequestItemHandleOffset.Should().Be(7,
            "the header is seven bytes");
        GameAuctionPackets.AuctionRegisterRequestSize.Should().Be(7 + 25,
            "32 = 7 header + 4 + 4 + 8 + 8 + 1");
    }

    [Test]
    public void Request_LeavesNoGapBetweenItsFields()
    {
        // The fields are contiguous and in rzu's order: two int32, then two int64, then the byte. A hole
        // anywhere — typically the four reserved bytes of the client's internal message — would shift
        // every price read after it (spec §3.1, §3.2).
        GameAuctionPackets.AuctionRegisterRequestItemCountOffset.Should()
            .Be(GameAuctionPackets.AuctionRegisterRequestItemHandleOffset + 4);
        GameAuctionPackets.AuctionRegisterRequestStartPriceOffset.Should()
            .Be(GameAuctionPackets.AuctionRegisterRequestItemCountOffset + 4);
        GameAuctionPackets.AuctionRegisterRequestInstantPurchasePriceOffset.Should()
            .Be(GameAuctionPackets.AuctionRegisterRequestStartPriceOffset + 8);
        GameAuctionPackets.AuctionRegisterRequestDurationTypeOffset.Should()
            .Be(GameAuctionPackets.AuctionRegisterRequestInstantPurchasePriceOffset + 8);
        GameAuctionPackets.AuctionRegisterRequestSize.Should()
            .Be(GameAuctionPackets.AuctionRegisterRequestDurationTypeOffset + 1,
                "duration_type is the last byte of the frame");

        (ItemHandleOffset + 4 + 4 + 8 + 8 + 1).Should().Be(RequestLength,
            "the payload is 25 bytes and holds exactly five fields");
    }

    [Test]
    public void Request_PutsEveryFieldAtItsOwnOffsetOnTheClientFrame()
    {
        var frame = ClientRegisterFrame(itemHandle: 1_000, itemCount: 2, startPrice: 1_000_000,
            instantPurchasePrice: 3_000_000_000, durationType: 2);

        // Length, four bytes at [0, 3], little endian: the client's own literal 32.
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(LengthOffset, 4)).Should().Be(RequestLength);
        frame[LengthOffset].Should().Be(0x20, "little endian: 32 is 20 00 00 00 and never 00 00 00 20");
        frame[1].Should().Be(0x00);
        frame[2].Should().Be(0x00);
        frame[3].Should().Be(0x00);
        BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(LengthOffset, 4)).Should()
            .NotBe(RequestLength, "the field is little endian on the wire");

        // Id, two bytes at [4, 5]: 1309 is 0x51D, low byte first.
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(IdOffset, 2)).Should().Be(1309);
        frame[IdOffset].Should().Be(0x1D, "Id 1309 is 0x51D, low byte first");
        frame[IdOffset + 1].Should().Be(0x05);

        // Checksum, one byte at [6]: the sum of the first six header bytes, 32 + 0x1D + 0x05 = 66.
        frame[ChecksumOffset].Should().Be(Checksum(frame));
        frame[ChecksumOffset].Should().Be(0x42, "32 + 0x1D + 0x05 = 66");

        // item_handle, four bytes at [7, 10], little endian.
        frame[ItemHandleOffset].Should().Be(0xE8, "little endian: 1000 is E8 03 00 00");
        frame[ItemHandleOffset + 1].Should().Be(0x03);
        frame[ItemHandleOffset + 2].Should().Be(0x00);
        frame[ItemHandleOffset + 3].Should().Be(0x00);
        BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(ItemHandleOffset, 4)).Should()
            .NotBe(1_000u, "the field is little endian on the wire");

        // item_count, four bytes at [11, 14].
        frame[ItemCountOffset].Should().Be(0x02);
        frame[ItemCountOffset + 1].Should().Be(0x00);
        frame[ItemCountOffset + 2].Should().Be(0x00);
        frame[ItemCountOffset + 3].Should().Be(0x00);
        BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(ItemCountOffset, 4)).Should().NotBe(2);

        // start_price, eight bytes at [15, 22]: 1 000 000 is 0x0F4240.
        frame[StartPriceOffset].Should().Be(0x40);
        frame[StartPriceOffset + 1].Should().Be(0x42);
        frame[StartPriceOffset + 2].Should().Be(0x0F);
        frame[StartPriceOffset + 3].Should().Be(0x00);
        frame[StartPriceOffset + 4].Should().Be(0x00);
        frame[StartPriceOffset + 5].Should().Be(0x00);
        frame[StartPriceOffset + 6].Should().Be(0x00);
        frame[StartPriceOffset + 7].Should().Be(0x00);

        // instant_purchase_price, eight bytes at [23, 30]: 3 000 000 000 is 0xB2D05E00, so the upper
        // dword is not zero — the field really is 64 bits wide (spec §3.1).
        frame[InstantPurchasePriceOffset].Should().Be(0x00);
        frame[InstantPurchasePriceOffset + 1].Should().Be(0x5E);
        frame[InstantPurchasePriceOffset + 2].Should().Be(0xD0);
        frame[InstantPurchasePriceOffset + 3].Should().Be(0xB2);
        frame[InstantPurchasePriceOffset + 4].Should().Be(0x00);
        frame[InstantPurchasePriceOffset + 5].Should().Be(0x00);
        frame[InstantPurchasePriceOffset + 6].Should().Be(0x00);
        frame[InstantPurchasePriceOffset + 7].Should().Be(0x00);

        // duration_type, one byte at [31]: the option button `common_radiobutton01` sends index + 1 = 2
        // (spec §3.5), and nothing follows it.
        frame[DurationTypeOffset].Should().Be(0x02);
        DurationTypeOffset.Should().Be(RequestLength - 1, "duration_type ends the frame");
    }

    [Test]
    public void TryReadAuctionRegister_HandsBackEveryField()
    {
        GameAuctionPackets.TryReadAuctionRegister(ClientRegisterFrame(), out var itemHandle,
            out var itemCount, out var startPrice, out var instantPurchasePrice, out var durationType)
            .Should().BeTrue();

        itemHandle.Should().Be(1_000u);
        itemCount.Should().Be(2);
        startPrice.Should().Be(1_000_000L);
        instantPurchasePrice.Should().Be(3_000_000_000L);
        durationType.Should().Be(2);
    }

    [Test]
    public void TryReadAuctionRegister_ReadsTheTwoPricesInSixtyFourBits()
    {
        // 3 000 000 000 does not fit in an int32: read through a 32-bit field, or truncated to one, the
        // instant purchase price would come back as something else. The client parses both edit controls
        // straight into an int64 (spec §3.4), so the whole 64-bit field belongs to the price.
        const long expectedInstantPurchasePrice = 3_000_000_000L;
        const long expectedStartPrice = 2_500_000_000L;
        var frame = ClientRegisterFrame(startPrice: expectedStartPrice,
            instantPurchasePrice: expectedInstantPurchasePrice);

        GameAuctionPackets.TryReadAuctionRegister(frame, out _, out _, out var startPrice,
            out var instantPurchasePrice, out _).Should().BeTrue();

        instantPurchasePrice.Should().Be(expectedInstantPurchasePrice);
        startPrice.Should().Be(expectedStartPrice);
        instantPurchasePrice.Should().BeGreaterThan(int.MaxValue,
            "a 32-bit read could not return 3 000 000 000");
        startPrice.Should().BeGreaterThan(int.MaxValue,
            "a 32-bit read could not return 2 500 000 000");
        ((int)instantPurchasePrice).Should().Be(unchecked((int)expectedInstantPurchasePrice),
            "the low dword alone is 0xB2D05E00, i.e. a negative int32, not the price");
    }

    [Test]
    public void TryReadAuctionRegister_ReadsTheItemHandleUnsigned()
    {
        // The handle is a plain dword in the frame; 0xFFFFFFFF must come back as uint.MaxValue and never
        // as -1 widened into the uint parameter (spec §4: `item_handle` is read unsigned, like the
        // family's other handles).
        GameAuctionPackets.TryReadAuctionRegister(ClientRegisterFrame(itemHandle: uint.MaxValue),
            out var itemHandle, out _, out _, out _, out _).Should().BeTrue();

        itemHandle.Should().Be(uint.MaxValue);
        ((int)itemHandle).Should().Be(-1, "the same four bytes read as a signed int32 are -1");
    }

    [Test]
    public void TryReadAuctionRegister_ReadsTheCountAsASignedInt32WithoutJudgingIt()
    {
        // rzu declares int32_t item_count and the client copies the selected row's cell; whether a
        // non-positive or absurd count must be refused is an open decision (spec §7c), so the reader
        // hands the value back exactly as the four bytes spell it — it neither clamps nor rejects.
        GameAuctionPackets.TryReadAuctionRegister(ClientRegisterFrame(itemCount: -1), out _, out var count,
            out _, out _, out _).Should().BeTrue();

        count.Should().Be(-1);
    }

    [Test]
    public void TryReadAuctionRegister_LeavesThePricesUnsignedInWidthAndUnjudged()
    {
        // The sheet measures two int64 parsed from the edit controls with no visible bound (spec §7b):
        // the reader hands the raw 64-bit values over, including a negative one, and refuses nothing —
        // the bounds belong to the registration rules, which are not written.
        GameAuctionPackets.TryReadAuctionRegister(
            ClientRegisterFrame(startPrice: long.MinValue, instantPurchasePrice: -1),
            out _, out _, out var startPrice, out var instantPurchasePrice, out _).Should().BeTrue();

        startPrice.Should().Be(long.MinValue);
        instantPurchasePrice.Should().Be(-1L);
    }

    [TestCase(1, TestName = "TryReadAuctionRegister_AcceptsTheShortDuration")]
    [TestCase(2, TestName = "TryReadAuctionRegister_AcceptsTheMidDuration")]
    [TestCase(3, TestName = "TryReadAuctionRegister_AcceptsTheLongDuration")]
    [TestCase(0, TestName = "TryReadAuctionRegister_HandsOverTheNeverEmittedZero")]
    [TestCase(255, TestName = "TryReadAuctionRegister_HandsOverAnOutOfDomainByte")]
    public void TryReadAuctionRegister_HandsTheDurationTypeOverWithoutDecidingItsDomain(byte durationType)
    {
        // Measured: the client emits 1, 2 or 3 and never 0 (spec §3.5). What each value is worth in time,
        // and whether 0 means "unspecified" and may be accepted, are decisions of Killian (spec §7a,
        // §8 q1): the reader therefore bounds the frame only and never judges the byte.
        GameAuctionPackets.TryReadAuctionRegister(ClientRegisterFrame(durationType: durationType),
            out _, out _, out _, out _, out var readDurationType).Should().BeTrue();

        readDurationType.Should().Be(durationType);
    }

    [TestCase(0, TestName = "TryReadAuctionRegister_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadAuctionRegister_RejectsAHeaderOnlyFrame")]
    [TestCase(15, TestName = "TryReadAuctionRegister_RejectsAFrameStoppingBeforeTheStartPrice")]
    [TestCase(23, TestName = "TryReadAuctionRegister_RejectsAFrameMissingItsInstantPurchasePrice")]
    [TestCase(31, TestName = "TryReadAuctionRegister_RejectsAFrameMissingItsLastByte")]
    [TestCase(33, TestName = "TryReadAuctionRegister_RejectsAPaddedFrame")]
    [TestCase(36, TestName = "TryReadAuctionRegister_RejectsTheInternalMessageSReservedDword")]
    public void TryReadAuctionRegister_RejectsAnyLengthOtherThanThirtyTwo(int length)
    {
        // The exact length is the only accepted form: the layout is fixed, so a short frame would only
        // misalign its fields, and 36 is the trap of this lot — the client's internal message reserves a
        // dword between item_count and start_price, and a reader built from that message instead of from
        // the wire would ask for 36 bytes (spec §3.2).
        var packet = new byte[length];
        Array.Copy(ClientRegisterFrame(), packet, Math.Min(length, RequestLength));

        GameAuctionPackets.TryReadAuctionRegister(packet, out var itemHandle, out var itemCount,
            out var startPrice, out var instantPurchasePrice, out var durationType).Should().BeFalse();

        itemHandle.Should().Be(0u, "the refusal leaves no partially read handle behind");
        itemCount.Should().Be(0, "the refusal leaves no partially read count behind");
        startPrice.Should().Be(0L, "the refusal leaves no partially read price behind");
        instantPurchasePrice.Should().Be(0L, "the refusal leaves no partially read price behind");
        durationType.Should().Be(0, "the refusal leaves no partially read duration behind");
    }

    [Test]
    public void RegisterRequest_IsConsumedByTheReceiveLoopWithoutThrowing()
    {
        var connection = new StorageTestHarness.FrameConnection(ClientRegisterFrame());
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(RequestLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
    }

    [Test]
    public void RegisterRequest_ExecutesNothingAndAnswersNothing()
    {
        // The sheet is explicit: the registration is neither executed nor answered in this lot. Nothing in
        // the server can write an announcement, no ResultCode for a well-formed request is established
        // (spec §5.5, §7.1), and 1309 has no dedicated answer frame: answering Success would claim a
        // registration that never happened, answering a refusal would invent a code. The client therefore
        // receives no frame at all.
        var connection = new StorageTestHarness.FrameConnection(ClientRegisterFrame(itemHandle: 77));
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(RequestLength);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "the frame was consumed even though nothing answers it");
        connection.Sent.Should().BeEmpty(
            "a well-formed registration request is a no-op until the announcement writer and its rules exist");
    }

    [Test]
    public void RegisterRequest_OutOfDomainDurationStillAnswersNothing()
    {
        // A duration_type of 0 is a frame the 7.3 client cannot build, but its fate is the open question 1
        // of sheet §8: refusing it (spec §5.4 row 2) and accepting it as "unspecified" (spec §7a) are both
        // defensible, so the lot only reports it and keeps the loop unchanged. The evidence for this
        // choice is written in the sheet's §14.
        var connection = new StorageTestHarness.FrameConnection(ClientRegisterFrame(durationType: 0));
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(RequestLength);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0);
        connection.Sent.Should().BeEmpty();
    }

    [Test]
    public void RegisterRequest_MalformedFrameIsRefusedWithTheFamilyResult()
    {
        // 31 bytes is a frame no client can build. The refusal is a TS_SC_RESULT carrying the request id —
        // never a TM_SC_AUCTION_* error frame, which exists in no reference and in no client — and it is
        // the family's own InvalidArgument (spec §5.4, §5.7 item 4).
        var frame = new byte[RequestLength - 1];
        Array.Copy(ClientRegisterFrame(), frame, frame.Length);
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

        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(ResultRequestIdOffset, 2)).Should().Be(1309,
            "the result names the request it refuses, not 1306, 1308 or the result frame's own id");
        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(ResultCodeOffset, 2))
            .Should().Be((ushort)ResultCode.InvalidArgument);
        BinaryPrimitives.ReadInt32LittleEndian(answer.AsSpan(ResultValueOffset, 4)).Should().Be(0);
    }

    [Test]
    public void RegisterRequest_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[ChecksumOffset] = Checksum(keepalive);

        var frame = ClientRegisterFrame().Concat(keepalive).ToArray();
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
        connection.Sent.Should().BeEmpty("neither the registration request nor the keepalive is answered");
    }
}
