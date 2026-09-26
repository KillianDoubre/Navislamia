using System;
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
/// TM_CS_PUTOFF_CARD (215) is 8 bytes on the wire — the 7-byte header plus a single signed ordinal at
/// offset 7, worth 0..5 or 0xFF — and owns no answer of its own in this family, so the only verdict the
/// client can be handed is TS_SC_RESULT (0). The client writes the length in hard at 8, so a short or
/// padded frame is an anomaly, refused before the ordinal is used.
/// See docs/packet-specs/215-putoff-card.md.
/// </summary>
[TestFixture]
public class PutoffCardPacketsTests
{
    private const int PacketLength = 8;
    private const int PositionOffset = 7;
    private const int ResultPacketLength = 15;

    /// <summary>Builds the 8-byte client frame: Length, ID, checksum, then the raw ordinal at offset 7.</summary>
    private static byte[] ClientFrame(byte position)
    {
        var packet = new byte[PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_PUTOFF_CARD);

        packet[6] = Checksum(packet);
        packet[PositionOffset] = position;
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

    /// <summary>
    /// Reads the TS_SC_RESULT frame the server pushed back: Length, ID, checksum, then RequestMsgID at 7,
    /// Result at 9 and Value at 11.
    /// </summary>
    private static (ushort Id, ushort RequestId, ushort Result, int Value) DecodeAnswer(byte[] frame)
    {
        frame.Length.Should().Be(ResultPacketLength, "TS_SC_RESULT is a 7-byte header plus 8 payload bytes");
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(0, 4)).Should().Be(ResultPacketLength);
        frame[6].Should().Be(Checksum(frame), "the checksum is the sum of the first six header bytes");

        return (BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(7, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(9, 2)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(11, 4)));
    }

    [Test]
    public void Id_IsTheEpic73One()
    {
        ((ushort)GamePackets.TM_CS_PUTOFF_CARD).Should().Be(215);

        // rzu gates the id to 1215 from EPIC_9_6_3 on (0x070300 is below it), so 1215 must not be declared.
        Enum.IsDefined(typeof(GamePackets), (ushort)1215).Should().BeFalse();

        // Without the member the frame is dropped as "Undefined packet ID" before any dispatch, and with the
        // member but no arm in OnDataReceived it reaches the throwing switch: both must exist together.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_PUTOFF_CARD).Should().BeTrue();
    }

    [Test]
    public void ClientPacket_UsesTheEpic73Layout()
    {
        var packet = ClientFrame(0);

        packet.Length.Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(PacketLength,
            "the length sits at 0 and the 7.3 client writes 8 in hard");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(215, "the id sits at 4");
        packet[6].Should().Be(Checksum(packet), "the checksum sits at 6");
        packet[PositionOffset].Should().Be(0, "the single payload byte sits at 7 and is the last one");
    }

    [Test]
    public void ClientPacket_HasNoFieldOutsideTheHeaderAndTheOrdinal()
    {
        // 7 + 1: any handle, padding or string would push the total past 8, and the client writes 8 in hard.
        Marshal.SizeOf<Header>().Should().Be(7);
        new Header(ClientFrame(3)).Length.Should().Be((uint)PacketLength);
        Marshal.SizeOf<Header>().Should().Be(PositionOffset, "the ordinal starts where the header ends");
    }

    [Test]
    public void ClientPacket_ChecksumIgnoresTheOrdinal()
    {
        // The client sums the six header bytes only, so editing the ordinal in transit does not break the
        // checksum: the value is not covered by any integrity check on the wire.
        ClientFrame(0)[6].Should().Be(ClientFrame(0xFF)[6]);
        ClientFrame(5)[6].Should().Be(ClientFrame(0xFF)[6]);
    }

    [Test]
    public void TryReadPutoffCard_ReadsTheOrdinalAtOffsetSeven()
    {
        GameActionPackets.TryReadPutoffCard(ClientFrame(0), out var position).Should().BeTrue();

        position.Should().Be(0, "0 is the first ordinal of the six-entry table");
    }

    [TestCase(0, 0, TestName = "TryReadPutoffCard_ReadsTheFirstOrdinal")]
    [TestCase(1, 1, TestName = "TryReadPutoffCard_ReadsTheSecondOrdinal")]
    [TestCase(5, 5, TestName = "TryReadPutoffCard_ReadsTheLastOrdinal")]
    public void TryReadPutoffCard_KeepsTheOrdinalRaw(byte raw, int expected)
    {
        GameActionPackets.TryReadPutoffCard(ClientFrame(raw), out var position).Should().BeTrue();

        // 0..5 is the whole established domain (a six-entry table); the reader must not filter or remap it.
        position.Should().Be((sbyte)expected);
    }

    [Test]
    public void TryReadPutoffCard_ReadsTheOrdinalAsSigned()
    {
        // The field is int8, not uint8: the fallback byte 0xFF is -1 signed, and a reader that took the
        // payload as a byte would answer 255 here.
        GameActionPackets.TryReadPutoffCard(ClientFrame(0xFF), out var position).Should().BeTrue();

        position.Should().Be((sbyte)-1);
        position.Should().Be(GameActionPackets.PutoffCardNotInTable);
        ((int)position).Should().NotBe(255, "0xFF is the signed -1 of an int8 field");
    }

    [Test]
    public void TryReadPutoffCard_KeepsAnUnknownHighByteSigned()
    {
        // 0x80 is -128 signed and 128 unsigned: only the signed read matches rzu's int8_t.
        GameActionPackets.TryReadPutoffCard(ClientFrame(0x80), out var position).Should().BeTrue();

        position.Should().Be((sbyte)-128);
        ((int)position).Should().NotBe(128, "0x80 is the signed -128 of an int8 field");
    }

    [Test]
    public void TryReadPutoffCard_DoesNotFilterTheHighBits()
    {
        // The reader interprets nothing: an out of domain byte crosses it untouched, the handler decides.
        GameActionPackets.TryReadPutoffCard(ClientFrame(0x7F), out var position).Should().BeTrue();

        position.Should().Be(127);
        position.Should().BeGreaterThan(GameActionPackets.PutoffCardMaxPosition);
    }

    [TestCase(0, TestName = "TryReadPutoffCard_RejectsAnEmptyFrame")]
    [TestCase(6, TestName = "TryReadPutoffCard_RejectsAFrameShorterThanTheHeader")]
    [TestCase(7, TestName = "TryReadPutoffCard_RejectsAHeaderOnlyFrame")]
    [TestCase(9, TestName = "TryReadPutoffCard_RejectsAPaddedFrame")]
    [TestCase(15, TestName = "TryReadPutoffCard_RejectsALongFrame")]
    public void TryReadPutoffCard_RejectsAnyLengthOtherThanEight(int length)
    {
        var packet = new byte[length];
        if (length >= PacketLength)
        {
            ClientFrame(5).CopyTo(packet, 0);
        }

        GameActionPackets.TryReadPutoffCard(packet, out var position).Should().BeFalse();
        position.Should().Be(0);
    }

    [TestCase(0, TestName = "OnDataReceived_ConsumesTheFirstOrdinal")]
    [TestCase(5, TestName = "OnDataReceived_ConsumesTheLastOrdinal")]
    public void OnDataReceived_ConsumesThePacketWithoutThrowing(byte position)
    {
        var connection = new FrameConnection(ClientFrame(position));
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(PacketLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
    }

    [TestCase(0, TestName = "OnDataReceived_AnswersNothingForTheFirstOrdinal")]
    [TestCase(5, TestName = "OnDataReceived_AnswersNothingForTheLastOrdinal")]
    public void OnDataReceived_AnswersNothingForAnOrdinalInsideTheDomain(byte position)
    {
        // Nothing on master can resolve what the six entries hold, nor clear a socket and hand the stone
        // back atomically (spec §7.a, §5.4): no state is written and no verdict is invented, so the frame
        // is only logged. This is the same behaviour as before the id was declared, minus the generic log.
        var connection = new FrameConnection(ClientFrame(position));
        var client = NewGameClient(connection);

        client.OnDataReceived(PacketLength);

        connection.Sent.Should().BeEmpty("no answer can be justified before §7.a is arbitrated");
        connection.BytesAvailable.Should().Be(0);
    }

    [Test]
    public void OnDataReceived_RefusesTheNotInTableMarker()
    {
        // 0xFF is the documented fallback: the target handle is not in the client's six-entry table, so the
        // ordinal names nothing and the request is refused explicitly (spec §5.3.f).
        var connection = new FrameConnection(ClientFrame(0xFF));
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(PacketLength);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0);
        connection.Sent.Should().HaveCount(1);

        var answer = DecodeAnswer(connection.Sent[0]);
        answer.Id.Should().Be((ushort)GamePackets.TM_SC_RESULT);
        answer.RequestId.Should().Be(215, "the verdict names the request it answers");
        answer.Result.Should().Be((ushort)ResultCode.InvalidArgument);
        answer.Value.Should().Be(0);
    }

    [TestCase(6, TestName = "OnDataReceived_RefusesTheFirstBytePastTheDomain")]
    [TestCase(127, TestName = "OnDataReceived_RefusesAnUnknownHighByte")]
    public void OnDataReceived_RefusesAPositionOutsideTheEstablishedDomain(byte position)
    {
        // The table holds six entries, so 6..127 is no more meaningful than 0xFF: refused, not guessed at.
        var connection = new FrameConnection(ClientFrame(position));
        var client = NewGameClient(connection);

        client.OnDataReceived(PacketLength);

        connection.Sent.Should().HaveCount(1);
        DecodeAnswer(connection.Sent[0]).Result.Should().Be((ushort)ResultCode.InvalidArgument);
    }

    [Test]
    public void OnDataReceived_RefusesAHeaderOnlyFrame()
    {
        // A frame whose announced length is 7 carries no ordinal at all: the missing payload is refused
        // rather than read past the end of the buffer.
        var frame = MalformedFrame(7);
        var connection = new FrameConnection(frame);
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(7);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "a refused frame is still consumed entirely");
        DecodeAnswer(connection.Sent[0]).Result.Should().Be((ushort)ResultCode.InvalidArgument);
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        // The frame must not swallow or desynchronise the one behind it: the keepalive is consumed too.
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[6] = Checksum(keepalive);

        var frame = ClientFrame(3).Concat(keepalive).ToArray();
        var connection = new FrameConnection(frame);
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
    }

    /// <summary>
    /// Rebuilds frame 215 with another announced length, keeping a valid checksum: the receive loop rejects
    /// an invalid one before any dispatch, which would hide what this test measures.
    /// </summary>
    private static byte[] MalformedFrame(int length)
    {
        var frame = new byte[length];
        Array.Copy(ClientFrame(5), frame, Math.Min(length, PacketLength));
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)length);
        frame[6] = Checksum(frame);
        return frame;
    }

    private static GameClient NewGameClient(FrameConnection connection)
    {
        var networkService = new NetworkService(
            A.Fake<ILogger<NetworkService>>(),
            Options.Create(new NetworkOptions { CipherKey = "putoff-card-test-key" }),
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
    /// In-memory replacement for the socket-backed connection: it serves a plaintext frame byte by byte and
    /// records everything the receive loop pushes back, so a test can tell an ignored packet from an
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
