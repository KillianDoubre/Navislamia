using System;
using System.Buffers.Binary;
using System.Text;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>GameMessage.h guild window messages, packed header plus payload, Epic 7.3.</summary>
public static class GameGuildPackets
{
    public static byte[] BuildWindow(GamePackets type)
    {
        if (type is not (GamePackets.TM_SC_SHOW_CREATE_GUILD or GamePackets.TM_SC_OPEN_GUILD_WINDOW
            or GamePackets.TM_SC_SHOW_CREATE_ALLIANCE)) throw new ArgumentOutOfRangeException(nameof(type));
        return Frame((ushort)type, 7);
    }
    public static byte[] BuildUploadWindow(bool banner, int characterId, int accountId, int password, string serverName)
    {
        var frame = Frame((ushort)(banner ? GamePackets.TM_SC_UPDATE_GUILD_BANNER : GamePackets.TM_SC_UPDATE_GUILD_ICON), 51);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(7), characterId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(11), accountId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(15), password);
        var name = Encoding.ASCII.GetBytes(serverName ?? "Unknown");
        name.AsSpan(0, Math.Min(31, name.Length)).CopyTo(frame.AsSpan(19, 32));
        return frame;
    }
    public static byte[] Frame(ushort id, int size)
    {
        var frame = new byte[size]; BinaryPrimitives.WriteInt32LittleEndian(frame, size);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), id);
        for (var i = 0; i < 6; i++) frame[6] += frame[i];
        return frame;
    }
}
