using System.Buffers.Binary;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
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
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

/// <summary>
/// TM_CS_RESURRECTION (513) — the death/respawn foundation, see
/// docs/packet-specs/socle-mort-respawn.md. The frame is fixed at 12 bytes: the 7-byte header, the
/// 4-byte handle at offset 7 and the 1-byte type at offset 11. The dispatch is exercised against the
/// real receive loop on purpose: a member of <see cref="GamePackets"/> that no branch claims reaches
/// the final switch and throws "Unknown Packet Type" inside that loop.
/// </summary>
[TestFixture]
public class ResurrectionPacketTests
{
    private const uint Handle = 0x40000456u;
    private const int PacketLength = 12;

    private static byte[] ClientFrame(uint handle, ResurrectionType type, int length = PacketLength)
    {
        var packet = new byte[length];

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_RESURRECTION);

        if (length >= 11)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), handle);
        }

        if (length > 11)
        {
            packet[11] = (byte)type;
        }

        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        packet[6] = checksum;
        return packet;
    }

    [Test]
    public void EnumMember_SitsBetweenItsNeighbours()
    {
        ((ushort)GamePackets.TM_CS_TARGETING).Should().Be(511);
        ((ushort)GamePackets.TM_CS_RESURRECTION).Should().Be(513);
        ((ushort)GamePackets.TM_CS_MONSTER_RECOGNIZE).Should().Be(517);
    }

    [Test]
    public void ClientFrame_IsTheFixedTwelveByteEpic73Shape()
    {
        var packet = ClientFrame(Handle, ResurrectionType.UseNone);

        GameActionPackets.ResurrectionPacketLength.Should().Be(PacketLength);
        packet.Should().HaveCount(12, "the 7.3 frame is length, id, checksum, handle and type");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(12);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(513);
    }

    [Test]
    public void ClientFrame_HasTheHandleAtOffsetSevenAndTheTypeAtOffsetEleven()
    {
        var packet = ClientFrame(0x01020304u, ResurrectionType.UsePotion);

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x01020304u);
        packet[11].Should().Be(2, "the type is the last byte of the frame, with no padding behind it");

        GameActionPackets.TryReadResurrection(packet, out var request).Should().BeTrue();
        request.Handle.Should().Be(0x01020304u);
        request.Type.Should().Be(ResurrectionType.UsePotion);
    }

    [Test]
    public void TryReadResurrection_ReadsEveryTypeTheReferenceDeclares()
    {
        ((byte)ResurrectionType.UseNone).Should().Be(0);
        ((byte)ResurrectionType.UseState).Should().Be(1);
        ((byte)ResurrectionType.UsePotion).Should().Be(2);
        ((byte)ResurrectionType.Compete).Should().Be(3);
        ((byte)ResurrectionType.Deathmatch).Should().Be(4);

        foreach (var type in Enum.GetValues<ResurrectionType>())
        {
            GameActionPackets.TryReadResurrection(ClientFrame(Handle, type), out var request)
                .Should().BeTrue();
            request.Type.Should().Be(type);
        }
    }

    [Test]
    public void TryReadResurrection_RefusesThePre61ThirteenByteShape()
    {
        var pre61 = ClientFrame(Handle, ResurrectionType.UseNone, PacketLength + 1);

        GameActionPackets.TryReadResurrection(pre61, out var request).Should().BeFalse(
            "the pre-6.1 shape adds use_state/use_potion behind the type, and its extra byte would misalign the stream");
        request.Should().Be(default(GameActionPackets.ResurrectionRequest));
    }

    [Test]
    public void TryReadResurrection_RefusesAShortFrame()
    {
        GameActionPackets.TryReadResurrection(ClientFrame(Handle, ResurrectionType.UseNone, 7), out _)
            .Should().BeFalse();
        GameActionPackets.TryReadResurrection(ClientFrame(Handle, ResurrectionType.UseNone, 11), out _)
            .Should().BeFalse();
    }

    [Test]
    public void CheckRequest_AcceptsTheTownPathForTheDeadOwnCharacter()
    {
        ResurrectionRules.CheckRequest(Handle, ResurrectionType.UseNone, Handle, 0)
            .Should().Be(ResultCode.Success);
    }

    [Test]
    public void CheckRequest_RefusesAnotherCharactersHandle()
    {
        ResurrectionRules.CheckRequest(Handle, ResurrectionType.UseNone, Handle + 1, 0)
            .Should().Be(ResultCode.NotOwn);
    }

    [Test]
    public void CheckRequest_RefusesALivingCharacter()
    {
        ResurrectionRules.CheckRequest(Handle, ResurrectionType.UseNone, Handle, 1)
            .Should().Be(ResultCode.NotActable);
    }

    [Test]
    public void CheckRequest_RefusesTheTypesThatBelongToALaterLot()
    {
        foreach (var type in new[]
                 {
                     ResurrectionType.UseState, ResurrectionType.UsePotion, ResurrectionType.Compete,
                     ResurrectionType.Deathmatch
                 })
        {
            ResurrectionRules.CheckRequest(Handle, type, Handle, 0).Should().Be(ResultCode.NotActable);
        }
    }

    [Test]
    public void CheckRequest_RefusesASessionWithoutACharacter()
    {
        ResurrectionRules.CheckRequest(0, ResurrectionType.UseNone, 0, 0).Should().Be(ResultCode.NotActable);
    }

    [Test]
    public void RestoredVitals_AreTheRecomputedMaxima()
    {
        var (hp, mp) = ResurrectionRules.RestoredVitals(5000f, 800f);

        hp.Should().Be(5000);
        mp.Should().Be(800);
    }

    [Test]
    public void OnDataReceived_ConsumesThePacketWithoutThrowing()
    {
        var connection = new FrameConnection(ClientFrame(Handle, ResurrectionType.UseNone));
        var client = NewGameClient(connection, out _);

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
    }

    [Test]
    public void OnDataReceived_RefusesASessionWithoutACharacter()
    {
        var connection = new FrameConnection(ClientFrame(Handle, ResurrectionType.UseNone));
        var client = NewGameClient(connection, out _);

        client.OnDataReceived(connection.BytesAvailable);

        Results(connection).Should().Equal(A574Result((ushort)ResultCode.NotActable));
    }

    [Test]
    public void OnDataReceived_AnswersInvalidArgumentOnAMalformedFrame()
    {
        var connection = new FrameConnection(ClientFrame(Handle, ResurrectionType.UseNone, PacketLength + 1));
        var client = NewGameClient(connection, out var services, realWarp: false);

        client.OnDataReceived(connection.BytesAvailable);

        Results(connection).Should().Equal(A574Result((ushort)ResultCode.InvalidArgument));
        A.CallTo(() => services.WarpCalls.Warp(A<GameClient>._, A<float>._, A<float>._))
            .MustNotHaveHappened();
    }

    [Test]
    public void Resurrect_WarpsToTheReturnPointRestoresTheVitalsAndAcknowledges()
    {
        const float respawnX = 94454f;
        const float respawnY = 126040f;

        var connection = new FrameConnection(ClientFrame(Handle, ResurrectionType.UseNone));
        var client = NewGameClient(connection, out _);
        Seed(client, new ConnectionInfo
        {
            CharacterHandle = Handle,
            CharacterHp = 0,
            CharacterMp = 0,
            CharacterName = "Resurrected",
            X = 1000f,
            Y = 2000f,
            Layer = 2,
            RespawnX = respawnX,
            RespawnY = respawnY,
            RespawnLayer = 5
        });

        client.OnDataReceived(connection.BytesAvailable);

        var info = ConnectionInfoOf(client);
        info.CharacterHp.Should().Be(5000, "the returned vitals are the recomputed maxima");
        info.CharacterMp.Should().Be(800);
        info.Layer.Should().Be(5, "the return point carries its own layer");

        // The warp frame is the real WarpService's, so the position on the wire is the return point.
        BinaryPrimitives.ReadUInt16LittleEndian(connection.Sent[0].AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_WARP);
        BinaryPrimitives.ReadSingleLittleEndian(connection.Sent[0].AsSpan(7, 4)).Should().Be(respawnX);
        BinaryPrimitives.ReadSingleLittleEndian(connection.Sent[0].AsSpan(11, 4)).Should().Be(respawnY);
        connection.Sent[0][19].Should().Be(5);

        Properties(connection).Should().Equal(("hp", 5000L), ("mp", 800L));
        Results(connection).Should().Equal(A574Result((ushort)ResultCode.Success));
        connection.Sent.Should().HaveCount(4, "warp, hp, mp and the acknowledgment");
    }

    [Test]
    public void Resurrect_DoesNothingOnALivingCharacter()
    {
        var connection = new FrameConnection(ClientFrame(Handle, ResurrectionType.UseNone));
        var client = NewGameClient(connection, out var services, realWarp: false);
        Seed(client, new ConnectionInfo { CharacterHandle = Handle, CharacterHp = 4000 });

        client.OnDataReceived(connection.BytesAvailable);

        Results(connection).Should().Equal(A574Result((ushort)ResultCode.NotActable));
        A.CallTo(() => services.WarpCalls.Warp(A<GameClient>._, A<float>._, A<float>._))
            .MustNotHaveHappened();
        A.CallTo(() => services.StatService.Compute(A<ConnectionInfo>._)).MustNotHaveHappened();
        ConnectionInfoOf(client).CharacterHp.Should().Be(4000);
    }

    [Test]
    public void Resurrect_RefusesTheLaterLotTypesWithoutMoving()
    {
        var connection = new FrameConnection(ClientFrame(Handle, ResurrectionType.UsePotion));
        var client = NewGameClient(connection, out var services, realWarp: false);
        Seed(client, new ConnectionInfo { CharacterHandle = Handle, CharacterHp = 0 });

        client.OnDataReceived(connection.BytesAvailable);

        Results(connection).Should().Equal(A574Result((ushort)ResultCode.NotActable));
        A.CallTo(() => services.WarpCalls.Warp(A<GameClient>._, A<float>._, A<float>._))
            .MustNotHaveHappened();
        ConnectionInfoOf(client).CharacterHp.Should().Be(0, "a refused request changes nothing");
    }

    [Test]
    public void Resurrect_AnswersEveryCoalescedRequest()
    {
        var frame = ClientFrame(Handle, ResurrectionType.UseNone)
            .Concat(ClientFrame(Handle, ResurrectionType.UseNone))
            .ToArray();
        var connection = new FrameConnection(frame);
        var client = NewGameClient(connection, out _);
        Seed(client, new ConnectionInfo
        {
            CharacterHandle = Handle,
            CharacterHp = 0,
            RespawnX = 94454f,
            RespawnY = 126040f
        });

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
        Results(connection).Should().Equal(
            new[] { A574Result((ushort)ResultCode.Success), A574Result((ushort)ResultCode.NotActable) },
            "the first request raises the character, the second finds it alive");
    }

    private static (ushort RequestId, ushort Result) A574Result(ushort result) =>
        ((ushort)GamePackets.TM_CS_RESURRECTION, result);

    private static List<(ushort RequestId, ushort Result)> Results(FrameConnection connection) =>
        connection.Sent
            .Where(packet => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2))
                             == (ushort)GamePackets.TM_SC_RESULT)
            .Select(packet => (
                BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(7, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(9, 2))))
            .ToList();

    private static List<(string Name, long Value)> Properties(FrameConnection connection) =>
        connection.Sent
            .Where(packet => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2))
                             == (ushort)GamePackets.TM_SC_PROPERTY)
            .Select(packet => (
                Encoding.ASCII.GetString(packet, 12, 16).TrimEnd('\0'),
                BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(28, 8))))
            .ToList();

    /// <summary>
    /// The tests live in another assembly, so the internal connection state has to be reached by
    /// reflection. The client reads it per packet, so replacing it seeds the whole session.
    /// </summary>
    private static void Seed(GameClient client, ConnectionInfo info)
    {
        var property = SessionState();

        property.Should().NotBeNull("ConnectionInfo is expected to stay the single session state holder");
        property!.SetValue(client, info);
    }

    private static ConnectionInfo ConnectionInfoOf(GameClient client) =>
        (ConnectionInfo)SessionState().GetValue(client)!;

    private static PropertyInfo SessionState() => typeof(Client).GetProperty(nameof(ConnectionInfo),
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

    private static GameClient NewGameClient(FrameConnection connection, out TestServices services,
        bool realWarp = true)
    {
        services = new TestServices();

        var networkService = new NetworkService(
            A.Fake<ILogger<NetworkService>>(),
            Options.Create(new NetworkOptions { CipherKey = "resurrection-test-key" }),
            A.Fake<ICharacterService>(),
            A.Fake<IBannedWordsRepository>(),
            services.StatService,
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
            new ResurrectionService(realWarp ? services.WarpService : services.WarpCalls,
                services.StatService));

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        return new GameClient(socket, networkService) { Connection = connection };
    }

    /// <summary>
    /// The fakes the assertions reach for. The warp service is the real one, so the frame it sends —
    /// and not merely the call — proves where the character reappears; the refused requests are run
    /// against the fake instead, to prove they never reach it.
    /// </summary>
    private sealed class TestServices
    {
        public IStatService StatService { get; } = A.Fake<IStatService>();

        public IWarpService WarpService { get; }

        public IWarpService WarpCalls { get; } = A.Fake<IWarpService>();

        public TestServices()
        {
            A.CallTo(() => StatService.Compute(A<ConnectionInfo>._)).Returns(
                new CharacterStatResult(new StatBlock { MaxHp = 5000f, MaxMp = 800f }, new StatBlock()));

            WarpService = new WarpService(A.Fake<INpcSpawnService>(), A.Fake<IMonsterSpawnService>(),
                A.Fake<IFieldPropService>(), A.Fake<ICombatService>());
        }
    }

    /// <summary>
    /// In-memory replacement for the socket-backed connection: it serves a plaintext frame byte by
    /// byte and records everything the receive loop pushes back.
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
