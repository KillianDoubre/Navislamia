using System.Linq;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.DataAccess.Repositories;

public class StatResourceRepository : IStatResourceRepository
{
    private readonly ArcadiaContext _context;

    public StatResourceRepository(DbContextOptions<ArcadiaContext> options)
    {
        _context = new ArcadiaContext(options);
    }

    public StatResourceEntity GetById(int id)
    {
        return _context.StatResources.FirstOrDefault(s => s.Id == id);
    }
}
