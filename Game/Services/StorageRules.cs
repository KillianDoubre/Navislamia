using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Navislamia.Game.Services;

/// <summary>
/// The decisions the storage path takes before anything moves, ported from
/// <c>WorldSession::onStorage</c> in NGemity (Chihiro/src/Network/GameNetwork/WorldSession.cpp:1590-1676)
/// and from the account-scoped container both references query
/// (rzgame/src/Database/DB_StorageItem.cpp:7 ; Chihiro/src/Database/Implementation/CharacterDatabase.cpp:93-97).
/// The four divergences assumed here are listed in docs/packet-specs/211-212-storage.md §6: an unknown
/// mode is refused instead of silently dropped, an insufficient balance is refused instead of silently
/// ignored, an over-large count is clamped instead of dropped, and the storage is commanded by the
/// account rather than the character.
/// </summary>
public static class StorageRules
{
    /// <summary>Inventory to storage, object (<c>ITEM_INVENTORY_TO_STORAGE</c>, WorldSession.h:23).</summary>
    public const byte ItemToStorage = 0;

    /// <summary>Storage to inventory, object (<c>ITEM_STORAGE_TO_INVENTORY</c>, WorldSession.h:24).</summary>
    public const byte ItemToInventory = 1;

    /// <summary>Inventory to storage, gold (<c>GOLD_INVENTORY_TO_STORAGE</c>, WorldSession.h:26).</summary>
    public const byte GoldToStorage = 2;

    /// <summary>Storage to inventory, gold (<c>GOLD_STORAGE_TO_INVENTORY</c>, WorldSession.h:27).</summary>
    public const byte GoldToInventory = 3;

    /// <summary>Window closed (<c>STORAGE_CLOSE</c>, WorldSession.h:29).</summary>
    public const byte CloseMode = 4;

    /// <summary>
    /// rzu types the field <c>int8_t</c> and NGemity switches on it with no bound check
    /// (WorldSession.cpp:1600), so 5..255 fall into its silent <c>default: break</c>
    /// (WorldSession.cpp:1673-1675). Navislamia refuses them instead (§6).
    /// </summary>
    public static bool IsKnownMode(byte mode) => mode <= CloseMode;

    /// <summary>The two modes that carry an item handle and a unit count.</summary>
    public static bool IsItemMode(byte mode) => mode is ItemToStorage or ItemToInventory;

    /// <summary>The two modes that carry a gold amount.</summary>
    public static bool IsGoldMode(byte mode) => mode is GoldToStorage or GoldToInventory;

    /// <summary>Whether the mode moves the handled stack out of the inventory (0, 2) or back into it (1, 3).</summary>
    public static bool MovesToStorage(byte mode) => mode is ItemToStorage or GoldToStorage;

    /// <summary>
    /// The units actually moved. NGemity drops the request when <c>count</c> exceeds the stack
    /// (<c>Player::MoveStorageToInventory</c>, Player.cpp:2983-2987, returns <c>false</c> without an
    /// answer), the repository clamps everywhere instead — the fiche keeps the clamp (§5.3, §6) because
    /// the item frames then report the real quantity. A non-positive request or an empty stack moves
    /// nothing.
    /// </summary>
    public static long MoveCount(long requested, long available)
        => requested <= 0 || available <= 0 ? 0 : Math.Min(requested, available);

    /// <summary>
    /// The slot index the moved stack takes in its destination list. NGemity hands the destination
    /// inventory a fresh index (<c>IssueNewIndex()</c>, Player.cpp:2985 and :3025); with no server-side
    /// capacity in 7.3 (§7.2) the lowest index not already used is that free slot, accounted for
    /// separately per list.
    /// </summary>
    public static int NextFreeIndex(IEnumerable<int> usedIndices)
    {
        var used = new HashSet<int>(usedIndices ?? Array.Empty<int>());
        var index = 0;
        while (used.Contains(index))
        {
            index++;
        }

        return index;
    }

    /// <summary>
    /// An inventory row: the item table holds one row per owned stack and the inventory side is
    /// discriminated by the character (<c>account_id = 0 AND owner_id = ?</c>,
    /// CharacterDatabase.cpp:53).
    /// </summary>
    public static bool IsInventoryRow(ItemEntity item, long characterId) => item.CharacterId == characterId;

    /// <summary>
    /// A storage row: the same table, discriminated by the account and with no owning character, no
    /// auction and no keeping (<c>account_id = ? AND owner_id = 0 AND auction_id = 0 AND keeping_id = 0</c>,
    /// CharacterDatabase.cpp:93-97). The last two conditions keep auction rows — which
    /// <c>ItemStorageEntity</c> also uses — out of the storage list.
    /// </summary>
    public static bool IsStorageRow(ItemEntity item, int accountId)
        => item.CharacterId is null
           && item.AccountId == accountId
           && item.AuctionId is null
           && item.StorageId is null;

    /// <summary>
    /// Puts a row on one side of the account/character split: the storage side is carried by
    /// <see cref="ItemEntity.AccountId"/> with <see cref="ItemEntity.CharacterId"/> left empty, the
    /// inventory side by the character with no account column — the shape NGemity's two queries read.
    /// </summary>
    public static void Own(ItemEntity item, bool storage, long characterId, int accountId)
    {
        item.CharacterId = storage ? null : characterId;
        item.AccountId = storage ? accountId : null;
    }

    /// <summary>
    /// The row a partial move creates in the destination list: the same object with a unit count of its
    /// own and the destination slot. The item-defining columns are copied so both halves stay identical
    /// on the wire; the ownership columns follow the source and are re-righted by
    /// <see cref="Own"/>, and no link that belongs to the source row (auction, keeping, summon
    /// equipment) is carried over.
    /// </summary>
    public static ItemEntity Divide(ItemEntity item, long amount, int index)
    {
        return new ItemEntity
        {
            ItemResourceId = item.ItemResourceId,
            Amount = amount,
            Level = item.Level,
            Enhance = item.Enhance,
            EtherealDurability = item.EtherealDurability,
            Endurance = item.Endurance,
            Flag = item.Flag,
            GenerateBySource = item.GenerateBySource,
            WearInfo = item.WearInfo,
            SocketItemIds = item.SocketItemIds?.ToArray(),
            RemainingTime = item.RemainingTime,
            ElementalEffectType = item.ElementalEffectType,
            ElementalEffectExpireTime = item.ElementalEffectExpireTime,
            ElementalEffectAttackPoint = item.ElementalEffectAttackPoint,
            ElementalEffectMagicPoint = item.ElementalEffectMagicPoint,
            CharacterId = item.CharacterId,
            AccountId = item.AccountId,
            Idx = index
        };
    }
}
