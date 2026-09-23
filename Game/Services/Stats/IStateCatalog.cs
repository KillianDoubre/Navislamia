using System.Collections.Generic;

namespace Navislamia.Game.Services.Stats;

public interface IStateCatalog
{
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
}
