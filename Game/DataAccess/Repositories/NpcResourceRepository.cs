using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.DataAccess.Repositories;

public class NpcResourceRepository : INpcResourceRepository
{
    private readonly ArcadiaContext _context;

    public NpcResourceRepository(DbContextOptions<ArcadiaContext> options)
    {
        _context = new ArcadiaContext(options);
    }

    public IReadOnlyList<NpcResourceEntity> GetAll()
    {
        return _context.NpcResources.AsNoTracking().ToList();
    }
}
