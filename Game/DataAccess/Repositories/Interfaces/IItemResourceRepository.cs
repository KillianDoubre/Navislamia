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
/// The four template columns the crafting conditions read: <c>group</c>, <c>class</c>, <c>rank</c> and
/// <c>wear_type</c> (NGemity <c>ObjectMgr.cpp:122-128</c>). <c>Class</c> is the item resource's
/// <see cref="ItemType"/>: the mapper fills it from the <c>Class</c> column of the source table
/// (<c>ArcadiaResourcesMappingProfile.cs</c>), which is the column NGemity reads into <c>eClass</c>, the
/// value behind <c>Item::GetItemClass()</c>.
/// </summary>
public readonly record struct ItemMatchFields(int Id, ItemGroup Group, ItemType Class, int Rank,
    ItemWearType WearType);

/// <summary>
/// The two columns a sale prices an item from: its <c>rank</c> and its <c>price</c>
/// (<c>MarketSellPrice</c>, docs/packet-specs/252-sell-item.md §5.3).
/// </summary>
public readonly record struct ItemSellFields(int Id, int Rank, int Price);

public readonly record struct ItemUseFields(int Id, int UseMinLevel, int UseMaxLevel, ItemBaseType BaseType,
    bool RenamesPet = false);

/// <summary>
/// The two resource fields that decide whether an item can be offered as the sacrifice of
/// <c>TM_CS_TRANSMIT_ETHEREAL_DURABILITY</c> (263): the wear type the client's "cannot be sacrificed"
/// clause turns on, and the ethereal durability the resource can carry at all.
/// See docs/packet-specs/263-transmit-ethereal-durability.md.
/// </summary>
public readonly record struct ItemEtherealFields(int Id, ItemWearType WearType, int EtherealDurability);

/// <summary>
/// What <c>TM_CS_SOULSTONE_CRAFT</c> (260) needs from an item resource: the chassis count of the item
/// being socketed, the three axes that decide whether a stone is a soul stone, the price its socketing
/// costs and the profile two stones are told apart by
/// (NGemity <c>WorldSession.cpp:1509-1553</c>). Only the first value column of each slot is carried:
/// the reference compares <c>base_var[k][0]</c> and dips into no other column.
/// </summary>
public readonly record struct ItemSoulstoneCraftFields(
    int Id,
    ItemBaseType ItemBaseType,
    ItemGroup Group,
    ItemType ItemType,
    int SocketCount,
    int Price,
    short[] BaseTypes,
    decimal[] BaseVar1,
    short[] OptTypes,
    decimal[] OptVar1);

public readonly record struct ItemSocketFields(int Id, int SocketCount, ItemBaseType BaseType, ItemType ItemType,
    ItemGroup Group);

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

    /// <summary>
    /// The four columns the crafting conditions compare, for every item resource: the
    /// <c>GetItemGroup()</c>/<c>GetItemClass()</c>/<c>GetItemRank()</c>/<c>GetWearType()</c> of NGemity's
    /// item accessors (<c>Item.h:62-65</c>, <c>Item.cpp:63-67</c>, <c>Item.cpp:202</c>).
    /// </summary>
    IReadOnlyList<ItemMatchFields> GetMatchFields();

    IReadOnlyList<ItemUseFields> GetUseFields();

    IReadOnlyList<ItemSocketFields> GetSocketFields();

    /// <summary>
    /// The <c>rank</c> and <c>price</c> of every item resource, read once by <c>ItemSellCatalog</c>: the
    /// pair the sell price of <c>TM_CS_SELL_ITEM</c> (252) is computed from.
    /// </summary>
    IReadOnlyList<ItemSellFields> GetSellPriceFields();

    /// <summary>
    /// The wear type and the ethereal durability of every item resource, for the sacrifice guard of
    /// <c>TM_CS_TRANSMIT_ETHEREAL_DURABILITY</c> (263).
    /// </summary>
    IReadOnlyList<ItemEtherealFields> GetEtherealFields();

    /// <summary>
    /// Every item resource, with its chassis count, its price and the four axis arrays the two-stone
    /// rule compares. The whole table is read because any item can be the one being socketed and any
    /// item code can already sit in one of its chassis.
    /// </summary>
    IReadOnlyList<ItemSoulstoneCraftFields> GetSoulstoneCraftFields();
}
