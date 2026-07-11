using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Navislamia.Game.Network.Packets;

namespace Navislamia.AuthServer.Protocol;

public static class AuthMessage
{
    public const int HeaderSize = 7;

    public static byte[] Build(ushort id, ReadOnlySpan<byte> payload)
    {
        var total = HeaderSize + payload.Length;
        var buffer = new byte[total];

        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4, 2), id);

        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += buffer[i];
        }
        buffer[6] = checksum;

        payload.CopyTo(buffer.AsSpan(HeaderSize));
        return buffer;
    }

    public static byte[] FromStruct<T>(ushort id, T value) => Build(id, value.StructToByte());

    public static ReadOnlySpan<byte> Payload(ReadOnlySpan<byte> packet) => packet.Slice(HeaderSize);

    public static T ToStruct<T>(ReadOnlySpan<byte> packet) where T : struct
    {
        var payload = packet.Slice(HeaderSize, Marshal.SizeOf<T>()).ToArray();
        var handle = GCHandle.Alloc(payload, GCHandleType.Pinned);
        try
        {
            return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }
}
