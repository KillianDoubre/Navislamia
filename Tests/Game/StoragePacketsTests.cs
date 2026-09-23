using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// TM_CS_STORAGE (212) is 20 bytes on the wire — the 7-byte header, a uint32 item handle at offset 7, a
/// mode at offset 11 and a signed int64 count at offset 12 — while TM_SC_OPEN_STORAGE (211) is the 7-byte
/// header alone for Epic 7.3: rzu only adds maxStorageItemCount from EPIC_7_4 on. See
/// docs/packet-specs/211-212-storage.md §3.
/// </summary>
[TestFixture]
public class StoragePacketsTests
{
    private const int PacketLength = 20;

    /// <summary>Builds the 20-byte client frame: Length, ID, checksum, handle at 7, mode at 11, count at 12.</summary>
    private static byte[] ClientFrame(uint itemHandle, byte mode, long count)
    {
        var packet = new byte[PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_STORAGE);
        packet[6] = StorageTestHarness.Checksum(packet);

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), itemHandle);
        packet[11] = mode;
        BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(12, 8), count);
        return packet;
    }

    [Test]
    public void Ids_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_SC_OPEN_STORAGE).Should().Be(211);
        ((ushort)GamePackets.TM_CS_STORAGE).Should().Be(212);

        // rzu gates both ids to 1211/1212 from EPIC_9_6_3 on; 0x070300 is below it, so 7.3 stays on 211
        // and 212 and the 9.6.3 values must not be declared here.
        Enum.IsDefined(typeof(GamePackets), (ushort)1211).Should().BeFalse();
        Enum.IsDefined(typeof(GamePackets), (ushort)1212).Should().BeFalse();

        // Both ids must be defined, otherwise OnDataReceived drops the frame as "Undefined packet ID"
        // before any dispatch and the receive loop stays there.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_SC_OPEN_STORAGE).Should().BeTrue();
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_STORAGE).Should().BeTrue();
    }

    [Test]
    public void ClientPacket_UsesTheEpic73Layout()
    {
        var packet = ClientFrame(0x80000123u, 0, 5);

        packet.Length.Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(212);
        packet[6].Should().Be(StorageTestHarness.Checksum(packet), "the checksum sums the first six header bytes");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x80000123u);
        packet[11].Should().Be(0);
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(12, 8)).Should().Be(5);
    }

    [Test]
    public void ClientPacket_HasNoFieldOutsideTheHeaderHandleModeAndCount()
    {
        // 7 + 4 + 1 + 8: any extra field would push the total past 20.
        Marshal.SizeOf<Header>().Should().Be(7);
        new Header(ClientFrame(1, 0, 1)).Length.Should().Be((uint)PacketLength);
        ClientFrame(1, 0, 1).Length.Should().Be(PacketLength, "no payload byte exists past offset 19");
    }

    [Test]
    public void TryReadStorage_ReadsHandleModeAndCountAtTheirOffsets()
    {
        GameActionPackets.TryReadStorage(ClientFrame(0x80000123u, 4, 0), out var request).Should().BeTrue();

        request.ItemHandle.Should().Be(0x80000123u, "the handle sits at offset 7");
        request.Mode.Should().Be(4, "the mode sits at offset 11");
        request.Count.Should().Be(0, "the count sits at offset 12");
    }

    [Test]
    public void TryReadStorage_ReadsTheFieldsLittleEndian()
    {
        // The four handle bytes are laid down by hand — 04 03 02 01 — so a big-endian read would give
        // 0x04030201 (67305985) instead of the little-endian 0x01020304 (16909060).
        var frame = ClientFrame(0, 0, 0);
        frame[7] = 0x04;
        frame[8] = 0x03;
        frame[9] = 0x02;
        frame[10] = 0x01;

        GameActionPackets.TryReadStorage(frame, out var request).Should().BeTrue();

        request.ItemHandle.Should().Be(16909060u);
        request.ItemHandle.Should().NotBe(67305985u, "rzu lays the scalar uint32 down as it stands on x86");
    }

    [Test]
    public void TryReadStorage_ReadsTheCountAsASignedInt64()
    {
        // The count is signed in the Epic 7.3 form (int64_t from EPIC_4_1_1 on). A negative value proves
        // the read is signed and 64-bit: a uint32 read would give 4294967294 and a uint64 read would
        // overflow the long.
        GameActionPackets.TryReadStorage(ClientFrame(7, 0, -2), out var request).Should().BeTrue();

        request.Count.Should().Be(-2);
        BinaryPrimitives.ReadInt64LittleEndian(ClientFrame(7, 0, -2).AsSpan(12, 8)).Should().Be(-2);
    }

    [Test]
    public void TryReadStorage_CountsEightBytesSoAWeightAboveTwoBillionFits()
    {
        GameActionPackets.TryReadStorage(ClientFrame(7, 2, 3_000_000_000L), out var request).Should().BeTrue();

        request.Count.Should().Be(3_000_000_000L, "the count is an int64, not the count of a 4.1.1- frame");
    }

    [TestCase(0, TestName = "TryReadStorage_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadStorage_RejectsAHeaderOnlyFrame")]
    [TestCase(11, TestName = "TryReadStorage_RejectsAFrameStoppingAfterTheMode")]
    [TestCase(19, TestName = "TryReadStorage_RejectsATruncatedCount")]
    public void TryReadStorage_RejectsAFrameShorterThanTwenty(int length)
    {
        var packet = new byte[length];

        GameActionPackets.TryReadStorage(packet, out var request).Should().BeFalse();
        request.Should().Be(default(GameActionPackets.StorageRequest));
    }

    [Test]
    public void TryReadStorage_KeepsTheLongerFrameConvention()
    {
        // Every reader of GameActionPackets refuses only a frame shorter than the Epic 7.3 form; the
        // receive loop hands over exactly the length the header declared, so a padded frame is not a wire
        // shape the client can produce.
        var packet = new byte[PacketLength + 1];
        ClientFrame(7, 1, 3).CopyTo(packet, 0);

        GameActionPackets.TryReadStorage(packet, out var request).Should().BeTrue();
        request.ItemHandle.Should().Be(7u);
    }

    [Test]
    public void TryReadStorage_RejectsARequestWithNoChecksumCheckOfItsOwn()
    {
        // The receive loop rejects a bad checksum before any dispatch, so the reader only measures the
        // length: a frame whose checksum is wrong still reads, exactly like the other readers.
        var frame = ClientFrame(7, 1, 3);
        frame[6] = 0xFF;

        GameActionPackets.TryReadStorage(frame, out var request).Should().BeTrue();
        request.ItemHandle.Should().Be(7u);
    }

    [Test]
    public void BuildOpenStorage_LaysOutTheSevenByteHeader()
    {
        var packet = GameStoragePackets.BuildOpenStorage();

        packet.Length.Should().Be(7, "Epic 7.3 carries no field: maxStorageItemCount is 7.4 and later");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(7);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(211);
        packet[6].Should().Be(StorageTestHarness.Checksum(packet), "the checksum sums the first six header bytes");
    }

    [Test]
    public void BuildOpenStorage_HasNoPayloadByte()
    {
        var packet = GameStoragePackets.BuildOpenStorage();

        new Header(packet).ID.Should().Be((ushort)GamePackets.TM_SC_OPEN_STORAGE);
        new Header(packet).Length.Should().Be(7);
        packet.Length.Should().Be(7, "a 7.4 frame would add four bytes here and the 7.3 client would misread them");
    }

    [Test]
    public void OnDataReceived_HandsTheWellFormedFrameToTheStorageService()
    {
        var storageService = A.Fake<IStorageService>();
        var received = false;
        A.CallTo(() => storageService.HandleAsync(A<GameClient>._, A<GameActionPackets.StorageRequest>._))
            .Invokes(() => received = true);

        var connection = new StorageTestHarness.FrameConnection(ClientFrame(0x80000123u, 1, 250));
        var client = StorageTestHarness.NewGameClient(connection, storageService);

        var receive = () => client.OnDataReceived(PacketLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");

        StorageTestHarness.WaitFor(() => received);
        received.Should().BeTrue("the arm hands the frame to the storage service");
        A.CallTo(() => storageService.HandleAsync(client,
            new GameActionPackets.StorageRequest(0x80000123u, 1, 250))).MustHaveHappenedOnceExactly();
    }

    [Test]
    public void OnDataReceived_AnswersInvalidArgumentOnATruncatedFrame()
    {
        // The arm is reached on the id alone: a frame the client never writes is refused before the count
        // is read, so this also proves the id is dispatched rather than swallowed by the final switch.
        const int length = 19;
        var frame = new byte[length];
        ClientFrame(7, 0, 5).AsSpan(0, length).CopyTo(frame);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), length);
        frame[6] = StorageTestHarness.Checksum(frame);

        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(length);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "a refused frame is still consumed entirely");
        connection.Sent.Should().ContainSingle();

        var result = new Packet<TS_SC_RESULT>(connection.Sent[0]).GetDataStruct<TS_SC_RESULT>();
        result.RequestMsgID.Should().Be(212);
        result.Result.Should().Be((ushort)ResultCode.InvalidArgument);
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        // The frame must not swallow or desynchronise the one behind it: the keepalive is consumed too.
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[6] = StorageTestHarness.Checksum(keepalive);

        var frame = ClientFrame(7, 4, 0).Concat(keepalive).ToArray();
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
    }
}
