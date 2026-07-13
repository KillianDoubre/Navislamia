using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.DataAccess.Repositories;

public class LevelResourceRepository : ILevelResourceRepository
{
    private readonly ArcadiaContext _context;

    public LevelResourceRepository(DbContextOptions<ArcadiaContext> options)
    {
        _context = new ArcadiaContext(options);
    }

    public IReadOnlyList<LevelResourceEntity> GetAll()
    {
        return _context.LevelResources
            .AsNoTracking()
            .Select(level => new LevelResourceEntity
            {
                Level = level.Level,
                NormalExp = level.NormalExp
            })
            .ToList();
    }
}
