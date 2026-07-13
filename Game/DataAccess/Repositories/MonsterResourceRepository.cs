using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.DataAccess.Repositories;

public class MonsterResourceRepository : IMonsterResourceRepository
{
    private readonly ArcadiaContext _context;

    public MonsterResourceRepository(DbContextOptions<ArcadiaContext> options)
    {
        _context = new ArcadiaContext(options);
    }

    public IReadOnlyList<MonsterResourceEntity> GetByIds(IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0)
        {
            return Array.Empty<MonsterResourceEntity>();
        }

        var resourceIds = ids.Select(id => (long)id).ToArray();

        return _context.MonsterResources
            .AsNoTracking()
            .Where(resource => resourceIds.Contains(resource.Id))
            .Select(resource => new MonsterResourceEntity
            {
                Id = resource.Id,
                Level = resource.Level,
                Hp = resource.Hp,
                Race = resource.Race
            })
            .ToList();
    }
}
