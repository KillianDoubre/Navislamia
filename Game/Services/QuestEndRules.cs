using Navislamia.Game.Network.Packets;

namespace Navislamia.Game.Services;

/// <summary>
/// What <c>TM_CS_END_QUEST</c> (605) can be judged on before the character's state is read
/// (docs/packet-specs/605-end-quest.md §5, decisions of socle-cycle-quete.md §10).
/// </summary>
public static class QuestEndRules
{
    /// <summary><c>nOptionalReward</c> <c>-1</c>: no optional reward chosen, the client's own value.</summary>
    public const sbyte NoOptionalReward = -1;

    /// <summary>The 9.4 data carries six optional reward slots (<c>optional_reward_id1..6</c>).</summary>
    public const int OptionalRewardSlots = 6;

    /// <summary>
    /// A negative code is <see cref="ResultCode.NotActable"/>, as for 603 (<see cref="QuestDropRules"/>):
    /// no quest carries one. A slot outside <c>-1..5</c> is <see cref="ResultCode.InvalidArgument"/>
    /// (socle-cycle-quete.md §10 point 3); whether a slot in range is filled needs the catalogue (lot b4).
    /// </summary>
    public static ResultCode CheckRequest(int code, sbyte optionalReward)
    {
        if (code < 0)
        {
            return ResultCode.NotActable;
        }

        return optionalReward < NoOptionalReward || optionalReward >= OptionalRewardSlots
            ? ResultCode.InvalidArgument
            : ResultCode.Success;
    }
}
