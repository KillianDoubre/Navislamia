using System;
using Navislamia.Game.DataAccess.Entities.Enums;

namespace Navislamia.Game.Services;

/// <summary>
/// The rules the drop path judges before removing anything. The summon card guard and the count clamp
/// are ported from <c>WorldSession::onDropItem</c> in NGemity
/// (Chihiro/src/Network/GameNetwork/WorldSession.cpp:1433-1443); the equipped-item refusal is our own,
/// neither reference has it. A "may not drop" flag stays absent: it is not exploitable in this
/// repository (see docs/packet-specs/203-drop-item.md §5.3 and §7).
/// </summary>
public static class GroundItemDropRules
{
    /// <summary>
    /// NGemity's <c>FlagBits::ITEM_FLAG_SUMMON = 0x80000000</c> (ItemTemplate.hpp), the same mask the
    /// client reads in the inventory record's flag field.
    /// </summary>
    /// <remarks>
    /// Careful: the repository's <see cref="ItemFlag.Summon"/> member holds the bit <em>index</em> 31,
    /// not that mask — the enum mixes NGemity's <c>FlagBits</c> values as indices (Card = 0 for 0x01,
    /// Summon = 31 for 0x80000000). A stored flag is read here as the retail bitset, so the guard fires
    /// on bit 31; a producer writing the bare enum member instead of the mask would not be recognised.
    /// </remarks>
    public const uint SummonFlagMask = 0x80000000u;

    /// <summary>
    /// A summon card already bound to a creature may not be dropped. <see cref="ItemFlag.None"/> is the
    /// <c>-1</c> sentinel for "no flag" and aliases to every bit set once read as a <c>uint</c>, so it is
    /// excluded explicitly; an unknown group (null) leaves the rule ungated rather than refuse.
    /// </summary>
    public static bool IsBoundSummonCard(ItemFlag flag, ItemGroup? group)
    {
        if (flag == ItemFlag.None || group != ItemGroup.Summoncard)
        {
            return false;
        }

        return (unchecked((uint)flag) & SummonFlagMask) != 0;
    }

    /// <summary>
    /// A worn item may not be dropped. Neither reference refuses it (NGemity's <c>IsDropable</c> never
    /// reads the wear slot), but removing it here would delete the row while the model and the stats
    /// still show it worn: nothing on the drop path sends <c>TS_SC_WEAR_INFO</c> (202) or
    /// <c>TS_SC_ITEM_WEAR_INFO</c> (287). The player unequips first.
    /// </summary>
    public static bool IsEquipped(ItemWearType wearInfo)
    {
        return wearInfo != ItemWearType.None;
    }

    /// <summary>
    /// Units to remove from the stack: nothing for a request of zero or less, and a request larger than
    /// the stack is clamped to it (the repository clamps everywhere, and the answer packet carries no
    /// corrected count — the erase notification carries what was really removed).
    /// </summary>
    public static long ResolveDropCount(int requestedCount, long stackCount)
    {
        return requestedCount <= 0 ? 0 : Math.Min(requestedCount, stackCount);
    }
}
