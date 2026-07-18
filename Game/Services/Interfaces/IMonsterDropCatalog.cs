using System.Collections.Generic;

namespace Navislamia.Game.Services;

public interface IMonsterDropCatalog
{
    IReadOnlyList<DropEntry> GetDrops(int monsterId);

    /// <summary>Drop groups by id (negative). A negative <see cref="DropEntry.ItemId"/> resolves here.</summary>
    IReadOnlyDictionary<int, DropGroupEntry[]> Groups { get; }
}
