using System;
using System.Buffers.Binary;
using System.Linq;
using System.Runtime.InteropServices;
using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// TM_CS_SUMMON (304) is 12 bytes on the wire — the 7-byte header, an <c>int8_t is_summon</c> at offset 7
/// and an <c>ar_handle_t card_handle</c> at offsets 8-11 — and has no server to client answer. No capture
/// of this frame exists and none can exist: the 7.3 client never builds it, summoning goes through the
/// summon creature skill (TM_CS_SKILL = 400), and NGemity has no handler for it either. These tests
/// therefore assert the declared shape and the dispatch, never an observed frame.
/// The dispatch is exercised against the real receive loop on purpose: a member of <see cref="GamePackets"/>
/// that no branch claims reaches the final switch and throws "Unknown Packet Type" inside that loop.
/// See docs/packet-specs/304-summon.md §10.
/// </summary>
[TestFixture]
public class SummonPacketsTests
{
    private const int PacketLength = 12;
    private const uint CardHandle = 0x05060708u;

    /// <summary>
    /// Builds the 12-byte client frame: Length, ID, checksum, then <c>is_summon</c> at offset 7 and
    /// <c>card_handle</c> at offsets 8-11. Another announced length keeps a valid checksum, since the
    /// receive loop refuses an invalid one before any dispatch.
    /// </summary>
    private static byte[] ClientFrame(sbyte isSummon, uint cardHandle, int length = PacketLength)
    {
        var packet = new byte[length];

        if (length < 7)
        {
            // No complete header: the receive loop never dispatches such a frame, but the reader must still
            // refuse it without reading past the buffer.
            return packet;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_SUMMON);

        if (length > 7)
        {
            packet[7] = (byte)isSummon;
        }

        if (length >= PacketLength)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8, 4), cardHandle);
        }

        packet[6] = StorageTestHarness.Checksum(packet);
        return packet;
    }

    [Test]
    public void Ids_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_CS_SUMMON).Should().Be(304);

        // rzu names 1304 from EPIC_9_6_3 on. In 7.3 that id is TM_CS_AUCTION_BIDDED_LIST, so it must stay
        // undeclared here: declaring it would re-point the auction family at the summon family.
        Enum.IsDefined(typeof(GamePackets), (ushort)1304).Should().BeFalse(
            "1304 is the 9.6.3 remap of the summon request and the 7.3 auction id");

        // The id must be defined, otherwise OnDataReceived drops the frame as "Undefined packet ID" before
        // any dispatch, and the receive loop stays there.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_SUMMON).Should().BeTrue();
    }

    [Test]
    public void EnumMember_SitsBetweenItsNeighbours()
    {
        ((ushort)GamePackets.TM_EQUIP_SUMMON).Should().Be(303);
        ((ushort)GamePackets.TM_CS_SUMMON).Should().Be(304);
        ((ushort)GamePackets.TM_SC_UNSUMMON).Should().Be(305);
    }

    [Test]
    public void LayoutConstants_NameTheHeaderAndThePayloadSeparately()
    {
        // 7 + 1 + 4: naming the parts apart means a change of the 7-byte header breaks this test even when
        // the total happens to stay at 12.
        Marshal.SizeOf<Header>().Should().Be(7);
        GameActionPackets.SummonPacketSize.Should().Be(PacketLength);
        GameActionPackets.SummonPayloadSize.Should().Be(5, "1 byte of flag plus 4 bytes of handle");

        GameActionPackets.SummonFlagOffset.Should().Be(7, "the flag is the first payload byte");
        GameActionPackets.SummonCardHandleOffset.Should().Be(8);
        GameActionPackets.SummonCardHandleOffset.Should().Be(GameActionPackets.SummonFlagOffset + 1);
    }

    [Test]
    public void ClientFrame_CarriesTheFlagAtOffsetSevenAndTheHandleAtEightToEleven()
    {
        var packet = ClientFrame(1, CardHandle);

        packet.Should().HaveCount(PacketLength, "the 7.3 frame is length, id, checksum, flag and handle");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(304);
        packet[6].Should().Be(StorageTestHarness.Checksum(packet),
            "the checksum is the sum of the first six header bytes");
        packet[7].Should().Be(1);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8, 4)).Should().Be(CardHandle);

        new Header(packet).Length.Should().Be((uint)PacketLength);
    }

    [Test]
    public void TryReadSummon_ReadsAZeroFlagAndAZeroHandleAsValues()
    {
        // 0 / 0 is the case the frame can carry without any field being absent: the reader must report both
        // as read values, not as a missing payload.
        GameActionPackets.TryReadSummon(ClientFrame(0, 0u), out var isSummon, out var cardHandle)
            .Should().BeTrue();

        isSummon.Should().Be((sbyte)0);
        cardHandle.Should().Be(0u);

        GameActionPackets.TryReadSummon(ClientFrame(1, CardHandle), out var flag, out var handle)
            .Should().BeTrue();

        flag.Should().Be((sbyte)1);
        handle.Should().Be(CardHandle);
    }

    [Test]
    public void TryReadSummon_ReadsTheHandleLittleEndian()
    {
        // The four payload bytes are laid down by hand — 04 03 02 01 — so a big-endian read would give
        // 0x04030201 (67305985) instead of the little-endian 0x01020304 (16909060).
        var frame = ClientFrame(0, 0u);
        frame[8] = 0x04;
        frame[9] = 0x03;
        frame[10] = 0x02;
        frame[11] = 0x01;

        GameActionPackets.TryReadSummon(frame, out _, out var cardHandle).Should().BeTrue();

        cardHandle.Should().Be(16909060u);
        cardHandle.Should().NotBe(67305985u,
            "rzu declares ar_handle_t, a uint32 written as it stands on x86, so little-endian");
    }

    [Test]
    public void TryReadSummon_ReadsTheFlagAsTheSignedByteRzuDeclares()
    {
        // rzu declares int8_t, not uint8_t: the byte 0xFF is -1 and not 255, which is the whole reason the
        // flag type is worth asserting. The value itself is NOT interpreted — rzu and NGemity name it
        // without defining its domain (fiche §3.2, §7).
        var frame = ClientFrame(0, 0u);
        frame[7] = 0xFF;

        GameActionPackets.TryReadSummon(frame, out var isSummon, out _).Should().BeTrue();

        isSummon.Should().Be((sbyte)-1,
            "rzu declares int8_t, so the wire byte 0xFF reads as -1 and not as 255");
        ((byte)isSummon).Should().Be(255, "the raw wire byte is left untouched");

        frame[7] = 0x7F;
        GameActionPackets.TryReadSummon(frame, out var positive, out _).Should().BeTrue();
        positive.Should().Be((sbyte)127);
    }

    [TestCase(0, TestName = "TryReadSummon_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadSummon_RejectsAHeaderOnlyFrame")]
    [TestCase(11, TestName = "TryReadSummon_RejectsAFrameWithoutTheLastHandleByte")]
    public void TryReadSummon_RejectsAFrameShorterThanTwelveBytes(int length)
    {
        var frame = ClientFrame(1, CardHandle, length);

        GameActionPackets.TryReadSummon(frame, out var isSummon, out var cardHandle).Should().BeFalse();

        // A refused frame leaves no half read value behind.
        isSummon.Should().Be((sbyte)0);
        cardHandle.Should().Be(0u);
    }

    [Test]
    public void TryReadSummon_ReadsALongerFrameFromItsFirstTwelveBytes()
    {
        // No refusal rule is established for this packet (fiche §5.3.4): a longer frame is read rather than
        // refused, on the first twelve bytes, and the caller keeps its own disposition for the extra bytes.
        var frame = ClientFrame(1, CardHandle, PacketLength + 1);

        GameActionPackets.TryReadSummon(frame, out var isSummon, out var cardHandle).Should().BeTrue();

        isSummon.Should().Be((sbyte)1);
        cardHandle.Should().Be(CardHandle);
    }

    [Test]
    public void OnDataReceived_ConsumesThePacketWithoutThrowing()
    {
        var connection = new StorageTestHarness.FrameConnection(ClientFrame(1, CardHandle));
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(PacketLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
    }

    [Test]
    public void OnDataReceived_AnswersNothing()
    {
        // Neither rzu, NGemity, op_codes.md nor the 7.3 client's incoming dispatcher holds an answer to 304
        // (fiche §5.4): sending one back would be invented. The summon path of the repository is the skill,
        // not this frame.
        var connection = new StorageTestHarness.FrameConnection(ClientFrame(1, CardHandle));
        var client = StorageTestHarness.NewGameClient(connection);

        client.OnDataReceived(PacketLength);

        connection.Sent.Should().BeEmpty("no reference answers packet 304");
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        // The frame must not swallow or desynchronise the one behind it: the keepalive is consumed too.
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[6] = StorageTestHarness.Checksum(keepalive);

        var frame = ClientFrame(0, 0u).Concat(keepalive).ToArray();
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
    }

    [TestCase(7, TestName = "OnDataReceived_ConsumesAHeaderOnlyFrame")]
    [TestCase(13, TestName = "OnDataReceived_ConsumesALongerFrame")]
    public void OnDataReceived_ConsumesANonConformingLengthWithoutThrowing(int length)
    {
        // No refusal rule is established for 304: a non conforming length is read as far as it goes, logged
        // and dropped — never answered, never left in the stream.
        var frame = ClientFrame(1, CardHandle, length);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "a dropped frame is still consumed entirely");
    }
}
