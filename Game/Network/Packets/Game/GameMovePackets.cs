using System;
using System.Buffers.Binary;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;

namespace Navislamia.Game.Network.Packets.Game;

public static class GameMovePackets
{
    private const int HeaderSize = 7;
    private const int MoveHeaderSize = 12;
    private const int WaypointSize = 8;

    /// <summary>
    /// Index of the region holding <paramref name="position"/>, as the client 7.3 computes it for its own
    /// visibility window: the position is divided by the divisor it was told at login
    /// (<see cref="WorldVisibility.RegionSize"/>, 180) and truncated toward zero. <c>WorldOption.RegionSize</c>
    /// (150) is not the announced value and would put the client in a region its own 7 x 7 window does not
    /// cover, so it must never be substituted here.
    /// </summary>
    public static int GetRegionIndex(float position) => (int)(position / WorldVisibility.RegionSize);

    /// <summary>
    /// TM_SC_REGION_ACK (11) answers TM_CS_GET_REGION_INFO (550): the region indices the asking client
    /// computed for itself, echoed back so it can build its visibility window. No handle travels in this
    /// packet, and it is an answer to one client, never a broadcast.
    /// </summary>
    public static byte[] BuildRegionAck(int rx, int ry)
    {
        var packet = new byte[HeaderSize + 8];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_SC_REGION_ACK);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(HeaderSize, 4), rx);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(HeaderSize + 4, 4), ry);
        WriteChecksum(packet);
        return packet;
    }

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
