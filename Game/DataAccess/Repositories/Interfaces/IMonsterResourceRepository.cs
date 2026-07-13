using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Arcadia;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

public interface IMonsterResourceRepository
{
    IReadOnlyList<MonsterResourceEntity> GetByIds(IReadOnlyCollection<int> ids);
}
