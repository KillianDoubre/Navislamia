using System.Collections.Generic;

namespace Navislamia.Game.Services.Stats;

public interface IStateCatalog
{
    IReadOnlyList<StatEffect> Resolve(int stateId, int stateLevel);

    /// <summary>Whether <paramref name="stateId"/> is a <c>StateResource</c>, stat state or not.</summary>
    bool Exists(int stateId);
}
