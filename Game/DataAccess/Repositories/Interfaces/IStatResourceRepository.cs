using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Arcadia;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

public interface IStatResourceRepository
{
    StatResourceEntity GetById(int id);

    /// <summary>The rows among <paramref name="ids"/> that exist, untracked, in one query.</summary>
    IReadOnlyList<StatResourceEntity> GetByIds(IReadOnlyCollection<int> ids);
}
