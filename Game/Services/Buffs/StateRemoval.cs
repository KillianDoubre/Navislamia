using System.Collections.Generic;
using Navislamia.Game.Network.Packets;

namespace Navislamia.Game.Services.Buffs;

/// <summary>The state a removal request resolves to, plus the aura group to switch off with it.</summary>
/// <param name="Index">Position of the entry in <c>ActiveBuffs</c>, so the caller removes that exact one.</param>
/// <param name="Buff">The resolved state.</param>
/// <param name="ToggleGroup">
/// The aura group this state belongs to, or null when it is not a toggled aura. Nullable because group 0 is a
/// real group, not "no group": auras sharing it exclude each other.
/// </param>
public readonly record struct StateRemovalPlan(int Index, ActiveBuff Buff, int? ToggleGroup);

/// <summary>
/// The rule behind <c>TM_CS_REQUEST_REMOVE_STATE</c> (408): the player cancels one state from the client's
/// state window, and the frame carries only the creature handle and the state code.
/// </summary>
/// <remarks>
/// Ported from the reference server's <c>Unit::RemoveState(StateCode, state_level)</c>, minus its
/// <c>state_level</c>: the 408 has no level field, so the first entry matching the code wins. That is
/// deterministic because <c>ApplyState</c> replaces the existing entry for the same state id, so two
/// instances of one code never coexist.
/// <para>
/// Kept pure so the refusals are testable without a socket: the caller owns the lock and the packets.
/// </para>
/// </remarks>
public static class StateRemoval
{
    /// <summary>
    /// Resolves a removal request against the player's active states.
    /// </summary>
    /// <param name="target">The handle the state window was bound to.</param>
    /// <param name="characterHandle">The handle of the player issuing the request.</param>
    /// <param name="stateCode">The <c>StateId</c> to cancel.</param>
    /// <param name="buffs">The player's active states.</param>
    /// <param name="auras">Active auras by toggle group.</param>
    /// <param name="eraseOnRequest">Whether the state carries <c>StateTimeType.EraseOnRequest</c>.</param>
    public static bool TryResolve(uint target, uint characterHandle, int stateCode,
        IReadOnlyList<ActiveBuff> buffs, IReadOnlyDictionary<int, int> auras, bool eraseOnRequest,
        out StateRemovalPlan plan, out ResultCode error)
    {
        plan = default;

        // Only the player's own window is serviced: summons and other creatures are not modelled, and the
        // client never builds the message for a null handle.
        if (target == 0 || target != characterHandle)
        {
            error = ResultCode.NotExist;
            return false;
        }

        var index = IndexOf(buffs, stateCode);
        if (index < 0)
        {
            error = ResultCode.NotExist;
            return false;
        }

        // No sentinel means "all states": a code that is not cancellable is refused, never expanded.
        if (!eraseOnRequest)
        {
            error = ResultCode.NotActable;
            return false;
        }

        plan = new StateRemovalPlan(index, buffs[index], AuraGroup(auras, buffs[index].SkillId));
        error = ResultCode.Success;
        return true;
    }

    private static int IndexOf(IReadOnlyList<ActiveBuff> buffs, int stateCode)
    {
        for (var i = 0; i < buffs.Count; i++)
        {
            if (buffs[i].StateId == stateCode)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The toggle group whose active aura produced <paramref name="skillId"/>, or null when the state did not
    /// come from an aura. The smallest matching group wins, so the answer never depends on dictionary order.
    /// </summary>
    private static int? AuraGroup(IReadOnlyDictionary<int, int> auras, int skillId)
    {
        // A state put by /buff carries no skill, and no aura is ever recorded under skill 0.
        if (skillId == 0)
        {
            return null;
        }

        int? group = null;
        foreach (var pair in auras)
        {
            if (pair.Value == skillId && (group is null || pair.Key < group))
            {
                group = pair.Key;
            }
        }

        return group;
    }
}
