using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;

namespace Tests.Game;

[TestFixture]
public class GameStatPacketsTests
{
    private static byte Checksum(byte[] p)
    {
        byte c = 0;
        for (var i = 0; i < 6; i++) c += p[i];
        return c;
    }

    [Test]
    public void BuildStatInfo_HasCorrectFrameAndBaseStats()
    {
        var stats = new CharacterStats(7, 20, 21, 22, 23, 24, 25, 26, 200, 150);

        var p = GameStatPackets.BuildStatInfo(handle: 0x11223344, stats);

        p.Length.Should().Be(96);
        BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(0, 4)).Should().Be(96);
        BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2)).Should().Be(1000);
        p[6].Should().Be(Checksum(p));
        BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(7, 4)).Should().Be(0x11223344);
        BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(11, 2)).Should().Be(7);
        BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(13, 2)).Should().Be(20);
        BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(15, 2)).Should().Be(21);
        BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(25, 2)).Should().Be(26);
        p[95].Should().Be(0);
    }

    [Test]
    public void BuildProperty_NumericLayoutIsCorrect()
    {
        var p = GameStatPackets.BuildProperty(handle: 0x0A0B0C0D, "level", 42);

        p.Length.Should().Be(37);
        BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(0, 4)).Should().Be(37);
        BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2)).Should().Be(507);
        p[6].Should().Be(Checksum(p));
        BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(7, 4)).Should().Be(0x0A0B0C0D);
        p[11].Should().Be(1);
        Encoding.ASCII.GetString(p, 12, 5).Should().Be("level");
        p[17].Should().Be(0);
        BinaryPrimitives.ReadInt64LittleEndian(p.AsSpan(28, 8)).Should().Be(42);
        p[36].Should().Be(0);
    }
}
