using System.Buffers.Binary;

namespace Navislamia.AuthServer.Protocol.Packets;

public sealed class TS_AC_AES_KEY_IV
{
    private readonly byte[] _rsaEncryptedData;

    public TS_AC_AES_KEY_IV(byte[] rsaEncryptedData) => _rsaEncryptedData = rsaEncryptedData;

    public byte[] ToPacket()
    {
        var payload = new byte[4 + _rsaEncryptedData.Length];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), _rsaEncryptedData.Length);
        _rsaEncryptedData.CopyTo(payload.AsSpan(4));
        return AuthMessage.Build((ushort)AuthClientPackets.TS_AC_AES_KEY_IV, payload);
    }
}
