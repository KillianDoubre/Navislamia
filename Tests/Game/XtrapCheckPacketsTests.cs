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
/// TM_CS_XTRAP_CHECK (59) is 135 bytes on the wire — the 7-byte header plus a fixed uint8[128]
/// pCheckBuffer starting at offset 7 — and carries no length field, so the exact 135-byte form is the
/// only one accepted. No reference server handles it and the 7.3 client never emits it: the frame is
/// bounded and dropped, with no answer and no sanction.
/// See docs/packet-specs/59-xtrap-check.md.
/// </summary>
[TestFixture]
public class XtrapCheckPacketsTests
{
    private const int PacketLength = 135;
    private const int BufferOffset = 7;
    private const int BufferLength = 128;

    /// <summary>
    /// Builds the 135-byte client frame: Length, ID, checksum, then 128 payload bytes whose value is
    /// derived from their position (<c>0x80 + i</c>), so a byte read at the wrong offset is visible.
    /// </summary>
    private static byte[] ClientFrame(ushort id = (ushort)GamePackets.TM_CS_XTRAP_CHECK)
    {
        var packet = new byte[PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), id);

        packet[6] = Checksum(packet);
        for (var i = 0; i < BufferLength; i++)
        {
            packet[BufferOffset + i] = (byte)(0x80 + i);
        }

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
        ((ushort)GamePackets.TM_CS_XTRAP_CHECK).Should().Be(59);

        // rzu gates the id to 1059 from EPIC_9_6_3 on; 0x070300 is below that step, so 7.3 stays on 59
        // and the 9.6.3 value must not be declared here.
        Enum.IsDefined(typeof(GamePackets), (ushort)1059).Should().BeFalse();

        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_XTRAP_CHECK).Should().BeTrue(
            "an id absent from GamePackets is dropped as \"Undefined packet ID\" before any dispatch");
    }

    [Test]
    public void CounterpartId_58_IsDeliberatelyNotDeclared()
    {
        // TM_SC_XTRAP_CHECK (58) is server to client and no code path in this server ever sends one.
        // Declaring it would add an inert enum member and widen the collision zone with the anti-cheat
        // base work; spec §9.6 leaves the choice to Killian and defaults to leaving it out.
        Enum.IsDefined(typeof(GamePackets), (ushort)58).Should().BeFalse();
    }

    [Test]
    public void Frame_IsOneHundredAndThirtyFiveBytes()
    {
        Marshal.SizeOf<Header>().Should().Be(7);

        GameXtrapPackets.XtrapCheckPacketSize.Should().Be(PacketLength);
        GameXtrapPackets.XtrapCheckBufferSize.Should().Be(BufferLength);
        GameXtrapPackets.XtrapCheckBufferOffset.Should().Be(BufferOffset);

        // 7 header + 128 payload: the payload is a fixed array with no length field, so nothing else can
        // change the total and there is no variable part.
        (Marshal.SizeOf<Header>() + BufferLength).Should().Be(PacketLength);

        var frame = ClientFrame();
        frame.Length.Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(0, 4)).Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)).Should().Be(59);
    }

    [Test]
    public void Frame_ChecksumIsTheSumOfTheSixHeaderBytes()
    {
        // Length 135 (0x87 00 00 00) + id 59 (0x3B 00): the spec's own value, 194 (0xC2).
        var frame = ClientFrame();

        Checksum(frame).Should().Be(194);
        frame[6].Should().Be(0xC2);
    }

    [Test]
    public void CounterpartFrame_58_HasTheSameAnatomyAtAOneLowerChecksum()
    {
        // Same layout and same total, one lower id: checksum 193 (0xC1). The frame exists in the protocol
        // even though the server never sends it, and this is what proves the two are one layout.
        var frame = ClientFrame(58);

        frame.Length.Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)).Should().Be(58);
        frame[6].Should().Be(193);
        frame[6].Should().Be(0xC1);
    }

    [Test]
    public void Request_PutsEveryPayloadByteAtItsOwnOffset()
    {
        var frame = ClientFrame();

        // Byte-by-byte on the frame itself: position i of the payload sits at absolute offset 7 + i, so
        // the last payload byte is at 134 and there is no byte at 135.
        for (var i = 0; i < BufferLength; i++)
        {
            frame[BufferOffset + i].Should().Be((byte)(0x80 + i),
                $"payload byte {i} belongs to absolute offset {BufferOffset + i}");
        }

        frame[BufferOffset + BufferLength - 1].Should().Be(frame[134]);
        (BufferOffset + BufferLength).Should().Be(PacketLength, "no payload byte exists at offset 135");
    }

    [Test]
    public void TryReadXtrapCheck_HandsBackTheBufferStartingAtOffsetSeven()
    {
        var frame = ClientFrame();
        var expected = frame.AsSpan(BufferOffset, BufferLength).ToArray();

        GameXtrapPackets.TryReadXtrapCheck(frame, out var checkBuffer).Should().BeTrue();

        checkBuffer.Length.Should().Be(BufferLength);
        checkBuffer[0].Should().Be(frame[BufferOffset]);
        checkBuffer[BufferLength - 1].Should().Be(frame[PacketLength - 1]);
        checkBuffer.ToArray().Should().Equal(expected);
    }

    [Test]
    public void TryReadXtrapCheck_BoundsTheFrameOnlyAndLeavesTheIdToTheReceiveLoop()
    {
        // The reader is a size gate, not an id gate: a padded or short frame is refused, while any
        // 135-byte frame is read. The id is checked by the dispatch chain that calls this reader.
        GameXtrapPackets.TryReadXtrapCheck(ClientFrame(58), out var checkBuffer).Should().BeTrue();
        checkBuffer.Length.Should().Be(BufferLength);
    }

    [Test]
    public void TryReadXtrapCheck_AcceptsAFullyZeroBuffer()
    {
        // A zero buffer is not a malformed frame: nothing establishes that "no buffer" differs from a
        // legitimately zero one (spec §10.3), so the reader must not invent that distinction.
        var frame = new byte[PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), 59);
        frame[6] = Checksum(frame);

        GameXtrapPackets.TryReadXtrapCheck(frame, out var checkBuffer).Should().BeTrue();
        checkBuffer.Length.Should().Be(BufferLength);
        checkBuffer.ToArray().Should().OnlyContain(b => b == 0);
    }

    [TestCase(0, TestName = "TryReadXtrapCheck_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadXtrapCheck_RejectsAHeaderOnlyFrame")]
    [TestCase(11, TestName = "TryReadXtrapCheck_RejectsAFrameTheSizeOfPacket57")]
    [TestCase(134, TestName = "TryReadXtrapCheck_RejectsATruncatedFrame")]
    [TestCase(136, TestName = "TryReadXtrapCheck_RejectsAPaddedFrame")]
    [TestCase(143, TestName = "TryReadXtrapCheck_RejectsALongPaddedFrame")]
    public void TryReadXtrapCheck_RejectsAnyLengthOtherThanOneHundredAndThirtyFive(int length)
    {
        var packet = new byte[length];
        Array.Copy(ClientFrame(), packet, Math.Min(length, PacketLength));

        GameXtrapPackets.TryReadXtrapCheck(packet, out var checkBuffer).Should().BeFalse();
        checkBuffer.Length.Should().Be(0);
    }

    [Test]
    public void OnDataReceived_ConsumesThePacketWithoutThrowing()
    {
        var connection = new FrameConnection(ClientFrame());
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(PacketLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
    }

    [Test]
    public void OnDataReceived_AnswersNothing()
    {
        // No TS_SC_XTRAP_CHECK emission exists in this server and the 7.3 client parses 58 into an empty
        // branch, so any answer would be invented. No sanction either: nothing establishes one.
        var connection = new FrameConnection(ClientFrame());
        var client = NewGameClient(connection);

        client.OnDataReceived(PacketLength);

        connection.Sent.Should().BeEmpty("no reference answers packet 59");
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        // The frame must not swallow or desynchronise the one behind it: the keepalive is consumed too.
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[6] = Checksum(keepalive);

        var frame = ClientFrame().Concat(keepalive).ToArray();
        var connection = new FrameConnection(frame);
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
    }

    [TestCase(7, TestName = "OnDataReceived_ConsumesAMalformedHeaderOnlyFrame")]
    [TestCase(134, TestName = "OnDataReceived_ConsumesAMalformedTruncatedFrame")]
    [TestCase(143, TestName = "OnDataReceived_ConsumesAMalformedPaddedFrame")]
    public void OnDataReceived_ConsumesAMalformedFrameWithoutThrowing(int length)
    {
        // The payload is fixed, so any other length is an anomaly: refused without an answer, and still
        // consumed entirely so the following frame stays aligned.
        var frame = MalformedFrame(length);
        var connection = new FrameConnection(frame);
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "a refused frame is still consumed entirely");
    }

    /// <summary>
    /// Rebuilds frame 59 with another announced length, keeping a valid checksum: the receive loop rejects
    /// an invalid one before any dispatch, which would hide what this test measures.
    /// </summary>
    private static byte[] MalformedFrame(int length)
    {
        var frame = new byte[length];
        Array.Copy(ClientFrame(), frame, Math.Min(length, PacketLength));
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)length);
        frame[6] = Checksum(frame);
        return frame;
    }

    private static GameClient NewGameClient(FrameConnection connection)
    {
        var networkService = new NetworkService(
            A.Fake<ILogger<NetworkService>>(),
            Options.Create(new NetworkOptions { CipherKey = "xtrap-check-test-key" }),
            A.Fake<ICharacterService>(),
            A.Fake<IBannedWordsRepository>(),
            A.Fake<IStatService>(),
            Options.Create(new ServerOptions()),
            A.Fake<INpcSpawnService>(),
            A.Fake<INpcDialogService>(),
            A.Fake<IMonsterSpawnService>(),
            A.Fake<ICombatService>(),
            A.Fake<ILevelingService>(),
            A.Fake<ISkillService>(),
            A.Fake<IEquipmentService>(),
            A.Fake<IInventoryService>(),
            A.Fake<IGroundItemService>(),
            A.Fake<ISkillCastService>(),
            A.Fake<IFieldPropService>(),
            A.Fake<IItemUseService>());

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        return new GameClient(socket, networkService) { Connection = connection };
    }

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
