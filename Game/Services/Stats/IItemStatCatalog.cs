using System.Collections.Generic;

namespace Navislamia.Game.Services.Stats;

public interface IItemStatCatalog
{
    IReadOnlyList<ItemStatEffect> GetEffects(int itemResourceId);
}
