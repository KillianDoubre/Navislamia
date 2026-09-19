using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// Offsets of <c>TM_CS_PUTON_ITEM_SET</c> (281), the Epic 7.3 equipment-set request: a 7 byte header
/// then 24 positional item handles, with no position and no target field. The client builds a 119 byte
/// frame (<c>length = 0x77</c>, 28 dwords copied, SFrame.exe VA 0x48c816 and 0x48d800) while rzu and
/// NGemity only describe the first 24: the reader takes those and ignores the trailing four dwords
/// rather than refuse them (sheet §3 and §7.1).
/// </summary>
[TestFixture]
public class PutonItemSetPacketsTests
{
    private const int ClientFrameLength = 119;
    private const int MinimumFrameLength = 103;
    private const int HandleCount = 24;
    private const int FirstHandleOffset = 7;

    /// <summary>The frame SFrame.exe sends: length 0x77, id 281 in +4, checksum in +6, then handles.</summary>
    private static byte[] ClientFrame()
    {
        var packet = new byte[ClientFrameLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), 0x77);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), 281);

        for (var i = 0; i < HandleCount; i++)
        {
            Write(packet, i, 0x80000000u + (uint)i);
        }

        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        packet[6] = checksum;
        return packet;
    }

    private static void Write(byte[] packet, int handleIndex, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(FirstHandleOffset + handleIndex * 4, 4), value);
    }

    [Test]
    public void PutonItemSetId_MatchesTheEpic73Protocol()
    {
        ((ushort)GamePackets.TM_CS_PUTON_ITEM_SET).Should().Be(281);
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_PUTON_ITEM_SET).Should().BeTrue();
    }

    [Test]
    public void TryReadPutonItemSet_ReadsTheClientFrame()
    {
        var packet = ClientFrame();

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(0x77);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(281);

        GameActionPackets.TryReadPutonItemSet(packet, out var handles).Should().BeTrue();

        handles.Should().HaveCount(HandleCount);
        handles[0].Should().Be(0x80000000u);
        handles[1].Should().Be(0x80000001u);
        handles[23].Should().Be(0x80000017u);
    }

    [Test]
    public void TryReadPutonItemSet_PlacesTheHandlesAtTheirOwnFourByteStride()
    {
        // handle[i] sits at 7 + 4i: the first at 7, the second at 11, the twenty-fourth at 99.
        var packet = new byte[MinimumFrameLength];
        for (var i = 0; i < HandleCount; i++)
        {
            Write(packet, i, (uint)(0x1000 + i));
        }

        GameActionPackets.TryReadPutonItemSet(packet, out var handles).Should().BeTrue();

        for (var i = 0; i < HandleCount; i++)
        {
            handles[i].Should().Be((uint)(0x1000 + i), "handle {0} sits at offset {1}", i, FirstHandleOffset + i * 4);
        }
    }

    [Test]
    public void TryReadPutonItemSet_TakesAHandleZeroAsAnEmptySlot()
    {
        var packet = ClientFrame();
        Write(packet, 0, 0u);

        GameActionPackets.TryReadPutonItemSet(packet, out var handles).Should().BeTrue();

        handles[0].Should().Be(0u);
        handles[1].Should().Be(0x80000001u);
    }

    [Test]
    public void TryReadPutonItemSet_IgnoresTheFourTrailingDwords()
    {
        var packet = ClientFrame();
        for (var i = MinimumFrameLength; i < ClientFrameLength; i++)
        {
            packet[i] = 0xAB;
        }

        GameActionPackets.TryReadPutonItemSet(packet, out var handles).Should().BeTrue();

        handles.Should().HaveCount(HandleCount);
        handles[23].Should().Be(0x80000017u);
        handles.Should().NotContain(0xABABABABu);
    }

    [Test]
    public void TryReadPutonItemSet_AcceptsTheMinimumFrameOfTwentyFourHandles()
    {
        var packet = new byte[MinimumFrameLength];
        Write(packet, 23, 0x51000017u);

        GameActionPackets.TryReadPutonItemSet(packet, out var handles).Should().BeTrue();

        handles[23].Should().Be(0x51000017u);
    }

    [Test]
    public void TryReadPutonItemSet_RejectsAFrameOneByteShort()
    {
        // 102 bytes cannot hold the twenty-fourth handle, which ends at 103: the receiver answers
        // InvalidArgument for that frame, the reader only reports the refusal.
        GameActionPackets.TryReadPutonItemSet(new byte[MinimumFrameLength - 1], out var handles).Should().BeFalse();
        handles.Should().BeNull();
    }

    [Test]
    public void TryReadPutonItemSet_RejectsAHeaderOnlyFrame()
    {
        GameActionPackets.TryReadPutonItemSet(new byte[7], out _).Should().BeFalse();
    }
}
