using System.Collections.Generic;

namespace Navislamia.Game.Services.Stats;

public interface IStateCatalog
{
    Buffs.PeriodicStateRule Periodic(int stateId) => default;
    IReadOnlyList<StatEffect> Resolve(int stateId, int stateLevel);

    /// <summary>
    /// Whether the state carries <c>StateTimeType.EraseOnRequest</c>: the client only builds a
    /// cancellation request for such a state, so the server refuses the others.
    /// </summary>
    /// <remarks>
    /// Backed by its own projection over <c>state_time_type</c>, <em>not</em> by the stat-effect map:
    /// that map is filtered to the decoded effect types while a player can cancel a state whose effect
    /// is not modelled.
    /// </remarks>
    bool IsEraseOnRequest(int stateId);

    /// <summary>Whether <paramref name="stateId"/> is a <c>StateResource</c>, stat state or not.</summary>
    bool Exists(int stateId);

    /// <summary>
    /// The HP/MP ratios of a <see cref="Navislamia.Game.DataAccess.Entities.Enums.StateEffectType.Resurrection"/>
    /// state. False for any other state.
    /// </summary>
    bool TryGetResurrection(int stateId, out ResurrectionStateValues values);

    /// <summary>
    /// What stacking and casting read of a state (<see cref="Casting.StateRule"/>);
    /// <see cref="Casting.StateRule.None"/> for an unknown id, which then shares no group with anything.
    /// </summary>
    Casting.StateRule GetRule(int stateId) => Casting.StateRule.None;

    /// <summary>
    /// The values of an <c>EF_RIDING</c> state (<see cref="RidingStateValues"/>), read from its
    /// <see cref="GetRule"/>; false for any other state.
    /// </summary>
    bool TryGetRiding(int stateId, out RidingStateValues values)
    {
        var rule = GetRule(stateId);
        if (rule.EffectType != RidingStateValues.EffectType)
        {
            values = default;
            return false;
        }

        values = RidingStateValues.From(rule.Values);
        return true;
    }
}
