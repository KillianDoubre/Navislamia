using System.Buffers.Binary;
using System.Text;

namespace Navislamia.AuthServer.Protocol.Packets;

public sealed class TS_AC_SERVER_LIST
{
    public const int ServerInfoSize = 302;

    public ushort LastLoginServerIdx { get; set; }
    public List<TS_SERVER_INFO> Servers { get; } = new();

    public byte[] ToPacket()
    {
        var payload = new byte[2 + 2 + Servers.Count * ServerInfoSize];
        var span = payload.AsSpan();

        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0, 2), LastLoginServerIdx);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2, 2), (ushort)Servers.Count);

        var offset = 4;
        foreach (var server in Servers)
        {
            var entry = span.Slice(offset, ServerInfoSize);
            BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(0, 2), server.ServerIdx);
            WriteFixedAscii(entry.Slice(2, 21), server.ServerName);
            entry[23] = (byte)(server.IsAdultServer ? 1 : 0);
            WriteFixedAscii(entry.Slice(24, 256), server.ServerScreenshotUrl);
            WriteFixedAscii(entry.Slice(280, 16), server.ServerIp);
            BinaryPrimitives.WriteInt32LittleEndian(entry.Slice(296, 4), server.ServerPort);
            BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(300, 2), server.UserRatio);
            offset += ServerInfoSize;
        }

        return AuthMessage.Build((ushort)AuthClientPackets.TS_AC_SERVER_LIST, payload);
    }

    private static void WriteFixedAscii(Span<byte> destination, string value)
    {
        destination.Clear();
        var bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
        var count = Math.Min(bytes.Length, destination.Length - 1);
        bytes.AsSpan(0, count).CopyTo(destination);
    }
}

public sealed class TS_SERVER_INFO
{
    public ushort ServerIdx { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public bool IsAdultServer { get; set; }
    public string ServerScreenshotUrl { get; set; } = string.Empty;
    public string ServerIp { get; set; } = string.Empty;
    public int ServerPort { get; set; }
    public ushort UserRatio { get; set; }
}
