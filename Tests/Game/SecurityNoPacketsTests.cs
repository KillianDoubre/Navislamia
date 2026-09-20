using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Serilog;
using Serilog.Core;
using Serilog.Events;
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
/// TM_CS_SECURITY_NO (9005) is 30 bytes on the wire — the 7-byte header, an int32 mode at offset 7 and a
/// fixed 19-byte container for the code at offset 11 — and answers TM_SC_REQUEST_SECURITY_NO (9004). The
/// client writes the length in hard at 30, so a short or padded frame is an anomaly, refused before mode is
/// read. Nothing is verified, stored or answered: no reference implements the answer, and the code is a
/// reusable secret that must never reach a log.
/// See docs/packet-specs/9005-security-no.md.
/// </summary>
[TestFixture]
public class SecurityNoPacketsTests
{
    private const int PacketLength = 30;
    private const int SecurityNoLength = 19;

    /// <summary>Builds the 30-byte client frame: Length, ID, checksum, mode at 7, then the code at 11.</summary>
    private static byte[] ClientFrame(int mode, string securityNo)
    {
        var packet = new byte[PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_SECURITY_NO);

        packet[6] = Checksum(packet);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(7, 4), mode);

        var code = System.Text.Encoding.ASCII.GetBytes(securityNo);
        code.AsSpan(0, Math.Min(code.Length, SecurityNoLength - 1)).CopyTo(packet.AsSpan(11));
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
    public void SecurityNoIds_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_CS_SECURITY_NO).Should().Be(9005);

        // The cousin the client sends in the other direction: 9004 is the server's own prompt, so a handler
        // for it would answer a packet only the server emits (spec §5.5, piège 1).
        ((ushort)GamePackets.TM_CS_SECURITY_NO).Should().NotBe(9004);

        // rzu remaps the id to 8105 from EPIC_9_6_3 on (EPIC_7_3 = 0x070300 is below it); 7.3 stays on 9005
        // and the 9.6.3 value must not be declared here, where it names a commercial storage takeout.
        Enum.IsDefined(typeof(GamePackets), (ushort)8105).Should().BeFalse();

        // The id must be defined, otherwise OnDataReceived drops the frame as "Undefined packet ID" before
        // any dispatch, and the receive loop stays there.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_SECURITY_NO).Should().BeTrue();
    }

    [Test]
    public void ClientPacket_UsesTheEpic73Layout()
    {
        var packet = ClientFrame(1, "123456");

        GameSecurityPackets.PacketLength.Should().Be(PacketLength);
        packet.Length.Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(PacketLength,
            "the client writes 0x1e in the length field (VR 0x48cfb0)");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(9005);
        packet[6].Should().Be(Checksum(packet), "the checksum is the sum of the first six header bytes");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(1);
        System.Text.Encoding.ASCII.GetString(packet, 11, 6).Should().Be("123456");
    }

    [Test]
    public void ClientPacket_LeavesTheNineteenthByteOfTheContainerAtZero()
    {
        // 7 + 4 + 19: the client copies 18 bytes into a payload it has just zeroed, so offset 29 is a
        // terminator the client never fills.
        var packet = ClientFrame(2, "123456");

        packet[29].Should().Be(0, "the 19th byte of the container is a zero in the client's own frame");
        packet.Skip(17).Should().OnlyContain(b => b == 0, "the client zeroes the whole payload before the memcpy");
    }

    [Test]
    public void ClientPacket_HasNoFieldOutsideTheHeaderModeAndCode()
    {
        // 7 + 4 + 19: no account(64), result or security_no_1/_2 exists below EPIC_9_6_7 — copying the 9.x
        // form would make 94 bytes instead of 30 and desynchronise the client.
        Marshal.SizeOf<Header>().Should().Be(7);
        new Header(ClientFrame(0, "000000")).Length.Should().Be((uint)PacketLength);
        ClientFrame(0, "000000").Length.Should().Be(PacketLength, "no payload byte exists past offset 29");
    }

    [Test]
    public void ClientPacket_ChecksumIgnoresThePayload()
    {
        // The client sums the six header bytes only, so editing the mode or the code in transit does not
        // break the checksum: a modified security code is therefore indistinguishable on the wire.
        var frame = ClientFrame(1, "123456");
        var modified = ClientFrame(2, "999999");

        modified[6].Should().Be(frame[6]);
    }

    [Test]
    public void TryReadSecurityNo_ReadsModeAtOffsetSevenAndTheCodeAtOffsetEleven()
    {
        GameSecurityPackets.TryReadSecurityNo(ClientFrame(1, "654321"), out var request).Should().BeTrue();

        request.Mode.Should().Be(1);
        request.SecurityNo.Should().Be("654321");
        request.SecurityNo.Length.Should().Be(6);
    }

    [Test]
    public void TryReadSecurityNo_KeepsFieldOrder()
    {
        // Asymmetric values: rzu lays mode out first and the string second, and the client fills offset 7
        // then offset 11 (VR 0x6658b6, 0x6658bc). Swapping the two would fail here.
        GameSecurityPackets.TryReadSecurityNo(ClientFrame(2, "111111"), out var request).Should().BeTrue();

        request.Mode.Should().Be(2);
        request.SecurityNo.Should().Be("111111");
    }

    [Test]
    public void TryReadSecurityNo_ReadsTheModeLittleEndian()
    {
        // The four bytes at offsets 7-10 are laid down by hand — 04 03 02 01 — so a big-endian read would
        // give 0x04030201 (67305985) instead of the little-endian 0x01020304 (16909060).
        var frame = ClientFrame(0, "000000");
        frame[7] = 0x04;
        frame[8] = 0x03;
        frame[9] = 0x02;
        frame[10] = 0x01;

        GameSecurityPackets.TryReadSecurityNo(frame, out var request).Should().BeTrue();

        request.Mode.Should().Be(16909060);
        request.Mode.Should().NotBe(67305985, "rzu writes the scalar int32 as it stands on x86, so little-endian");
    }

    [Test]
    public void TryReadSecurityNo_StartsTheCodeAtOffsetEleven()
    {
        // Byte 10 is the top byte of mode, not the first byte of the code: a one-byte-off read window would
        // pick 'X' up as the first character of the code.
        var frame = ClientFrame(0, "123456");
        frame[10] = (byte)'X';

        GameSecurityPackets.TryReadSecurityNo(frame, out var request).Should().BeTrue();

        request.SecurityNo.Should().Be("123456");
        request.Mode.Should().Be(0x58000000, "byte 10 is the most significant byte of the little-endian mode");
    }

    [Test]
    public void TryReadSecurityNo_StopsTheCodeAtTheFirstZero()
    {
        // Row 9005 of the client's own log format prints the code with %s (VR 0xa52318): it is a C string in
        // a 19-byte container, not 19 bytes of data. Trailing bytes behind the terminator are not part of it.
        var frame = ClientFrame(1, "1234");
        frame[16] = (byte)'9';
        frame[17] = (byte)'9';
        frame[18] = (byte)'9';

        GameSecurityPackets.TryReadSecurityNo(frame, out var request).Should().BeTrue();

        request.SecurityNo.Should().Be("1234", "the code is read up to the first zero byte");
        request.SecurityNo.Length.Should().Be(4);
    }

    [Test]
    public void TryReadSecurityNo_KeepsAtMostEighteenCharacters()
    {
        // rzu converts maxSize - 1 characters at read time (MessageBuffer.cpp:104-109) and the client copies
        // exactly 18 bytes, so a 19-byte field with no terminator yields 18 characters, never 19.
        var frame = ClientFrame(1, "");
        for (var i = 0; i < SecurityNoLength; i++)
        {
            frame[11 + i] = (byte)('a' + i % 26);
        }

        GameSecurityPackets.TryReadSecurityNo(frame, out var request).Should().BeTrue();

        request.SecurityNo.Length.Should().Be(18);
        request.SecurityNo.Should().Be("abcdefghijklmnopqr");
    }

    [TestCase("", TestName = "TryReadSecurityNo_AcceptsTheCodeOfTheCancelPath")]
    [TestCase("1", TestName = "TryReadSecurityNo_AcceptsASingleCharacter")]
    [TestCase("123456", TestName = "TryReadSecurityNo_AcceptsTheSixDigitsTheWindowAsksFor")]
    public void TryReadSecurityNo_DoesNotConstrainTheCodeLength(string securityNo)
    {
        // The six digits of ui_text_6559 are the window's constraint, not the protocol's, and "hit Cancel"
        // is an attested player path: bounding the code here would refuse frames the client really sends.
        GameSecurityPackets.TryReadSecurityNo(ClientFrame(1, securityNo), out var request).Should().BeTrue();

        request.SecurityNo.Should().Be(securityNo);
    }

    [TestCase(0, TestName = "TryReadSecurityNo_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadSecurityNo_RejectsAHeaderOnlyFrame")]
    [TestCase(29, TestName = "TryReadSecurityNo_RejectsATruncatedFrame")]
    [TestCase(31, TestName = "TryReadSecurityNo_RejectsAPaddedFrame")]
    public void TryReadSecurityNo_RejectsAnyLengthOtherThanThirty(int length)
    {
        var packet = new byte[length];
        if (length >= PacketLength)
        {
            ClientFrame(1, "123456").CopyTo(packet, 0);
        }

        GameSecurityPackets.TryReadSecurityNo(packet, out var request).Should().BeFalse();
        request.Should().Be(default(GameSecurityPackets.SecurityNoRequest));
    }

    [Test]
    public void OnDataReceived_ConsumesThePacketWithoutThrowing()
    {
        var connection = new FrameConnection(ClientFrame(1, "123456"));
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(PacketLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
    }

    [Test]
    public void OnDataReceived_AnswersNothing()
    {
        // No reference implements an answer: rzu verifies the code on the authentication server (40001 ->
        // 40000) and Navislamia has neither that transport nor any storage for the code. The four candidate
        // answers of §5.4 are open decisions, so sending one here would be invented.
        var connection = new FrameConnection(ClientFrame(1, "123456"));
        var client = NewGameClient(connection);

        client.OnDataReceived(PacketLength);

        connection.Sent.Should().BeEmpty("no reference answers packet 9005");
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        // The frame must not swallow or desynchronise the one behind it: the keepalive is consumed too.
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[6] = Checksum(keepalive);

        var frame = ClientFrame(1, "123456").Concat(keepalive).ToArray();
        var connection = new FrameConnection(frame);
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
    }

    [TestCase(7, TestName = "OnDataReceived_ConsumesAMalformedHeaderOnlyFrame")]
    [TestCase(29, TestName = "OnDataReceived_ConsumesAMalformedTruncatedFrame")]
    [TestCase(40, TestName = "OnDataReceived_ConsumesAMalformedPaddedFrame")]
    public void OnDataReceived_ConsumesAMalformedFrameWithoutThrowing(int length)
    {
        // The client always writes 30, so any other length is an anomaly; it must be refused without
        // sending an answer and without leaving bytes in the stream.
        var frame = MalformedFrame(length);
        var connection = new FrameConnection(frame);
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "a refused frame is still consumed entirely");
    }

    [Test]
    public void OnDataReceived_LogsTheModeAndTheLengthButNeverTheCode()
    {
        // The code is a reusable authentication secret: the same one guards character deletion and the
        // warehouse, and a client can send it many times. The arm reports mode and length only (§5.5).
        var sink = new CapturingSink();
        var previousLogger = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();

        try
        {
            var connection = new FrameConnection(ClientFrame(2, "530917"));
            var client = NewGameClient(connection);

            client.OnDataReceived(PacketLength);

            var rendered = sink.Events.Select(logEvent => logEvent.RenderMessage()).ToList();

            rendered.Should().NotBeEmpty("the arm must report the mode and the length at Debug");
            rendered.Should().Contain(message => message.Contains("mode=2") && message.Contains("securityNoLength=6"),
                "mode and length are the two facts the specification allows in a log");
            rendered.Should().OnlyContain(message => !message.Contains("530917"),
                "the security code is a secret and must never appear in a log line");
        }
        finally
        {
            Log.Logger = previousLogger;
        }
    }

    /// <summary>
    /// Rebuilds frame 9005 with another announced length, keeping a valid checksum: the receive loop rejects
    /// an invalid one before any dispatch, which would hide what this test measures.
    /// </summary>
    private static byte[] MalformedFrame(int length)
    {
        var frame = new byte[length];
        Array.Copy(ClientFrame(1, "123456"), frame, Math.Min(length, PacketLength));
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)length);
        frame[6] = Checksum(frame);
        return frame;
    }

    private static GameClient NewGameClient(FrameConnection connection)
    {
        var networkService = new NetworkService(
            A.Fake<ILogger<NetworkService>>(),
            Options.Create(new NetworkOptions { CipherKey = "security-no-test-key" }),
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

    /// <summary>
    /// Serilog sink collecting the log events of one test, so a test can assert which facts the arm reports
    /// and, above all, that the security code is not one of them.
    /// </summary>
    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
