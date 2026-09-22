using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// Offset tests for <c>TM_CS_DROP_ITEM</c> (203) and <c>TS_SC_DROP_RESULT</c> (205). The request is
/// 15 bytes on the wire (7 byte header, then the 4 byte handle at offset 7 and the signed 4 byte unit
/// count at offset 11) and the result is 12 bytes (7 byte header, handle at offset 7, one byte verdict
/// at offset 11). Both sizes come from the Epic 7.3 client structures, not from a guess.
/// </summary>
[TestFixture]
public class DropItemPacketsTests
{
    private const int HeaderSize = 7;
    private const int RequestLength = HeaderSize + 8;
    private const int ResultLength = HeaderSize + 5;

    [Test]
    public void TryReadDropItem_LaysOutTheHandleAtSevenAndTheCountAtEleven()
    {
        var packet = new byte[RequestLength];
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_DROP_ITEM);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), 0x80000123u);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(11, 4), 7);

        GameActionPackets.TryReadDropItem(packet, out var request).Should().BeTrue();

        packet.Length.Should().Be(15);
        request.ItemHandle.Should().Be(0x80000123u);
        request.Count.Should().Be(7);
    }

    [Test]
    public void TryReadDropItem_ReadsTheCountAsASignedUnitCount()
    {
        var packet = new byte[RequestLength];
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(11, 4), -1);

        GameActionPackets.TryReadDropItem(packet, out var request).Should().BeTrue();

        // The count is a signed 32 bit field: a negative value keeps its sign and is refused by the
        // service, it must not be silently reinterpreted as 4294967295 units.
        request.Count.Should().Be(-1);
    }

    [Test]
    public void TryReadDropItem_RejectsATruncatedFrame()
    {
        var packet = new byte[RequestLength - 1];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), 0x80000123u);

        GameActionPackets.TryReadDropItem(packet, out var request).Should().BeFalse();

        request.ItemHandle.Should().Be(0u);
        request.Count.Should().Be(0);
    }

    [Test]
    public void TryReadDropItem_AcceptsATrailingByteButIgnoresIt()
    {
        var packet = new byte[RequestLength + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), 0x80000456u);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(11, 4), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(15, 4), 0xDEADBEEFu);

        GameActionPackets.TryReadDropItem(packet, out var request).Should().BeTrue();

        request.ItemHandle.Should().Be(0x80000456u);
        request.Count.Should().Be(3);
    }

    [Test]
    public void BuildDropResult_LaysOutTheEchoedHandleAndTheVerdictByte()
    {
        var packet = GameCharacterPackets.BuildDropResult(itemHandle: 0x80000123u, isAccepted: true);

        packet.Should().HaveCount(ResultLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(ResultLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_DROP_RESULT);
        packet[6].Should().Be(Checksum(packet));
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x80000123u);
        packet[11].Should().Be(1);
    }

    [Test]
    public void BuildDropResult_EchoesAnUnknownHandleWithAZeroVerdict()
    {
        var packet = GameCharacterPackets.BuildDropResult(itemHandle: 0xDEADBEEFu, isAccepted: false);

        packet.Should().HaveCount(ResultLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0xDEADBEEFu);
        packet[11].Should().Be(0);
    }

    [Test]
    public void DropItemPackets_CarryTheirEpic73Ids()
    {
        ((ushort)GamePackets.TM_CS_DROP_ITEM).Should().Be(203);
        ((ushort)GamePackets.TM_SC_DROP_RESULT).Should().Be(205);

        // 203 is client to server and therefore part of the receive chain; 205 is server to client, the
        // same situation as the other TM_SC_* members already declared next to it (202, 207, 209, 210).
        System.Enum.IsDefined(typeof(GamePackets), (ushort)203).Should().BeTrue();
        System.Enum.IsDefined(typeof(GamePackets), (ushort)205).Should().BeTrue();
    }

    private static byte Checksum(byte[] packet)
    {
        byte sum = 0;
        for (var i = 0; i < 6; i++) sum += packet[i];
        return sum;
    }
}
