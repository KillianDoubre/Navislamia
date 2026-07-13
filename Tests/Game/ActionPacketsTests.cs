using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

[TestFixture]
public class ActionPacketsTests
{
    private static byte[] HandlePacket(uint handle)
    {
        var packet = new byte[11];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), handle);
        return packet;
    }

    [Test]
    public void ReadTargetHandle_ReturnsEncodedHandle()
    {
        var packet = HandlePacket(0x40000123u);

        GameActionPackets.ReadTargetHandle(packet).Should().Be(0x40000123u);
    }

    [Test]
    public void ReadTargetHandle_ReturnsZeroForDeselect()
    {
        var packet = HandlePacket(0u);

        GameActionPackets.ReadTargetHandle(packet).Should().Be(0u);
    }

    [Test]
    public void ReadCancelActionHandle_ReturnsEncodedHandle()
    {
        var packet = HandlePacket(0x40000456u);

        GameActionPackets.ReadCancelActionHandle(packet).Should().Be(0x40000456u);
    }

    [Test]
    public void ConnectionInfo_TargetHandleDefaultsToZero()
    {
        new ConnectionInfo().TargetHandle.Should().Be(0u);
    }
}
