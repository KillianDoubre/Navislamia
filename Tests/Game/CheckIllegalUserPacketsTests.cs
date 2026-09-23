using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;

namespace Tests.Game;

/// <summary>
/// TM_CS_CHECK_ILLEGAL_USER (57) is 11 bytes on the wire — the 7-byte header plus a single uint32 log_code
/// at offset 7 — and has no server to client answer. The client writes its length in hard at 11, so a
/// short or padded frame is an anomaly, refused before log_code is read.
/// See docs/packet-specs/57-check-illegal-user.md.
/// </summary>
[TestFixture]
public class CheckIllegalUserPacketsTests
{
    private const int PacketLength = 11;

    /// <summary>Builds the 11-byte client frame: Length, ID, checksum, then log_code at offset 7.</summary>
    private static byte[] ClientFrame(uint logCode)
    {
        var packet = new byte[PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_CHECK_ILLEGAL_USER);

        packet[6] = Checksum(packet);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), logCode);
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
        ((ushort)GamePackets.TM_CS_CHECK_ILLEGAL_USER).Should().Be(57);

        // rzu gates the id to 1057 from EPIC_9_6_3 on; 0x070300 is below it, so 7.3 stays on 57 and the
        // 9.6.3 value must not be declared here.
        Enum.IsDefined(typeof(GamePackets), (ushort)1057).Should().BeFalse();

        // The id must be defined, otherwise OnDataReceived drops the frame as "Undefined packet ID" before
        // any dispatch, and the receive loop stays there.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_CHECK_ILLEGAL_USER).Should().BeTrue();
    }

    [Test]
    public void ClientPacket_UsesTheEpic73Layout()
    {
        var packet = ClientFrame(0);

        packet.Length.Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(57);
        packet[6].Should().Be(Checksum(packet), "the checksum is the sum of the first six header bytes");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0u);
    }

    [Test]
    public void ClientPacket_HasNoFieldOutsideTheHeaderAndLogCode()
    {
        // 7 + 4: any extra field would push the total past 11, and the client writes the length in hard.
        Marshal.SizeOf<Header>().Should().Be(7);
        new Header(ClientFrame(0)).Length.Should().Be((uint)PacketLength);
        ClientFrame(0).Length.Should().Be(PacketLength, "no payload byte exists past offset 10");
    }

    [Test]
    public void ClientPacket_ChecksumIgnoresThePayload()
    {
        // The client sums the six header bytes only, so editing log_code in transit does not break the
        // checksum — a modified log_code is therefore indistinguishable on the wire.
        var frame = ClientFrame(0);
        var modified = ClientFrame(12345);

        modified[6].Should().Be(frame[6]);
    }

    [Test]
    public void TryReadCheckIllegalUser_ReadsLogCodeAtOffsetSeven()
    {
        GameActionPackets.TryReadCheckIllegalUser(ClientFrame(0), out var logCode).Should().BeTrue();

        logCode.Should().Be(0u, "0 is the only value the client writes on the emission path found in SFrame.exe");
    }

    [Test]
    public void TryReadCheckIllegalUser_ReadsTheValueLittleEndian()
    {
        // The four payload bytes are laid down by hand — 04 03 02 01 — so a big-endian read would give
        // 0x04030201 (67305985) instead of the little-endian 0x01020304 (16909060).
        var frame = ClientFrame(0);
        frame[7] = 0x04;
        frame[8] = 0x03;
        frame[9] = 0x02;
        frame[10] = 0x01;

        GameActionPackets.TryReadCheckIllegalUser(frame, out var logCode).Should().BeTrue();

        logCode.Should().Be(16909060u);
        logCode.Should().NotBe(67305985u, "rzu writes the scalar uint32 as it stands on x86, so little-endian");
    }

    [TestCase(0, TestName = "TryReadCheckIllegalUser_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadCheckIllegalUser_RejectsAHeaderOnlyFrame")]
    [TestCase(10, TestName = "TryReadCheckIllegalUser_RejectsATruncatedFrame")]
    [TestCase(12, TestName = "TryReadCheckIllegalUser_RejectsAPaddedFrame")]
    public void TryReadCheckIllegalUser_RejectsAnyLengthOtherThanEleven(int length)
    {
        var packet = new byte[length];
        if (length >= PacketLength)
        {
            ClientFrame(7).CopyTo(packet, 0);
        }

        GameActionPackets.TryReadCheckIllegalUser(packet, out var logCode).Should().BeFalse();
        logCode.Should().Be(0u);
    }

    [Test]
    public void OnDataReceived_ConsumesThePacketWithoutThrowing()
    {
        var connection = new FrameConnection(ClientFrame(0));
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(PacketLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
    }

    [Test]
    public void OnDataReceived_AnswersNothing()
    {
        // No TS_SC_CHECK_ILLEGAL_USER exists in rzu, NGemity or op_codes.md, and the 7.3 client's incoming
        // dispatcher has no case for 57: sending anything back would be invented.
        var connection = new FrameConnection(ClientFrame(0));
        var client = NewGameClient(connection);

        client.OnDataReceived(PacketLength);

        connection.Sent.Should().BeEmpty("no reference answers packet 57");
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        // The frame must not swallow or desynchronise the one behind it: the keepalive is consumed too.
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[6] = Checksum(keepalive);

        var frame = ClientFrame(0).Concat(keepalive).ToArray();
        var connection = new FrameConnection(frame);
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
    }

    [TestCase(7, TestName = "OnDataReceived_ConsumesAMalformedHeaderOnlyFrame")]
    [TestCase(15, TestName = "OnDataReceived_ConsumesAMalformedPaddedFrame")]
    public void OnDataReceived_ConsumesAMalformedFrameWithoutThrowing(int length)
    {
        // The client always writes 11, so any other length is an anomaly; it must be refused without
        // sending an answer and without leaving bytes in the stream.
        var frame = MalformedFrame(length);
        var connection = new FrameConnection(frame);
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "a refused frame is still consumed entirely");
    }

    /// <summary>
    /// Rebuilds frame 57 with another announced length, keeping a valid checksum: the receive loop rejects
    /// an invalid one before any dispatch, which would hide what this test measures.
    /// </summary>
    private static byte[] MalformedFrame(int length)
    {
        var frame = new byte[length];
        Array.Copy(ClientFrame(0), frame, Math.Min(length, PacketLength));
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)length);
        frame[6] = Checksum(frame);
        return frame;
    }

    /// <summary>
    /// The shared harness builds the game client against fakes and is kept in step with the
    /// <c>NetworkService</c> constructor, which this file's own copy had fallen behind.
    /// </summary>
    private static GameClient NewGameClient(FrameConnection connection) =>
        StorageTestHarness.NewGameClient(connection);

    /// <summary>
    /// In-memory replacement for the socket-backed connection: it serves a plaintext frame byte by byte
    /// and records everything the receive loop pushes back, so a test can tell an ignored packet from an
    /// answered one.
    /// </summary>
    private sealed class FrameConnection : Connection
    {
        private readonly byte[] _frame;
        private int _offset;

        public FrameConnection(byte[] frame)
            : base(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            _frame = frame;
        }

        public List<byte[]> Sent { get; } = new();

        public int BytesAvailable => _frame.Length - _offset;

        public override ReadOnlySpan<byte> Peek(int length) => new(_frame, _offset, length);

        public override byte[] Read(int input)
        {
            var length = Math.Min(BytesAvailable, input);
            var read = _frame.AsSpan(_offset, length).ToArray();
            _offset += length;
            return read;
        }

        public override void Send(byte[] buffer) => Sent.Add(buffer);
    }
}
