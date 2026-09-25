using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

public readonly record struct ItemSortFields(int Id, int Category, int Group, int Rank);

public readonly record struct ItemEffectFields(
    int Id,
    ItemType ItemType,
    short[] BaseTypes,
    decimal[] BaseVar1,
    decimal[] BaseVar2,
    short[] OptTypes,
    decimal[] OptVar1,
    decimal[] OptVar2);

public readonly record struct ItemGroupFields(int Id, ItemGroup Group);

/// <summary>
/// The two columns a sale prices an item from: its <c>rank</c> and its <c>price</c>
/// (<c>MarketSellPrice</c>, docs/packet-specs/252-sell-item.md §5.3).
/// </summary>
public readonly record struct ItemSellFields(int Id, int Rank, int Price);

public readonly record struct ItemUseFields(int Id, int UseMinLevel, int UseMaxLevel, ItemBaseType BaseType,
    bool RenamesPet = false);

public interface IItemResourceRepository
{
    IReadOnlyList<ItemSortFields> GetSortFields();

    IReadOnlyList<ItemEffectFields> GetEffectFields();

    /// <summary>
    /// The items with an <c>ItemEffectInstant.Skill</c> (5) effect in their base or option slots: using one
    /// casts the skill of var1 at the level of var2 (NGemity <c>Unit::onItemUseEffect</c>).
    /// </summary>
    IReadOnlyList<ItemEffectFields> GetInstantSkillItems();

    IReadOnlyList<ItemGroupFields> GetGroupFields();

    IReadOnlyList<ItemUseFields> GetUseFields();

    /// <summary>
    /// The <c>rank</c> and <c>price</c> of every item resource, read once by <c>ItemSellCatalog</c>: the
    /// pair the sell price of <c>TM_CS_SELL_ITEM</c> (252) is computed from.
    /// </summary>
    IReadOnlyList<ItemSellFields> GetSellPriceFields();
}
