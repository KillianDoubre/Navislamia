using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

/// <summary>
/// The destination slot of an equipped item. <c>TM_CS_PUTON_ITEM</c> (200) carries it, while
/// <c>TM_CS_PUTON_ITEM_SET</c> (281) does not: its handles are resolved through the <c>wear_type</c>
/// of their item resource (<see cref="IItemWearCatalog"/>).
/// </summary>
public static class ItemWearRules
{
    /// <summary>
    /// Whether a wear type can be stored in <c>ItemEntity.WearInfo</c> and reported by
    /// <c>TM_SC_WEAR_INFO</c> (202), which has exactly <see cref="GameCharacterPackets.WearSlots"/>
    /// slots at Epic 7.3. <c>None</c> / <c>CantWear</c> (-1) and the types outside that range
    /// (<c>TwofingerRing</c> 94, <c>Twohand</c> 99, <c>Skill</c> 100, <c>SummonOnly</c> 200) are not
    /// representable: writing them would hide the item from the wear info while marking it worn.
    /// </summary>
    public static bool IsWearableSlot(ItemWearType wearType)
    {
        return InSlotRange((int)wearType);
    }

    /// <summary>
    /// The same check for the position a client sends in <c>TM_CS_PUTON_ITEM</c> (200), which is a
    /// signed byte on the wire.
    /// </summary>
    public static bool IsWearableSlot(sbyte position)
    {
        return InSlotRange(position);
    }

    private static bool InSlotRange(int position)
    {
        return position >= 0 && position < GameCharacterPackets.WearSlots;
    }

    /// <summary>
    /// The slot an item must be equipped in when the request does not carry one
    /// (<c>TM_CS_PUTON_ITEM_SET</c>, 281). A wear type that is already a slot is used as it is. The
    /// only other resolved case is <c>Twohand</c> (99): it is not a slot but the class marker of a
    /// two-handed weapon, which NGemity keeps in <c>m_anWear[WEAR_WEAPON]</c>
    /// (<c>Player.cpp:1860</c>, <c>:1877</c>) and never stores as a position
    /// (<c>Player::putonItem</c> refuses <c>pos >= MAX_ITEM_WEAR</c>) — the weapon slot is the only
    /// port such an item can take. Everything else (<c>TwofingerRing</c> 94, the spare slots 24..27,
    /// <c>Skill</c> 100, <c>SummonOnly</c> 200) has no single port: the caller must not guess one.
    /// </summary>
    public static bool TryResolveSlot(ItemWearType wearType, out ItemWearType slot)
    {
        if (IsWearableSlot(wearType))
        {
            slot = wearType;
            return true;
        }

        if (wearType == ItemWearType.Twohand)
        {
            slot = ItemWearType.Weapon;
            return true;
        }

        slot = ItemWearType.None;
        return false;
    }
}
