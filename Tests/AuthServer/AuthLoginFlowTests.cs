using System;
using System.Buffers.Binary;
using System.Text;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Navislamia.AuthServer.Accounts;
using Navislamia.AuthServer.Crypto;
using Navislamia.AuthServer.GameServers;
using Navislamia.AuthServer.Protocol;
using Navislamia.AuthServer.Protocol.Packets;
using Navislamia.AuthServer.Sessions;
using Navislamia.Game.DataAccess.Entities.Auth;
using Navislamia.Game.Network.Packets.Auth;
using Navislamia.Game.Network.Packets.Enums;
using NUnit.Framework;
using Serilog;

namespace Tests.AuthServer;

[TestFixture]
public class AuthLoginFlowTests
{
    private static readonly ILogger Silent = new LoggerConfiguration().CreateLogger();

    private static (AuthClientSession session, IGameServerRegistry registry, IOneTimeKeyStore keyStore)
        NewSession(IAccountService accountService)
    {
        var registry = new GameServerRegistry();
        registry.Register(new GameServerInfo { Index = 1, Name = "Navislamia", Ip = "127.0.0.1", Port = 4515 });
        var keyStore = new OneTimeKeyStore();
        var session = new AuthClientSession(accountService, registry, keyStore, Silent);
        return (session, registry, keyStore);
    }

    private static byte[] BuildAccountPacket(string account, string password)
    {
        var payload = new byte[61 + 61];
        Encoding.ASCII.GetBytes(account).CopyTo(payload.AsSpan(0));
        DesPasswordCipher.EncryptPassword(password).CopyTo(payload.AsSpan(61));
        return AuthMessage.Build((ushort)AuthClientPackets.TS_CA_ACCOUNT, payload);
    }

    private static IAccountService FakeAccountService(AccountEntity account, string validPassword)
    {
        var service = A.Fake<IAccountService>();
        A.CallTo(() => service.ValidateCredentialsAsync(account.Username, validPassword)).Returns(account);
        A.CallTo(() => service.ValidateCredentialsAsync(A<string>._, A<string>.That.Matches(p => p != validPassword)))
            .Returns((AccountEntity?)null);
        return service;
    }

    [Test]
    public async Task Full_handshake_authenticates_and_issues_one_time_key()
    {
        var account = new AccountEntity { Id = 42, Username = "test" };
        var (session, _, keyStore) = NewSession(FakeAccountService(account, "pw"));

        var accountResp = await session.DispatchAsync(BuildAccountPacket("test", "pw"));
        var result = AuthMessage.ToStruct<TS_AC_RESULT>(accountResp!);
        result.Result.Should().Be(0);
        result.LoginFlag.Should().Be(1);

        var listResp = await session.DispatchAsync(
            AuthMessage.Build((ushort)AuthClientPackets.TS_CA_SERVER_LIST, ReadOnlySpan<byte>.Empty));
        BinaryPrimitives.ReadUInt16LittleEndian(AuthMessage.Payload(listResp!).Slice(2, 2)).Should().Be((ushort)1);

        var selPayload = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(selPayload, 1);
        var selResp = await session.DispatchAsync(
            AuthMessage.Build((ushort)AuthClientPackets.TS_CA_SELECT_SERVER, selPayload));
        var select = AuthMessage.ToStruct<TS_AC_SELECT_SERVER>(selResp!);
        select.Result.Should().Be(0);
        select.OneTimeKey.Should().NotBe(0);

        keyStore.Verify("test", select.OneTimeKey).Should().Be(42);
    }

    [Test]
    public async Task Wrong_password_yields_access_denied()
    {
        var account = new AccountEntity { Id = 42, Username = "test" };
        var (session, _, _) = NewSession(FakeAccountService(account, "pw"));

        var resp = await session.DispatchAsync(BuildAccountPacket("test", "WRONG"));

        AuthMessage.ToStruct<TS_AC_RESULT>(resp!).Result.Should().Be(6);
    }

    [Test]
    public async Task Game_server_login_registers_server()
    {
        var registry = new GameServerRegistry();
        var handler = new GameServerHandler(registry, new OneTimeKeyStore(), Silent);
        var packet = AuthMessage.FromStruct((ushort)AuthPackets.TS_GA_LOGIN,
            new TS_GA_LOGIN(7, "TestServer", "about:blank", 0, "10.0.0.1", 4515));

        var response = await handler.DispatchAsync(packet);

        response.Should().NotBeNull();
        registry.Get(7)!.Name.Should().Be("TestServer");
        registry.Get(7)!.Port.Should().Be(4515);
    }

    [Test]
    public async Task Game_server_verifies_and_consumes_one_time_key()
    {
        var registry = new GameServerRegistry();
        var keyStore = new OneTimeKeyStore();
        var handler = new GameServerHandler(registry, keyStore, Silent);
        var key = keyStore.Issue("alice", 42, 1);

        var badResp = await handler.DispatchAsync(AuthMessage.FromStruct(
            (ushort)AuthPackets.TS_GA_CLIENT_LOGIN, new TS_GA_CLIENT_LOGIN("alice", key + 1)));
        AuthMessage.ToStruct<TS_AG_CLIENT_LOGIN>(badResp!).Result.Should().Be(6);

        var okResp = await handler.DispatchAsync(AuthMessage.FromStruct(
            (ushort)AuthPackets.TS_GA_CLIENT_LOGIN, new TS_GA_CLIENT_LOGIN("alice", key)));
        var ok = AuthMessage.ToStruct<TS_AG_CLIENT_LOGIN>(okResp!);
        ok.Result.Should().Be(0);
        ok.AccountID.Should().Be(42);

        var replay = await handler.DispatchAsync(AuthMessage.FromStruct(
            (ushort)AuthPackets.TS_GA_CLIENT_LOGIN, new TS_GA_CLIENT_LOGIN("alice", key)));
        AuthMessage.ToStruct<TS_AG_CLIENT_LOGIN>(replay!).Result.Should().Be(6);
    }
}
