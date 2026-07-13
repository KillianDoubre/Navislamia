using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

public static class GameCharacterPackets
{
    private const int HeaderSize = 7;
    private const int WearSlots = 24;
    private const int InventoryItemSize = 85;
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

    public static byte[] BuildSkinInfo(uint handle, int skinColor)
    {
        var packet = CreatePacket(GamePackets.TM_SC_SKIN_INFO, HeaderSize + 8);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), handle);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(HeaderSize + 4, 4), skinColor);
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
        var items = character.Items?.ToArray() ?? Array.Empty<ItemEntity>();
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

    public static byte[] BuildEquipSummon(long[] summonSlots)
    {
        const int slotCount = 6;
        var packet = CreatePacket(GamePackets.TM_EQUIP_SUMMON, HeaderSize + 1 + slotCount * 4);
        var span = packet.AsSpan();

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

    private static void WriteInventoryItem(Span<byte> span, ItemEntity item)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0, 4), (uint)item.Id);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), (int)item.ItemResourceId);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(8, 8), item.Id);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(16, 8), item.Amount);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(24, 4), item.EtherealDurability);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(28, 4), (uint)Math.Max(0, item.Endurance));
        span[32] = (byte)item.Enhance;
        span[33] = (byte)item.Level;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(34, 4), unchecked((uint)item.Flag));

        for (var i = 0; i < 4 && item.SocketItemIds != null && i < item.SocketItemIds.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(38 + i * 4, 4), (int)item.SocketItemIds[i]);
        }

        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(54, 4), item.RemainingTime);
        span[58] = (byte)item.ElementalEffectType;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(63, 4), item.ElementalEffectAttackPoint);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(67, 4), item.ElementalEffectMagicPoint);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(71, 4), 0);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(75, 2), (short)item.WearInfo);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(77, 4), (uint)(item.EquippedBySummonId ?? 0));
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(81, 4), item.Idx);
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
