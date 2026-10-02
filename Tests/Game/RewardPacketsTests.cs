using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// The two frames of a monster's death, laid out field by field
/// (docs/packet-specs/socle-recompenses-monstres.md §3.2, §3.5).
/// </summary>
[TestFixture]
public class RewardPacketsTests
{
    [Test]
    public void ChaosGainWritesTheEpic73Layout()
    {
        var frame = GameRewardPackets.BuildGetChaos(0x11223344u, 0x55667788u, -1234, -2, 5, -7);

        frame.Length.Should().Be(25);
        ((ushort)GamePackets.TM_SC_GET_CHAOS).Should().Be(213);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)).Should().Be(213);

        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(7, 4)).Should().Be(0x11223344u);
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(11, 4)).Should().Be(0x55667788u);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(15, 4)).Should().Be(-1234);
        frame[19].Should().Be(unchecked((byte)-2));
        frame[20].Should().Be(5);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(21, 4)).Should().Be(-7);

        Checksum(frame);
    }

    [Test]
    public void ChaosGainWithoutBonusWritesZerosAfterTheAmount()
    {
        var frame = GameRewardPackets.BuildGetChaos(42, 7, 12);

        frame.Length.Should().Be(25);
        frame[19].Should().Be(0);
        frame[20].Should().Be(0);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(21, 4)).Should().Be(0);
        Checksum(frame);
    }

    [Test]
    public void ItemDropInfoWritesTheCorpseThenTheObject()
    {
        var frame = GameRewardPackets.BuildItemDropInfo(0x55667788u, 0x99aabbccu);

        frame.Length.Should().Be(15);
        ((ushort)GamePackets.TM_SC_ITEM_DROP_INFO).Should().Be(282);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)).Should().Be(282);

        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(7, 4)).Should().Be(0x55667788u);
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(11, 4)).Should().Be(0x99aabbccu);

        Checksum(frame);
    }

    /// <summary>The 7 byte header: the size, the id, then the sum of the first six bytes.</summary>
    private static void Checksum(byte[] frame)
    {
        byte sum = 0;
        for (var i = 0; i < 6; i++)
        {
            sum += frame[i];
        }

        frame[6].Should().Be(sum);
    }
}
