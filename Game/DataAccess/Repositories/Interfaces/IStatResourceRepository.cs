using Navislamia.Game.DataAccess.Entities.Arcadia;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

public interface IStatResourceRepository
{
    StatResourceEntity GetById(int id);
}
