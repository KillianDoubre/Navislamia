using System;
using System.Collections.Generic;
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

    public IReadOnlyList<StatResourceEntity> GetByIds(IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0)
        {
            return Array.Empty<StatResourceEntity>();
        }

        var wanted = ids.Select(id => (long)id).ToArray();
        return _context.StatResources.AsNoTracking().Where(s => wanted.Contains(s.Id)).ToList();
    }
}
