using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

[TestFixture]
public class ChatPacketsTests
{
    [Test]
    public void BuildChatLocal_LaysOutHandleCountTypeAndNullTerminatedMessage()
    {
        var packet = GameChatPackets.BuildChatLocal(0x11223344u, type: 0, message: "hi");

        packet.Length.Should().Be(7 + 4 + 1 + 1 + 3);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be((uint)packet.Length);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_SC_CHAT_LOCAL);
        packet[6].Should().Be(Checksum(packet));
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x11223344u);
        packet[11].Should().Be(3);
        packet[12].Should().Be(0);
        Encoding.ASCII.GetString(packet, 13, 2).Should().Be("hi");
        packet[15].Should().Be(0);
    }

    [Test]
    public void BuildChat_LaysOutSenderCountTypeAndNullTerminatedMessage()
    {
        var packet = GameChatPackets.BuildChat("Freezeraid", type: 0x0B, message: "hello");

        packet.Length.Should().Be(7 + 21 + 2 + 1 + 6);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_SC_CHAT);
        packet[6].Should().Be(Checksum(packet));
        Encoding.ASCII.GetString(packet, 7, 10).Should().Be("Freezeraid");
        packet[7 + 10].Should().Be(0);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(28, 2)).Should().Be(6);
        packet[30].Should().Be(0x0B);
        Encoding.ASCII.GetString(packet, 31, 5).Should().Be("hello");
        packet[36].Should().Be(0);
    }

    private static byte Checksum(byte[] packet)
    {
        byte c = 0;
        for (var i = 0; i < 6; i++) c += packet[i];
        return c;
    }
}
