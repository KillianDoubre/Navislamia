using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

public readonly record struct StateEffectFields(int StateId, int EffectType, decimal[] Values);

public readonly record struct StateFlagFields(int StateId, StateTimeType StateTimeType);

public interface IStateResourceRepository
{
    IReadOnlyList<StateEffectFields> GetStatStates();

    /// <summary>
    /// The state ids whose <c>state_time_type</c> carries <c>EraseOnRequest</c>: the states a player may
    /// cancel from the client's state window.
    /// </summary>
    /// <remarks>
    /// Deliberately a second projection: <see cref="GetStatStates"/> is filtered to the two decoded
    /// effect types, while the cancellable states are not a subset of them.
    /// </remarks>
    IReadOnlyList<int> GetEraseOnRequestStateIds();
}
