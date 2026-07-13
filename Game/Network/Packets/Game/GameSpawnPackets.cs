using System;
using System.Buffers.Binary;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

public static class GameSpawnPackets
{
    private const int HeaderSize = 7;
    private const byte EnterTypeNpc = 1;
    private const byte ObjTypeNpc = 1;

    public static byte[] BuildEnterNpc(uint handle, float x, float y, float z, byte layer,
        int hp, int level, byte race, int npcId)
    {
        const int total = HeaderSize + 1 + 4 + 12 + 1 + 1 + 38 + 8;
        var p = new byte[total];
        var s = p.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(0, 4), total);
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(4, 2), (ushort)GamePackets.TM_SC_ENTER);

        p[7] = EnterTypeNpc;
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(8, 4), handle);
        BinaryPrimitives.WriteSingleLittleEndian(s.Slice(12, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(s.Slice(16, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(s.Slice(20, 4), z);
        p[24] = layer;
        p[25] = ObjTypeNpc;

        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(26, 4), 0);
        BinaryPrimitives.WriteSingleLittleEndian(s.Slice(30, 4), 0f);
        BinaryPrimitives.WriteInt32LittleEndian(s.Slice(34, 4), hp);
        BinaryPrimitives.WriteInt32LittleEndian(s.Slice(38, 4), hp);
        BinaryPrimitives.WriteInt32LittleEndian(s.Slice(42, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(s.Slice(46, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(s.Slice(50, 4), level);
        p[54] = race;
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(55, 4), 0);
        p[59] = 0;
        BinaryPrimitives.WriteInt32LittleEndian(s.Slice(60, 4), 0);

        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(64, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(66, 2), (ushort)((npcId >> 16) & 0xFFFF));
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(68, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(70, 2), (ushort)(npcId & 0xFFFF));

        byte c = 0;
        for (var i = 0; i < 6; i++) c += p[i];
        p[6] = c;

        return p;
    }

    public static byte[] BuildLeave(uint handle)
    {
        const int total = HeaderSize + 4;
        var p = new byte[total];
        var s = p.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(0, 4), total);
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(4, 2), (ushort)GamePackets.TM_SC_LEAVE);
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(7, 4), handle);

        byte c = 0;
        for (var i = 0; i < 6; i++) c += p[i];
        p[6] = c;

        return p;
    }
}
