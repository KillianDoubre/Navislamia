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
/// The two resource fields that decide whether an item can be offered as the sacrifice of
/// <c>TM_CS_TRANSMIT_ETHEREAL_DURABILITY</c> (263): the wear type the client's "cannot be sacrificed"
/// clause turns on, and the ethereal durability the resource can carry at all.
/// See docs/packet-specs/263-transmit-ethereal-durability.md.
/// </summary>
public readonly record struct ItemEtherealFields(int Id, ItemWearType WearType, int EtherealDurability);

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
    /// The wear type and the ethereal durability of every item resource, for the sacrifice guard of
    /// <c>TM_CS_TRANSMIT_ETHEREAL_DURABILITY</c> (263).
    /// </summary>
    IReadOnlyList<ItemEtherealFields> GetEtherealFields();
}
