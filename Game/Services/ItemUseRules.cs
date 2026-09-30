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
    /// NGemity's <c>Player::UseItem</c> (Player.cpp:2179) erases one unit of every used item except
    /// <c>TYPE_USE</c> (6), the reusable type — the same value as <see cref="ItemBaseType.Use"/>,
    /// which holds 404 resources in the imported data.
    /// </summary>
    public static bool IsConsumedOnUse(ItemBaseType baseType)
    {
        return baseType != ItemBaseType.Use;
    }
}
