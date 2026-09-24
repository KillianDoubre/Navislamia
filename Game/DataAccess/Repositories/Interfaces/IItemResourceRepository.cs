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

public readonly record struct ItemUseFields(int Id, int UseMinLevel, int UseMaxLevel, ItemBaseType BaseType,
    bool RenamesPet = false);

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
    /// Every item resource, with its chassis count, its price and the four axis arrays the two-stone
    /// rule compares. The whole table is read because any item can be the one being socketed and any
    /// item code can already sit in one of its chassis.
    /// </summary>
    IReadOnlyList<ItemSoulstoneCraftFields> GetSoulstoneCraftFields();
}
