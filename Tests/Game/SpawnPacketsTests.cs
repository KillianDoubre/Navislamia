using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

[TestFixture]
public class SpawnPacketsTests
{
    [Test]
    public void BuildEnterNpc_LaysOutHeaderPositionCreatureInfoAndNpcId()
    {
        var packet = GameSpawnPackets.BuildEnterNpc(
            handle: 0x40000001u, x: 92044f, y: 116950f, z: 12.5f,
            layer: 3, hp: 250, level: 7, race: 2, npcId: 200015);

        packet.Length.Should().Be(72);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(72);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_SC_ENTER);
        packet[6].Should().Be(Checksum(packet));
        packet[7].Should().Be(1);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8, 4)).Should().Be(0x40000001u);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(12, 4)).Should().Be(92044f);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(16, 4)).Should().Be(116950f);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(20, 4)).Should().Be(12.5f);
        packet[24].Should().Be(3);
        packet[25].Should().Be(1);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(34, 4)).Should().Be(250);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(38, 4)).Should().Be(250);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(50, 4)).Should().Be(7);
        packet[54].Should().Be(2);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(64, 2)).Should().Be(0);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(66, 2)).Should().Be(200015 >> 16);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(68, 2)).Should().Be(0);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(70, 2)).Should().Be(200015 & 0xFFFF);
    }

    [Test]
    public void ScrambledInt_DecodeReversesEncode_AndScrambles()
    {
        ScrambledInt.Encode(0).Should().Be(0);
        foreach (var v in new uint[] { 1, 2, 42, 2101, 200015, 0xDEADBEEF, uint.MaxValue })
        {
            ScrambledInt.Decode(ScrambledInt.Encode(v)).Should().Be(v);
        }

        ScrambledInt.Encode(2101).Should().NotBe(2101);
    }

    [Test]
    public void BuildEnterMonster_LaysOutHeaderCreatureInfoScrambledIdAndIsTamed()
    {
        var packet = GameSpawnPackets.BuildEnterMonster(
            handle: 0x40000002u, x: 83950f, y: 115980f, z: 4f,
            layer: 3, hp: 900, level: 5, race: 1, monsterId: 2101, faceDir: 1.5f);

        var enc = ScrambledInt.Encode(2101);

        packet.Length.Should().Be(73);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(30, 4)).Should().Be(1.5f);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(73);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_SC_ENTER);
        packet[6].Should().Be(Checksum(packet));
        packet[7].Should().Be(1);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8, 4)).Should().Be(0x40000002u);
        packet[24].Should().Be(3);
        packet[25].Should().Be(3);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(34, 4)).Should().Be(900);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(50, 4)).Should().Be(5);
        packet[54].Should().Be(1);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(64, 2)).Should().Be(0);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(66, 2)).Should().Be((ushort)((enc >> 16) & 0xFFFF));
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(68, 2)).Should().Be(0);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(70, 2)).Should().Be((ushort)(enc & 0xFFFF));
        packet[72].Should().Be(0);
    }

    [Test]
    public void BuildLeave_LaysOutHandle()
    {
        var packet = GameSpawnPackets.BuildLeave(0x40000005u);

        packet.Length.Should().Be(11);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(11);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_SC_LEAVE);
        packet[6].Should().Be(Checksum(packet));
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x40000005u);
    }

    [Test]
    public void WorldObjectHandle_Next_ReturnsIncreasingUniqueHandlesAboveSeed()
    {
        var a = WorldObjectHandle.Next();
        var b = WorldObjectHandle.Next();

        a.Should().BeGreaterThan(0x40000000u);
        b.Should().BeGreaterThan(a);
    }

    private static byte Checksum(byte[] packet)
    {
        byte c = 0;
        for (var i = 0; i < 6; i++) c += packet[i];
        return c;
    }
}
