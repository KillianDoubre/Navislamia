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

public readonly record struct ItemUseFields(int Id, int UseMinLevel, int UseMaxLevel, ItemBaseType BaseType);

/// <summary>
/// The wear slot an item resource belongs to (<c>wear_type</c> column of the <c>ItemResource</c>
/// table), the only way to know where <c>TM_CS_PUTON_ITEM_SET</c> (281) must place a handle: that
/// request carries no position.
/// </summary>
public readonly record struct ItemWearFields(int Id, ItemWearType WearType);

public interface IItemResourceRepository
{
    IReadOnlyList<ItemSortFields> GetSortFields();

    IReadOnlyList<ItemEffectFields> GetEffectFields();

    IReadOnlyList<ItemGroupFields> GetGroupFields();

    IReadOnlyList<ItemUseFields> GetUseFields();

    IReadOnlyList<ItemWearFields> GetWearFields();
}
