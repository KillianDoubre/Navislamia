using System;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
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
    /// The level floor every rank carries, indexed by the rank itself, so <c>{0, 0, 20, 50, 80, 100,
    /// 120, 150, 170}</c> for the ranks 0..8. It is the table of the official Epic 7 Part 4 server
    /// (<c>StructItem::GetLevelLimit</c> <c>0x1400ad250</c>, reading it at <c>0x1404f7fd8</c>);
    /// NGemity only differs at rank 8, where it says 180 (<c>GameRule.cpp:102-103</c>) — the official
    /// decides. <c>GetLevelLimit</c> clamps the rank to 1..8 before reading, so a rank outside the
    /// table reads the nearest end instead of running off the array.
    /// </summary>
    private static readonly int[] RankLevelFloors = { 0, 0, 20, 50, 80, 100, 120, 150, 170 };

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
    /// (<c>TM_CS_PUTON_ITEM_SET</c>, 281). A wear type that is already a slot is used as it is, and the
    /// two types NGemity folds onto a slot are folded the same way
    /// (<c>Player::TranslateWearPosition</c>, <c>Player.cpp:1754-1758</c>): <c>Twohand</c> (99) is the
    /// class marker of a two-handed weapon, which NGemity keeps in the weapon slot and never stores as a
    /// position (the port checks bound it to <c>MAX_ITEM_WEAR</c>, <c>Player.cpp:1853</c> and
    /// <c>Unit.cpp:1491</c>, never to 99);
    /// <c>TwofingerRing</c> (94) is worn in the first ring slot. Everything else
    /// (<c>CantWear</c>, the spare slots 24..27, <c>Skill</c> 100, <c>SummonOnly</c> 200) has no single
    /// port, and the caller must not guess one.
    /// </summary>
    public static bool TryResolveSlot(ItemWearType wearType, out ItemWearType slot)
    {
        if (IsWearableSlot(wearType))
        {
            slot = wearType;
            return true;
        }

        switch (wearType)
        {
            case ItemWearType.Twohand:
                slot = ItemWearType.Weapon;
                return true;
            case ItemWearType.TwofingerRing:
                slot = ItemWearType.Ring;
                return true;
            default:
                slot = ItemWearType.None;
                return false;
        }
    }

    /// <summary>
    /// The level floor a rank carries: <c>0</c>, <c>20</c>, <c>50</c>, <c>80</c>, <c>100</c>,
    /// <c>120</c>, <c>150</c> and <c>170</c> for the ranks 1 to 8, and <c>0</c> for the rank 0. It is
    /// the first term of the <c>max()</c> in <c>StructItem::GetLevelLimit</c>.
    /// </summary>
    public static int RankLevelFloor(int rank)
    {
        return RankLevelFloors[Math.Clamp(rank, 0, RankLevelFloors.Length - 1)];
    }

    /// <summary>
    /// Whether a character may wear an item resource, from the port requirements it declares.
    /// </summary>
    public static bool IsWearAllowed(ItemWearFields fields, int characterLevel)
    {
        return IsWearAllowed(fields.WearType, fields.Rank, fields.UseMinLevel, fields.UseMaxLevel,
            characterLevel);
    }

    /// <summary>
    /// The requirements gate of <c>TM_CS_PUTON_ITEM</c> (200) and <c>TM_CS_PUTON_ITEM_SET</c> (281),
    /// in the order of the official server's <c>StructCreature::TranslateWearPosition</c>
    /// (<c>0x140080e40</c>, sheet §5.2):
    /// <list type="number">
    /// <item>an item that declares no port at all (<c>wear_type</c> -1) refuses everyone
    /// (<c>0x140080e57</c>) — the official reaches it through <c>StructItem::IsWearable</c>
    /// (<c>0x1400adab0</c>);</item>
    /// <item>the level floor <c>max(plancher de rang, use_min_level)</c> must not exceed the character
    /// level (<c>0x140080ea9</c>, <c>0x140080eb2</c>). The official compares it to
    /// <c>max(GetLevel(), m_nUnitExpertLevel)</c>; no equivalent of that expert level is established
    /// in this server, so the character level alone decides, which is stricter and an assumed
    /// deviation (sheet §7.3). This step also carries the official's own <c>use_min_level</c> lower
    /// bound, which the <c>max()</c> makes redundant;</item>
    /// <item>the ceiling <c>use_max_level</c>, where <c>0</c> means no ceiling at all
    /// (<c>0x140080ed0</c>, <c>0x140080ec4</c>).</item>
    /// </list>
    /// Race, class and job depth come next in the official (<c>StructPlayer::TranslateWearPosition</c>)
    /// and are not judged here: the columns they read are empty in this server's data
    /// (sheet §5.6, lot 2).
    /// </summary>
    public static bool IsWearAllowed(ItemWearType wearType, int rank, int useMinLevel, int useMaxLevel,
        int characterLevel)
    {
        if (wearType == ItemWearType.CantWear)
        {
            return false;
        }

        if (Math.Max(RankLevelFloor(rank), useMinLevel) > characterLevel)
        {
            return false;
        }

        return useMaxLevel == 0 || characterLevel <= useMaxLevel;
    }
}
