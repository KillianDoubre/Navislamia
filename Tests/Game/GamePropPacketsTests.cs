using System;
using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Game;
using NUnit.Framework;

namespace Tests.Game;

[TestFixture]
public class GamePropPacketsTests
{
    private static byte Checksum(byte[] packet)
    {
        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        return checksum;
    }

    [Test]
    public void BuildEnterFieldProp_is_63_bytes_with_the_field_prop_object_type()
    {
        var packet = GameSpawnPackets.BuildEnterFieldProp(0x11223344, 91864f, 124260f, 0f, 0,
            123001, 1.5f, 0f, 0f, 3.14f, 1f, 1f, 1f, false, 0f);

        packet.Length.Should().Be(63);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(63);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(3);
        packet[6].Should().Be(Checksum(packet));

        packet[7].Should().Be(2, "the type is ET_StaticObject");
        packet[25].Should().Be(6, "the wire objType is EOT_FieldProp, not the emulator's OBJ_STATIC");
    }

    [Test]
    public void BuildEnterFieldProp_writes_every_field_at_its_offset()
    {
        var packet = GameSpawnPackets.BuildEnterFieldProp(0x11223344, 91864f, 124260f, 7f, 3,
            123001, 1.5f, 0.25f, 0.5f, 3.14f, 1f, 2f, 3f, true, 42.5f);

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8, 4)).Should().Be(0x11223344);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(12, 4)).Should().Be(91864f);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(16, 4)).Should().Be(124260f);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(20, 4)).Should().Be(7f);
        packet[24].Should().Be(3);

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(26, 4)).Should().Be(123001);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(30, 4)).Should().Be(1.5f);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(34, 4)).Should().Be(0.25f);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(38, 4)).Should().Be(0.5f);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(42, 4)).Should().Be(3.14f);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(46, 4)).Should().Be(1f);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(50, 4)).Should().Be(2f);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(54, 4)).Should().Be(3f);
        packet[58].Should().Be(1);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(59, 4)).Should().Be(42.5f);
    }

    [Test]
    public void BuildWarp_is_20_bytes_at_id_12()
    {
        var packet = GameSpawnPackets.BuildWarp(105093f, 137583f, 0f, 0);

        packet.Length.Should().Be(20);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(20);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2))
            .Should().Be(12, "TS_SC_WARP is 12 below EPIC_9_6_3");
        packet[6].Should().Be(Checksum(packet));

        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(7, 4)).Should().Be(105093f);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(11, 4)).Should().Be(137583f);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(15, 4)).Should().Be(0f);
    }

    [Test]
    public void BuildWarp_writes_the_layer_as_a_signed_byte()
    {
        GameSpawnPackets.BuildWarp(0f, 0f, 0f, -1)[19].Should().Be(0xFF);
    }
}
