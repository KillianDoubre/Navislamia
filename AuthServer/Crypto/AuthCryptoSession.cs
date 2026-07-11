using System.Security.Cryptography;
using System.Text;

namespace Navislamia.AuthServer.Crypto;

public sealed class AuthCryptoSession
{
    private byte[]? _sessionKey;

    public byte[] GenerateSessionKey()
    {
        _sessionKey = RandomNumberGenerator.GetBytes(32);
        return _sessionKey;
    }

    public byte[] EncryptSessionKeyForClient(ReadOnlySpan<char> clientRsaPublicKeyPem)
    {
        EnsureKey();
        using var rsa = RSA.Create();
        rsa.ImportFromPem(clientRsaPublicKeyPem);
        return rsa.Encrypt(_sessionKey!, RSAEncryptionPadding.Pkcs1);
    }

    public string DecryptPassword(ReadOnlySpan<byte> ciphertext)
    {
        EnsureKey();
        using var aes = Aes.Create();
        aes.Key = _sessionKey!.AsSpan(0, 16).ToArray();
        var plain = aes.DecryptCbc(ciphertext, _sessionKey!.AsSpan(16, 16), PaddingMode.PKCS7);
        return Encoding.ASCII.GetString(plain).TrimEnd('\0');
    }

    private void EnsureKey()
    {
        if (_sessionKey is null)
        {
            throw new InvalidOperationException("GenerateSessionKey() must be called before crypto operations.");
        }
    }
}
