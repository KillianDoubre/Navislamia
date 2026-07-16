using System.Collections.Generic;

namespace Navislamia.Game.Services;

public interface IMonsterDropCatalog
{
    IReadOnlyList<DropEntry> GetDrops(int monsterId);
}
