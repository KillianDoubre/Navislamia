using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Navislamia.AuthServer.Accounts;
using Navislamia.AuthServer.GameServers;
using Navislamia.AuthServer.Protocol;
using Navislamia.AuthServer.Protocol.Packets;
using Navislamia.AuthServer.Sessions;
using Navislamia.Game.DataAccess.Entities.Auth;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.LoadTest;
using NUnit.Framework;
using Serilog;

namespace Tests.Tools;

/// <summary>
/// The load test bots speak the client side of the protocol: each frame is checked against the structure or the
/// offsets the server reads it with, so a bot that fails under load fails because of the server, not of itself.
/// </summary>
[TestFixture]
public class LoadTestFramesTests
{
    private static Header HeaderOf(byte[] frame) => new(frame);

    private static void AssertHeader(byte[] frame, ushort id, int length)
    {
        var header = HeaderOf(frame);
        header.ID.Should().Be(id);
        header.Length.Should().Be((uint)length);
        frame.Length.Should().Be(length);
        header.Checksum.Should().Be(header.CalculateChecksum());
    }

    [Test]
    public async Task The_auth_server_accepts_the_bot_account_frame_and_issues_a_key()
    {
        var account = new AccountEntity { Id = 9, Username = "load001" };
        var accounts = A.Fake<IAccountService>();
        A.CallTo(() => accounts.ValidateCredentialsAsync("load001", "load")).Returns(account);
        var registry = new GameServerRegistry();
        registry.Register(new GameServerInfo { Index = 1, Name = "Navislamia", Ip = "127.0.0.1", Port = 4515 });
        var session = new AuthClientSession(accounts, registry, new OneTimeKeyStore(),
            new LoggerConfiguration().CreateLogger());

        var result = await session.DispatchAsync(Frames.AuthAccountFrame("load001", "load"));
        Frames.AuthResultCode(result!).Should().Be(0);

        var list = Frames.ServerList((await session.DispatchAsync(Frames.AuthServerListFrame()))!);
        list.Should().ContainSingle().Which.Should().Be(((ushort)1, "127.0.0.1", 4515));

        var (code, key) = Frames.SelectServer((await session.DispatchAsync(Frames.AuthSelectServerFrame(1)))!);
        code.Should().Be(0);
        key.Should().NotBe(0);
    }

    [Test]
    public void Lobby_frames_have_the_size_of_the_structures_the_server_reads()
    {
        var header = Marshal.SizeOf<Header>();
        AssertHeader(Frames.VersionFrame(), Frames.Version, header + Marshal.SizeOf<TM_CS_VERSION>());
        AssertHeader(Frames.AccountWithAuthFrame("load001", 42), Frames.AccountWithAuth,
            header + Marshal.SizeOf<TM_CS_ACCOUNT_WITH_AUTH>());
        AssertHeader(Frames.CharacterListFrame("load001"), Frames.CharacterList,
            header + Marshal.SizeOf<TS_CS_CHARACTER_LIST>());
        AssertHeader(Frames.LoginFrame("Load001", 4), Frames.Login, header + Marshal.SizeOf<TS_CS_LOGIN>());

        var create = Frames.CreateCharacterFrame(new LobbyCharacterInfo
            { Race = 4, Sex = 2, ModelId = new int[5], Name = "Load001", CreateTime = "", DeleteTime = "" });
        AssertHeader(create, Frames.CreateCharacter, header + Marshal.SizeOf<TS_CS_CREATE_CHARACTER>());

        var login = Frames.LoginFrame("Load001", 4);
        Encoding.ASCII.GetString(login, 7, 7).Should().Be("Load001");
        login[26].Should().Be(4);
    }

    [Test]
    public void World_frames_match_the_offsets_of_the_server_handlers()
    {
        // HandleMoveRequest: 26 + 8 × count, handle @7, x/y @11/15, cur_time @19, count @24, waypoints @26.
        var move = Frames.MoveRequestFrame(77, 10, 20, 1234, 30, 40);
        AssertHeader(move, Frames.MoveRequest, 34);
        BinaryPrimitives.ReadUInt32LittleEndian(move.AsSpan(7)).Should().Be(77);
        BinaryPrimitives.ReadSingleLittleEndian(move.AsSpan(11)).Should().Be(10);
        BinaryPrimitives.ReadUInt32LittleEndian(move.AsSpan(19)).Should().Be(1234);
        BinaryPrimitives.ReadUInt16LittleEndian(move.AsSpan(24)).Should().Be(1);
        BinaryPrimitives.ReadSingleLittleEndian(move.AsSpan(30)).Should().Be(40);

        // HandleChatRequest: count @29 (message + NUL), type @30, message @31.
        var chat = Frames.ChatFrame("loadtest 1 1");
        AssertHeader(chat, Frames.ChatRequest, 31 + 13);
        chat[29].Should().Be(13);
        chat[30].Should().Be(0);
        Encoding.ASCII.GetString(chat, 31, 12).Should().Be("loadtest 1 1");

        AssertHeader(Frames.AttackFrame(1, 2), Frames.AttackRequest, 15);
        AssertHeader(Frames.ResurrectionFrame(1), Frames.Resurrection, 12);
        AssertHeader(Frames.TimeSyncFrame(5), Frames.TimeSync, 11);
        AssertHeader(Frames.CancelActionFrame(1), Frames.CancelAction, 11);
    }

    [Test]
    public void The_character_list_is_read_with_the_server_record_layout()
    {
        var record = new LobbyCharacterInfo
        {
            Race = 5, Sex = 1, ModelId = new int[5], Name = "Load042", CreateTime = "2026/10/06", DeleteTime = "9999/12/01"
        }.StructToByte();
        var fixedPart = Marshal.SizeOf<TS_SC_CHARACTER_LIST>();
        var frame = new byte[7 + fixedPart + record.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), Frames.CharacterListResult);
        new TS_SC_CHARACTER_LIST(0, 0, 1).StructToByte().CopyTo(frame, 7);
        record.CopyTo(frame, 7 + fixedPart);

        Frames.Characters(frame).Should().ContainSingle().Which.Should().Be(("Load042", 5));
    }

    [Test]
    public void Server_frames_are_read_where_the_server_writes_them()
    {
        var attack = Navislamia.Game.Network.Packets.Game.GameAttackPackets.BuildAttackEvent(5, 9, 100, 100, 0, 12, 0, 0, 300, 0);
        Frames.AttackOf(attack).Should().Be((5u, 9u, (int?)0));

        var property = Navislamia.Game.Network.Packets.Game.GameStatPackets.BuildProperty(5, "hp", 0);
        Frames.PropertyOf(property).Should().Be((5u, "hp", 0L));

        var chat = Navislamia.Game.Network.Packets.Game.GameChatPackets.BuildChatLocal(5, 0, "hello");
        Frames.U32(chat, 7).Should().Be(5);
    }
}
