using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.DataAccess.Repositories;

public class EnhanceResourceRepository : IEnhanceResourceRepository
{
    private readonly ArcadiaContext _context;

    public EnhanceResourceRepository(DbContextOptions<ArcadiaContext> options)
    {
        _context = new ArcadiaContext(options);
    }

    public IReadOnlyList<EnhanceResourceEntity> GetAll()
    {
        return _context.EnhanceResources
            .AsNoTracking()
            .OrderBy(enhance => enhance.Id)
            .ThenBy(enhance => enhance.LocalFlag)
            .ToList();
    }
}
