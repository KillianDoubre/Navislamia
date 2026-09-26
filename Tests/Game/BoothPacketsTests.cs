using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// Offset tests for <c>TM_CS_START_BOOTH</c> (700), <c>TM_CS_STOP_BOOTH</c> (701) and
/// <c>TM_CS_CHECK_BOOTH_STARTABLE</c> (711) of the player booth family (docs/packet-specs/socle-booths.md
/// §3.2 and §3.3, docs/packet-specs/711-check-booth-startable.md §3). Every size is measured in the
/// Epic 7.3 client, not deduced: its frame builder <c>VA 0x48CBD0</c> writes 59 in the length, adds
/// <c>16 × N</c> with a stride of <c>0x10</c>, <c>VA 0x48CC20</c> builds 701 as the bare 7 byte header
/// and <c>VA 0x48CFD0</c> builds 711 the same way. 700 is therefore 59 + 16×N bytes — name[49] at 7,
/// type at 56, count (uint16) at 57, items at 59 — and 701 and 711 have no field at all.
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
    public void TryReadCheckBoothStartable_AcceptsTheSevenByteHeaderAlone()
    {
        var packet = ClientCheckBoothStartableFrame();

        // Epic 7.3 layout (fiche §3): the 7 byte header and nothing else. The client writes the id
        // 0x2C7 at offset 4 (0x48CFF2), the length 7 as a literal (0x48CFD8) and the checksum over the
        // six first bytes (0x48D010).
        packet.Should().HaveCount(7, "the total size is the header: no field follows offset 7");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(7, "length at offset 0");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(711, "id at offset 4");
        packet[4].Should().Be(0xC7, "id low byte at offset 4 (711 = 0x02C7, little endian)");
        packet[5].Should().Be(0x02, "id high byte at offset 5");
        packet[6].Should().Be(Checksum(packet), "checksum at offset 6: the sum of the bytes 0..5");

        BoothPackets.CheckBoothStartableLength.Should().Be(7);
        BoothPackets.TryReadCheckBoothStartable(packet).Should().BeTrue();
    }

    [Test]
    public void TryReadCheckBoothStartable_RejectsAnythingThatIsNotExactlySevenBytes()
    {
        var packet = ClientCheckBoothStartableFrame();

        BoothPackets.TryReadCheckBoothStartable(packet.AsSpan(0, 6)).Should().BeFalse(
            "a truncated frame does not even carry the whole header");

        BoothPackets.TryReadCheckBoothStartable(Array.Empty<byte>()).Should().BeFalse();

        // The receive loop slices exactly the announced length, so a frame declaring more than the
        // client can build is the negative twin of the truncation: the 7.3 builder writes a literal 7
        // and never recomputes it from a count, unlike 700.
        var overlong = new byte[BoothPackets.CheckBoothStartableLength + 1];
        packet.CopyTo(overlong, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(overlong.AsSpan(0, 4),
            (uint)overlong.Length);
        overlong[6] = Checksum(overlong);

        BoothPackets.TryReadCheckBoothStartable(overlong).Should().BeFalse(
            "an 8 byte frame is not the 7 byte frame the client builds");
    }

    [Test]
    public void Packet_IsDispatchedBeforeTheUnknownPacketThrow()
    {
        // GameClient's dispatch is a chain of ifs, so a member added to the enum without a branch reaches
        // the final switch and its `throw` kills the receive loop. Nothing smaller than a source scan can
        // check that without a live socket.
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "Game", "Network", "Clients", "GameClient.cs"));

        var branch = source.IndexOf(
            "header.ID == (ushort)GamePackets.TM_CS_CHECK_BOOTH_STARTABLE", StringComparison.Ordinal);
        var finalSwitch = source.IndexOf("throw new Exception($\"Unknown Packet Type", StringComparison.Ordinal);

        branch.Should().BeGreaterThan(-1, "711 needs a branch of its own in OnDataReceived");
        finalSwitch.Should().BeGreaterThan(-1, "the final switch is the guard this test is about");
        branch.Should().BeLessThan(finalSwitch, "711 must be handled before the final switch throws");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Navislamia.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the repository root is needed to check the dispatch chain");

        return directory!.FullName;
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
        BoothPackets.CheckBoothStartableLength.Should().Be(7);
    }

    [Test]
    public void BoothPackets_CarryTheirEpic73IdsAndNothingElseOfTheFamily()
    {
        ((ushort)GamePackets.TM_CS_START_BOOTH).Should().Be(700);
        ((ushort)GamePackets.TM_CS_STOP_BOOTH).Should().Be(701);
        ((ushort)GamePackets.TM_CS_CHECK_BOOTH_STARTABLE).Should().Be(711);

        Enum.IsDefined(typeof(GamePackets), (ushort)700).Should().BeTrue();
        Enum.IsDefined(typeof(GamePackets), (ushort)701).Should().BeTrue();

        // 711 is declared here since the 7.3 client does build and send that frame (SFrame.exe builder
        // VA 0x48CFD0, single call site 0x49A176): what stops at 710 is its incoming dispatcher, not its
        // emission (fiche §2.2, §2.3 and §5.2). 1711 is the 9.6.3 remap of the same id and must not be
        // declared for a 7.3 server.
        Enum.IsDefined(typeof(GamePackets), (ushort)711).Should().BeTrue();
        Enum.IsDefined(typeof(GamePackets), (ushort)1711).Should().BeFalse();

        // 702 to 710 stay out of this socle by decision (fiche §1.1).
        foreach (var id in new ushort[] { 702, 703, 704, 705, 706, 707, 708, 709, 710 })
        {
            Enum.IsDefined(typeof(GamePackets), id).Should().BeFalse();
        }
    }

    /// <summary>
    /// Builds the 7 byte frame <c>TM_CS_CHECK_BOOTH_STARTABLE</c> (711) exactly as the 7.3 client does
    /// (<c>VA 0x48CFD0</c>): length 7, id 711, checksum over the first six bytes, and no payload.
    /// </summary>
    private static byte[] ClientCheckBoothStartableFrame()
    {
        var packet = new byte[BoothPackets.CheckBoothStartableLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4),
            (uint)BoothPackets.CheckBoothStartableLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2),
            (ushort)GamePackets.TM_CS_CHECK_BOOTH_STARTABLE);
        packet[6] = Checksum(packet);
        return packet;
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
