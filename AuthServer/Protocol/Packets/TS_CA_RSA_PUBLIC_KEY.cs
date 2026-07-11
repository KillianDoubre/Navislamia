using System.Buffers.Binary;

namespace Navislamia.AuthServer.Protocol.Packets;

public sealed class TS_CA_RSA_PUBLIC_KEY
{
    public int KeySize { get; init; }
    public byte[] Key { get; init; } = Array.Empty<byte>();

    public static TS_CA_RSA_PUBLIC_KEY Parse(ReadOnlySpan<byte> payload)
    {
        var keySize = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0, 4));
        var key = payload.Slice(4, keySize).ToArray();
        return new TS_CA_RSA_PUBLIC_KEY { KeySize = keySize, Key = key };
    }
}
