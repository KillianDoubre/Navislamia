using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Navislamia.AuthServer.Crypto;
using NUnit.Framework;

namespace Tests.AuthServer;

[TestFixture]
public class AuthCryptoSessionTests
{
    [Test]
    public void DecryptPassword_roundtrips_password_encrypted_by_client()
    {
        var session = new AuthCryptoSession();
        var key = session.GenerateSessionKey();

        using var aes = Aes.Create();
        aes.Key = key[..16];
        var cipher = aes.EncryptCbc(Encoding.ASCII.GetBytes("s3cr3t!"), key.AsSpan(16, 16), PaddingMode.PKCS7);

        session.DecryptPassword(cipher).Should().Be("s3cr3t!");
    }

    [Test]
    public void EncryptSessionKeyForClient_can_be_decrypted_by_client_private_key()
    {
        using var clientRsa = RSA.Create(2048);
        var publicKeyPem = clientRsa.ExportSubjectPublicKeyInfoPem();

        var session = new AuthCryptoSession();
        var key = session.GenerateSessionKey();

        var encrypted = session.EncryptSessionKeyForClient(publicKeyPem);
        var decrypted = clientRsa.Decrypt(encrypted, RSAEncryptionPadding.Pkcs1);

        decrypted.Should().Equal(key);
    }

    [Test]
    public void DecryptPassword_before_key_generation_throws()
    {
        var session = new AuthCryptoSession();

        var act = () => session.DecryptPassword(new byte[16]);

        act.Should().Throw<InvalidOperationException>();
    }
}
