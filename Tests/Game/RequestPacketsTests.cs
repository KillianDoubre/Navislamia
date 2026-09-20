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
/// TM_CS_REQUEST (60) is the only variable frame of the anti-cheat family: a 7 byte header, the selector
/// t at offset 7, then the command at offset 8 with its NUL terminator, so the total is 9 + L bytes with
/// L = the number of command bytes before the terminator. <c>endstring</c> has no length prefix, which is
/// the whole difficulty of the frame: the command ends at the end of the datagram, never at the first NUL
/// and never at the end of the receive buffer.
/// See docs/packet-specs/60-request.md.
/// </summary>
[TestFixture]
public class RequestPacketsTests
{
    /// <summary>Smallest well formed frame: header, selector and the terminator of an empty command.</summary>
    private const int MinPacketLength = 9;

    /// <summary>Builds a client frame: Length, ID, checksum, t at offset 7, command at 8, NUL last.</summary>
    private static byte[] ClientFrame(byte selector, byte[] command)
    {
        var packet = new byte[MinPacketLength + command.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_REQUEST);

        packet[6] = Checksum(packet);
        packet[GameRequestPackets.SelectorOffset] = selector;
        command.CopyTo(packet, GameRequestPackets.CommandOffset);
        packet[^1] = 0;
        return packet;
    }

    /// <summary>Builds the supervision tool's frame: t = 'u' and a hexadecimal ASCII command.</summary>
    private static byte[] SelectFrame(string command)
        => ClientFrame(0x75, System.Text.Encoding.ASCII.GetBytes(command));

    private static byte[] KeepaliveFrame()
    {
        var packet = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
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
    public void Ids_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_CS_REQUEST).Should().Be(60);

        // rzu gates the id to 1060 from EPIC_9_6_3 on; 0x070300 is below it, so 7.3 stays on 60 and the
        // 9.6.3 value must not be declared here.
        Enum.IsDefined(typeof(GamePackets), (ushort)1060).Should().BeFalse();

        // The id must be defined, otherwise OnDataReceived drops the frame as "Undefined packet ID" before
        // any dispatch, and the arm below is never reached.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_REQUEST).Should().BeTrue();
    }

    [Test]
    public void ClientFrame_UsesTheEpic73Layout()
    {
        var packet = ClientFrame(0x75, Array.Empty<byte>());

        packet.Length.Should().Be(MinPacketLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(MinPacketLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(60);
        packet[6].Should().Be(Checksum(packet), "the checksum is the sum of the first six header bytes");
        packet[7].Should().Be(0x75, "t sits at offset 7, right after the header");
        packet[8].Should().Be(0, "the command is empty: the terminator is the last byte of the frame");

        Marshal.SizeOf<Header>().Should().Be(7);
        new Header(packet).Length.Should().Be((uint)MinPacketLength);
    }

    [TestCase(0, TestName = "ClientFrame_LengthIsNinePlusTheCommandLength_Empty")]
    [TestCase(1, TestName = "ClientFrame_LengthIsNinePlusTheCommandLength_One")]
    [TestCase(30, TestName = "ClientFrame_LengthIsNinePlusTheCommandLength_Thirty")]
    [TestCase(46, TestName = "ClientFrame_LengthIsNinePlusTheCommandLength_HexBlob")]
    [TestCase(100, TestName = "ClientFrame_LengthIsNinePlusTheCommandLength_Hundred")]
    public void ClientFrame_LengthIsNinePlusTheCommandLength(int commandLength)
    {
        var command = new byte[commandLength];
        for (var i = 0; i < commandLength; i++)
        {
            command[i] = (byte)('a' + i % 26);
        }

        var packet = ClientFrame(0x75, command);

        // Length = 7 (header) + 1 (t) + L (command) + 1 (terminator), the form of §3.3 of the sheet.
        packet.Length.Should().Be(9 + commandLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be((uint)(9 + commandLength));
        packet[8 + commandLength].Should().Be(0, "the terminator is counted in Length, so it is at 8 + L");

        GameRequestPackets.TryReadRequest(packet, out var selector, out var readCommand).Should().BeTrue();

        selector.Should().Be(0x75);
        readCommand.Length.Should().Be(commandLength, "L is Length - 9, terminator excluded");
        readCommand.ToArray().Should().Equal(command, "the command is handed back byte for byte");
    }

    [Test]
    public void Layout_FieldOffsetsAreTheOnesTheSheetEstablishes()
    {
        GameRequestPackets.SelectorOffset.Should().Be(7);
        GameRequestPackets.CommandOffset.Should().Be(8);
        GameRequestPackets.MinPacketSize.Should().Be(9, "7 header + 1 selector + 1 terminator");
    }

    [Test]
    public void TryReadRequest_ReadsTheSelectorAtOffsetSeven()
    {
        // 'u' (0x75) is the only value ever observed, and it comes from the supervision tool; nothing here
        // validates it, so any other byte is read as it stands.
        GameRequestPackets.TryReadRequest(SelectFrame("53454c454354"), out var selector, out _).Should().BeTrue();

        selector.Should().Be(0x75);

        GameRequestPackets.TryReadRequest(ClientFrame(0x01, new byte[] { 0x41 }), out var other, out _)
            .Should().BeTrue();
        other.Should().Be(0x01, "no value of t is enumerated or rejected");
    }

    [Test]
    public void TryReadRequest_AcceptsAnEmptyCommand()
    {
        // A 9 byte frame is a well formed command channel frame carrying an empty command — not a missing
        // one: rzu sizes the field as name.size() + hasNullTerminator, which is 0 + 1 here.
        var packet = ClientFrame(0x75, Array.Empty<byte>());

        GameRequestPackets.TryReadRequest(packet, out var selector, out var command).Should().BeTrue();

        selector.Should().Be(0x75);
        command.IsEmpty.Should().BeTrue();

        // It is also the only length the reader accepts with no command byte at all: 8 bytes is below the
        // smallest frame, because it has no terminator.
        GameRequestPackets.TryReadRequest(packet.AsSpan(0, 8), out _, out _).Should().BeFalse();
    }

    [Test]
    public void TryReadRequest_KeepsAnInternalNulInTheCommand()
    {
        // endstring has no length prefix: the field is Length - 9 bytes wide, so a NUL inside it is a byte
        // of the command and must not truncate the read.
        var command = new byte[] { 0x41, 0x00, 0x42, 0x00, 0x43 };
        var packet = ClientFrame(0x75, command);

        GameRequestPackets.TryReadRequest(packet, out _, out var readCommand).Should().BeTrue();

        readCommand.Length.Should().Be(5, "the first NUL does not end the field");
        readCommand.ToArray().Should().Equal(command);
    }

    [Test]
    public void TryReadRequest_RejectsAFrameWhoseLastByteIsNotTheTerminator()
    {
        // The frame is defined with hasNullTerminator = true, so a frame ending on something else is not
        // the frame of the sheet. rzu would silently lose its last byte (MessageBuffer.cpp:131-132); it is
        // refused here instead of being guessed at.
        var packet = SelectFrame("53454c454354");
        packet[^1].Should().Be(0);
        packet[^1] = 0x5a;

        GameRequestPackets.TryReadRequest(packet, out var selector, out var command).Should().BeFalse();

        selector.Should().Be(0, "nothing is handed back from a refused frame");
        command.IsEmpty.Should().BeTrue();
    }

    [TestCase(0, TestName = "TryReadRequest_RejectsAShortFrame_Empty")]
    [TestCase(6, TestName = "TryReadRequest_RejectsAShortFrame_BelowTheHeader")]
    [TestCase(7, TestName = "TryReadRequest_RejectsAShortFrame_HeaderOnly")]
    [TestCase(8, TestName = "TryReadRequest_RejectsAShortFrame_HeaderAndSelector")]
    public void TryReadRequest_RejectsAShortFrame(int length)
    {
        // Below 9 bytes there is no room for the terminator the frame is defined with: 7 is the header
        // alone (TM_CS_RETURN_LOBBY and friends are header only, this one is not), 8 is a frame without
        // the NUL. Both are refused with nothing handed back.
        var packet = new byte[length];
        if (length >= 7)
        {
            SelectFrame("414243").AsSpan(0, length).CopyTo(packet);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)length);
            packet[6] = Checksum(packet);
        }

        GameRequestPackets.TryReadRequest(packet, out var selector, out var command).Should().BeFalse();

        selector.Should().Be(0);
        command.IsEmpty.Should().BeTrue();
    }

    [Test]
    public void Bounds_FollowTheReceiveBuffer()
    {
        // rzu's own field bound is 65536 bytes; the binding one here is Connection's 32768 byte receive
        // buffer, which the receive loop never grows past, so L <= 32759.
        GameRequestPackets.MaxPacketSize.Should().Be(32768, "Connection reads into a 32768 byte buffer");
        GameRequestPackets.MaxCommandLength.Should().Be(32759);
        GameRequestPackets.MaxCommandLength.Should()
            .Be(GameRequestPackets.MaxPacketSize - GameRequestPackets.MinPacketSize);
    }

    [Test]
    public void TryReadRequest_AcceptsTheLargestFrameTheReceiveBufferCarries()
    {
        var command = new byte[GameRequestPackets.MaxCommandLength];
        var packet = ClientFrame(0x75, command);

        packet.Length.Should().Be(GameRequestPackets.MaxPacketSize);

        GameRequestPackets.TryReadRequest(packet, out var selector, out var readCommand).Should().BeTrue();

        selector.Should().Be(0x75);
        readCommand.Length.Should().Be(GameRequestPackets.MaxCommandLength);
    }

    [Test]
    public void OnDataReceived_ConsumesThePacketWithoutThrowing()
    {
        var frame = SelectFrame("53454c45435420544f5020312069642046524f4d20436861726163746572");
        var connection = new FrameConnection(frame);
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
    }

    [Test]
    public void OnDataReceived_AnswersNothing()
    {
        // No server to client frame 60 exists in op_codes.md, and the 7.3 client's incoming dispatcher has
        // no case for 60 at all: answering would be invented, and would make the server a participant of a
        // command channel it deliberately only observes.
        var frame = SelectFrame("53454c454354");
        var connection = new FrameConnection(frame);
        var client = NewGameClient(connection);

        client.OnDataReceived(frame.Length);

        connection.Sent.Should().BeEmpty("no reference answers packet 60");
    }

    [Test]
    public void OnDataReceived_ConsumesAFrameCoalescedWithAnotherOne()
    {
        // The command extends to the end of its own datagram, not to the end of the TCP read: the frame
        // behind it must survive untouched.
        var frame = SelectFrame("414243").Concat(KeepaliveFrame()).ToArray();
        var connection = new FrameConnection(frame);
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
    }

    [Test]
    public void OnDataReceived_ConsumesTheLargestFrameWithoutThrowing()
    {
        var frame = SelectFrame(new string('4', GameRequestPackets.MaxCommandLength));
        frame.Length.Should().Be(GameRequestPackets.MaxPacketSize);

        var connection = new FrameConnection(frame);
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0);
    }

    [TestCase(7, TestName = "OnDataReceived_ConsumesAMalformedFrame_HeaderOnly")]
    [TestCase(8, TestName = "OnDataReceived_ConsumesAMalformedFrame_NoTerminator")]
    public void OnDataReceived_ConsumesAMalformedFrameWithoutThrowing(int length)
    {
        // A frame shorter than 9 bytes cannot carry the terminator: it is refused — no answer, no
        // sanction — but still consumed entirely, so the loop keeps its place in the stream.
        var frame = MalformedFrame(length);
        var connection = new FrameConnection(frame);
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "a refused frame is still consumed entirely");
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopAlignedAfterARefusedFrame()
    {
        // The refused frame must not swallow or shift the one behind it: a real client frame followed by
        // the same connection's keepalive leaves nothing in the stream.
        var frame = MalformedFrame(8).Concat(KeepaliveFrame()).ToArray();
        var connection = new FrameConnection(frame);
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "the keepalive behind the refused frame was consumed too");
    }

    /// <summary>
    /// Rebuilds a frame 60 with another announced length, keeping a valid checksum: the receive loop
    /// rejects an invalid checksum before any dispatch, which would hide what these tests measure.
    /// </summary>
    private static byte[] MalformedFrame(int length)
    {
        var frame = new byte[length];
        var source = SelectFrame("414243");
        Array.Copy(source, frame, Math.Min(length, source.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)length);
        frame[6] = Checksum(frame);
        return frame;
    }

    private static GameClient NewGameClient(FrameConnection connection)
    {
        var networkService = new NetworkService(
            A.Fake<ILogger<NetworkService>>(),
            Options.Create(new NetworkOptions { CipherKey = "request-test-key" }),
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
    /// In-memory replacement for the socket-backed connection: it serves a plaintext frame out of its own
    /// buffer and records everything the receive loop pushes back, so a test can tell an ignored packet
    /// from an answered one.
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
