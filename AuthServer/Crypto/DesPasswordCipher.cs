using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Navislamia.AuthServer.Crypto;

public static class DesPasswordCipher
{
    public const string DefaultPassphrase = "MERONG";

    public static string DecryptPassword(ReadOnlySpan<byte> encrypted, string passphrase = DefaultPassphrase)
    {
        var length = encrypted.Length & ~7;
        var plain = Transform(encrypted.Slice(0, length).ToArray(), passphrase, encrypt: false);

        var end = Array.IndexOf(plain, (byte)0);
        if (end < 0)
        {
            end = plain.Length;
        }
        return Encoding.ASCII.GetString(plain, 0, end);
    }

    public static byte[] EncryptPassword(string password, int fieldSize = 61, string passphrase = DefaultPassphrase)
    {
        var field = new byte[fieldSize];
        Encoding.ASCII.GetBytes(password).CopyTo(field, 0);

        var length = fieldSize & ~7;
        var encrypted = Transform(field.AsSpan(0, length).ToArray(), passphrase, encrypt: true);
        encrypted.CopyTo(field, 0);
        return field;
    }

    private static byte[] Transform(byte[] data, string passphrase, bool encrypt)
    {
        var key = DeriveKey(passphrase);
        using var des = DES.Create();
        des.Mode = CipherMode.ECB;
        des.Padding = PaddingMode.None;

        using var transform = encrypt ? des.CreateEncryptor(key, null) : des.CreateDecryptor(key, null);
        return transform.TransformFinalBlock(data, 0, data.Length);
    }

    private static byte[] DeriveKey(string passphrase)
    {
        var key = new byte[8];
        var bytes = Encoding.ASCII.GetBytes(passphrase);
        for (var i = 0; i < bytes.Length && i < 40; i++)
        {
            key[i % 8] ^= bytes[i];
        }

        for (var i = 0; i < 8; i++)
        {
            var b = (byte)(key[i] & 0xFE);
            var ones = BitOperations.PopCount((uint)b);
            key[i] = (byte)(b | (ones % 2 == 1 ? 0 : 1));
        }
        return key;
    }
}
