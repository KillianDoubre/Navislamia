using Navislamia.Game.Network.Packets;

namespace Navislamia.Game.Services;

/// <summary>
/// What the server may judge about <c>TM_CS_DROP_QUEST</c> (603) before touching the player's state.
/// NGemity knows exactly one eligibility condition — the quest sits in the player's own active list
/// (<c>Player::DropQuest</c>, Chihiro/src/Entities/Player/Player.cpp:3173-3187) — and the lookup itself
/// decides that, so no abandon flag, cool-down or quest-type exclusion is invented here (fiche §8.1).
/// </summary>
public static class QuestDropRules
{
    /// <summary>
    /// The frame's own sign is the only thing left to judge: <c>code</c> is an <c>int32_t</c> in both
    /// references, and a negative value can never name a stored quest. It is refused as
    /// <see cref="ResultCode.NotActable"/> — NGemity's answer for a quest the player does not carry —
    /// rather than being folded into a large unsigned code (fiche §3.1).
    /// </summary>
    public static ResultCode CheckRequest(int code)
    {
        return code < 0 ? ResultCode.NotActable : ResultCode.Success;
    }
}
