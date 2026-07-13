using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Arcadia;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

public interface INpcResourceRepository
{
    IReadOnlyList<NpcResourceEntity> GetAll();
}
