using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

public readonly record struct StateEffectFields(int StateId, int EffectType, decimal[] Values);

public readonly record struct StateFlagFields(int StateId, StateTimeType StateTimeType);

/// <summary>The stacking fields of a state: duplicate groups, reiteration cap (a text column), time flags.</summary>
public readonly record struct StateRuleFields(int StateId, int[] DuplicateGroups, string ReiterationCount,
    StateTimeType StateTimeType, int EffectType, decimal[] Values);

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

    /// <summary>
    /// Every state with what <c>StructCreature::AddState</c> reads to stack it
    /// (docs/packet-specs/socle-lancer-competences.md §4).
    /// </summary>
    IReadOnlyList<StateRuleFields> GetStateRules();

    /// <summary>Every <c>StateResource</c> id, whatever its effect type.</summary>
    IReadOnlyList<int> GetStateIds();

    /// <summary>Every <c>StateResource</c> of <paramref name="effectType"/>, with its values.</summary>
    IReadOnlyList<StateEffectFields> GetStatesWithEffect(int effectType);
}
