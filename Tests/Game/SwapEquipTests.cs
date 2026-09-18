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
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;

namespace Tests.Game;

/// <summary>
/// TM_CS_SWAP_EQUIP (223) is header-only: 7 bytes, no payload, no response. See
/// docs/packet-specs/223-swap-equip.md. There is no parser to test here, so the offsets are pinned on
/// the frame itself and the dispatch is exercised against the real receive loop: a member of
/// <see cref="GamePackets"/> that reaches the final switch throws "Unknown Packet Type" inside that
/// loop, which this test would catch.
/// </summary>
[TestFixture]
public class SwapEquipTests
{
    private const int ClientPacketLength = 7;

    /// <summary>Builds the 7-byte client frame: Length, ID, checksum, and nothing else.</summary>
    private static byte[] ClientFrame(GamePackets id)
    {
        var packet = new byte[ClientPacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), ClientPacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)id);

        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        packet[6] = checksum;
        return packet;
    }

    [Test]
    public void ClientPacket_IsTheSevenByteHeaderOnlyEpic73Frame()
    {
        var packet = ClientFrame(GamePackets.TM_CS_SWAP_EQUIP);

        packet.Should().HaveCount(ClientPacketLength, "the reference declares an empty _DEF(_) body");
        ((ushort)GamePackets.TM_CS_SWAP_EQUIP).Should().Be(223);

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(ClientPacketLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(223);

        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        packet[6].Should().Be(checksum, "the checksum is the sum of the first six header bytes");
    }

    [Test]
    public void ClientPacket_HasNoFieldBehindTheHeader()
    {
        var packet = ClientFrame(GamePackets.TM_CS_SWAP_EQUIP);

        Marshal.SizeOf<Header>().Should().Be(ClientPacketLength);
        new Header(packet).Length.Should().Be((uint)packet.Length, "no byte may be read past offset 6");
        packet.Length.Should().Be(ClientPacketLength, "no payload field exists at offset 7");
    }

    [Test]
    public void EnumMember_SitsBetweenItsNeighbours()
    {
        ((ushort)GamePackets.TM_SC_HIDE_EQUIP_INFO).Should().Be(222);
        ((ushort)GamePackets.TM_CS_SWAP_EQUIP).Should().Be(223);
        ((ushort)GamePackets.TM_SC_SKIN_INFO).Should().Be(224);
    }

    [Test]
    public void OnDataReceived_ConsumesThePacketWithoutThrowing()
    {
        var connection = new FrameConnection(ClientFrame(GamePackets.TM_CS_SWAP_EQUIP));
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(ClientPacketLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.Sent.Should().BeEmpty("neither reference answers packet 223");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
    }

    [Test]
    public void OnDataReceived_ConsumesTheHeaderOnlyPacketWhenItIsCoalescedWithAnotherOne()
    {
        // The receive loop compared the remaining byte count with a bare '>' in the past, which
        // silently dropped a header-only packet arriving alone; a 223 followed by a keepalive also
        // proves the loop keeps both packets instead of stopping on the first one.
        var keepalive = ClientFrame(GamePackets.TM_NONE);

        var frame = ClientFrame(GamePackets.TM_CS_SWAP_EQUIP).Concat(keepalive).ToArray();
        var connection = new FrameConnection(frame);
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
    }

    private static GameClient NewGameClient(FrameConnection connection)
    {
        var networkService = new NetworkService(
            A.Fake<ILogger<NetworkService>>(),
            Options.Create(new NetworkOptions { CipherKey = "swap-equip-test-key" }),
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
            A.Fake<IFieldPropService>());

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        return new GameClient(socket, networkService) { Connection = connection };
    }

    /// <summary>
    /// In-memory replacement for the socket-backed connection: it serves a plaintext frame byte by
    /// byte and records everything the receive loop pushes back, so a test can tell an ignored
    /// packet from an answered one.
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
