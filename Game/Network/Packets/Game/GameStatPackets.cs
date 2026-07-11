using System;
using System.Buffers.Binary;
using System.Text;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;

namespace Navislamia.Game.Network.Packets.Game;

public static class GameStatPackets
{
    private const int HeaderSize = 7;

    // TS_SC_STAT_INFO (id 1000, EPIC 7.3): handle + 8 x int16 base + 34 x int16 attrib + type byte.
    public static byte[] BuildStatInfo(uint handle, CharacterStats stats)
    {
        const int baseCount = 8;
        const int attribCount = 34;
        const int payload = 4 + baseCount * 2 + attribCount * 2 + 1; // 89
        var total = HeaderSize + payload; // 96
        var p = new byte[total];
        var s = p.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(0, 4), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(4, 2), (ushort)GamePackets.TM_SC_STAT_INFO);
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(7, 4), handle);

        // TS_STAT_INFO_BASE
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

        // TS_STAT_INFO_ATTRIB: 34 x int16 combat stats. Left 0 for V1 (see plan note).
        for (var i = 0; i < attribCount; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(o, 2), 0);
            o += 2;
        }

        p[o] = 0; // type = SIT_Total

        WriteChecksum(p);
        return p;
    }

    // TS_SC_PROPERTY (id 507, EPIC 7.3): handle + is_number(bool) + name[16] + int64 value + null string_value.
    public static byte[] BuildProperty(uint handle, string name, long value)
    {
        const int nameSize = 16;
        const int payload = 4 + 1 + nameSize + 8 + 1; // 30
        var total = HeaderSize + payload; // 37
        var p = new byte[total];
        var s = p.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(0, 4), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(4, 2), (ushort)GamePackets.TM_SC_PROPERTY);
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(7, 4), handle);

        p[11] = 1; // is_number

        var nameBytes = Encoding.ASCII.GetBytes(name);
        var len = Math.Min(nameBytes.Length, nameSize - 1);
        nameBytes.AsSpan(0, len).CopyTo(s.Slice(12, len)); // remaining bytes stay 0 (null-padded)

        BinaryPrimitives.WriteInt64LittleEndian(s.Slice(28, 8), value);
        p[36] = 0; // string_value: empty, null-terminated

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
