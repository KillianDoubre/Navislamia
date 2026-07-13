using System;
using System.Buffers.Binary;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

public static class GameMovePackets
{
    private const int HeaderSize = 7;
    private const int MoveHeaderSize = 12;
    private const int WaypointSize = 8;

    public static byte[] BuildMove(uint handle, uint startTime, byte layer, byte speed, float tx, float ty)
    {
        var total = HeaderSize + MoveHeaderSize + WaypointSize;
        var packet = new byte[total];
        var p = packet.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(0, 4), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(4, 2), (ushort)GamePackets.TM_SC_MOVE);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(7, 4), startTime);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(11, 4), handle);
        p[15] = layer;
        p[16] = speed;
        BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(17, 2), 1);
        BinaryPrimitives.WriteSingleLittleEndian(p.Slice(19, 4), tx);
        BinaryPrimitives.WriteSingleLittleEndian(p.Slice(23, 4), ty);

        byte checksum = 0;
        for (var i = 0; i < 6; i++) checksum += packet[i];
        packet[6] = checksum;

        return packet;
    }
}
