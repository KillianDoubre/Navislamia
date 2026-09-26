using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// TM_CS_HUNTAHOLIC_INSTANCE_LIST (4000) is 11 bytes on the wire — the 7-byte header plus a single signed
/// int32 <c>page</c> at offset 7 — and is answered by nothing in this lot: the client's lobby is fed by
/// TM_SC_HUNTAHOLIC_INSTANCE_LIST (4001), whose content, pagination and huntaholic_id have no source in the
/// repository, and neither rzu nor NGemity handles 4000 at all. The client writes its length in hard at 11
/// (SFrame.exe 0x4c920e), so a short or padded frame is refused before <c>page</c> is read.
/// See docs/packet-specs/4000-huntaholic-instance-list.md §3, §5.3, §5.5, §7.
/// </summary>
[TestFixture]
public class HuntaholicInstanceListPacketsTests
{
    private const int PacketLength = 11;

    /// <summary>
    /// An arbitrary, clearly non-zero page: its four bytes differ in every position (DE C0 AD 0B on the wire),
    /// so a read at offset 6 or 8, or a big-endian read, returns something visibly different.
    /// </summary>
    private const int Page = 0x0BADC0DE;

    /// <summary>Builds the 11-byte client frame: Length, ID, checksum, then <c>page</c> at offset 7.</summary>
    private static byte[] ClientFrame(int page)
    {
        var packet = new byte[PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2),
            (ushort)GamePackets.TM_CS_HUNTAHOLIC_INSTANCE_LIST);

        packet[6] = Checksum(packet);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(7, 4), page);
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
        ((ushort)GamePackets.TM_CS_HUNTAHOLIC_INSTANCE_LIST).Should().Be(4000);

        // rzu declares it X(4000, true), which expands to `if (true) id = id_;`: unconditional, verified up to
        // EPIC_9_8_1. There is no second candidate id for the frame in the 7.3 client either — its only id
        // write is `mov eax,0xfa0` at 0x4c9205 — so 4000 must be declared exactly once, unconditioned.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_HUNTAHOLIC_INSTANCE_LIST).Should().BeTrue();

        // The id must be defined, otherwise OnDataReceived drops the frame as "Undefined packet ID" before any
        // dispatch is reached.
        ((ushort)GamePackets.TM_CS_HUNTAHOLIC_INSTANCE_LIST).Should().Be(0x0FA0);
    }

    [Test]
    public void ClientPacket_UsesTheEpic73Layout()
    {
        var packet = ClientFrame(Page);

        packet.Length.Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should()
            .Be(PacketLength, "Length sits at offset 0 and the client writes 11 (0xB) in hard");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(4000, "ID sits at offset 4");
        packet[6].Should().Be(Checksum(packet), "the checksum at offset 6 is the sum of the first six bytes");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(7, 4)).Should()
            .Be(Page, "page is the only payload field and sits at offset 7");
    }

    [Test]
    public void ClientPacket_HasNoFieldOutsideTheHeaderAndPage()
    {
        // 7 + 4: the single field of the rzu declaration `_(simple)(int32_t, page)` is the whole payload. The
        // client's stack buffer runs from [ebp-0xc] to [ebp-0x2], whose last byte is page's fourth byte.
        Marshal.SizeOf<Header>().Should().Be(7);
        new Header(ClientFrame(Page)).Length.Should().Be((uint)PacketLength);
        GameHuntaholicPackets.InstanceListLength.Should().Be(PacketLength);
        GameHuntaholicPackets.PageOffset.Should().Be(7, "page starts right after the header");
        GameHuntaholicPackets.PageFieldLength.Should().Be(4);
    }

    [Test]
    public void ClientPacket_ChecksumIgnoresThePayload()
    {
        // The client sums the six header bytes only, so editing page in transit does not break the checksum.
        var frame = ClientFrame(Page);
        var modified = ClientFrame(Page + 1);

        modified[6].Should().Be(frame[6]);
    }

    [Test]
    public void TryReadHuntaholicInstanceList_ReadsPageAtOffsetSeven()
    {
        GameHuntaholicPackets.TryReadHuntaholicInstanceList(ClientFrame(Page), out var page).Should().BeTrue();

        page.Should().Be(Page, "the payload starts at offset 7, not 6 and not 8");
    }

    [Test]
    public void TryReadHuntaholicInstanceList_ReadsTheValueLittleEndian()
    {
        // The four payload bytes are laid down by hand — 04 03 02 01 — so a big-endian read would give
        // 0x04030201 (67305985) instead of the little-endian 0x01020304 (16909060).
        var frame = ClientFrame(0);
        frame[7] = 0x04;
        frame[8] = 0x03;
        frame[9] = 0x02;
        frame[10] = 0x01;

        GameHuntaholicPackets.TryReadHuntaholicInstanceList(frame, out var page).Should().BeTrue();

        page.Should().Be(16909060);
        page.Should().NotBe(67305985, "rzu writes the scalar int32_t as it stands on x86");
    }

    [Test]
    public void TryReadHuntaholicInstanceList_ReadsPageAsSignedInt32()
    {
        // rzu declares `int32_t page`; a uint read would report 4294967295 here instead of -1. The client never
        // emits a negative page (opening the lobby pushes 1, refreshing clamps to at least 1), but the field is
        // signed on the wire and §7c leaves the server rule open, so the reader reports it raw.
        GameHuntaholicPackets.TryReadHuntaholicInstanceList(ClientFrame(-1), out var page).Should().BeTrue();

        page.Should().Be(-1, "the four wire bytes are FF FF FF FF and rzu declares a signed int32_t");
    }

    [Test]
    public void TryReadHuntaholicInstanceList_ReadsTheRawPageWithoutClampingOrDefaulting()
    {
        // The first page is 1 (the lobby's opening request), and 0 or a large page is reported as received:
        // no range rule is established for the server (§7c), so the reader invents neither a floor nor a ceiling.
        GameHuntaholicPackets.TryReadHuntaholicInstanceList(ClientFrame(1), out var firstPage).Should().BeTrue();
        firstPage.Should().Be(1);

        GameHuntaholicPackets.TryReadHuntaholicInstanceList(ClientFrame(0), out var zeroPage).Should().BeTrue();
        zeroPage.Should().Be(0);

        GameHuntaholicPackets.TryReadHuntaholicInstanceList(ClientFrame(int.MaxValue), out var hugePage)
            .Should().BeTrue();
        hugePage.Should().Be(int.MaxValue);
    }

    [TestCase(0, TestName = "TryReadHuntaholicInstanceList_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadHuntaholicInstanceList_RejectsAHeaderOnlyFrame")]
    [TestCase(10, TestName = "TryReadHuntaholicInstanceList_RejectsATruncatedFrame")]
    [TestCase(12, TestName = "TryReadHuntaholicInstanceList_RejectsAPaddedFrame")]
    public void TryReadHuntaholicInstanceList_RejectsAnyLengthOtherThanEleven(int length)
    {
        var packet = new byte[length];
        if (length >= PacketLength)
        {
            ClientFrame(Page).CopyTo(packet, 0);
        }
        else
        {
            // Even a frame whose last three bytes look like the start of a page must not be partially read.
            ClientFrame(Page).AsSpan(0, length).CopyTo(packet);
        }

        GameHuntaholicPackets.TryReadHuntaholicInstanceList(packet, out var page).Should().BeFalse();
        page.Should().Be(0, "a refused frame leaves no half-read value behind");
    }

    [Test]
    public void OnDataReceived_ConsumesThePacketWithoutThrowing()
    {
        var connection = new StorageTestHarness.FrameConnection(ClientFrame(1));
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(PacketLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
    }

    [Test]
    public void OnDataReceived_AnswersNothing()
    {
        // The expected answer is 4001, but its three inputs (the rooms to list, the entries per page and the
        // huntaholic_id) have no source in the repository: the server holds no resource entity for the family.
        // Emitting a lobby, echoing the page back, or sending a TS_SC_RESULT would all be invented here.
        var connection = new StorageTestHarness.FrameConnection(ClientFrame(1));
        var client = StorageTestHarness.NewGameClient(connection);

        client.OnDataReceived(PacketLength);

        connection.Sent.Should().BeEmpty("no reference answers packet 4000");
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        // The frame must not swallow or desynchronise the one behind it: the keepalive is consumed too.
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[6] = Checksum(keepalive);

        var frame = ClientFrame(1).Concat(keepalive).ToArray();
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
    }

    [TestCase(7, TestName = "OnDataReceived_ConsumesAMalformedHeaderOnlyFrame")]
    [TestCase(10, TestName = "OnDataReceived_ConsumesATruncatedFrame")]
    [TestCase(15, TestName = "OnDataReceived_ConsumesAMalformedPaddedFrame")]
    public void OnDataReceived_ConsumesAMalformedFrameWithoutThrowing(int length)
    {
        // The client always writes 11, so any other length is an anomaly; it must be refused without sending an
        // answer and without leaving bytes in the stream.
        var frame = MalformedFrame(length);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "a refused frame is still consumed entirely");
    }

    /// <summary>
    /// Rebuilds frame 4000 with another announced length, keeping a valid checksum: the receive loop rejects an
    /// invalid one before any dispatch, which would hide what this test measures.
    /// </summary>
    private static byte[] MalformedFrame(int length)
    {
        var frame = new byte[length];
        Array.Copy(ClientFrame(Page), frame, Math.Min(length, PacketLength));
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)length);
        frame[6] = Checksum(frame);
        return frame;
    }

    [Test]
    public void Packet_IsDispatchedBeforeTheUnknownPacketThrow()
    {
        // GameClient's dispatch is a chain of ifs, so a member added to the enum without a branch reaches the
        // final switch and its `throw` kills the receive loop. Nothing smaller than a source scan can check that
        // without a live socket.
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "Game", "Network", "Clients", "GameClient.cs"));

        var branch = source.IndexOf(
            "header.ID == (ushort)GamePackets.TM_CS_HUNTAHOLIC_INSTANCE_LIST", StringComparison.Ordinal);
        var finalSwitch = source.IndexOf("throw new Exception($\"Unknown Packet Type", StringComparison.Ordinal);

        branch.Should().BeGreaterThan(-1, "4000 needs a branch of its own in OnDataReceived");
        finalSwitch.Should().BeGreaterThan(-1, "the final switch is the guard this test is about");
        branch.Should().BeLessThan(finalSwitch, "4000 must be handled before the final switch throws");
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
