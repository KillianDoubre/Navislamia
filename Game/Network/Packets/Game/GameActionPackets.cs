using System;
using System.Buffers.Binary;

namespace Navislamia.Game.Network.Packets.Game;

public static class GameActionPackets
{
    private const int HeaderSize = 7;

    public static uint ReadTargetHandle(ReadOnlySpan<byte> packet)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4));
    }

    public static uint ReadCancelActionHandle(ReadOnlySpan<byte> packet)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4));
    }
}
