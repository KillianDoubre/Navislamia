using System.Collections.Generic;

namespace Navislamia.Game.Services.Stats;

public interface IStateCatalog
{
    IReadOnlyList<StatEffect> Resolve(int stateId, int stateLevel);
}
