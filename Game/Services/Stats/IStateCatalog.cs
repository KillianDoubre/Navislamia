using System.Collections.Generic;

namespace Navislamia.Game.Services.Stats;

public interface IStateCatalog
{
    IReadOnlyList<StatEffect> Resolve(int stateId, int stateLevel);

    /// <summary>Whether <paramref name="stateId"/> is a <c>StateResource</c>, stat state or not.</summary>
    bool Exists(int stateId);

    /// <summary>
    /// The HP/MP ratios of a <see cref="Navislamia.Game.DataAccess.Entities.Enums.StateEffectType.Resurrection"/>
    /// state. False for any other state.
    /// </summary>
    bool TryGetResurrection(int stateId, out ResurrectionStateValues values);
}
