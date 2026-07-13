using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

[TestFixture]
public class MovePacketsTests
{
    [Test]
    public void BuildMove_LaysOutHeaderAndSingleWaypoint()
    {
        var packet = GameMovePackets.BuildMove(
            handle: 0x40000005u, startTime: 123456u, layer: 3, speed: 30, tx: 94500f, ty: 126100f);

        packet.Length.Should().Be(27);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(27);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_SC_MOVE);
        packet[6].Should().Be(Checksum(packet));
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(123456u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(0x40000005u);
        packet[15].Should().Be(3);
        packet[16].Should().Be(30);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(17, 2)).Should().Be(1);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(19, 4)).Should().Be(94500f);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(23, 4)).Should().Be(126100f);
    }

    private static byte Checksum(byte[] packet)
    {
        byte sum = 0;
        for (var i = 0; i < 6; i++) sum += packet[i];
        return sum;
    }
}
