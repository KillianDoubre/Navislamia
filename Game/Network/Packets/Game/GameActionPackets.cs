using System;
using System.Buffers.Binary;
using Navislamia.Game.Network.Packets.Enums;

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
    /// One <c>TS_REWARD_INFO</c> record of <c>TM_CS_DONATE_REWARD</c> (259): a signed reward slot followed
    /// by its unsigned quantity. The record is exactly three bytes — rzu declares `int8_t` then `uint16_t`
    /// with no padding, and the 7.3 client advances its write pointer by three per record (§3).
    /// </summary>
    public readonly record struct DonateRewardEntry(sbyte RewardType, ushort Count);

    /// <summary>
    /// <c>TM_CS_DONATE_REWARD</c> (259), the Epic 7.3 form: a signed record count at offset 7, then that
    /// many three-byte records, so the whole frame is 8 + 3N bytes (8, 11, 14, 17 or 20 for N in 0..4).
    /// The envelope is judged before any byte of a record is read: the count must agree with the announced
    /// length (the client derives both from the same N), a 7.3 client only ever fills the four slots it
    /// declares, each slot once, and never emits a zero quantity (§3, §5.4). The empty frame (N = 0) is a
    /// legitimate selection and is accepted. Refusing on <c>false</c>; nothing is invented past offset 7.
    /// </summary>
    public static bool TryReadDonateReward(ReadOnlySpan<byte> packet, out DonateRewardEntry[] rewards)
    {
        const int recordSize = 3;
        const int minPacketLength = HeaderSize + 1;
        const int declaredRewardSlots = 4;

        rewards = null;

        if (packet.Length < minPacketLength)
        {
            return false;
        }

        var count = (sbyte)packet[HeaderSize];
        if (count < 0 || count > declaredRewardSlots)
        {
            return false;
        }

        if (packet.Length != minPacketLength + count * recordSize)
        {
            return false;
        }

        var records = new DonateRewardEntry[count];
        var seenSlots = 0;

        for (var i = 0; i < count; i++)
        {
            var record = packet.Slice(minPacketLength + i * recordSize, recordSize);
            var rewardType = (sbyte)record[0];

            if (rewardType < 0 || rewardType >= declaredRewardSlots)
            {
                return false;
            }

            if ((seenSlots & (1 << rewardType)) != 0)
            {
                return false;
            }

            seenSlots |= 1 << rewardType;

            var recordCount = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(1, 2));
            if (recordCount == 0)
            {
                return false;
            }

            records[i] = new DonateRewardEntry(rewardType, recordCount);
        }

        rewards = records;
        return true;
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

    /// <summary>
    /// The fixed Epic 7.3 <c>TM_CS_RESURRECTION</c> frame: the 7-byte header, the 4-byte
    /// <c>handle</c> at offset 7 and the 1-byte <c>type</c> at offset 11, with no padding.
    /// </summary>
    public const int ResurrectionPacketLength = HeaderSize + 5;

    public readonly record struct ResurrectionRequest(uint Handle, ResurrectionType Type);

    /// <summary>
    /// Reads <c>TM_CS_RESURRECTION</c> (513). The length must match exactly rather than merely be
    /// sufficient: the packet is fixed at 12 bytes in Epic 7.3, and the pre-6.1 shape carries a second
    /// boolean (13 bytes) whose extra byte would misalign every packet that follows in the stream.
    /// </summary>
    public static bool TryReadResurrection(ReadOnlySpan<byte> packet, out ResurrectionRequest request)
    {
        if (packet.Length != ResurrectionPacketLength)
        {
            request = default;
            return false;
        }

        request = new ResurrectionRequest(
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4)),
            (ResurrectionType)(sbyte)packet[HeaderSize + 4]);
        return true;
    }
}
