using System;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Services.Buffs;

namespace Navislamia.Game.Services.Casting;

/// <summary>What a state carries for stacking: its duplicate groups, its reiteration cap and its time flags.</summary>
public readonly record struct StateRule(int StateId, int[] DuplicateGroups, int ReiterationCount,
    StateTimeType TimeType, int EffectType, decimal[] Values)
{
    public static readonly StateRule None = new(0, Array.Empty<int>(), 0, (StateTimeType)0, 0,
        Array.Empty<decimal>());

    /// <summary><c>StructState::IsDuplicatedGroup</c>: one of this state's three groups is <paramref name="group"/>.</summary>
    public bool IsDuplicatedGroup(int group)
    {
        if (group == 0 || DuplicateGroups is null)
        {
            return false;
        }

        foreach (var own in DuplicateGroups)
        {
            if (own != 0 && own == group)
            {
                return true;
            }
        }

        return false;
    }

    public bool NotErasable => (TimeType & StateTimeType.NotErasable) != 0;
}

/// <summary>The outcome of adding a state to a unit's list.</summary>
public readonly record struct StackDecision(bool Refused, int RefreshIndex, int Level, IReadOnlyList<int> Removed)
{
    public static StackDecision Refuse() => new(true, -1, 0, Array.Empty<int>());
}

/// <summary>
/// The stacking rule of <c>StructCreature::AddState</c> (2012-11 <c>0x140093480</c>,
/// docs/packet-specs/socle-lancer-competences.md §4). For every state already on the unit that is the same
/// state or shares one of the new state's duplicate groups:
/// <list type="bullet">
/// <item>the same state with <c>reiteration_count</c> ≥ 2 first adds its level to the new one, capped;</item>
/// <item>an aura in place refuses;</item>
/// <item>of two states one only of which is <c>NotErasable</c>, the non-erasable one wins (a new one removes the
/// old, an old one refuses the new);</item>
/// <item>otherwise a higher level in place refuses, as does the same level lasting longer; anything weaker is
/// removed — the same state is refreshed in place instead.</item>
/// </list>
/// A refusal is the reference's result 9.
/// </summary>
public static class StateStacking
{
    public const uint NeverExpires = uint.MaxValue;

    public static StackDecision Decide(IReadOnlyList<ActiveBuff> existing, int stateId, StateRule rule, int level,
        uint endTick, Func<int, StateRule> rules, bool force = false)
    {
        var refresh = -1;
        var removed = new List<int>();

        for (var i = 0; i < existing.Count; i++)
        {
            var state = existing[i];
            var same = state.StateId == stateId;
            var other = same ? rule : rules(state.StateId);
            if (!same && !SharesAGroup(rule, other))
            {
                continue;
            }

            if (same)
            {
                refresh = i;
                if (rule.ReiterationCount >= 2)
                {
                    level = Math.Min(state.StateLevel + level, rule.ReiterationCount);
                }
            }

            if (force)
            {
                // An aura being switched on clears whatever it collides with.
                if (!same)
                {
                    removed.Add(i);
                }

                continue;
            }

            if (state.EndTick == NeverExpires)
            {
                // An aura is only ever switched off by its owner.
                return StackDecision.Refuse();
            }

            if (other.NotErasable != rule.NotErasable)
            {
                if (!rule.NotErasable)
                {
                    return StackDecision.Refuse();
                }

                if (!same)
                {
                    removed.Add(i);
                }

                continue;
            }

            if (state.StateLevel > level || state.StateLevel == level && Later(state.EndTick, endTick))
            {
                return StackDecision.Refuse();
            }

            if (!same)
            {
                removed.Add(i);
            }
        }

        return new StackDecision(false, refresh, level, removed);
    }

    private static bool SharesAGroup(StateRule incoming, StateRule present)
    {
        if (incoming.DuplicateGroups is null)
        {
            return false;
        }

        foreach (var group in incoming.DuplicateGroups)
        {
            if (present.IsDuplicatedGroup(group))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="a"/> ends after <paramref name="b"/> on the wrapping 32-bit clock.</summary>
    private static bool Later(uint a, uint b) => unchecked((int)(a - b)) > 0;
}
