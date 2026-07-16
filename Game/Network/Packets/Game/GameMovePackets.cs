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
        var packet = CreateMove(handle, startTime, layer, speed, 1);
        var payload = packet.AsSpan();

        BinaryPrimitives.WriteSingleLittleEndian(payload.Slice(19, 4), tx);
        BinaryPrimitives.WriteSingleLittleEndian(payload.Slice(23, 4), ty);

        WriteChecksum(packet);
        return packet;
    }

    public static byte[] BuildStopMove(uint handle, uint startTime, byte layer)
    {
        var packet = CreateMove(handle, startTime, layer, 0, 0);
        WriteChecksum(packet);
        return packet;
    }

    private static byte[] CreateMove(uint handle, uint startTime, byte layer, byte speed, ushort waypoints)
    {
        var total = HeaderSize + MoveHeaderSize + waypoints * WaypointSize;
        var packet = new byte[total];
        var payload = packet.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(4, 2), (ushort)GamePackets.TM_SC_MOVE);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(7, 4), startTime);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(11, 4), handle);
        payload[15] = layer;
        payload[16] = speed;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(17, 2), waypoints);

        return packet;
    }

    private static void WriteChecksum(byte[] packet)
    {
        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        packet[6] = checksum;
    }
}
