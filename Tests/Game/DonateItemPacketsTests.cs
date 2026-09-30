using System.Buffers.Binary;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// Offsets of <c>TM_CS_DONATE_ITEM</c> (258), the Epic 7.3 client to server donation frame: a 7 byte
/// header, <c>gold</c> (int64) at 7, <c>jp</c> (int32) at 15, the item count (int8) at 19 and then
/// <c>12 × count</c> bytes of records — handle (uint32) then count (int64) each. The total is
/// therefore <c>20 + 12 × count</c> (20 bytes for an offer of gold and jp only, 32 for one item),
/// which is the length the client's own frame builder computes.
/// See docs/packet-specs/258-donate-item.md.
/// </summary>
[TestFixture]
public class DonateItemPacketsTests
{
    private const int HeaderSize = 7;
    private const int RecordSize = 12;

    private static byte[] Frame(long gold = 0, int jp = 0,
        params (uint Handle, long Count)[] items)
    {
        var packet = new byte[20 + RecordSize * items.Length];
        WriteHeader(packet);
        BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(7, 8), gold);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(15, 4), jp);
        packet[19] = (byte)(sbyte)items.Length;

        for (var i = 0; i < items.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(20 + i * RecordSize, 4), items[i].Handle);
            BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(24 + i * RecordSize, 8), items[i].Count);
        }

        return packet;
    }

    private static void WriteHeader(byte[] packet)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_DONATE_ITEM);
        packet[6] = Checksum(packet);
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
    public void DonateItemId_IsTheEpic73One()
    {
        ((ushort)GamePackets.TM_CS_DONATE_ITEM).Should().Be(258);

        // rzu remaps the family to 1258/1259 from EPIC_9_6_3 on (EPIC_7_3 = 0x070300 is below it), so
        // 7.3 stays on the low branch. The id must also be defined, otherwise OnDataReceived drops the
        // frame as "Undefined packet ID" before any dispatch, and it must have its arm, because a
        // declared member without one reaches the final switch and kills the receive loop.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_DONATE_ITEM).Should().BeTrue();
        typeof(GameClient).GetMethod("HandleDonateItemAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            .Should().NotBeNull();
    }

    [TestCase(0, 20, TestName = "FrameLength_IsTwentyBytesWithoutAnyItem")]
    [TestCase(1, 32, TestName = "FrameLength_IsThirtyTwoBytesWithOneItem")]
    [TestCase(19, 248, TestName = "FrameLength_Is248BytesAtTheClientPerDonationCap")]
    public void FrameLength_IsTwentyPlusTwelvePerItem(int items, int expectedLength)
    {
        // The client's builder writes 0x14 (20) as the floor length and 12 x N + 0x14 when items are
        // selected, so the two must agree, or the record walk desynchronises the frame.
        var packet = Frame(items == 0 ? 0 : 1, items == 0 ? 0 : 1,
            Enumerable.Range(0, items).Select(i => ((uint)(0x80000000u + (uint)i), 1L)).ToArray());

        packet.Length.Should().Be(expectedLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be((uint)expectedLength);
    }

    [Test]
    public void TryReadDonateItem_ReadsGoldJpAndCountAtTheirOffsets()
    {
        var packet = Frame(30_000L, 45, (0x80000123u, 3L));
        packet[6].Should().Be(Checksum(packet));

        GameActionPackets.TryReadDonateItem(packet, out var request).Should().BeTrue();
        request.Gold.Should().Be(30_000L);
        request.Jp.Should().Be(45);
        request.Items.Should().HaveCount(1);
        request.Items[0].Handle.Should().Be(0x80000123u);
        request.Items[0].Count.Should().Be(3L);
    }

    [Test]
    public void TryReadDonateItem_KeepsFieldOrder()
    {
        // Asymmetric values: gold and jp swapped in the layout, or the item count read as a 16 bit
        // field, would fail here.
        GameActionPackets.TryReadDonateItem(Frame(7L, 3), out var request).Should().BeTrue();

        request.Gold.Should().Be(7L);
        request.Jp.Should().Be(3);
        request.Items.Should().BeEmpty();
    }

    [Test]
    public void TryReadDonateItem_ReadsEveryRecordOfAMultiItemOffer()
    {
        var packet = Frame(1_000L, 0, (0x80000001u, 1L), (0x80000002u, 5000L), (0x80000003u, 4L));

        GameActionPackets.TryReadDonateItem(packet, out var request).Should().BeTrue();
        request.Items.Should().HaveCount(3);
        request.Items[0].Should().Be(new GameActionPackets.DonateItemEntry(0x80000001u, 1L));
        request.Items[1].Should().Be(new GameActionPackets.DonateItemEntry(0x80000002u, 5000L));
        request.Items[2].Should().Be(new GameActionPackets.DonateItemEntry(0x80000003u, 4L));
    }

    [Test]
    public void TryReadDonateItem_KeepsASignedSixtyFourBitCount()
    {
        // The record count is int64 in 7.3: a value above int32 must survive the read, and a negative
        // one is kept as such instead of being reported as a huge unsigned amount.
        GameActionPackets.TryReadDonateItem(Frame(0L, 0, (0x80000001u, 0x1_0000_0002L)), out var request)
            .Should().BeTrue();
        request.Items[0].Count.Should().Be(0x1_0000_0002L);

        GameActionPackets.TryReadDonateItem(Frame(0L, 0, (0x80000001u, -5L)), out var negative).Should().BeTrue();
        negative.Items[0].Count.Should().Be(-5L);
    }

    [Test]
    public void TryReadDonateItem_ReadsGoldAsSixtyFourBitSigned()
    {
        GameActionPackets.TryReadDonateItem(Frame(-2L, 0), out var request).Should().BeTrue();

        request.Gold.Should().Be(-2L);
    }

    [Test]
    public void TryReadDonateItem_AcceptsTheGoldOnlyAndItemOnlyForms()
    {
        // The client's builder tests the three fields separately and emits the frame as soon as one of
        // them is set, so both single-sided offers are legitimate frames.
        GameActionPackets.TryReadDonateItem(Frame(100L, 0), out var gold).Should().BeTrue();
        gold.Gold.Should().Be(100L);
        gold.Items.Should().BeEmpty();

        GameActionPackets.TryReadDonateItem(Frame(0L, 0, (0x80000001u, 1L)), out var item).Should().BeTrue();
        item.Gold.Should().Be(0L);
        item.Items.Should().HaveCount(1);
    }

    [TestCase(0, TestName = "TryReadDonateItem_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadDonateItem_RejectsAHeaderOnlyFrame")]
    [TestCase(19, TestName = "TryReadDonateItem_RejectsAFrameMissingTheItemCount")]
    public void TryReadDonateItem_RejectsAFrameTooShortForTheFixedFields(int length)
    {
        var packet = new byte[length];
        if (length >= 20)
        {
            Frame(1L, 1).CopyTo(packet, 0);
        }

        GameActionPackets.TryReadDonateItem(packet, out var request).Should().BeFalse();
        request.Should().Be(default(GameActionPackets.DonateItemRequest));
    }

    [Test]
    public void TryReadDonateItem_RejectsAFrameShorterThanItsAnnouncedCount()
    {
        // 20 + 12 x 2 announced, one record present: the walk must stop instead of reading past the
        // end of the buffer.
        var truncated = new byte[20 + RecordSize];
        WriteHeader(truncated);
        truncated[19] = 2;

        GameActionPackets.TryReadDonateItem(truncated, out _).Should().BeFalse();

        var complete = Frame(0L, 0, (0x80000001u, 1L), (0x80000002u, 1L));
        GameActionPackets.TryReadDonateItem(complete, out var request).Should().BeTrue();
        request.Items.Should().HaveCount(2);
    }

    [Test]
    public void TryReadDonateItem_RejectsANegativeCountInsteadOfReadingAnArray()
    {
        // The dynarray count is an int8: an octet >= 0x80 is a negative count, an invalid argument,
        // never a large number of records.
        var packet = new byte[20];
        WriteHeader(packet);
        packet[19] = 0x80;

        GameActionPackets.TryReadDonateItem(packet, out var request).Should().BeFalse();
        request.Should().Be(default(GameActionPackets.DonateItemRequest));

        packet[19] = 0xFF;
        GameActionPackets.TryReadDonateItem(packet, out _).Should().BeFalse();
    }

    [Test]
    public void TryReadDonateItem_AcceptsTheLargestCountTheSignedOctetAllows()
    {
        var packet = new byte[20 + RecordSize * 127];
        WriteHeader(packet);
        packet[19] = 127;

        GameActionPackets.TryReadDonateItem(packet, out var request).Should().BeTrue();
        request.Items.Should().HaveCount(127);
    }

    [Test]
    public void TryReadDonateItem_IgnoresTrailingBytes()
    {
        // The frame builder never pads, but the specification only requires the announced records to
        // fit: extra bytes are not an argument to fail the offer.
        var packet = new byte[20 + RecordSize + 4];
        Frame(5L, 0, (0x80000001u, 1L)).CopyTo(packet, 0);

        GameActionPackets.TryReadDonateItem(packet, out var request).Should().BeTrue();
        request.Gold.Should().Be(5L);
        request.Items.Should().HaveCount(1);
    }
}
