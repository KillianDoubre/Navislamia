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

    /// <summary>
    /// The four columns the crafting conditions compare, for every item resource: the
    /// <c>GetItemGroup()</c>/<c>GetItemClass()</c>/<c>GetItemRank()</c>/<c>GetWearType()</c> of NGemity's
    /// item accessors (<c>Item.h:62-65</c>, <c>Item.cpp:63-67</c>, <c>Item.cpp:202</c>).
    /// </summary>
    IReadOnlyList<ItemMatchFields> GetMatchFields();

    IReadOnlyList<ItemUseFields> GetUseFields();
}
