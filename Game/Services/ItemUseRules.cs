using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Network.Packets;

namespace Navislamia.Game.Services;

/// <summary>
/// The level gate of an item use, ported from <c>Player::IsUseableItem</c> in NGemity
/// (Chihiro/src/Entities/Player/Player.cpp:2088-2107): only the template's own use levels are
/// judged here. The item service checks cooldowns separately.
/// </summary>
public static class ItemUseRules
{
    public static ResultCode CheckUseLevel(int characterLevel, in ItemUseLevels levels)
    {
        // NGemity tests the ceiling first, so an item bounded on both ends reports LimitMax.
        if (levels.MaxLevel != 0 && levels.MaxLevel < characterLevel)
        {
            return ResultCode.LimitMax;
        }

        return levels.MinLevel <= characterLevel ? ResultCode.Success : ResultCode.LimitMin;
    }

    /// <summary>
    /// <c>onUseItem</c>'s HuntaHolic gate (GameMessage.cpp 2015, after the Secroute, siege and event map gates): inside
    /// HuntaHolic, lobby or dungeon, an item flagged <c>FLAG_CANT_USE_IN_HUNTAHOLIC</c> (bit 22) is refused with
    /// <c>NotActableInHuntaholic</c>; outside it, one flagged <c>FLAG_USABLE_IN_ONLY_HUNTAHOLIC</c> (bit 23) with
    /// <c>ActableOnlyInHuntaholic</c> — the Bear Road potions, buffs and bombs (docs/packet-specs/socle-huntaholic.md §7).
    /// </summary>
    public static ResultCode CheckHuntaholic(int useFlags, bool inHuntaholic)
    {
        if (inHuntaholic)
        {
            return HasFlag(useFlags, ItemUseFlag.CantUseInHuntaholic) ? ResultCode.NotActableInHuntaholic : ResultCode.Success;
        }

        return HasFlag(useFlags, ItemUseFlag.UsableInOnlyHuntaholic) ? ResultCode.ActableOnlyInHuntaholic : ResultCode.Success;
    }

    /// <summary><see cref="ItemUseFlag"/> members are bit indexes of the bitset, not masks.</summary>
    public static bool HasFlag(int useFlags, ItemUseFlag flag) => (useFlags & (1 << (int)flag)) != 0;

    /// <summary>
    /// NGemity's <c>Player::UseItem</c> (Player.cpp:2179) erases one unit of every used item except
    /// <c>TYPE_USE</c> (6), the reusable type — the same value as <see cref="ItemBaseType.Use"/>,
    /// which holds 404 resources in the imported data.
    /// </summary>
    public static bool IsConsumedOnUse(ItemBaseType baseType)
    {
        return baseType != ItemBaseType.Use;
    }
}
