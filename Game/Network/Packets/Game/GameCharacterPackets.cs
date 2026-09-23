using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

public readonly record struct SkillListEntry(
    int SkillId,
    byte Level,
    uint TotalCoolTimeTicks,
    uint RemainCoolTimeTicks);

public static class GameCharacterPackets
{
    private const int HeaderSize = 7;
    public const int WearSlots = 24;
    private const int InventoryItemSize = ItemFixedInfoWriter.Size + 10;
    private const int MaxInventoryItemsPerPacket = 45;

    public static uint GetHairId(CharacterEntity character)
    {
        return character.Models is { Length: > 1 } ? (uint)character.Models[1] : 0;
    }

    public static uint GetFaceId(CharacterEntity character)
    {
        return character.Models is { Length: > 0 } ? (uint)character.Models[0] : 0;
    }

    public static byte[] BuildHairInfo(uint handle, uint hairId, int colorIndex, int colorRgb)
    {
        var packet = CreatePacket(GamePackets.TM_SC_HAIR_INFO, HeaderSize + 16);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), handle);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize + 4, 4), hairId);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(HeaderSize + 8, 4), colorIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize + 12, 4), unchecked((uint)colorRgb));
        WriteChecksum(packet);
        return packet;
    }

    public static byte[] BuildHideEquipInfo(uint handle, int hideEquipFlag)
    {
        var packet = CreatePacket(GamePackets.TM_SC_HIDE_EQUIP_INFO, HeaderSize + 8);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), handle);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize + 4, 4), unchecked((uint)hideEquipFlag));
        WriteChecksum(packet);
        return packet;
    }

    /// <summary>
    /// TM_SC_EMOTION (1201) echoes the emotion received in 1202: the handle of the author first, then
    /// the value verbatim. The client resolves both the animation and the local message itself.
    /// </summary>
    public static byte[] BuildEmotion(uint handle, int emotion)
    {
        var packet = CreatePacket(GamePackets.TM_SC_EMOTION, HeaderSize + 8);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), handle);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(HeaderSize + 4, 4), emotion);
        WriteChecksum(packet);
        return packet;
    }

    public static byte[] BuildSkinInfo(uint handle, int skinColor)
    {
        var packet = CreatePacket(GamePackets.TM_SC_SKIN_INFO, HeaderSize + 8);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), handle);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(HeaderSize + 4, 4), skinColor);
        WriteChecksum(packet);
        return packet;
    }

    public static byte[] BuildItemWearInfo(uint itemHandle, short wearPosition, uint targetHandle, int enhance,
        byte elementalEffectType)
    {
        var packet = CreatePacket(GamePackets.TM_SC_ITEM_WEAR_INFO, HeaderSize + 15);
        var payload = packet.AsSpan(HeaderSize);

        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), itemHandle);
        BinaryPrimitives.WriteInt16LittleEndian(payload.Slice(4, 2), wearPosition);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(6, 4), targetHandle);
        BinaryPrimitives.WriteInt32LittleEndian(payload.Slice(10, 4), enhance);
        payload[14] = elementalEffectType;

        WriteChecksum(packet);
        return packet;
    }

    public static byte[] BuildWearInfo(uint handle, CharacterEntity character)
    {
        var total = HeaderSize + 4 + WearSlots * 4 * 3 + WearSlots;
        var packet = CreatePacket(GamePackets.TM_SC_WEAR_INFO, total);
        var span = packet.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(HeaderSize, 4), handle);

        var codeBase = HeaderSize + 4;
        var enhanceBase = codeBase + WearSlots * 4;
        var levelBase = enhanceBase + WearSlots * 4;
        var elementalBase = levelBase + WearSlots * 4;

        if (character.Items != null)
        {
            foreach (var item in character.Items)
            {
                var slot = (int)item.WearInfo;
                if (slot < 0 || slot >= WearSlots)
                {
                    continue;
                }

                BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(codeBase + slot * 4, 4), (uint)item.ItemResourceId);
                BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(enhanceBase + slot * 4, 4), item.Enhance);
                BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(levelBase + slot * 4, 4), item.Level);
                span[elementalBase + slot] = (byte)item.ElementalEffectType;
            }
        }

        InjectBaseModelIfEmpty(packet, codeBase, ItemWearType.Armor, character.Models, 2);
        InjectBaseModelIfEmpty(packet, codeBase, ItemWearType.Glove, character.Models, 3);
        InjectBaseModelIfEmpty(packet, codeBase, ItemWearType.Boots, character.Models, 4);

        WriteChecksum(packet);
        return packet;
    }

    public static IReadOnlyList<byte[]> BuildInventory(CharacterEntity character)
    {
        return BuildInventory(character.Items?.OrderBy(item => item.Idx).ToArray()
                              ?? Array.Empty<ItemEntity>());
    }

    public static IReadOnlyList<byte[]> BuildInventory(ItemEntity[] items)
    {
        var packets = new List<byte[]>(Math.Max(1, (items.Length + MaxInventoryItemsPerPacket - 1) / MaxInventoryItemsPerPacket));

        if (items.Length == 0)
        {
            packets.Add(BuildInventoryChunk(Array.Empty<ItemEntity>()));
            return packets;
        }

        for (var offset = 0; offset < items.Length; offset += MaxInventoryItemsPerPacket)
        {
            var count = Math.Min(MaxInventoryItemsPerPacket, items.Length - offset);
            packets.Add(BuildInventoryChunk(items.AsSpan(offset, count)));
        }

        return packets;
    }

    public static byte[] BuildEraseItem(IReadOnlyList<(uint Handle, long Count)> erased)
    {
        const int recordSize = 12;
        var packet = CreatePacket(GamePackets.TM_SC_ERASE_ITEM, HeaderSize + 1 + erased.Count * recordSize);
        var payload = packet.AsSpan(HeaderSize);
        payload[0] = (byte)(sbyte)erased.Count;

        for (var i = 0; i < erased.Count; i++)
        {
            var record = payload.Slice(1 + i * recordSize, recordSize);
            BinaryPrimitives.WriteUInt32LittleEndian(record.Slice(0, 4), erased[i].Handle);
            BinaryPrimitives.WriteInt64LittleEndian(record.Slice(4, 8), erased[i].Count);
        }

        WriteChecksum(packet);
        return packet;
    }

    /// <summary>
    /// <c>TS_SC_DROP_RESULT</c> (205): the inventory handle from the request, echoed even when it is
    /// unknown, then a single byte saying whether anything was dropped. NGemity sends nothing else on
    /// a refusal, so a refusal is this frame alone.
    /// </summary>
    public static byte[] BuildDropResult(uint itemHandle, bool isAccepted)
    {
        var packet = CreatePacket(GamePackets.TM_SC_DROP_RESULT, HeaderSize + 4 + 1);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), itemHandle);
        packet[HeaderSize + 4] = (byte)(isAccepted ? 1 : 0);
        WriteChecksum(packet);
        return packet;
    }

    public static byte[] BuildDestroyItem(uint itemHandle)
    {
        var packet = CreatePacket(GamePackets.TM_SC_DESTROY_ITEM, HeaderSize + 4);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), itemHandle);
        WriteChecksum(packet);
        return packet;
    }

    public static byte[] BuildUpdateItemCount(uint itemHandle, long count)
    {
        // count is int64 from EPIC_4_1 (rzu TS_SC_UPDATE_ITEM_COUNT).
        var packet = CreatePacket(GamePackets.TM_SC_UPDATE_ITEM_COUNT, HeaderSize + 12);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), itemHandle);
        BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(HeaderSize + 4, 8), count);
        WriteChecksum(packet);
        return packet;
    }

    public static byte[] BuildUseItemResult(uint itemHandle, uint targetHandle)
    {
        var packet = CreatePacket(GamePackets.TM_SC_USE_ITEM_RESULT, HeaderSize + 8);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), itemHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize + 4, 4), targetHandle);
        WriteChecksum(packet);
        return packet;
    }

    /// <summary>
    /// TM_EQUIP_SUMMON (303), 32 bytes: the seven byte header, the <c>open_dialog</c> byte at offset 7,
    /// then the six formation handles at offsets 8, 12, 16, 20, 24 and 28. The same client sends a 303 in
    /// the other direction (with <c>open_dialog = 0</c>) when the player validates a formation; this
    /// builder only serves the two downward sites. <paramref name="openDialog"/> replays the
    /// <c>show_dialog</c> of the 324 request: world entry passes <c>false</c>, the answer to 324 passes
    /// what it received.
    /// </summary>
    public static byte[] BuildEquipSummon(long[] summonSlots, bool openDialog = false)
    {
        const int slotCount = 6;
        var packet = CreatePacket(GamePackets.TM_EQUIP_SUMMON, HeaderSize + 1 + slotCount * 4);
        var span = packet.AsSpan();

        packet[HeaderSize] = (byte)(openDialog ? 1 : 0);

        for (var i = 0; i < slotCount && summonSlots != null && i < summonSlots.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(HeaderSize + 1 + i * 4, 4), (uint)summonSlots[i]);
        }

        WriteChecksum(packet);
        return packet;
    }

    public static byte[] BuildGoldUpdate(long gold, int chaos)
    {
        var packet = CreatePacket(GamePackets.TM_SC_GOLD_UPDATE, HeaderSize + 12);
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(HeaderSize, 8), (ulong)Math.Max(0, gold));
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize + 8, 4), (uint)Math.Max(0, chaos));
        WriteChecksum(packet);
        return packet;
    }

    public static byte[] BuildLevelUpdate(uint handle, int level, int jobLevel)
    {
        var packet = CreatePacket(GamePackets.TM_SC_LEVEL_UPDATE, HeaderSize + 12);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), handle);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(HeaderSize + 4, 4), level);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(HeaderSize + 8, 4), jobLevel);
        WriteChecksum(packet);
        return packet;
    }

    public static byte[] BuildExpUpdate(uint handle, long exp, long jp)
    {
        var packet = CreatePacket(GamePackets.TM_SC_EXP_UPDATE, HeaderSize + 20);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), handle);
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(HeaderSize + 4, 8), (ulong)Math.Max(0, exp));
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(HeaderSize + 12, 8), (ulong)Math.Max(0, jp));
        WriteChecksum(packet);
        return packet;
    }

    public static byte[] BuildEmptyAddedSkillList(uint handle)
    {
        var packet = CreatePacket(GamePackets.TM_SC_ADDED_SKILL_LIST, HeaderSize + 6);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), handle);
        WriteChecksum(packet);
        return packet;
    }

    public static byte[] BuildSkillList(uint handle, IReadOnlyCollection<KeyValuePair<int, byte>> skills)
    {
        return BuildSkillList(handle,
            skills.Select(skill => new SkillListEntry(skill.Key, skill.Value, 0, 0)).ToArray());
    }

    /// <summary>
    /// <c>TS_SC_SKILL_LIST</c> (403). Cooldowns reach the client through this packet, not a dedicated one:
    /// the reference server re-sends the skill after a successful cast for exactly that reason.
    /// </summary>
    public static byte[] BuildSkillList(uint handle, IReadOnlyCollection<SkillListEntry> skills)
    {
        const int fixedPayloadSize = 7;
        const int skillRecordSize = 14;
        var packet = CreatePacket(GamePackets.TM_SC_SKILL_LIST,
            HeaderSize + fixedPayloadSize + skills.Count * skillRecordSize);
        var payload = packet.AsSpan(HeaderSize);

        BinaryPrimitives.WriteUInt32LittleEndian(payload, handle);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(4, 2), checked((ushort)skills.Count));

        var offset = fixedPayloadSize;
        foreach (var skill in skills)
        {
            BinaryPrimitives.WriteInt32LittleEndian(payload.Slice(offset, 4), skill.SkillId);
            payload[offset + 4] = skill.Level;
            payload[offset + 5] = skill.Level;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(offset + 6, 4), skill.TotalCoolTimeTicks);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(offset + 10, 4), skill.RemainCoolTimeTicks);
            offset += skillRecordSize;
        }

        WriteChecksum(packet);
        return packet;
    }

    public static byte[] BuildBeltSlotInfo(long[] beltSlots)
    {
        const int slotCount = 6;
        var packet = CreatePacket(GamePackets.TM_SC_BELT_SLOT_INFO, HeaderSize + slotCount * 4);

        for (var i = 0; i < slotCount && beltSlots != null && i < beltSlots.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize + i * 4, 4), (uint)beltSlots[i]);
        }

        WriteChecksum(packet);
        return packet;
    }

    public static byte[] BuildStatusChange(uint handle, uint status = 0)
    {
        var packet = CreatePacket(GamePackets.TM_SC_STATUS_CHANGE, HeaderSize + 8);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), handle);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize + 4, 4), status);
        WriteChecksum(packet);
        return packet;
    }

    private static byte[] BuildInventoryChunk(ReadOnlySpan<ItemEntity> items)
    {
        var packet = CreatePacket(GamePackets.TM_SC_INVENTORY, HeaderSize + 2 + items.Length * InventoryItemSize);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(HeaderSize, 2), (ushort)items.Length);

        var offset = HeaderSize + 2;
        foreach (var item in items)
        {
            WriteInventoryItem(packet.AsSpan(offset, InventoryItemSize), item);
            offset += InventoryItemSize;
        }

        WriteChecksum(packet);
        return packet;
    }

    /// <summary>
    /// The Epic 7.3 inventory record: the shared 75-byte <see cref="ItemFixedInfo"/> motif followed by
    /// the three position bytes the auction family does not carry — <c>wear_position</c> at 75,
    /// <c>own_summon_handle</c> at 77 and <c>index</c> at 81. The 85-byte stride was confirmed in the
    /// client; the 75-byte motif must come from <see cref="ItemFixedInfoWriter"/> so both families stay
    /// aligned on <c>appearance_code</c>.
    /// </summary>
    private static void WriteInventoryItem(Span<byte> span, ItemEntity item)
    {
        ItemFixedInfoWriter.Write(span.Slice(0, ItemFixedInfoWriter.Size), ItemFixedInfo.FromItem(item));

        var positionOffset = ItemFixedInfoWriter.Size;
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(positionOffset, 2), (short)item.WearInfo);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(positionOffset + 2, 4),
            (uint)(item.EquippedBySummonId ?? 0));
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(positionOffset + 6, 4), item.Idx);
    }

    private static void InjectBaseModelIfEmpty(byte[] packet, int codeBase, ItemWearType slot, int[] models, int modelIndex)
    {
        if (models == null || models.Length <= modelIndex)
        {
            return;
        }

        var offset = codeBase + (int)slot * 4;
        if (BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(offset, 4)) == 0)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(offset, 4), (uint)models[modelIndex]);
        }
    }

    private static byte[] CreatePacket(GamePackets id, int total)
    {
        var packet = new byte[total];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)id);
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
