using FluentAssertions;
using Navislamia.AuthServer.Crypto;
using NUnit.Framework;

namespace Tests.AuthServer;

[TestFixture]
public class DesPasswordCipherTests
{
    [Test]
    public void Decrypts_real_client_password_block_to_plaintext()
    {
        var encrypted = new byte[]
        {
            0x66, 0xDD, 0xE3, 0x3B, 0x17, 0xC1, 0x58, 0x3F,
            0x93, 0x5C, 0x38, 0x05, 0xC7, 0xE6, 0xD1, 0xFC,
            0x93, 0x5C, 0x38, 0x05, 0xC7, 0xE6, 0xD1, 0xFC,
        };

        DesPasswordCipher.DecryptPassword(encrypted).Should().Be("test");
    }

    [Test]
    public void Encrypt_then_decrypt_roundtrips()
    {
        var field = DesPasswordCipher.EncryptPassword("hunter2");

        DesPasswordCipher.DecryptPassword(field).Should().Be("hunter2");
    }
}
