using System;
using System.Buffers.Binary;
using System.Linq;
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
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Tests.Game;

/// <summary>
/// TM_CS_OPEN_ITEM_SHOP (10000) is a header-only request: 7 bytes on the wire, no field past the checksum
/// at offset 6. The 7.3 client never sends it (opening the item shop is a purely local command that builds
/// the web shop URL from shop_url) and Navislamia answers nothing: the four fields of the answer
/// TM_SC_OPEN_ITEM_SHOP (10001) are web shop credentials the server does not hold.
/// See docs/packet-specs/10000-open-item-shop.md.
/// </summary>
[TestFixture]
public class OpenItemShopPacketsTests
{
    /// <summary>The whole frame: 7-byte header, nothing after it.</summary>
    private const int PacketLength = 7;

    /// <summary>Sum of the six header bytes of <see cref="ClientFrame"/>: 0x07 + 0x10 + 0x27.</summary>
    private const byte ExpectedChecksum = 0x3e;

    /// <summary>Builds the 7-byte client frame: Length, ID, checksum, no payload.</summary>
    private static byte[] ClientFrame()
    {
        var packet = new byte[PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_OPEN_ITEM_SHOP);

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
        ((ushort)GamePackets.TM_CS_OPEN_ITEM_SHOP).Should().Be(10000);

        // rzu gates the pair to 9000/9001 from EPIC_9_6_3 on; 0x070300 is below it, so 7.3 stays on
        // 10000/10001. In 7.3, 9000 and 9001 are already TM_SC_OPEN_URL and TM_SC_URL_LIST: declaring them
        // here would answer a shop request with a URL list.
        Enum.IsDefined(typeof(GamePackets), (ushort)9000).Should().BeFalse();
        Enum.IsDefined(typeof(GamePackets), (ushort)9001).Should().BeFalse();

        // The request id must be defined, otherwise OnDataReceived drops the frame as "Undefined packet ID"
        // before any dispatch. The answer id must NOT be declared: it is a server to client packet with no
        // dispatch arm, so a declared 10001 would reach the final "Unknown Packet Type" throw.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_OPEN_ITEM_SHOP).Should().BeTrue();
        Enum.IsDefined(typeof(GamePackets), (ushort)10001).Should().BeFalse();
    }

    [Test]
    public void ClientPacket_IsHeaderOnly()
    {
        var packet = ClientFrame();

        // Total size, then the offset of every field the frame has: Length at 0, ID at 4, checksum at 6.
        packet.Length.Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(10000);
        packet[6].Should().Be(ExpectedChecksum, "the checksum is the sum of the first six header bytes");

        // 7 + nothing: any body byte would push the total past 7 and desynchronise the receive loop.
        Marshal.SizeOf<Header>().Should().Be(7);
        new Header(packet).Length.Should().Be((uint)PacketLength);
        new Header(packet).ID.Should().Be(10000);

        // rzu (TS_CS_OPEN_ITEM_SHOP_DEF is empty) and NGemity agree: there is no field past offset 6. The
        // client's packet to event converter has a case for 10000 that reads 8 payload bytes, which the
        // specification refuses to follow (section 7a) — nothing is read there.
        packet.Skip(7).Should().BeEmpty();
    }

    [Test]
    public void ClientPacket_ReadsLengthAndIdLittleEndian()
    {
        // The header bytes are laid down by hand, and the announced values are not symmetrical, so a
        // big-endian read gives 0x07000000 (117440512) for the length and 0x1027 (4135) for the id.
        var packet = ClientFrame();
        packet[0].Should().Be(0x07);
        packet[1].Should().Be(0x00);
        packet[2].Should().Be(0x00);
        packet[3].Should().Be(0x00);
        packet[4].Should().Be(0x10);
        packet[5].Should().Be(0x27);

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(7u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().NotBe(117440512u);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(10000);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().NotBe(4135);
    }

    [Test]
    public void ClientPacket_ChecksumCoversTheLengthAndTheId()
    {
        // Rewriting the id to the 9.6.3 value changes the checksum: the sum covers the whole header, so a
        // frame whose id was rewritten in transit no longer matches and the receive loop rejects it.
        var packet = ClientFrame();
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), 9000);

        Checksum(packet).Should().NotBe(ExpectedChecksum);
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
        // No answer exists for this request: the four fields of TM_SC_OPEN_ITEM_SHOP (10001) are item shop
        // credentials (client_id, account_id, one_time_password, raw_server_name) that Navislamia does not
        // hold, and no reference server ever answers.
        var connection = new FrameConnection(ClientFrame());
        var client = NewGameClient(connection);

        client.OnDataReceived(PacketLength);

        connection.Sent.Should().BeEmpty("no reference answers an item shop request");
    }

    [Test]
    public void OnDataReceived_LogsTheRefusalAsAWarning()
    {
        // The frame is refused explicitly, not dropped by the generic "Undefined packet ID" guard: the
        // receive loop must have gone through the dedicated arm, which is what stops it from reaching the
        // final throw.
        var sink = new CapturingSink();
        var previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();

        try
        {
            var client = NewGameClient(new FrameConnection(ClientFrame()));

            client.OnDataReceived(PacketLength);
        }
        finally
        {
            Log.Logger = previous;
        }

        sink.Events.Should().Contain(
            e => e.Level == LogEventLevel.Warning
                 && e.MessageTemplate.Text.Contains("TM_CS_OPEN_ITEM_SHOP"),
            "the dedicated dispatch arm logs the refusal");
        sink.Events.Should().NotContain(
            e => e.MessageTemplate.Text.Contains("Undefined packet ID"),
            "the frame must be dispatched, not dropped as an undefined id");
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        // A header-only frame arriving alone in a TCP read: the receive loop must use >= on the header size,
        // otherwise the frame would never be consumed and the keepalive behind it would stall too.
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

    [TestCase(8, TestName = "OnDataReceived_IgnoresAFrameCarryingTheHypotheticalPayload")]
    [TestCase(15, TestName = "OnDataReceived_IgnoresAPaddedFrame")]
    public void OnDataReceived_IgnoresAFrameLongerThanTheHeader(int length)
    {
        // Section 7a of the sheet leaves open whether the client expects 8 payload bytes for 10000. Since
        // neither rzu nor NGemity declares a field, nothing is read: the extra bytes are consumed with the
        // frame, ignored, and never answered — the stream stays in sync either way.
        var frame = new byte[length];
        Array.Copy(ClientFrame(), frame, PacketLength);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)length);
        frame[6] = Checksum(frame);

        var connection = new FrameConnection(frame);
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "the whole frame is consumed whatever its announced length");
    }

    private static GameClient NewGameClient(FrameConnection connection)
    {
        var networkService = new NetworkService(
            A.Fake<ILogger<NetworkService>>(),
            Options.Create(new NetworkOptions { CipherKey = "open-item-shop-test-key" }),
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

    /// <summary>Collects the Serilog events the receive loop emits, so a dispatched frame can be told from
    /// one dropped by the generic "Undefined packet ID" guard.</summary>
    private sealed class CapturingSink : ILogEventSink
    {
        public System.Collections.Generic.List<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
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

        public System.Collections.Generic.List<byte[]> Sent { get; } = new();

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
