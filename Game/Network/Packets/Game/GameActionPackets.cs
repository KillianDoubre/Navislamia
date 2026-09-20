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

    public readonly record struct UseItemRequest(uint ItemHandle, uint TargetHandle);

    public readonly record struct ChangeItemPositionRequest(bool IsStorage, uint ItemHandle1, uint ItemHandle2);

    public readonly record struct RegionInfoRequest(float X, float Y);

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

    /// <summary>
    /// <c>TS_CS_STORAGE</c> (212), the Epic 7.3 form: an item handle, a mode and a signed unit count
    /// (librzu/src/packets/GameClient/TS_CS_STORAGE.h:8-12 — the <c>int64_t</c> form holds from
    /// <c>EPIC_4_1_1</c> on, the <c>uint32_t</c> one only below it). The handle is meaningless for the
    /// close mode and carries the moved stack for the others.
    /// </summary>
    public readonly record struct StorageRequest(uint ItemHandle, byte Mode, long Count);

    public static bool TryReadStorage(ReadOnlySpan<byte> packet, out StorageRequest request)
    {
        const int packetLength = HeaderSize + 13;
        if (packet.Length < packetLength)
        {
            request = default;
            return false;
        }

        request = new StorageRequest(
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4)),
            packet[HeaderSize + 4],
            BinaryPrimitives.ReadInt64LittleEndian(packet.Slice(HeaderSize + 5, 8)));
        return true;
    }

    /// <summary>
    /// <c>TS_CS_DROP_ITEM</c> (203), the Epic 7.3 form: an inventory handle then a signed unit count.
    /// No position is carried — the reference server relocates the dropped item on the character.
    /// </summary>
    public readonly record struct DropItemRequest(uint ItemHandle, int Count);

    public static bool TryReadDropItem(ReadOnlySpan<byte> packet, out DropItemRequest request)
    {
        const int packetLength = HeaderSize + 8;
        if (packet.Length < packetLength)
        {
            request = default;
            return false;
        }

        request = new DropItemRequest(
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(HeaderSize + 4, 4)));
        return true;
    }

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

    public static bool TryReadUseItem(ReadOnlySpan<byte> packet, out UseItemRequest request)
    {
        // 7 header + item_handle (4) + target_handle (4) + szParameter (32). The 32 trailing bytes
        // are consumed for their size only: their content is not established (spec §7.3).
        const int packetLength = HeaderSize + 40;
        if (packet.Length < packetLength)
        {
            request = default;
            return false;
        }

        request = new UseItemRequest(
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize + 4, 4)));
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

    /// <summary>
    /// TM_CS_EMOTION (1202) carries one opaque emotion value. The server never interprets it: the
    /// client owns the animation and the local message, and neither rzu nor NGemity validates a
    /// range, so an invented bound would refuse legitimate emotions.
    /// </summary>
    public static bool TryReadEmotion(ReadOnlySpan<byte> packet, out int emotion)
    {
        const int packetLength = HeaderSize + 4;
        if (packet.Length < packetLength)
        {
            emotion = 0;
            return false;
        }

        emotion = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(HeaderSize, 4));
        return true;
    }

    /// <summary>
    /// TM_CS_GET_REGION_INFO (550) carries the current position of the client, as two floats, after the
    /// client converted that very position into its own region indices. Only the exact 15-byte form is
    /// accepted: the specification defines no answer at all for a request of another length, so a
    /// short or padded one is refused rather than partially read.
    /// </summary>
    public static bool TryReadGetRegionInfo(ReadOnlySpan<byte> packet, out RegionInfoRequest request)
    {
        const int packetLength = HeaderSize + 8;
        if (packet.Length != packetLength)
        {
            request = default;
            return false;
        }

        request = new RegionInfoRequest(
            BinaryPrimitives.ReadSingleLittleEndian(packet.Slice(HeaderSize, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(packet.Slice(HeaderSize + 4, 4)));
        return true;
    }
}
