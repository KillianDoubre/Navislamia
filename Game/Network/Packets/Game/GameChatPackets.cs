using System;
using System.Buffers.Binary;
using System.Text;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

public static class GameChatPackets
{
    private const int HeaderSize = 7;
    private const int SenderSize = 21;

    public static byte[] BuildChatLocal(uint handle, byte type, string message)
    {
        var msgBytes = Encoding.ASCII.GetBytes(message ?? string.Empty);
        var count = msgBytes.Length + 1;
        var total = HeaderSize + 4 + 1 + 1 + count;
        var p = new byte[total];
        var s = p.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(0, 4), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(4, 2), (ushort)GamePackets.TM_SC_CHAT_LOCAL);
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(7, 4), handle);
        p[11] = (byte)count;
        p[12] = type;
        msgBytes.CopyTo(s.Slice(13));

        WriteChecksum(p);
        return p;
    }

    public static byte[] BuildChat(string sender, byte type, string message)
    {
        var msgBytes = Encoding.ASCII.GetBytes(message ?? string.Empty);
        var count = msgBytes.Length + 1;
        var total = HeaderSize + SenderSize + 2 + 1 + count;
        var p = new byte[total];
        var s = p.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(0, 4), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(4, 2), (ushort)GamePackets.TM_SC_CHAT);

        var senderBytes = Encoding.ASCII.GetBytes(sender ?? string.Empty);
        var senderLen = Math.Min(senderBytes.Length, SenderSize - 1);
        senderBytes.AsSpan(0, senderLen).CopyTo(s.Slice(7, senderLen));

        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(28, 2), (ushort)count);
        p[30] = type;
        msgBytes.CopyTo(s.Slice(31));

        WriteChecksum(p);
        return p;
    }

    private static void WriteChecksum(byte[] p)
    {
        byte c = 0;
        for (var i = 0; i < 6; i++) c += p[i];
        p[6] = c;
    }
}
