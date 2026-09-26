using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.DataAccess.Repositories;

/// <summary>
/// Loads the crafting recipes. Unlike <see cref="AuctionCateryResourceRepository"/> there is no column
/// projection: the projected columns would be the 109 columns themselves, in the order of the row. The
/// positional reading belongs to <c>MixResourceRules</c>, not to the query.
/// </summary>
public class MixResourceRepository : IMixResourceRepository
{
    private readonly ArcadiaContext _context;

    public MixResourceRepository(DbContextOptions<ArcadiaContext> options)
    {
        _context = new ArcadiaContext(options);
    }

    public IReadOnlyList<MixResourceEntity> GetAll()
    {
        return _context.MixResources
            .AsNoTracking()
            .OrderBy(mix => mix.Id)
            .ToList();
    }
}
