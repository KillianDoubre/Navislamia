using System;
using System.Buffers.Binary;

namespace Navislamia.Game.Network.Packets.Game;

public static class GameActionPackets
{
    private const int HeaderSize = 7;

    public readonly record struct LearnSkillRequest(uint Handle, int SkillId, byte TargetLevel);

    public readonly record struct SkillRequest(ushort SkillId, uint Caster, uint Target, float X, float Y,
        float Z, sbyte Layer, byte SkillLevel);

    public readonly record struct PutoffItemRequest(sbyte Position, uint TargetHandle);

    public readonly record struct PutonItemRequest(sbyte Position, uint ItemHandle, uint TargetHandle);

    public readonly record struct ChangeItemPositionRequest(bool IsStorage, uint ItemHandle1, uint ItemHandle2);

    public static uint ReadTargetHandle(ReadOnlySpan<byte> packet)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4));
    }

    public static uint ReadCancelActionHandle(ReadOnlySpan<byte> packet)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4));
    }

    public static bool TryReadArrangeItem(ReadOnlySpan<byte> packet, out bool isStorage)
    {
        const int packetLength = HeaderSize + 1;
        if (packet.Length < packetLength)
        {
            isStorage = false;
            return false;
        }

        isStorage = packet[HeaderSize] != 0;
        return true;
    }

    public readonly record struct EraseItemRequest(uint ItemHandle, long Count);

    public static bool TryReadEraseItem(ReadOnlySpan<byte> packet, out EraseItemRequest[] requests)
    {
        const int recordSize = 12;
        requests = null;

        if (packet.Length < HeaderSize + 1)
        {
            return false;
        }

        var count = (sbyte)packet[HeaderSize];
        if (count <= 0 || packet.Length < HeaderSize + 1 + count * recordSize)
        {
            return false;
        }

        requests = new EraseItemRequest[count];
        for (var i = 0; i < count; i++)
        {
            var record = packet.Slice(HeaderSize + 1 + i * recordSize, recordSize);
            requests[i] = new EraseItemRequest(
                BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(0, 4)),
                BinaryPrimitives.ReadInt64LittleEndian(record.Slice(4, 8)));
        }

        return true;
    }

    public static bool TryReadTakeItem(ReadOnlySpan<byte> packet, out uint itemHandle)
    {
        const int packetLength = HeaderSize + 8;
        if (packet.Length < packetLength)
        {
            itemHandle = 0;
            return false;
        }

        itemHandle = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize + 4, 4));
        return true;
    }

    public static bool TryReadChangeItemPosition(ReadOnlySpan<byte> packet, out ChangeItemPositionRequest request)
    {
        const int packetLength = HeaderSize + 9;
        if (packet.Length < packetLength)
        {
            request = default;
            return false;
        }

        request = new ChangeItemPositionRequest(
            packet[HeaderSize] != 0,
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize + 1, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize + 5, 4)));
        return true;
    }

    public static bool TryReadSkill(ReadOnlySpan<byte> packet, out SkillRequest request)
    {
        const int packetLength = HeaderSize + 24;
        if (packet.Length < packetLength)
        {
            request = default;
            return false;
        }

        request = new SkillRequest(
            BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(HeaderSize, 2)),
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize + 2, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize + 6, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(packet.Slice(HeaderSize + 10, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(packet.Slice(HeaderSize + 14, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(packet.Slice(HeaderSize + 18, 4)),
            (sbyte)packet[HeaderSize + 22],
            packet[HeaderSize + 23]);
        return true;
    }

    public static bool TryReadPutonItem(ReadOnlySpan<byte> packet, out PutonItemRequest request)
    {
        const int packetLength = HeaderSize + 9;
        if (packet.Length < packetLength)
        {
            request = default;
            return false;
        }

        request = new PutonItemRequest(
            (sbyte)packet[HeaderSize],
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize + 1, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize + 5, 4)));
        return true;
    }

    public static bool TryReadPutoffItem(ReadOnlySpan<byte> packet, out PutoffItemRequest request)
    {
        const int packetLength = HeaderSize + 5;
        if (packet.Length < packetLength)
        {
            request = default;
            return false;
        }

        request = new PutoffItemRequest(
            (sbyte)packet[HeaderSize],
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize + 1, 4)));
        return true;
    }

    public static bool TryReadLearnSkill(ReadOnlySpan<byte> packet, out LearnSkillRequest request)
    {
        const int packetLength = HeaderSize + 10;
        if (packet.Length < packetLength)
        {
            request = default;
            return false;
        }

        request = new LearnSkillRequest(
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(HeaderSize + 4, 4)),
            packet[HeaderSize + 8]);
        return packet[HeaderSize + 9] == 0;
    }
}
