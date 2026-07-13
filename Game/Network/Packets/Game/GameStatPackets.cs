using System;
using System.Buffers.Binary;
using System.Text;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;

namespace Navislamia.Game.Network.Packets.Game;

public static class GameStatPackets
{
    private const int HeaderSize = 7;

    public static byte[] BuildStatInfo(uint handle, CharacterStats stats)
    {
        const int baseCount = 8;
        const int attribCount = 34;
        const int payload = 4 + baseCount * 2 + attribCount * 2 + 1;
        var total = HeaderSize + payload;
        var p = new byte[total];
        var s = p.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(0, 4), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(4, 2), (ushort)GamePackets.TM_SC_STAT_INFO);
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(7, 4), handle);

        var baseStats = new short[]
        {
            (short)stats.StatId,
            stats.Strength, stats.Vitality, stats.Dexterity, stats.Agility,
            stats.Intelligence, stats.Mentality, stats.Luck
        };

        var o = 11;
        foreach (var v in baseStats)
        {
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(o, 2), v);
            o += 2;
        }

        for (var i = 0; i < attribCount; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(o, 2), 0);
            o += 2;
        }

        p[o] = 0;

        WriteChecksum(p);
        return p;
    }

    public static byte[] BuildProperty(uint handle, string name, long value)
    {
        const int nameSize = 16;
        const int payload = 4 + 1 + nameSize + 8 + 1;
        var total = HeaderSize + payload;
        var p = new byte[total];
        var s = p.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(0, 4), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(4, 2), (ushort)GamePackets.TM_SC_PROPERTY);
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(7, 4), handle);

        p[11] = 1;

        var nameBytes = Encoding.ASCII.GetBytes(name);
        var len = Math.Min(nameBytes.Length, nameSize - 1);
        nameBytes.AsSpan(0, len).CopyTo(s.Slice(12, len));

        BinaryPrimitives.WriteInt64LittleEndian(s.Slice(28, 8), value);
        p[36] = 0;

        WriteChecksum(p);
        return p;
    }

    public static byte[] BuildStringProperty(uint handle, string name, string value)
    {
        const int nameSize = 16;
        const int stringOffset = HeaderSize + 4 + 1 + nameSize + 8;
        var valueBytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
        var packet = new byte[stringOffset + valueBytes.Length + 1];
        var span = packet.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4, 2), (ushort)GamePackets.TM_SC_PROPERTY);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(HeaderSize, 4), handle);

        var nameBytes = Encoding.ASCII.GetBytes(name);
        nameBytes.AsSpan(0, Math.Min(nameBytes.Length, nameSize - 1)).CopyTo(span.Slice(12, nameSize));
        valueBytes.CopyTo(span.Slice(stringOffset, valueBytes.Length));

        WriteChecksum(packet);
        return packet;
    }

    public static bool TryReadSetProperty(ReadOnlySpan<byte> packet, out string name, out string value)
    {
        const int nameOffset = HeaderSize;
        const int nameSize = 16;
        const int valueOffset = nameOffset + nameSize;
        name = string.Empty;
        value = string.Empty;

        if (packet.Length < valueOffset + 1)
        {
            return false;
        }

        name = ReadNullTerminatedAscii(packet.Slice(nameOffset, nameSize));
        value = ReadNullTerminatedAscii(packet.Slice(valueOffset));
        return name.Length > 0;
    }

    private static string ReadNullTerminatedAscii(ReadOnlySpan<byte> value)
    {
        var terminator = value.IndexOf((byte)0);
        return Encoding.ASCII.GetString(terminator >= 0 ? value.Slice(0, terminator) : value);
    }

    private static void WriteChecksum(byte[] p)
    {
        byte c = 0;
        for (var i = 0; i < 6; i++) c += p[i];
        p[6] = c;
    }
}
