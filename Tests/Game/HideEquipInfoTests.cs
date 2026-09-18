using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// TM_CS_HIDE_EQUIP_INFO (221) is 11 bytes on the wire: the 7-byte header plus one raw uint32 mask.
/// See docs/packet-specs/221-hide-equip-info.md.
/// </summary>
[TestFixture]
public class HideEquipInfoTests
{
    private const int ClientPacketLength = 11;

    private static byte[] ClientFrame(uint hideEquipFlag)
    {
        var packet = new byte[ClientPacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), ClientPacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_HIDE_EQUIP_INFO);

        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        packet[6] = checksum;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), hideEquipFlag);
        return packet;
    }

    [Test]
    public void ClientPacket_UsesTheEpic73Layout()
    {
        var packet = ClientFrame(2);

        packet.Length.Should().Be(ClientPacketLength);
        ((ushort)GamePackets.TM_CS_HIDE_EQUIP_INFO).Should().Be(221);

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(ClientPacketLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(221);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(2);
    }

    [Test]
    public void TryReadHideEquipInfo_ReadsTheMaskAtOffsetSeven()
    {
        GameActionPackets.TryReadHideEquipInfo(ClientFrame(0x40000001u), out var hideEquipFlag).Should().BeTrue();

        hideEquipFlag.Should().Be(0x40000001u);
    }

    [TestCase(0u, TestName = "TryReadHideEquipInfo_KeepsEverythingVisible")]
    [TestCase(1u, TestName = "TryReadHideEquipInfo_KeepsTheSingleBitMask")]
    [TestCase(2u, TestName = "TryReadHideEquipInfo_KeepsTheOtherSingleBitMask")]
    [TestCase(3u, TestName = "TryReadHideEquipInfo_KeepsBothBitsSet")]
    [TestCase(0xFFFFFFFFu, TestName = "TryReadHideEquipInfo_KeepsUnspecifiedBitsUntouched")]
    public void TryReadHideEquipInfo_ReturnsTheRawMask(uint mask)
    {
        GameActionPackets.TryReadHideEquipInfo(ClientFrame(mask), out var hideEquipFlag).Should().BeTrue();

        hideEquipFlag.Should().Be(mask);
    }

    [TestCase(0, TestName = "TryReadHideEquipInfo_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadHideEquipInfo_RejectsAHeaderOnlyFrame")]
    [TestCase(10, TestName = "TryReadHideEquipInfo_RejectsATruncatedFrame")]
    public void TryReadHideEquipInfo_RejectsAShortFrame(int length)
    {
        var packet = ClientFrame(3).AsSpan(0, length).ToArray();

        GameActionPackets.TryReadHideEquipInfo(packet, out var hideEquipFlag).Should().BeFalse();
        hideEquipFlag.Should().Be(0u);
    }

    [Test]
    public void AnswerPacket_IsTheExistingFifteenByteHideEquipInfo()
    {
        var packet = GameCharacterPackets.BuildHideEquipInfo(0x40000123, 3);

        packet.Length.Should().Be(15);
        ((ushort)GamePackets.TM_CS_HIDE_EQUIP_INFO).Should().Be(221);
        ((ushort)GamePackets.TM_SC_HIDE_EQUIP_INFO).Should().Be(222);

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(15);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(222);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x40000123u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(3);

        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        packet[6].Should().Be(checksum);
    }

    [Test]
    public void AnswerPacket_EchoesTheRequestedMaskVerbatim()
    {
        var request = ClientFrame(0x80000002u);
        GameActionPackets.TryReadHideEquipInfo(request, out var hideEquipFlag).Should().BeTrue();

        var answer = GameCharacterPackets.BuildHideEquipInfo(42, unchecked((int)hideEquipFlag));

        BinaryPrimitives.ReadUInt32LittleEndian(answer.AsSpan(11, 4)).Should().Be(0x80000002u);
    }
}
