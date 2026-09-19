using System;
using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// TM_CS_GET_REGION_INFO (550) is 15 bytes on the wire (7-byte header + two floats, x at offset 7 and y at
/// offset 11) and the answer TM_SC_REGION_ACK (11) is 15 bytes as well (two int32, rx at offset 7 and ry at
/// offset 11). The region indices are computed with the divisor announced to the client at login
/// (WorldVisibility.RegionSize = 180), truncated toward zero.
/// See docs/packet-specs/550-get-region-info.md.
/// </summary>
[TestFixture]
public class RegionInfoPacketsTests
{
    private const int PacketLength = 15;

    private static byte[] ClientFrame(float x, float y)
    {
        var packet = new byte[PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_GET_REGION_INFO);

        packet[6] = Checksum(packet);
        BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(7, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(11, 4), y);
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
    public void RegionInfoIds_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_CS_GET_REGION_INFO).Should().Be(550);
        ((ushort)GamePackets.TM_SC_REGION_ACK).Should().Be(11);

        // rzu remaps the pair to 1550/1011 from EPIC_9_6_3 on (EPIC_7_3 = 0x070300 is below it); 7.3 stays
        // on the low branch. Both ids must be defined, otherwise OnDataReceived drops the 550 as "Undefined
        // packet ID" before any dispatch.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_GET_REGION_INFO).Should().BeTrue();
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_SC_REGION_ACK).Should().BeTrue();
    }

    [Test]
    public void ClientPacket_UsesTheEpic73Layout()
    {
        var packet = ClientFrame(94500f, 126100f);

        packet.Length.Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(550);
        packet[6].Should().Be(Checksum(packet));
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(7, 4)).Should().Be(94500f);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(11, 4)).Should().Be(126100f);
    }

    [Test]
    public void TryReadGetRegionInfo_ReadsTheTwoFloatsAtSevenAndEleven()
    {
        GameActionPackets.TryReadGetRegionInfo(ClientFrame(1234.5f, -678.25f), out var request).Should().BeTrue();

        request.X.Should().Be(1234.5f);
        request.Y.Should().Be(-678.25f);
    }

    [Test]
    public void TryReadGetRegionInfo_KeepsFieldOrder()
    {
        // Asymmetric values: swapping x and y in the layout would fail here.
        GameActionPackets.TryReadGetRegionInfo(ClientFrame(1f, 2f), out var request).Should().BeTrue();

        request.X.Should().Be(1f);
        request.Y.Should().Be(2f);
    }

    [TestCase(0, TestName = "TryReadGetRegionInfo_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadGetRegionInfo_RejectsAHeaderOnlyFrame")]
    [TestCase(14, TestName = "TryReadGetRegionInfo_RejectsATruncatedFrame")]
    [TestCase(16, TestName = "TryReadGetRegionInfo_RejectsAPaddedFrame")]
    public void TryReadGetRegionInfo_RejectsAnyLengthOtherThanFifteen(int length)
    {
        var packet = new byte[length];
        if (length >= 15)
        {
            ClientFrame(360f, 180f).CopyTo(packet, 0);
        }

        GameActionPackets.TryReadGetRegionInfo(packet, out var request).Should().BeFalse();
        request.Should().Be(default(GameActionPackets.RegionInfoRequest));
    }

    [Test]
    public void AnswerPacket_LaysOutRxThenRy()
    {
        var packet = GameMovePackets.BuildRegionAck(1234, -7);

        packet.Length.Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(11);
        packet[6].Should().Be(Checksum(packet));
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(1234);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(-7);
    }

    [Test]
    public void AnswerPacket_HasNoFieldOutsideTheTwoIndices()
    {
        // 7 + 8: a handle or any extra field would push the total past 15 and desynchronise the client.
        GameMovePackets.BuildRegionAck(0, 0).Length.Should().Be(PacketLength);
    }

    [TestCase(0f, 0, TestName = "GetRegionIndex_MapsTheOriginToRegionZero")]
    [TestCase(1f, 0, TestName = "GetRegionIndex_MapsAPositiveOffsetToRegionZero")]
    [TestCase(179.99f, 0, TestName = "GetRegionIndex_KeepsTheLastBoundedPositionInRegionZero")]
    [TestCase(180f, 1, TestName = "GetRegionIndex_OpensRegionOneOnTheBoundary")]
    [TestCase(359.99f, 1, TestName = "GetRegionIndex_TruncatesInsideRegionOne")]
    [TestCase(360f, 2, TestName = "GetRegionIndex_OpensRegionTwoOnTheBoundary")]
    [TestCase(94500f, 525, TestName = "GetRegionIndex_MapsATelecasterPosition")]
    public void GetRegionIndex_DividesByTheAnnouncedRegionSize(float position, int expected)
    {
        WorldVisibility.RegionSize.Should().Be(180);

        GameMovePackets.GetRegionIndex(position).Should().Be(expected);
    }

    [TestCase(-0.5f, 0, TestName = "GetRegionIndex_TruncatesANegativeFractionTowardZero")]
    [TestCase(-180f, -1, TestName = "GetRegionIndex_TruncatesANegativeBoundaryTowardZero")]
    [TestCase(-181f, -1, TestName = "GetRegionIndex_KeepsTheFirstRegionBelowTheOriginBoundary")]
    [TestCase(-360f, -2, TestName = "GetRegionIndex_KeepsTwoRegionsBelowTheOrigin")]
    public void GetRegionIndex_TruncatesTowardZeroLikeTheClient(float position, int expected)
    {
        // The client divides with fidivl and stores with FISTP in truncation mode; a cast to uint would turn
        // a negative position into a huge index and place the client outside its own window.
        GameMovePackets.GetRegionIndex(position).Should().Be(expected);
    }

    [Test]
    public void GetRegionIndex_DoesNotUseThePreLoginClientDefault()
    {
        // 170 sits in region 1 under the 150 divisor (WorldOption.RegionSize, the client's pre-login default)
        // and in region 0 under the 180 announced at login. The client computes with 180, so the answer must
        // too, or the two windows drift apart.
        GameMovePackets.GetRegionIndex(170f).Should().Be(0);
        GameMovePackets.GetRegionIndex(WorldVisibility.RegionSize).Should().Be(1);
    }

    [TestCase(94500f, 126100f, 525, 700, TestName = "Answer_CarriesTheIndicesOfTheSentPosition")]
    [TestCase(0f, 0f, 0, 0, TestName = "Answer_AcceptsTheMapOrigin")]
    [TestCase(-0.5f, 200f, 0, 1, TestName = "Answer_TruncatesNegativePositionsTowardZero")]
    public void Answer_CarriesTheIndicesOfThePositionReadInTheRequest(float x, float y, int expectedRx, int expectedRy)
    {
        GameActionPackets.TryReadGetRegionInfo(ClientFrame(x, y), out var request).Should().BeTrue();

        var answer = GameMovePackets.BuildRegionAck(
            GameMovePackets.GetRegionIndex(request.X),
            GameMovePackets.GetRegionIndex(request.Y));

        answer.Length.Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(4, 2)).Should().Be(11);
        BinaryPrimitives.ReadInt32LittleEndian(answer.AsSpan(7, 4)).Should().Be(expectedRx);
        BinaryPrimitives.ReadInt32LittleEndian(answer.AsSpan(11, 4)).Should().Be(expectedRy);
    }
}
