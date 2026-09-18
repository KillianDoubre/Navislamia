using Navislamia.Game.Network.Packets;

namespace Navislamia.Game.Services;

/// <summary>
/// The level gate of an item use, ported from <c>Player::IsUseableItem</c> in NGemity
/// (Chihiro/src/Entities/Player/Player.cpp:2088-2107): only the template's own use levels are
/// judged. The cool-down and target level checks of the same function are out of scope, as is the
/// movement check of <c>WorldSession::onUseItem</c>: the fiche excludes them and the repository
/// keeps neither item cool-downs nor a movement state.
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
}
