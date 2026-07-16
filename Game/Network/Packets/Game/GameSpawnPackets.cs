using System;
using System.Buffers.Binary;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

public static class GameSpawnPackets
{
    private const int HeaderSize = 7;
    private const int EncodedIdOffset = 64;
    private const byte EnterTypeCreature = 1;
    private const byte EnterTypeStaticObject = 2;
    private const byte ObjectTypeNpc = 1;
    private const byte ObjectTypeItem = 2;
    private const byte ObjectTypeMonster = 3;

    public static byte[] BuildEnterNpc(uint handle, float x, float y, float z, byte layer,
        int hp, int level, byte race, int npcId)
    {
        const int length = HeaderSize + 1 + 4 + 12 + 1 + 1 + 38 + 8;
        var packet = BuildEnterCreature(length, handle, x, y, z, layer, hp, level, race, ObjectTypeNpc, 0f);

        WriteEncodedInt(packet.AsSpan(EncodedIdOffset, 8), (uint)npcId);
        WriteChecksum(packet);

        return packet;
    }

    public static byte[] BuildEnterMonster(uint handle, float x, float y, float z, byte layer,
        int hp, int level, byte race, int monsterId, float faceDir)
    {
        const int length = HeaderSize + 1 + 4 + 12 + 1 + 1 + 38 + 8 + 1;
        var packet = BuildEnterCreature(length, handle, x, y, z, layer, hp, level, race,
            ObjectTypeMonster, faceDir);

        WriteEncodedInt(packet.AsSpan(EncodedIdOffset, 8), ScrambledInt.Encode((uint)monsterId));
        packet[72] = 0;
        WriteChecksum(packet);

        return packet;
    }

    public static byte[] BuildEnterItem(uint handle, float x, float y, float z, byte layer,
        int itemCode, long count, uint dropTime, uint ownerHandle)
    {
        const int length = HeaderSize + 1 + 4 + 12 + 1 + 1 + 8 + 8 + 4 + 12 + 12;
        var packet = new byte[length];
        var span = packet.AsSpan();

        WriteHeader(span, length, GamePackets.TM_SC_ENTER);
        packet[7] = EnterTypeStaticObject;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8, 4), handle);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(12, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(16, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(20, 4), z);
        packet[24] = layer;
        packet[25] = ObjectTypeItem;

        WriteEncodedInt(span.Slice(26, 8), (uint)itemCode);
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(34, 8), (ulong)Math.Max(1, count));
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(42, 4), dropTime);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(46, 4), ownerHandle);

        WriteChecksum(packet);
        return packet;
    }

    public static byte[] BuildTakeItemResult(uint itemHandle, uint takerHandle)
    {
        const int length = HeaderSize + 8;
        var packet = new byte[length];
        var span = packet.AsSpan();

        WriteHeader(span, length, GamePackets.TM_SC_TAKE_ITEM_RESULT);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(HeaderSize, 4), itemHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(HeaderSize + 4, 4), takerHandle);

        WriteChecksum(packet);
        return packet;
    }

    public static byte[] BuildLeave(uint handle)
    {
        const int length = HeaderSize + 4;
        var packet = new byte[length];
        var span = packet.AsSpan();

        WriteHeader(span, length, GamePackets.TM_SC_LEAVE);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(HeaderSize, 4), handle);
        WriteChecksum(packet);

        return packet;
    }

    private static byte[] BuildEnterCreature(int length, uint handle, float x, float y, float z,
        byte layer, int hp, int level, byte race, byte objectType, float faceDir)
    {
        var packet = new byte[length];
        var span = packet.AsSpan();

        WriteHeader(span, length, GamePackets.TM_SC_ENTER);
        packet[7] = EnterTypeCreature;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8, 4), handle);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(12, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(16, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(20, 4), z);
        packet[24] = layer;
        packet[25] = objectType;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(26, 4), 0);
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(30, 4), faceDir);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(34, 4), hp);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(38, 4), hp);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(42, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(46, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(50, 4), level);
        packet[54] = race;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(55, 4), 0);
        packet[59] = 0;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(60, 4), 0);

        return packet;
    }

    private static void WriteHeader(Span<byte> packet, int length, GamePackets id)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.Slice(4, 2), (ushort)id);
    }

    private static void WriteEncodedInt(Span<byte> target, uint value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(0, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(2, 2), (ushort)(value >> 16));
        BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(4, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(6, 2), (ushort)value);
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
