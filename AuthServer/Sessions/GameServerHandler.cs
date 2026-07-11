using System.Buffers.Binary;
using System.Threading.Tasks;
using Navislamia.AuthServer.GameServers;
using Navislamia.AuthServer.Protocol;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Auth;
using Navislamia.Game.Network.Packets.Enums;
using Serilog;

namespace Navislamia.AuthServer.Sessions;

public sealed class GameServerHandler
{
    private readonly IGameServerRegistry _registry;
    private readonly IOneTimeKeyStore _keyStore;
    private readonly ILogger _logger;

    public GameServerHandler(IGameServerRegistry registry, IOneTimeKeyStore keyStore, ILogger logger)
    {
        _registry = registry;
        _keyStore = keyStore;
        _logger = logger;
    }

    public Task<byte[]?> DispatchAsync(byte[] packet)
    {
        var id = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));

        byte[]? response = (AuthPackets)id switch
        {
            AuthPackets.TS_GA_LOGIN => HandleServerLogin(packet),
            AuthPackets.TS_GA_CLIENT_LOGIN => HandleClientLogin(packet),
            _ => null
        };

        return Task.FromResult(response);
    }

    private byte[] HandleServerLogin(byte[] packet)
    {
        var msg = AuthMessage.ToStruct<TS_GA_LOGIN>(packet);

        _registry.Register(new GameServerInfo
        {
            Index = msg.ServerIndex,
            Name = msg.ServerName,
            IsAdultServer = msg.IsAdultServer != 0,
            ScreenshotUrl = msg.ServerScreenshotURL,
            Ip = msg.ServerIP,
            Port = msg.ServerPort
        });

        _logger.Information("Game server registered: [{idx}] {name} @ {ip}:{port}",
            msg.ServerIndex, msg.ServerName, msg.ServerIP, msg.ServerPort);

        return AuthMessage.FromStruct((ushort)AuthPackets.TS_AG_LOGIN_RESULT,
            new TS_AG_LOGIN_RESULT { Result = (ushort)ResultCode.Success });
    }

    private byte[] HandleClientLogin(byte[] packet)
    {
        var msg = AuthMessage.ToStruct<TS_GA_CLIENT_LOGIN>(packet);

        var accountId = _keyStore.Verify(msg.Account, msg.OneTimeKey);
        var result = accountId is not null ? ResultCode.Success : ResultCode.AccessDenied;

        _logger.Debug("Game server: client login verify {account} → {result}", msg.Account, result);

        return AuthMessage.FromStruct((ushort)AuthPackets.TS_AG_CLIENT_LOGIN,
            new TS_AG_CLIENT_LOGIN
            {
                Account = msg.Account,
                AccountID = accountId ?? 0,
                Result = (ushort)result
            });
    }
}
