using System.Buffers.Binary;
using System.Text;
using System.Threading.Tasks;
using Navislamia.AuthServer.Accounts;
using Navislamia.AuthServer.Crypto;
using Navislamia.AuthServer.GameServers;
using Navislamia.AuthServer.Protocol;
using Navislamia.AuthServer.Protocol.Packets;
using Navislamia.Game.DataAccess.Entities.Auth;
using Navislamia.Game.Network.Packets;
using Serilog;

namespace Navislamia.AuthServer.Sessions;

public sealed class AuthClientSession
{
    private readonly IAccountService _accountService;
    private readonly IGameServerRegistry _registry;
    private readonly IOneTimeKeyStore _keyStore;
    private readonly ILogger _logger;

    private readonly AuthCryptoSession _crypto = new();
    private AccountEntity? _account;

    public AuthClientSession(IAccountService accountService, IGameServerRegistry registry,
        IOneTimeKeyStore keyStore, ILogger logger)
    {
        _accountService = accountService;
        _registry = registry;
        _keyStore = keyStore;
        _logger = logger;
    }

    public async Task<byte[]?> DispatchAsync(byte[] packet)
    {
        var id = BitConverter.ToUInt16(packet, 4);

        switch ((AuthClientPackets)id)
        {
            case AuthClientPackets.TS_CA_VERSION:
                return null;

            case AuthClientPackets.TS_CA_RSA_PUBLIC_KEY:
                return HandleRsaPublicKey(packet);

            case AuthClientPackets.TS_CA_ACCOUNT:
                return await HandleAccountAsync(packet);

            case AuthClientPackets.TS_CA_SERVER_LIST:
                return HandleServerList();

            case AuthClientPackets.TS_CA_SELECT_SERVER:
                return HandleSelectServer(packet);

            default:
                _logger.Debug("Auth client: unhandled packet ID {id}", id);
                return null;
        }
    }

    private byte[] HandleRsaPublicKey(byte[] packet)
    {
        var message = TS_CA_RSA_PUBLIC_KEY.Parse(packet.AsSpan(AuthMessage.HeaderSize));
        _crypto.GenerateSessionKey();

        var pem = Encoding.ASCII.GetString(message.Key);
        var encrypted = _crypto.EncryptSessionKeyForClient(pem);

        _logger.Debug("Auth client: RSA public key received, sent AES key ({len} bytes)", encrypted.Length);
        return new TS_AC_AES_KEY_IV(encrypted).ToPacket();
    }

    private async Task<byte[]> HandleAccountAsync(byte[] packet)
    {
        var (accountName, password) = DecodeAccount(packet);

        var result = ResultCode.AccessDenied;
        if (password is not null)
        {
            var account = await _accountService.ValidateCredentialsAsync(accountName, password);
            if (account is not null)
            {
                _account = account;
                result = ResultCode.Success;
            }
        }

        var loginFlag = result == ResultCode.Success ? 1 : 0;

        _logger.Debug("Auth client: account {account} → {result}", accountName, result);
        return AuthMessage.FromStruct((ushort)AuthClientPackets.TS_AC_RESULT,
            new TS_AC_RESULT((ushort)AuthClientPackets.TS_CA_ACCOUNT, (ushort)result, loginFlag));
    }

    private (string account, string? password) DecodeAccount(byte[] packet)
    {
        var payload = packet.AsSpan(AuthMessage.HeaderSize);
        var accountName = ReadFixedAscii(payload.Slice(0, 61));
        try
        {
            var password = DesPasswordCipher.DecryptPassword(payload.Slice(61, 61));
            return (accountName, password);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Auth client: account decryption failed for {account}", accountName);
            return (accountName, null);
        }
    }

    private byte[] HandleServerList()
    {
        var list = new TS_AC_SERVER_LIST
        {
            LastLoginServerIdx = (ushort)(_account?.LastServerIdx ?? 0)
        };

        foreach (var server in _registry.GetAll())
        {
            list.Servers.Add(new TS_SERVER_INFO
            {
                ServerIdx = server.Index,
                ServerName = server.Name,
                IsAdultServer = server.IsAdultServer,
                ServerScreenshotUrl = server.ScreenshotUrl,
                ServerIp = server.Ip,
                ServerPort = server.Port,
                UserRatio = server.UserRatio
            });
        }

        _logger.Debug("Auth client: sent server list ({count} servers)", list.Servers.Count);
        return list.ToPacket();
    }

    private byte[] HandleSelectServer(byte[] packet)
    {
        var serverIdx = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(AuthMessage.HeaderSize, 2));

        if (_account is null)
        {
            return AuthMessage.FromStruct((ushort)AuthClientPackets.TS_AC_SELECT_SERVER,
                new TS_AC_SELECT_SERVER((ushort)ResultCode.AccessDenied, 0, 0));
        }

        var oneTimeKey = _keyStore.Issue(_account.Username, _account.Id, (int)serverIdx);

        _logger.Debug("Auth client: {account} selected server {idx}, issued one-time key", _account.Username, serverIdx);
        return AuthMessage.FromStruct((ushort)AuthClientPackets.TS_AC_SELECT_SERVER,
            new TS_AC_SELECT_SERVER((ushort)ResultCode.Success, oneTimeKey, 0));
    }

    private static string ReadFixedAscii(ReadOnlySpan<byte> span)
    {
        var end = span.IndexOf((byte)0);
        if (end < 0)
        {
            end = span.Length;
        }
        return Encoding.ASCII.GetString(span.Slice(0, end));
    }
}
