using System;
using System.Buffers.Binary;
using System.Text;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

public static class GameActionPackets
{
    private const int HeaderSize = 7;

    /// <summary>
    /// The maximum number of material slots of <c>TM_CS_MIX</c> (256), i.e. the size of the reference
    /// material table (NGemity <c>MAX_SUB_MATERIAL_COUNT</c>, MixManager.h:24) and of the reference
    /// guard. The 7.3 client itself has not been observed: 9 is the best established bound, not a
    /// measured one (spec §7 NON ÉTABLI 12).
    /// </summary>
    public const int MaxSubItems = 9;

    public readonly record struct LearnSkillRequest(uint Handle, int SkillId, byte TargetLevel);

    public readonly record struct SkillRequest(ushort SkillId, uint Caster, uint Target, float X, float Y,
        float Z, sbyte Layer, byte SkillLevel);

    public readonly record struct PutoffItemRequest(sbyte Position, uint TargetHandle);

    public readonly record struct PutonItemRequest(sbyte Position, uint ItemHandle, uint TargetHandle);

    public readonly record struct UseItemRequest(uint ItemHandle, uint TargetHandle);

    public readonly record struct ChangeItemPositionRequest(bool IsStorage, uint ItemHandle1, uint ItemHandle2);

    public readonly record struct RegionInfoRequest(float X, float Y);

    public readonly record struct TakeoutCommercialItemRequest(uint Uid, ushort Count);

    public readonly record struct RankingTopRecordRequest(sbyte RankingType);

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

    /// <summary>
    /// <c>TS_CS_DROP_QUEST</c> (603), the Epic 7.3 form: a single <b>signed</b> quest code and nothing
    /// else (the client writes <c>length = 0xb</c>). Negative values are representable and must be
    /// refused as such instead of being reinterpreted as a large unsigned code
    /// (<c>docs/packet-specs/socle-quetes.md</c> §3.1).
    /// </summary>
    public readonly record struct DropQuestRequest(int Code);

    public static bool TryReadDropQuest(ReadOnlySpan<byte> packet, out DropQuestRequest request)
    {
        const int packetLength = HeaderSize + 4;
        if (packet.Length < packetLength)
        {
            request = default;
            return false;
        }

        request = new DropQuestRequest(BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(HeaderSize, 4)));
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

    /// <summary>
    /// One slot of <c>TM_CS_MIX</c>: a 4-byte handle and a 2-byte count, in that order (rzu
    /// <c>TS_MIX_INFO</c>, TS_CS_MIX.h:7-11).
    /// </summary>
    public readonly record struct MixItemInfo(uint Handle, ushort Count);

    /// <summary>
    /// <c>TM_CS_MIX</c> (256): the target slot, then the material slots. <paramref name="MainItemHandle"/>
    /// of zero is a legitimate sentinel — the reference server only resolves it when it is not zero
    /// (NGemity WorldSession.cpp:1451-1453). <paramref name="DeclaredSubItemCount"/> is the array length
    /// the emitter wrote on the wire (rzu <c>_(count)(uint16_t, sub_items)</c>); it is consumed and must
    /// agree with the frame length, otherwise the frame is malformed.
    /// </summary>
    public readonly record struct MixRequest(uint MainItemHandle, ushort MainItemCount,
        ushort DeclaredSubItemCount, MixItemInfo[] SubItems);

    /// <summary>
    /// <c>TM_CS_MIX</c> (256), variable: 15 + 6N bytes (7 header, main handle at 7, main count at 11, the
    /// count of material slots at 13, then N 6-byte records from 15). N is bounded by 9, the maximum of
    /// the reference material table (NGemity <c>MAX_SUB_MATERIAL_COUNT</c>, MixManager.h:24) and of the
    /// reference guard. The 8.1+ frame is unchanged: rzu gates no field of this packet.
    /// See docs/packet-specs/socle-artisanat-objets.md §3.1 and §9.2.
    /// </summary>
    public static bool TryReadMix(ReadOnlySpan<byte> packet, out MixRequest request)
    {
        const int fixedPart = HeaderSize + 8;
        const int recordSize = 6;

        request = default;
        if (packet.Length < fixedPart)
        {
            return false;
        }

        var tail = packet.Length - fixedPart;
        if (tail % recordSize != 0)
        {
            return false;
        }

        var count = tail / recordSize;
        if (count > MaxSubItems)
        {
            return false;
        }

        var declaredCount = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(HeaderSize + 6, 2));
        if (declaredCount != count)
        {
            return false;
        }

        var subItems = new MixItemInfo[count];
        for (var i = 0; i < count; i++)
        {
            var record = packet.Slice(fixedPart + i * recordSize, recordSize);
            subItems[i] = new MixItemInfo(
                BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(0, 4)),
                BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(4, 2)));
        }

        request = new MixRequest(
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4)),
            BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(HeaderSize + 4, 2)),
            declaredCount,
            subItems);
        return true;
    }

    /// <summary>
    /// <c>TM_CS_SOULSTONE_CRAFT</c> (260): the item to socket, then its four soul stone slots, empty
    /// slots written as a zero handle (rzu <c>_(array)(ar_handle_t, soulstone_handle, 4)</c>,
    /// TS_CS_SOULSTONE_CRAFT.h:6-7). The frame is always 27 bytes, never truncated to the stones
    /// actually provided.
    /// </summary>
    public readonly record struct SoulstoneCraftRequest(uint CraftItemHandle, uint[] SoulstoneHandles);

    /// <summary>
    /// <c>TM_CS_SOULSTONE_CRAFT</c> (260), fixed 27 bytes: 7 header, the craft item handle at 7, then four
    /// slot handles at 11, 15, 19 and 23. Four is the socket count of both references (NGemity
    /// ItemInstance.h:89, the repository's TelecasterContext.cs HasMaxLength(4)).
    /// See docs/packet-specs/socle-artisanat-objets.md §3.3.
    /// </summary>
    public static bool TryReadSoulstoneCraft(ReadOnlySpan<byte> packet, out SoulstoneCraftRequest request)
    {
        const int socketCount = 4;
        const int packetLength = HeaderSize + 4 + socketCount * 4;

        request = default;
        if (packet.Length != packetLength)
        {
            return false;
        }

        var handles = new uint[socketCount];
        for (var i = 0; i < socketCount; i++)
        {
            handles[i] = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize + 4 + i * 4, 4));
        }

        request = new SoulstoneCraftRequest(
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4)),
            handles);
        return true;
    }

    /// <summary>
    /// <c>TM_CS_REPAIR_SOULSTONE</c> (262): six handles in one fixed array (rzu
    /// <c>_(array)(ar_handle_t, item_handle, 6)</c>, TS_CS_REPAIR_SOULSTONE.h:6). What the six designate
    /// is not established by any reference — only the frame is.
    /// </summary>
    public readonly record struct RepairSoulstoneRequest(uint[] ItemHandles);

    /// <summary>
    /// <c>TM_CS_REPAIR_SOULSTONE</c> (262), fixed 31 bytes: 7 header, then six handles at 7, 11, 15, 19,
    /// 23 and 27. See docs/packet-specs/socle-artisanat-objets.md §3.4.
    /// </summary>
    public static bool TryReadRepairSoulstone(ReadOnlySpan<byte> packet, out RepairSoulstoneRequest request)
    {
        const int handleCount = 6;
        const int packetLength = HeaderSize + handleCount * 4;

        request = default;
        if (packet.Length != packetLength)
        {
            return false;
        }

        var handles = new uint[handleCount];
        for (var i = 0; i < handleCount; i++)
        {
            handles[i] = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize + i * 4, 4));
        }

        request = new RepairSoulstoneRequest(handles);
        return true;
    }

    /// <summary>
    /// <c>TM_CS_TRANSMIT_ETHEREAL_DURABILITY</c> (263): the handle of the item the client offers. The
    /// client texts read this as the equipment whose durability is extracted into the Ethereal Stone,
    /// but no reference server implements the packet, so the field is kept at its wire meaning.
    /// </summary>
    public readonly record struct TransmitEtherealDurabilityRequest(uint Handle);

    /// <summary>
    /// <c>TM_CS_TRANSMIT_ETHEREAL_DURABILITY</c> (263), fixed 11 bytes in 7.3 (7 header, one handle at 7).
    /// The packet exists from EPIC_7_2 and carries no other field.
    /// See docs/packet-specs/socle-artisanat-objets.md §3.5.
    /// </summary>
    public static bool TryReadTransmitEtherealDurability(ReadOnlySpan<byte> packet,
        out TransmitEtherealDurabilityRequest request)
    {
        const int packetLength = HeaderSize + 4;

        request = default;
        if (packet.Length != packetLength)
        {
            return false;
        }

        request = new TransmitEtherealDurabilityRequest(
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4)));
        return true;
    }

    /// <summary>
    /// <c>TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT</c> (264): a float rate and nothing else in
    /// 7.3. The <c>target</c> field of the later clients (player, or the summon 1 to 6) is gated
    /// <c>version &gt;= EPIC_8_1</c> and is absent here, so the request only ever designates the player.
    /// The unit of the rate is not established.
    /// </summary>
    public readonly record struct TransmitEtherealDurabilityToEquipmentRequest(float Rate);

    /// <summary>
    /// <c>TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT</c> (264), fixed 11 bytes in 7.3: 7 header, one
    /// IEEE-754 float at 7. The 12-byte 8.1 form is refused, not partially read.
    /// See docs/packet-specs/socle-artisanat-objets.md §3.6.
    /// </summary>
    public static bool TryReadTransmitEtherealDurabilityToEquipment(ReadOnlySpan<byte> packet,
        out TransmitEtherealDurabilityToEquipmentRequest request)
    {
        const int packetLength = HeaderSize + 4;

        request = default;
        if (packet.Length != packetLength)
        {
            return false;
        }

        request = new TransmitEtherealDurabilityToEquipmentRequest(
            BinaryPrimitives.ReadSingleLittleEndian(packet.Slice(HeaderSize, 4)));
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

    /// <summary>
    /// TM_CS_TAKEOUT_COMMERCIAL_ITEM (10005), the gesture of pulling one line out of the commercial
    /// storage window: <c>commercial_item_uid</c> at offset 7 then <c>count</c> at offset 11. The frame
    /// is exactly 13 bytes and no other length is accepted — the client's own frame builder hardcodes
    /// <c>0xd</c> (<c>SFrame.exe</c> VA <c>0x48ce93</c>) and has no outgoing constraint, so the server is
    /// the only guard. The uid is opaque: the client hands back verbatim the value read in 10004.
    /// </summary>
    public static bool TryReadTakeoutCommercialItem(ReadOnlySpan<byte> packet, out TakeoutCommercialItemRequest request)
    {
        const int packetLength = HeaderSize + 6;
        if (packet.Length != packetLength)
        {
            request = default;
            return false;
        }

        request = new TakeoutCommercialItemRequest(
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4)),
            BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(HeaderSize + 4, 2)));
        return true;
    }

    /// <summary>
    /// Total size of <c>TM_CS_CHANGE_SUMMON_NAME</c> (323) on the wire, 7-byte header included: 26 bytes.
    /// The client writes that length in hard (<c>0x1a</c>, <c>SFrame.exe</c> 0x48c61d) from its only frame
    /// builder, so any other length is a malformed frame rather than a shorter or padded variant.
    /// </summary>
    public const int ChangeSummonNamePacketSize = 26;

    /// <summary>Payload size of the frame, 26 - 7 = 19 bytes: the whole payload is the name field.</summary>
    public const int ChangeSummonNamePayloadSize = ChangeSummonNamePacketSize - HeaderSize;

    /// <summary>Offset of the <c>name</c> field: 7, the first payload byte (offset 0 seen from the payload).</summary>
    public const int ChangeSummonNameOffset = HeaderSize;

    /// <summary>
    /// Width of the <c>name</c> field in Epic 7.3: 19 bytes. rzu declares
    /// <c>_(def)(string)(name, 20)</c> with an <c>_(impl)(string)(name, 19, version &lt; EPIC_9_6)</c>
    /// override, and the client's frame builder writes 7 + 19 = 26 bytes, which confirms 19 for 7.3.
    /// </summary>
    public const int ChangeSummonNameFieldSize = 19;

    /// <summary>
    /// Usable characters in the field: 18. The 19th byte is the terminator — the client copies the typed
    /// name up to the NUL and forces a NUL into the last byte of the field, so a longer name is truncated
    /// there. This is the width of the field, not a rule of the client: neither the accepted minimum nor
    /// the uniqueness of a name is established (fiche §7(e)), and nothing here enforces either.
    /// </summary>
    public const int ChangeSummonNameMaxLength = ChangeSummonNameFieldSize - 1;

    /// <summary>
    /// <c>TM_CS_CHANGE_SUMMON_NAME</c> (323), the summon rename request: the 7-byte header, then a
    /// <c>char[19] name</c> at offsets 7-25 — 26 bytes in all, the only client to server frame of the
    /// summon family that carries <strong>no</strong> handle.
    /// <para>
    /// Only the exact 26-byte form is accepted. The field must hold a NUL, which the client guarantees:
    /// its frame builder zeroes the 19 bytes first and forces a NUL into the 19th one. Reading past a
    /// field that is full would spill the byte that follows into the name, so such a frame is refused
    /// rather than guessed — the same discipline as the 4500 reader above. What follows the first NUL
    /// inside the field is ignored.
    /// </para>
    /// <para>
    /// The value crosses the server <strong>unapplied</strong>: this reader decides nothing about which
    /// summon is renamed (the frame carries no target, fiche §3.3) nor about the length or uniqueness of
    /// a name (fiche §7(d), §7(e)). See docs/packet-specs/323-change-summon-name.md.
    /// </para>
    /// </summary>
    public static bool TryReadChangeSummonName(ReadOnlySpan<byte> packet, out string name)
    {
        name = null;

        if (packet.Length != ChangeSummonNamePacketSize)
        {
            return false;
        }

        var field = packet.Slice(ChangeSummonNameOffset, ChangeSummonNameFieldSize);
        var terminator = field.IndexOf((byte)0);

        if (terminator < 0)
        {
            return false;
        }

        name = Encoding.ASCII.GetString(field.Slice(0, terminator));
        return true;
    }

    /// <summary>
    /// TM_CS_RANKING_TOP_RECORD (5000) carries one <c>int8 ranking_type</c> and nothing else: the 7.3
    /// client writes the length 8 in hard from its only emission site, so a frame of any other length
    /// comes from a non conforming client. The specification decides no answer for it (log and drop,
    /// spec §5.3 / §7i), which is why only the exact 8-byte form is accepted. The domain of the value
    /// is not established (§7a): it crosses the server untouched and is copied back into the answer.
    /// See docs/packet-specs/socle-classements.md and <see cref="GameRankingPackets"/>.
    /// </summary>
    public static bool TryReadRankingTopRecord(ReadOnlySpan<byte> packet, out RankingTopRecordRequest request)
    {
        const int packetLength = HeaderSize + 1;
        if (packet.Length != packetLength)
        {
            request = default;
            return false;
        }

        request = new RankingTopRecordRequest((sbyte)packet[HeaderSize]);
        return true;
    }
}
