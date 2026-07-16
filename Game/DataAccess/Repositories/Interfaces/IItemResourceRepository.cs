using System.Collections.Generic;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

public readonly record struct ItemSortFields(int Id, int Category, int Group, int Rank);

public interface IItemResourceRepository
{
    IReadOnlyList<ItemSortFields> GetSortFields();
}
