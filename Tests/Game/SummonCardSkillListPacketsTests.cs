using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// TM_CS_SUMMON_CARD_SKILL_LIST (452) is 11 bytes on the wire — the 7-byte header plus a single uint32
/// item_handle at offset 7 — and is answered by nothing: no reference implements 452 (NGemity declares it
/// without a handler, rzu only ships the client side) and the card to summon resolution its hypothetical
/// answer TM_SC_SKILL_LIST (403) would need is not established. The client writes its length in hard at 11,
/// so a short or padded frame is an anomaly, refused before item_handle is read.
/// See docs/packet-specs/452-summon-card-skill-list.md §3, §5.4, §5.5, §7.
/// </summary>
[TestFixture]
public class SummonCardSkillListPacketsTests
{
    private const int PacketLength = 11;

    /// <summary>
    /// An arbitrary, clearly non-zero card handle: the client fills offset 7 from its own card window
    /// ([fenêtre+0x4C4]), so no constant value exists to assert — only the position and the byte order are
    /// measurable server-side (fiche §7b).
    /// </summary>
    private const uint ItemHandle = 0x0A0B0C0D;

    /// <summary>Builds the 11-byte client frame: Length, ID, checksum, then item_handle at offset 7.</summary>
    private static byte[] ClientFrame(uint itemHandle)
    {
        var packet = new byte[PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2),
            (ushort)GamePackets.TM_CS_SUMMON_CARD_SKILL_LIST);

        packet[6] = Checksum(packet);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), itemHandle);
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
    public void Ids_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_CS_SUMMON_CARD_SKILL_LIST).Should().Be(452);

        // rzu gates the id to 1452 from EPIC_9_6_3 on; 0x070300 is below it, and the 7.3 client writes 452
        // (SFrame.exe 0x48EE35), so the 9.6.3 value must not be declared here.
        Enum.IsDefined(typeof(GamePackets), (ushort)1452).Should().BeFalse();

        // The id must be defined, otherwise OnDataReceived drops the frame as "Undefined packet ID" before
        // any dispatch, and the request is never seen.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_SUMMON_CARD_SKILL_LIST).Should().BeTrue();
    }

    [Test]
    public void Id_IsDistinctFromTheSkillListResponseItWouldBeAnsweredWith()
    {
        // 452 is a request; 403 (TM_SC_SKILL_LIST) is the deduced answer and travels the other way. The two
        // must never be confused in the dispatch chain.
        ((ushort)GamePackets.TM_CS_SUMMON_CARD_SKILL_LIST).Should()
            .NotBe((ushort)GamePackets.TM_SC_SKILL_LIST);
        ((ushort)GamePackets.TM_SC_SKILL_LIST).Should().Be(403);
    }

    [Test]
    public void ClientPacket_UsesTheEpic73Layout()
    {
        var packet = ClientFrame(ItemHandle);

        packet.Length.Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should()
            .Be(PacketLength, "Length sits at offset 0 and the client writes 11 (0xB) in hard");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(452, "ID sits at offset 4");
        packet[6].Should().Be(Checksum(packet), "the checksum at offset 6 is the sum of the first six bytes");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should()
            .Be(ItemHandle, "item_handle is the only payload field and sits at offset 7");
    }

    [Test]
    public void ClientPacket_HasNoFieldOutsideTheHeaderAndItemHandle()
    {
        // 7 + 4: the single field of the rzu declaration (ar_handle_t, 4 bytes) is the whole payload. Any
        // extra field would push the total past 11 while the client keeps announcing 11.
        Marshal.SizeOf<Header>().Should().Be(7);
        new Header(ClientFrame(ItemHandle)).Length.Should().Be((uint)PacketLength);
        ClientFrame(ItemHandle).Length.Should().Be(PacketLength, "no payload byte exists past offset 10");
    }

    [Test]
    public void ClientPacket_ChecksumIgnoresThePayload()
    {
        // The client sums the six header bytes only, so editing item_handle in transit does not break the
        // checksum — a modified handle is therefore indistinguishable on the wire.
        var frame = ClientFrame(ItemHandle);
        var modified = ClientFrame(ItemHandle + 1);

        modified[6].Should().Be(frame[6]);
    }

    [Test]
    public void TryReadSummonCardSkillList_ReadsItemHandleAtOffsetSeven()
    {
        GameActionPackets.TryReadSummonCardSkillList(ClientFrame(ItemHandle), out var itemHandle)
            .Should().BeTrue();

        itemHandle.Should().Be(ItemHandle);
    }

    [Test]
    public void TryReadSummonCardSkillList_AcceptsAZeroHandleWithoutInventingOne()
    {
        // Nothing establishes what the client puts in the field, so a zero is read as a zero: the parser
        // neither substitutes a default card nor refuses the frame.
        GameActionPackets.TryReadSummonCardSkillList(ClientFrame(0), out var itemHandle).Should().BeTrue();

        itemHandle.Should().Be(0u);
    }

    [Test]
    public void TryReadSummonCardSkillList_ReadsTheValueLittleEndian()
    {
        // The four payload bytes are laid down by hand — 04 03 02 01 — so a big-endian read would give
        // 0x04030201 (67305985) instead of the little-endian 0x01020304 (16909060).
        var frame = ClientFrame(0);
        frame[7] = 0x04;
        frame[8] = 0x03;
        frame[9] = 0x02;
        frame[10] = 0x01;

        GameActionPackets.TryReadSummonCardSkillList(frame, out var itemHandle).Should().BeTrue();

        itemHandle.Should().Be(16909060u);
        itemHandle.Should().NotBe(67305985u, "rzu writes the scalar ar_handle_t as it stands on x86");
    }

    [TestCase(0, TestName = "TryReadSummonCardSkillList_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadSummonCardSkillList_RejectsAHeaderOnlyFrame")]
    [TestCase(10, TestName = "TryReadSummonCardSkillList_RejectsATruncatedFrame")]
    [TestCase(12, TestName = "TryReadSummonCardSkillList_RejectsAPaddedFrame")]
    public void TryReadSummonCardSkillList_RejectsAnyLengthOtherThanEleven(int length)
    {
        var packet = new byte[length];
        if (length >= PacketLength)
        {
            ClientFrame(ItemHandle).CopyTo(packet, 0);
        }

        GameActionPackets.TryReadSummonCardSkillList(packet, out var itemHandle).Should().BeFalse();
        itemHandle.Should().Be(0u);
    }

    [Test]
    public void OnDataReceived_ConsumesThePacketWithoutThrowing()
    {
        var connection = new StorageTestHarness.FrameConnection(ClientFrame(ItemHandle));
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(PacketLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
    }

    [Test]
    public void OnDataReceived_AnswersNothing()
    {
        // No reference implements 452, and the only answer the fiche can deduce (403) would have to name the
        // summon tied to the card — a resolution left open by §7c. Sending either the 403 with the received
        // handle as target, or a TS_SC_RESULT, would be invented here.
        var connection = new StorageTestHarness.FrameConnection(ClientFrame(ItemHandle));
        var client = StorageTestHarness.NewGameClient(connection);

        client.OnDataReceived(PacketLength);

        connection.Sent.Should().BeEmpty("no reference answers packet 452");
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        // The frame must not swallow or desynchronise the one behind it: the keepalive is consumed too.
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[6] = Checksum(keepalive);

        var frame = ClientFrame(ItemHandle).Concat(keepalive).ToArray();
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
    }

    [TestCase(7, TestName = "OnDataReceived_ConsumesAMalformedHeaderOnlyFrame")]
    [TestCase(15, TestName = "OnDataReceived_ConsumesAMalformedPaddedFrame")]
    public void OnDataReceived_ConsumesAMalformedFrameWithoutThrowing(int length)
    {
        // The client always writes 11, so any other length is an anomaly; it must be refused without sending
        // an answer and without leaving bytes in the stream.
        var frame = MalformedFrame(length);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "a refused frame is still consumed entirely");
    }

    /// <summary>
    /// Rebuilds frame 452 with another announced length, keeping a valid checksum: the receive loop rejects
    /// an invalid one before any dispatch, which would hide what this test measures.
    /// </summary>
    private static byte[] MalformedFrame(int length)
    {
        var frame = new byte[length];
        Array.Copy(ClientFrame(ItemHandle), frame, Math.Min(length, PacketLength));
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)length);
        frame[6] = Checksum(frame);
        return frame;
    }

    [Test]
    public void Packet_IsDispatchedBeforeTheUnknownPacketThrow()
    {
        // GameClient's dispatch is a chain of ifs, so a member added to the enum without a branch reaches
        // the final switch and its `throw` kills the receive loop. Nothing smaller than a source scan can
        // check that without a live socket.
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "Game", "Network", "Clients", "GameClient.cs"));

        var branch = source.IndexOf(
            "header.ID == (ushort)GamePackets.TM_CS_SUMMON_CARD_SKILL_LIST", StringComparison.Ordinal);
        var finalSwitch = source.IndexOf("throw new Exception(\"Unknown Packet Type\")", StringComparison.Ordinal);

        branch.Should().BeGreaterThan(-1, "452 needs a branch of its own in OnDataReceived");
        finalSwitch.Should().BeGreaterThan(-1, "the final switch is the guard this test is about");
        branch.Should().BeLessThan(finalSwitch, "452 must be handled before the final switch throws");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Navislamia.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the repository root is needed to check the dispatch chain");

        return directory!.FullName;
    }
}
