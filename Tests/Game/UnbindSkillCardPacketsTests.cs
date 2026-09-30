using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// Offsets of <c>TM_CS_UNBIND_SKILLCARD</c> (285) and of its answer <c>TM_SC_SKILLCARD_INFO</c> (286).
/// Both frames are 15 bytes: a 7 byte header, <c>item_handle</c> at 7 and <c>target_handle</c> at 11.
/// rzu (<c>TS_CS_UNBIND_SKILLCARD.h</c>) and NGemity declare the same two handles, and the 7.3 client
/// builds the request with <c>Length = 15</c> and writes nothing after offset 14.
/// </summary>
[TestFixture]
public class UnbindSkillCardPacketsTests
{
    private static byte[] Request(uint itemHandle, uint targetHandle)
    {
        var packet = new byte[15];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), 15);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), 285);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), itemHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(11, 4), targetHandle);
        packet[6] = new Header(packet).CalculateChecksum();
        return packet;
    }

    [Test]
    public void SkillCardIds_MatchTheEpic73Protocol()
    {
        ((ushort)GamePackets.TM_CS_UNBIND_SKILLCARD).Should().Be(285);
        ((ushort)GamePackets.TM_SC_SKILLCARD_INFO).Should().Be(286);
    }

    [Test]
    public void TryReadUnbindSkillCard_ReadsTheFifteenByteLayout()
    {
        var packet = Request(0x80000123u, 0x40000001u);

        packet.Should().HaveCount(15);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(15);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(285);
        packet[6].Should().Be(0x2D);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x80000123u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(0x40000001u);

        GameActionPackets.TryReadUnbindSkillCard(packet, out var request).Should().BeTrue();
        request.ItemHandle.Should().Be(0x80000123u);
        request.TargetHandle.Should().Be(0x40000001u);
    }

    [Test]
    public void TryReadUnbindSkillCard_ReadsASelfTargetFrame()
    {
        GameActionPackets.TryReadUnbindSkillCard(Request(0x80000123u, 0x40000002u), out var request)
            .Should().BeTrue();
        request.TargetHandle.Should().Be(0x40000002u);
    }

    [Test]
    public void TryReadUnbindSkillCard_RejectsATruncatedFrame()
    {
        // 7 + 4 + 4 is the whole frame: nothing is consumed after offset 14 (unlike the 47 byte 253).
        var truncated = Request(0x80000123u, 0x40000001u)[..14];

        GameActionPackets.TryReadUnbindSkillCard(truncated, out var request).Should().BeFalse();
        request.ItemHandle.Should().Be(0u);
        request.TargetHandle.Should().Be(0u);
    }

    [Test]
    public void TryReadUnbindSkillCard_IgnoresBytesBeyondTheDeclaredFrame()
    {
        var padded = new byte[16];
        Request(0x80000123u, 0x40000001u).CopyTo(padded, 0);
        padded[15] = 0xAB;

        GameActionPackets.TryReadUnbindSkillCard(padded, out var request).Should().BeTrue();
        request.ItemHandle.Should().Be(0x80000123u);
        request.TargetHandle.Should().Be(0x40000001u);
    }

    [Test]
    public void BuildSkillCardInfo_LaysOutTheFifteenByteEcho()
    {
        var packet = GameCharacterPackets.BuildSkillCardInfo(0x80000123u, 0x40000001u);

        packet.Should().HaveCount(15);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(15);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(286);
        packet[6].Should().Be(new Header(packet).CalculateChecksum());
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x80000123u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(0x40000001u);
    }

    [Test]
    public void BuildSkillCardInfo_CarriesANullTargetAfterUnbinding()
    {
        var packet = GameCharacterPackets.BuildSkillCardInfo(0x80000123u, 0u);

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(0u);
        packet[6].Should().Be(new Header(packet).CalculateChecksum());
    }
}
