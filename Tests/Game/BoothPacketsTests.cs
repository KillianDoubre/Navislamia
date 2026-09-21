using System;
using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// Offset tests for <c>TM_CS_START_BOOTH</c> (700) and <c>TM_CS_STOP_BOOTH</c> (701) of the player
/// booth socle (docs/packet-specs/socle-booths.md §3.2 and §3.3). Both sizes are measured in the
/// Epic 7.3 client, not deduced: its frame builder <c>VA 0x48CBD0</c> writes 59 in the length, adds
/// <c>16 × N</c> with a stride of <c>0x10</c>, and <c>VA 0x48CC20</c> builds 701 as the bare 7 byte
/// header. 700 is therefore 59 + 16×N bytes — name[49] at 7, type at 56, count (uint16) at 57, items
/// at 59 — and 701 has no field at all.
/// </summary>
[TestFixture]
public class BoothPacketsTests
{
    private const int HeaderSize = 7;

    [Test]
    public void TryReadStartBooth_LaysOutAnEmptyFrameAtFiftyNineBytes()
    {
        var packet = BuildStartBooth(count: 0, name: "Boutique");

        packet.Should().HaveCount(59);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(59);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_CS_START_BOOTH);
        packet[HeaderSize].Should().Be((byte)'B', "the name starts right after the 7 byte header");
        packet[HeaderSize + 8].Should().Be(0, "strncpy leaves the field nul terminated");
        packet[56].Should().Be(1, "type sits at offset 56");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(57, 2)).Should().Be(0, "count sits at 57");

        // The client can build this frame (the empty constructor writes 59), so the wire reader
        // accepts it: "at least one item" is a game rule, not a length rule (fiche §3.2).
        BoothPackets.TryReadStartBooth(packet, out var request, out var result).Should().BeTrue();
        result.Should().Be(ResultCode.Success);
        request.Type.Should().Be(1);
        request.Items.Should().BeEmpty();
        Encoding.ASCII.GetString(request.Name).Should().Be("Boutique");
        request.Name.Should().HaveCount(8);
    }

    [Test]
    public void TryReadStartBooth_LaysOutEightItemsOfSixteenBytesStartingAtFiftyNine()
    {
        var packet = BuildStartBooth(count: BoothPackets.MaxBoothItemCount);

        packet.Should().HaveCount(59 + 16 * 8);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(187);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(57, 2)).Should().Be(8);

        // The first record starts at 59 and the last one at 59 + 7 × 16, each one 16 bytes wide.
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(59, 4)).Should().Be(0x80000000u);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(63, 4)).Should().Be(1);
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(67, 8)).Should().Be(1000);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(171, 4)).Should().Be(0x80000007u);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(175, 4)).Should().Be(8);
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(179, 8)).Should().Be(8000);

        BoothPackets.TryReadStartBooth(packet, out var request, out var result).Should().BeTrue();
        result.Should().Be(ResultCode.Success);
        request.Items.Should().HaveCount(8);

        for (var i = 0; i < 8; i++)
        {
            request.Items[i].ItemHandle.Should().Be(0x80000000u + (uint)i, "handle at record offset 0");
            request.Items[i].Count.Should().Be(i + 1, "count at record offset 4");
            request.Items[i].Gold.Should().Be(1000L * (i + 1), "gold at record offset 8, as an int64");
        }
    }

    [Test]
    public void TryReadStartBooth_BoundsTheItemRecordStrideAtSixteenBytes()
    {
        // One item: the frame is 75 bytes, not 59 + 8 + 8 (a 4 byte gold) and not 59 + 4 + 4.
        var packet = BuildStartBooth(count: 1);

        packet.Should().HaveCount(75);

        BoothPackets.TryReadStartBooth(packet, out var request, out _).Should().BeTrue();

        request.Items.Should().HaveCount(1);
        request.Items[0].Gold.Should().Be(1000, "the price is a full int64, which fixes the 16 byte stride");
    }

    [Test]
    public void TryReadStartBooth_ReadsTheNameUpToTheFirstNulOnly()
    {
        var packet = BuildStartBooth(count: 1, name: "abcdefgh");
        packet[HeaderSize + 3] = 0;
        packet[HeaderSize + 4] = (byte)'z';

        BoothPackets.TryReadStartBooth(packet, out var request, out _).Should().BeTrue();

        request.Name.Should().Equal(new byte[] { (byte)'a', (byte)'b', (byte)'c' },
            "everything after the first nul byte is padding, not part of the name");
    }

    [Test]
    public void TryReadStartBooth_ReadsAEvenAFullFortyEightCharacterName()
    {
        var name = new string('n', 48);
        var packet = BuildStartBooth(count: 1, name: name);

        BoothPackets.TryReadStartBooth(packet, out var request, out _).Should().BeTrue();

        request.Name.Should().HaveCount(48, "48 characters plus the nul fit in the 49 byte field");
        Encoding.ASCII.GetString(request.Name).Should().Be(name);
    }

    [Test]
    public void TryReadStartBooth_KeepsTheRawNameBytesWithoutReEncoding()
    {
        var raw = new byte[] { 0xE9, 0xE8, 0xE7, 0xE6, 0xE5, 0xE4 };
        var packet = BuildStartBooth(count: 1, name: "abcdef");
        raw.CopyTo(packet.AsSpan(HeaderSize));

        BoothPackets.TryReadStartBooth(packet, out var request, out _).Should().BeTrue();

        // The client strncpy's the name without any conversion; nothing is decoded or replaced here
        // (fiche §7.9), so the six bytes come back unchanged.
        request.Name.Should().Equal(raw);
    }

    [Test]
    public void TryReadStartBooth_RejectsAFrameShorterThanFiftyNineBytes()
    {
        var packet = new byte[58];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), 58);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2),
            (ushort)GamePackets.TM_CS_START_BOOTH);

        BoothPackets.TryReadStartBooth(packet, out var request, out var result).Should().BeFalse();

        result.Should().Be(ResultCode.InvalidArgument);
        request.Should().BeNull("nothing is read before the frame length is judged");
    }

    [Test]
    public void TryReadStartBooth_RejectsACountAboveTheEightItemCeiling()
    {
        foreach (var announced in new ushort[] { 9, 8 + 1, ushort.MaxValue })
        {
            var packet = BuildStartBooth(count: 0);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(57, 2), announced);

            BoothPackets.TryReadStartBooth(packet, out _, out var result).Should().BeFalse();

            result.Should().Be(ResultCode.LimitMax, $"MAX_BOOTH_ITEM_COUNT is 8, not {announced}");
        }
    }

    [Test]
    public void TryReadStartBooth_RejectsACountTheFrameDoesNotCarry()
    {
        var packet = BuildStartBooth(count: 2);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(57, 2), 3);

        BoothPackets.TryReadStartBooth(packet, out _, out var result).Should().BeFalse();

        result.Should().Be(ResultCode.InvalidArgument,
            "the announced length must be satisfied before any record is read");
    }

    [Test]
    public void TryReadStartBooth_IgnoresTheBytesBeyondTheLastRecord()
    {
        var packet = BuildStartBooth(count: 1);
        var padded = new byte[packet.Length + 4];
        packet.CopyTo(padded, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(padded.AsSpan(0, 4), (uint)padded.Length);

        BoothPackets.TryReadStartBooth(padded, out var request, out _).Should().BeTrue();

        request.Items.Should().HaveCount(1);
    }

    [Test]
    public void TryReadStopBooth_AcceptsTheSevenByteHeaderAlone()
    {
        var packet = new byte[BoothPackets.StopBoothLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_STOP_BOOTH);

        BoothPackets.StopBoothLength.Should().Be(7, "the client writes 7 in the length and no field");

        BoothPackets.TryReadStopBooth(packet).Should().BeTrue();
        BoothPackets.TryReadStopBooth(packet.AsSpan(0, 6)).Should().BeFalse();
    }

    [Test]
    public void TryReadStopBooth_IgnoresAnythingBeyondTheHeader()
    {
        var packet = new byte[BoothPackets.StopBoothLength + 5];

        BoothPackets.TryReadStopBooth(packet).Should().BeTrue();
    }

    [Test]
    public void BoothPackets_PinEveryOffsetOfTheLayout()
    {
        // One place that says the layout, so a silent drift of an offset breaks a test rather than a
        // client. Every value comes from the 7.3 frame builders (fiche §3.2 and §3.3).
        BoothPackets.HeaderSize.Should().Be(7);
        BoothPackets.NameOffset.Should().Be(7);
        BoothPackets.BoothNameFieldLength.Should().Be(49, "char[49]: 48 characters then a nul");
        BoothPackets.TypeOffset.Should().Be(56);
        BoothPackets.CountOffset.Should().Be(57);
        BoothPackets.ItemsOffset.Should().Be(59);
        BoothPackets.StartBoothMinLength.Should().Be(59);
        BoothPackets.StartBoothItemSize.Should().Be(16);
        BoothPackets.MaxBoothItemCount.Should().Be(8);
        BoothPackets.StopBoothLength.Should().Be(7);
    }

    [Test]
    public void BoothPackets_CarryTheirEpic73IdsAndNothingElseOfTheFamily()
    {
        ((ushort)GamePackets.TM_CS_START_BOOTH).Should().Be(700);
        ((ushort)GamePackets.TM_CS_STOP_BOOTH).Should().Be(701);

        Enum.IsDefined(typeof(GamePackets), (ushort)700).Should().BeTrue();
        Enum.IsDefined(typeof(GamePackets), (ushort)701).Should().BeTrue();

        // 711 (TM_CS_CHECK_BOOTH_STARTABLE) is absent from the 7.3 client and therefore from the socle;
        // 702 to 710 stay out of the socle by decision (fiche §1.3 and §5.2).
        foreach (var id in new ushort[] { 702, 703, 704, 705, 706, 707, 708, 709, 710, 711 })
        {
            Enum.IsDefined(typeof(GamePackets), id).Should().BeFalse();
        }
    }

    private static byte[] BuildStartBooth(int count, string name = "Boutique", byte type = 1)
    {
        var length = BoothPackets.StartBoothMinLength + BoothPackets.StartBoothItemSize * count;
        var packet = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2),
            (ushort)GamePackets.TM_CS_START_BOOTH);
        Encoding.ASCII.GetBytes(name).CopyTo(packet, BoothPackets.NameOffset);
        packet[BoothPackets.TypeOffset] = type;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(BoothPackets.CountOffset, 2), (ushort)count);

        for (var i = 0; i < count; i++)
        {
            var record = packet.AsSpan(BoothPackets.ItemsOffset + i * BoothPackets.StartBoothItemSize,
                BoothPackets.StartBoothItemSize);
            BinaryPrimitives.WriteUInt32LittleEndian(record.Slice(0, 4), 0x80000000u + (uint)i);
            BinaryPrimitives.WriteInt32LittleEndian(record.Slice(4, 4), i + 1);
            BinaryPrimitives.WriteInt64LittleEndian(record.Slice(8, 8), 1000L * (i + 1));
        }

        packet[6] = Checksum(packet);
        return packet;
    }

    private static byte Checksum(byte[] packet)
    {
        byte sum = 0;
        for (var i = 0; i < 6; i++) sum += packet[i];
        return sum;
    }
}
