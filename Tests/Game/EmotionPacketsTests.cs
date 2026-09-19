using System;
using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// TM_CS_EMOTION (1202) is 11 bytes on the wire (7-byte header + one int32 emotion at offset 7) and
/// the answer TM_SC_EMOTION (1201) is 15 bytes (handle at 7, the same emotion at 11).
/// See docs/packet-specs/1202-emotion.md.
/// </summary>
[TestFixture]
public class EmotionPacketsTests
{
    private const int ClientPacketLength = 11;
    private const int ServerPacketLength = 15;

    private static byte[] ClientFrame(int emotion)
    {
        var packet = new byte[ClientPacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), ClientPacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_EMOTION);

        packet[6] = Checksum(packet);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(7, 4), emotion);
        return packet;
    }

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
    public void EmotionIds_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_CS_EMOTION).Should().Be(1202);
        ((ushort)GamePackets.TM_SC_EMOTION).Should().Be(1201);

        // rzu remaps the pair to 2201/2202 from EPIC_9_6_3 on; 7.3 stays on the low branch.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_EMOTION).Should().BeTrue();
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_SC_EMOTION).Should().BeTrue();
    }

    [Test]
    public void ClientPacket_UsesTheEpic73Layout()
    {
        var packet = ClientFrame(4);

        packet.Length.Should().Be(ClientPacketLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(ClientPacketLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(1202);
        packet[6].Should().Be(Checksum(packet));
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(4);
    }

    [Test]
    public void TryReadEmotion_ReadsTheValueAtOffsetSeven()
    {
        GameActionPackets.TryReadEmotion(ClientFrame(9), out var emotion).Should().BeTrue();

        emotion.Should().Be(9);
    }

    [TestCase(1, TestName = "TryReadEmotion_KeepsTheLowestValue")]
    [TestCase(14, TestName = "TryReadEmotion_KeepsTheHighestKnownValue")]
    [TestCase(0, TestName = "TryReadEmotion_KeepsAnOutOfRangeZero")]
    [TestCase(999, TestName = "TryReadEmotion_KeepsAnUnknownValueUntouched")]
    [TestCase(-1, TestName = "TryReadEmotion_KeepsANegativeValueUntouched")]
    public void TryReadEmotion_ReturnsTheRawValue(int emotion)
    {
        GameActionPackets.TryReadEmotion(ClientFrame(emotion), out var read).Should().BeTrue();

        // The domain of the emotion ids is not established: the specification forbids a bound, so the
        // value crosses the server untouched, including values outside the presumed 1..14 range.
        read.Should().Be(emotion);
    }

    [TestCase(0, TestName = "TryReadEmotion_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadEmotion_RejectsAHeaderOnlyFrame")]
    [TestCase(10, TestName = "TryReadEmotion_RejectsATruncatedFrame")]
    public void TryReadEmotion_RejectsAShortFrame(int length)
    {
        var packet = ClientFrame(3).AsSpan(0, length).ToArray();

        GameActionPackets.TryReadEmotion(packet, out var emotion).Should().BeFalse();
        emotion.Should().Be(0);
    }

    [Test]
    public void AnswerPacket_LaysOutHandleThenEmotion()
    {
        var packet = GameCharacterPackets.BuildEmotion(0x40000123, 5);

        packet.Length.Should().Be(ServerPacketLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(ServerPacketLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(1201);
        packet[6].Should().Be(Checksum(packet));
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x40000123u);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(5);
    }

    [TestCase(1, TestName = "AnswerPacket_EchoesTheFirstEmotion")]
    [TestCase(14, TestName = "AnswerPacket_EchoesTheLastKnownEmotion")]
    [TestCase(0, TestName = "AnswerPacket_EchoesZero")]
    [TestCase(999, TestName = "AnswerPacket_EchoesAnUnknownValue")]
    [TestCase(-1, TestName = "AnswerPacket_EchoesANegativeValue")]
    public void AnswerPacket_EchoesTheRequestedEmotionVerbatim(int emotion)
    {
        GameActionPackets.TryReadEmotion(ClientFrame(emotion), out var read).Should().BeTrue();

        var answer = GameCharacterPackets.BuildEmotion(42, read);

        BinaryPrimitives.ReadUInt32LittleEndian(answer.AsSpan(7, 4)).Should().Be(42u);
        BinaryPrimitives.ReadInt32LittleEndian(answer.AsSpan(11, 4)).Should().Be(emotion);
    }
}
