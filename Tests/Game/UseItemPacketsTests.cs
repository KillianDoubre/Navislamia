using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// Offsets of <c>TM_CS_USE_ITEM</c> (253) and of its answer <c>TM_SC_USE_ITEM_RESULT</c> (283).
/// The request is 47 bytes: a 7 byte header, item_handle at 7, target_handle at 11 and the 32 byte
/// szParameter at 15. The answer is 15 bytes: a 7 byte header then both handles in the same order.
/// </summary>
[TestFixture]
public class UseItemPacketsTests
{
    private static byte[] Request(uint itemHandle, uint targetHandle)
    {
        var packet = new byte[47];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), itemHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(11, 4), targetHandle);
        return packet;
    }

    [Test]
    public void UseItemIds_MatchTheEpic73Protocol()
    {
        ((ushort)GamePackets.TM_CS_USE_ITEM).Should().Be(253);
        ((ushort)GamePackets.TM_SC_USE_ITEM_RESULT).Should().Be(283);
    }

    [Test]
    public void TryReadUseItem_ReadsTheEpic73Layout()
    {
        var packet = Request(0x80000123u, 0x40000001u);
        for (var i = 15; i < 47; i++)
        {
            // The parameter content is not established: only its 32 bytes belong to the frame.
            packet[i] = 0xAB;
        }

        GameActionPackets.TryReadUseItem(packet, out var request).Should().BeTrue();
        request.ItemHandle.Should().Be(0x80000123u);
        request.TargetHandle.Should().Be(0x40000001u);
    }

    [Test]
    public void TryReadUseItem_ReadsTheSelfTargetFrame()
    {
        GameActionPackets.TryReadUseItem(Request(0x80000123u, 0u), out var request).Should().BeTrue();
        request.ItemHandle.Should().Be(0x80000123u);
        request.TargetHandle.Should().Be(0u);
    }

    [Test]
    public void TryReadUseItem_RejectsAFrameShorterThanTheParameter()
    {
        GameActionPackets.TryReadUseItem(new byte[46], out _).Should().BeFalse();
        GameActionPackets.TryReadUseItem(Request(0x80000123u, 0x40000001u), out _).Should().BeTrue();
    }

    [Test]
    public void BuildUseItemResult_LaysOutTheFifteenByteAnswer()
    {
        var packet = GameCharacterPackets.BuildUseItemResult(0x80000123u, 0x40000001u);

        packet.Length.Should().Be(15);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(15);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_USE_ITEM_RESULT);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x80000123u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(0x40000001u);

        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        packet[6].Should().Be(checksum);
    }

    [Test]
    public void BuildDestroyItem_LaysOutTheElevenByteNotice()
    {
        var packet = GameCharacterPackets.BuildDestroyItem(0x80000123u);

        packet.Length.Should().Be(11);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_DESTROY_ITEM);
        ((ushort)GamePackets.TM_SC_DESTROY_ITEM).Should().Be(254);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x80000123u);
    }

    [Test]
    public void BuildUpdateItemCount_WritesTheCountAsInt64()
    {
        var packet = GameCharacterPackets.BuildUpdateItemCount(0x80000123u, 0x1_0000_0002L);

        packet.Length.Should().Be(19);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_UPDATE_ITEM_COUNT);
        ((ushort)GamePackets.TM_SC_UPDATE_ITEM_COUNT).Should().Be(255);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x80000123u);
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(11, 8)).Should().Be(0x1_0000_0002L);
    }
}
