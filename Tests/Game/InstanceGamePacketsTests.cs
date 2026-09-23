using System;
using System.Buffers.Binary;
using System.Linq;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// TM_CS/SC_INSTANCE_GAME_* (4250-4253), the instance game socle. The 7.3 client builds the three client to
/// server frames with hard coded lengths — 4250 = 11 bytes (7-byte header + one int32 at offset 7),
/// 4251 = 7 bytes and 4252 = 7 bytes, no payload at all — and its score deserialiser reads four DWORD at
/// offsets 7, 11, 15 and 19, so the answer 4253 is 7 + 16 = 23 bytes. The <c>battle_arena_*</c> fields of the
/// 8.1 form (32 more bytes) must never be written for 7.3.
/// See docs/packet-specs/socle-instances-jeu.md.
/// </summary>
[TestFixture]
public class InstanceGamePacketsTests
{
    private const int EnterLength = 11;
    private const int EmptyLength = 7;
    private const int ScoreResponseLength = 23;

    private static byte[] ClientFrame(GamePackets id, int length, int instanceGameType = 0)
    {
        var packet = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)id);

        if (length == EnterLength)
        {
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(7, 4), instanceGameType);
        }

        packet[6] = Checksum(packet);
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
    public void InstanceGameIds_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_CS_INSTANCE_GAME_ENTER).Should().Be(4250);
        ((ushort)GamePackets.TM_CS_INSTANCE_GAME_EXIT).Should().Be(4251);
        ((ushort)GamePackets.TM_CS_INSTANCE_GAME_SCORE_REQUEST).Should().Be(4252);
        ((ushort)GamePackets.TM_SC_INSTANCE_GAME_SCORE_REQUEST).Should().Be(4253);

        // rzu declares all four as X(<id>, true) and the family only exists from EPIC_6_3 on: EPIC_7_3
        // (0x070300) is above it, so 7.3 keeps these ids unchanged. They must be defined, otherwise
        // OnDataReceived drops them as "Undefined packet ID" before any dispatch arm can run.
        foreach (var id in new[]
                 {
                     GamePackets.TM_CS_INSTANCE_GAME_ENTER, GamePackets.TM_CS_INSTANCE_GAME_EXIT,
                     GamePackets.TM_CS_INSTANCE_GAME_SCORE_REQUEST, GamePackets.TM_SC_INSTANCE_GAME_SCORE_REQUEST
                 })
        {
            Enum.IsDefined(typeof(GamePackets), (ushort)id).Should().BeTrue();
        }
    }

    [Test]
    public void InstanceGameIds_DoNotCollideWithAnExistingMember()
    {
        // Two names sharing one value would silently route a foreign packet into these handlers (and they are
        // separated from TM_CS_REPORT = 8000 and TM_NONE = 9999, which must stay alone).
        var values = Enum.GetValues<GamePackets>().Select(value => (ushort)value).ToArray();

        values.Should().OnlyHaveUniqueItems();
        values.Should().Contain(4250).And.Contain(4251).And.Contain(4252).And.Contain(4253);
        ((ushort)GamePackets.TM_NONE).Should().Be(9999);
    }

    [Test]
    public void EnterPacket_IsElevenBytesWithOneInt32AtSeven()
    {
        var packet = ClientFrame(GamePackets.TM_CS_INSTANCE_GAME_ENTER, EnterLength, 2);

        packet.Length.Should().Be(EnterLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(EnterLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(4250);
        packet[6].Should().Be(Checksum(packet));
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(2);
    }

    [Test]
    public void TryReadEnter_ReadsTheInt32AtOffsetSeven()
    {
        GameInstanceGamePackets.TryReadEnter(ClientFrame(GamePackets.TM_CS_INSTANCE_GAME_ENTER, EnterLength, 1),
            out var request).Should().BeTrue();

        request.InstanceGameType.Should().Be(1);
    }

    [TestCase(0, TestName = "TryReadEnter_KeepsZeroAsALegitimateType")]
    [TestCase(1, TestName = "TryReadEnter_ReadsTypeOne")]
    [TestCase(2, TestName = "TryReadEnter_ReadsTypeTwo")]
    public void TryReadEnter_AcceptsEveryTypeObservedInTheClient(int instanceGameType)
    {
        // The client copies the value out of the incoming message it answers; 0 is a legitimate
        // "generic entry", not a "no type" marker, so it must survive the round trip.
        GameInstanceGamePackets.TryReadEnter(
            ClientFrame(GamePackets.TM_CS_INSTANCE_GAME_ENTER, EnterLength, instanceGameType),
            out var request).Should().BeTrue();

        request.InstanceGameType.Should().Be(instanceGameType);
    }

    [Test]
    public void TryReadEnter_DoesNotReadAnyOtherOffset()
    {
        // A frame carrying the type at offset 7 is read as such; had the reader pointed at offset 11 or 6 the
        // bytes there (all zero) would come back instead.
        var packet = ClientFrame(GamePackets.TM_CS_INSTANCE_GAME_ENTER, EnterLength, 0);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(7, 4), unchecked((int)0x0BADF00D));

        GameInstanceGamePackets.TryReadEnter(packet, out var request).Should().BeTrue();

        request.InstanceGameType.Should().Be(unchecked((int)0x0BADF00D));
    }

    [TestCase(0, TestName = "TryReadEnter_RejectsAnEmptyFrame")]
    [TestCase(6, TestName = "TryReadEnter_RejectsAFrameShorterThanAHeader")]
    [TestCase(7, TestName = "TryReadEnter_RejectsAHeaderOnlyFrame")]
    [TestCase(10, TestName = "TryReadEnter_RejectsATruncatedFrame")]
    [TestCase(12, TestName = "TryReadEnter_RejectsAPaddedFrame")]
    [TestCase(ScoreResponseLength, TestName = "TryReadEnter_RejectsAHeaderOnlyScoreFrame")]
    public void TryReadEnter_RejectsAnyLengthOtherThanEleven(int length)
    {
        // A 7-byte 4250 has no instance_game_type at all; reading one out of a shorter buffer would take the
        // value of whatever follows it in the receive buffer.
        var packet = new byte[length];
        if (length >= EnterLength)
        {
            ClientFrame(GamePackets.TM_CS_INSTANCE_GAME_ENTER, EnterLength, 2).CopyTo(packet, 0);
        }

        GameInstanceGamePackets.TryReadEnter(packet, out var request).Should().BeFalse();
        request.Should().Be(default(GameInstanceGamePackets.InstanceGameEnterRequest));
    }

    [Test]
    public void ExitAndScoreRequest_AreHeaderOnlyFrames()
    {
        GameInstanceGamePackets.HasNoPayload(ClientFrame(GamePackets.TM_CS_INSTANCE_GAME_EXIT, EmptyLength))
            .Should().BeTrue();
        GameInstanceGamePackets.HasNoPayload(
            ClientFrame(GamePackets.TM_CS_INSTANCE_GAME_SCORE_REQUEST, EmptyLength)).Should().BeTrue();

        // Both are exactly 7 bytes on the wire: the client writes that length in hard, and the header is all
        // there is. A padded frame means the sender is not the 7.3 client.
        GameInstanceGamePackets.EmptyLength.Should().Be(EmptyLength);
    }

    [TestCase(0, TestName = "HasNoPayload_RejectsAnEmptyFrame")]
    [TestCase(6, TestName = "HasNoPayload_RejectsAFrameShorterThanAHeader")]
    [TestCase(8, TestName = "HasNoPayload_RejectsAPaddedFrame")]
    [TestCase(EnterLength, TestName = "HasNoPayload_RejectsTheEnterFrameLength")]
    public void HasNoPayload_RejectsAnyLengthOtherThanSeven(int length)
    {
        GameInstanceGamePackets.HasNoPayload(new byte[length]).Should().BeFalse();
    }

    [Test]
    public void ScoreResponse_IsTwentyThreeBytesWithFourUint32AtSevenElevenFifteenNineteen()
    {
        var packet = GameInstanceGamePackets.BuildScoreResponse(1234u, 7u, 42u, 3u);

        packet.Length.Should().Be(ScoreResponseLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(ScoreResponseLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(4253);
        packet[6].Should().Be(Checksum(packet));
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(1234u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(7u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(15, 4)).Should().Be(42u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(19, 4)).Should().Be(3u);
    }

    [Test]
    public void ScoreResponse_KeepsFieldOrder()
    {
        // Asymmetric values: swapping any two fields in the layout would fail here.
        var packet = GameInstanceGamePackets.BuildScoreResponse(11u, 22u, 33u, 44u);

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(11u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(22u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(15, 4)).Should().Be(33u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(19, 4)).Should().Be(44u);
    }

    [Test]
    public void ScoreResponse_HasNoBattleArenaField()
    {
        // The 8.1 form adds battle_arena_point, battle_arena_mvp_count and the three record tables (32 bytes):
        // writing them would make the packet 55 bytes long and desynchronise the 7.3 client, which reads 16
        // payload bytes. The last field must therefore end exactly at offset 23.
        GameInstanceGamePackets.ScoreResponseLength.Should().Be(ScoreResponseLength);

        var packet = GameInstanceGamePackets.BuildScoreResponse(0u, 0u, 0u, uint.MaxValue);

        packet.Length.Should().Be(ScoreResponseLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(ScoreResponseLength - 4, 4)).Should().Be(uint.MaxValue);
    }

    [Test]
    public void ScoreResponse_FieldsAreUnsigned()
    {
        // The client stores the four DWORD as they are read: a signed write would turn 4294967295 into -1.
        var packet = GameInstanceGamePackets.BuildScoreResponse(uint.MaxValue, uint.MaxValue, uint.MaxValue,
            uint.MaxValue);

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(uint.MaxValue);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(19, 4)).Should().Be(uint.MaxValue);
    }

    [Test]
    public void ScoreResponse_WithNoSourceData_CarriesOnlyTheHolicPoint()
    {
        // Only holicpoint has a source in 7.3 (CharacterEntity.HuntaholicPoint); the three other fields have
        // none and are sent as zero rather than invented.
        var packet = GameInstanceGamePackets.BuildScoreResponse(98u, 0u, 0u, 0u);

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(98u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(0u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(15, 4)).Should().Be(0u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(19, 4)).Should().Be(0u);
    }

    [TestCase(0, 0u, TestName = "ToWireHolicPoint_KeepsZero")]
    [TestCase(1, 1u, TestName = "ToWireHolicPoint_KeepsOne")]
    [TestCase(65535, 65535u, TestName = "ToWireHolicPoint_KeepsAFullUInt16Value")]
    [TestCase(2147483647, 2147483647u, TestName = "ToWireHolicPoint_KeepsTheLargestSignedValue")]
    [TestCase(-1, 0u, TestName = "ToWireHolicPoint_ClampsAMinusOnePoint")]
    [TestCase(-2147483648, 0u, TestName = "ToWireHolicPoint_ClampsTheSmallestSignedValue")]
    public void ToWireHolicPoint_ConvertsTheStoredSignedPointToTheWiredUInt32(int stored, uint expected)
    {
        // Telecaster stores huntaholic_point as a signed int and 4253 carries a uint32: a negative stored value
        // must not wrap into a huge score.
        GameInstanceGamePackets.ToWireHolicPoint(stored).Should().Be(expected);
    }

    [Test]
    public void HolicPoint_RoundTripsThroughTheScoreResponse()
    {
        var packet = GameInstanceGamePackets.BuildScoreResponse(GameInstanceGamePackets.ToWireHolicPoint(4321), 0u,
            0u, 0u);

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(4321u);
    }
}
