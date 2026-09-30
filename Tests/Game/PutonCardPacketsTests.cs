using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// Offset tests for <c>TM_CS_PUTON_CARD</c> (214). The request is 12 bytes on the wire: the 7 byte
/// header, then <c>position</c> on one byte at offset 7 and the card handle on four bytes at offset 8.
/// <c>position</c> is a single byte under <c>EPIC_9_6_7</c>, where rzu only widens it to <c>int32</c>.
/// </summary>
[TestFixture]
public class PutonCardPacketsTests
{
    private const int HeaderSize = 7;
    private const int PacketLength = HeaderSize + 5;

    [Test]
    public void PutonCard_UsesTheWireIdTwoHundredFourteen()
    {
        // The whole packet is useless if the id drifts: the client sends 214.
        ((ushort)GamePackets.TM_CS_PUTON_CARD).Should().Be(214);
    }

    [Test]
    public void TryReadPutonCard_LaysOutThePositionAtSevenAndTheCardAtEight()
    {
        var packet = new byte[PacketLength];
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_PUTON_CARD);
        packet[7] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8, 4), 0x80000123u);

        GameActionPackets.TryReadPutonCard(packet, out var request).Should().BeTrue();

        packet.Length.Should().Be(12);
        request.Position.Should().Be(2);
        ((int)CardSocketRules.ResolveTarget(request.Position, request.ItemHandle).Slot).Should().Be(2);
        request.ItemHandle.Should().Be(0x80000123u);
    }

    [Test]
    public void TryReadPutonCard_ReadsThePositionAsASignedByte()
    {
        var packet = new byte[PacketLength];
        packet[7] = 0xFF;

        GameActionPackets.TryReadPutonCard(packet, out var request).Should().BeTrue();

        // ItemWearType.None is -1: the byte is signed and must survive as such, not as 255.
        request.Position.Should().Be(-1);
        ((int)CardSocketRules.ResolveTarget(request.Position, 0).Slot).Should().Be(-1);
    }

    [Test]
    public void TryReadPutonCard_DoesNotReadAFourBytePosition()
    {
        var packet = new byte[PacketLength];
        packet[7] = 0x02;
        // Only the first byte of the handle matters for the position: a four byte position would shift
        // the handle to offset 11 and read 0x04030201 here.
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8, 4), 0x04030201u);

        GameActionPackets.TryReadPutonCard(packet, out var request).Should().BeTrue();

        request.Position.Should().Be(2);
        request.ItemHandle.Should().Be(0x04030201u);
    }

    [Test]
    public void TryReadPutonCard_RejectsATruncatedFrame()
    {
        var packet = new byte[PacketLength - 1];
        packet[7] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), 0x80000123u);

        GameActionPackets.TryReadPutonCard(packet, out var request).Should().BeFalse();

        // A frame shorter than 12 bytes leaves the request at its default: nothing is read out of a
        // truncated buffer.
        request.Position.Should().Be(0);
        request.ItemHandle.Should().Be(0u);
    }

    [Test]
    public void TryReadPutonCard_AcceptsTrailingBytesButIgnoresThem()
    {
        var packet = new byte[PacketLength + 4];
        packet[7] = 3;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8, 4), 0x80000456u);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12, 4), 0xDEADBEEFu);

        GameActionPackets.TryReadPutonCard(packet, out var request).Should().BeTrue();

        // The reference declares 12 bytes; a longer frame is accepted rather than dropped, since the
        // frame the 7.3 client really emits was never measured. Nothing past offset 12 is read.
        request.Position.Should().Be(3);
        request.ItemHandle.Should().Be(0x80000456u);
    }
}
