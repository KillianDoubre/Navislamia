using System;
using System.Buffers.Binary;
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

    /// <summary>
    /// <c>TM_CS_REQUEST_REMOVE_STATE</c> (408): the state window sends one of these per state the player
    /// clicks, carrying only the creature the window is bound to and the state code.
    /// </summary>
    public readonly record struct RemoveStateRequest(uint Target, int StateCode);

    public readonly record struct PutoffItemRequest(sbyte Position, uint TargetHandle);

    public readonly record struct PutonItemRequest(sbyte Position, uint ItemHandle, uint TargetHandle);

    public readonly record struct UseItemRequest(uint ItemHandle, uint TargetHandle);

    public readonly record struct ChangeItemPositionRequest(bool IsStorage, uint ItemHandle1, uint ItemHandle2);

    public readonly record struct RegionInfoRequest(float X, float Y);

    /// <summary>
    /// <c>show_dialog</c> of TM_CS_GET_SUMMON_SETUP_INFO (324): computed by the client, replayed by the
    /// server in the <c>open_dialog</c> byte of the 303 answer.
    /// </summary>
    public readonly record struct SummonSetupInfoRequest(bool ShowDialog);

    /// <summary>
    /// TM_EQUIP_SUMMON (303) sent by the client: the formation the player validated, six card handles
    /// (0 for an empty slot) and the <c>open_dialog</c> byte, which the 7.3 client leaves at 0 on this path.
    /// </summary>
    public readonly record struct EquipSummonRequest(bool OpenDialog, uint[] CardHandles);

    public readonly record struct TakeoutCommercialItemRequest(uint Uid, ushort Count);

    public readonly record struct RankingTopRecordRequest(sbyte RankingType);

    /// <summary>
    /// <c>TM_CS_CHECK_ILLEGAL_USER</c> (57): the client's internal security watch reports a suspected
    /// illegal program. The frame is of fixed size — 7-byte header plus a single <c>uint32</c>
    /// <c>log_code</c> at offset 7 — so the exact 11-byte form is the only one accepted: a short or
    /// padded frame is refused rather than partially read, exactly like <see cref="TryReadGetRegionInfo"/>.
    /// The field is named by rzu; neither rzu nor NGemity says what a server does with it, and the client
    /// only ever sends 0 on the single emission path found in SFrame.exe (see
    /// docs/packet-specs/57-check-illegal-user.md §2.5, §5.5).
    /// </summary>
    public static bool TryReadCheckIllegalUser(ReadOnlySpan<byte> packet, out uint logCode)
    {
        const int packetLength = HeaderSize + 4;
        if (packet.Length != packetLength)
        {
            logCode = 0;
            return false;
        }

        logCode = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4));
        return true;
    }

    /// <summary>
    /// <c>TM_CS_SUMMON_CARD_SKILL_LIST</c> (452): the client asks for the skill list of the summon tied to
    /// a creature card. The frame is of fixed size — 7-byte header plus a single <c>uint32</c>
    /// <c>item_handle</c> at offset 7 — so the exact 11-byte form is the only one accepted: a short or
    /// padded frame is refused rather than partially read, exactly like
    /// <see cref="TryReadCheckIllegalUser"/>. The field is named by rzu; what the client actually puts in
    /// it is not established (docs/packet-specs/452-summon-card-skill-list.md §7b), so the value is only
    /// ever logged, never resolved into a card or a summon.
    /// </summary>
    public static bool TryReadSummonCardSkillList(ReadOnlySpan<byte> packet, out uint itemHandle)
    {
        const int packetLength = HeaderSize + 4;
        if (packet.Length != packetLength)
        {
            itemHandle = 0;
            return false;
        }

        itemHandle = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4));
        return true;
    }

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

    /// <summary>
    /// Parses the fixed 15-byte <c>TM_CS_REQUEST_REMOVE_STATE</c>: header, <c>target</c> at offset 7,
    /// <c>state_code</c> at offset 11. The frame length is constant, so anything else is refused rather
    /// than truncated.
    /// </summary>
    public static bool TryReadRemoveState(ReadOnlySpan<byte> packet, out RemoveStateRequest request)
    {
        const int packetLength = HeaderSize + 8;
        if (packet.Length != packetLength)
        {
            request = default;
            return false;
        }

        request = new RemoveStateRequest(
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(HeaderSize + 4, 4)));
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

    /// <summary>Total size of <c>TM_CS_SUMMON</c> (304) on the wire, 7-byte header included: 12 bytes.</summary>
    public const int SummonPacketSize = 12;

    /// <summary>Offset of the <c>int8_t is_summon</c> flag: 7, the first payload byte.</summary>
    public const int SummonFlagOffset = HeaderSize;

    /// <summary>Offset of the <c>ar_handle_t card_handle</c> field: 8.</summary>
    public const int SummonCardHandleOffset = SummonFlagOffset + 1;

    /// <summary>Payload size of the frame, 12 - 7 = 5 bytes (1 byte of flag and 4 bytes of handle).</summary>
    public const int SummonPayloadSize = SummonPacketSize - HeaderSize;

    /// <summary>
    /// <c>TM_CS_SUMMON</c> (304): a summon/unsummon request carried by a card. The 7.3 layout is the
    /// 7-byte header, then an <c>int8_t is_summon</c> at offset 7 and an <c>ar_handle_t card_handle</c> at
    /// offsets 8-11 (<see cref="SummonPacketSize"/> = 12, rzu <c>TS_CS_SUMMON.h</c> below EPIC_9_6_3;
    /// NGemity declares the same order and sizes).
    /// <para>
    /// Both fields cross the server <strong>unread</strong> in the sense that nothing here interprets
    /// them: rzu and NGemity name them without defining them, wait for a value of <c>is_summon</c>, and say
    /// nothing of what <c>card_handle</c> designates. The reader therefore returns the raw signed byte and
    /// the raw little-endian word, and nothing compares or validates them.
    /// </para>
    /// <para>
    /// No refusal rule is established for this packet: a frame shorter than the declared 12 bytes cannot be
    /// read at all and is refused, while a longer frame is read from its first 12 bytes rather than
    /// rejected, because the fiche leaves the disposition of a non conforming length open (§5.3.4, §7).
    /// The 7.3 client never builds this frame at all — summoning goes through the summon creature skill,
    /// that is <c>TM_CS_SKILL</c> (400) — so no real capture exists to confirm either case.
    /// See docs/packet-specs/304-summon.md.
    /// </para>
    /// </summary>
    public static bool TryReadSummon(ReadOnlySpan<byte> packet, out sbyte isSummon, out uint cardHandle)
    {
        isSummon = 0;
        cardHandle = 0;

        if (packet.Length < SummonPacketSize)
        {
            return false;
        }

        isSummon = (sbyte)packet[SummonFlagOffset];
        cardHandle = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(SummonCardHandleOffset, 4));
        return true;
    }

    /// <summary>
    /// TM_CS_GET_SUMMON_SETUP_INFO (324) is exactly eight bytes: the seven byte header plus one
    /// <c>show_dialog</c> byte at offset 7. The 7.3 client computes that byte per player
    /// (<c>show_dialog = !setting[44]</c>) and expects it back in the <c>open_dialog</c> byte of the 303
    /// answer, so it is read here and never fixed. The client only writes 0 or 1; reading any other
    /// non-zero value as true is a documented normalisation, not an observation. Only the 8-byte form is
    /// accepted: the sheet defines no answer at all for a request of another length.
    /// </summary>
    public static bool TryReadGetSummonSetupInfo(ReadOnlySpan<byte> packet, out SummonSetupInfoRequest request)
    {
        const int packetLength = HeaderSize + 1;
        if (packet.Length != packetLength)
        {
            request = default;
            return false;
        }

        request = new SummonSetupInfoRequest(packet[HeaderSize] != 0);
        return true;
    }

    /// <summary>
    /// TM_EQUIP_SUMMON (303) in the client to server direction, exactly 32 bytes: the 7-byte header, the
    /// <c>open_dialog</c> byte at offset 7, then six <c>card_handle</c> words at offsets 8, 12, 16, 20, 24
    /// and 28 — the layout of the server's own 303 (client builder VA <c>0x48cd10</c>, <c>Length = 0x20</c>;
    /// docs/packet-specs/324-get-summon-setup-info.md §5.4). Any other length is refused.
    /// </summary>
    public static bool TryReadEquipSummon(ReadOnlySpan<byte> packet, out EquipSummonRequest request)
    {
        const int slotCount = 6;
        const int packetLength = HeaderSize + 1 + slotCount * 4;
        if (packet.Length != packetLength)
        {
            request = default;
            return false;
        }

        var handles = new uint[slotCount];
        for (var i = 0; i < slotCount; i++)
        {
            handles[i] = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize + 1 + i * 4, 4));
        }

        request = new EquipSummonRequest(packet[HeaderSize] != 0, handles);
        return true;
    }

    /// <summary>
    /// The size of <c>TM_CS_TURN_ON_PK_MODE</c> (800) on the wire: the 7-byte header and nothing else.
    /// rzu's <c>TS_CS_TURN_ON_PK_MODE_DEF(_)</c> is empty — no field is defined after the header — and the
    /// client's own constructor writes <c>Length = 7</c> (SFrame.exe+0x684bb8, id 0x320 at
    /// +0x684bd2) without writing a single body byte.
    /// See docs/packet-specs/800-turn-on-pk-mode.md §3.1.
    /// </summary>
    public const int TurnOnPkModeLength = HeaderSize;

    /// <summary>
    /// <c>TM_CS_TURN_ON_PK_MODE</c> (800): the player's PK mode switch, a <b>header-only</b> frame — its
    /// whole body is the header, like 23, 25 and 27 (<c>TM_CS_RETURN_LOBBY</c> and friends). The exact
    /// 7-byte form is therefore the only one accepted: the client writes that length in hard and has no
    /// producer for any other form, so a longer frame is an anomaly refused before anything is applied,
    /// and nothing is ever read past the header. A frame shorter than its own header never reaches the
    /// dispatch at all — the receive loop disconnects on it (<c>GameClient.OnDataReceived</c>) — so the
    /// 7-byte case is the only one the loop can hand over.
    /// </summary>
    public static bool TryReadTurnOnPkMode(ReadOnlySpan<byte> packet) => packet.Length == TurnOnPkModeLength;
}
