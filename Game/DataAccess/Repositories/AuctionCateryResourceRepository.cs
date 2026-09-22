using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.DataAccess.Repositories;

/// <summary>
/// Loads the auction category tree. The columns are only carried across, never interpreted: their
/// meaning is not established (docs/packet-specs/socle-encheres.md §8.5).
/// </summary>
public class AuctionCateryResourceRepository : IAuctionCateryResourceRepository
{
    private readonly ArcadiaContext _context;

    public AuctionCateryResourceRepository(DbContextOptions<ArcadiaContext> options)
    {
        _context = new ArcadiaContext(options);
    }

    public IReadOnlyList<AuctionCateryResourceEntity> GetAll()
    {
        return _context.AuctionCateryResources
            .AsNoTracking()
            .OrderBy(catery => catery.CateryId)
            .ThenBy(catery => catery.SubCateryId)
            .Select(catery => new AuctionCateryResourceEntity
            {
                CateryId = catery.CateryId,
                SubCateryId = catery.SubCateryId,
                NameId = catery.NameId,
                LocalFlag = catery.LocalFlag,
                ItemGroup = catery.ItemGroup,
                ItemClass = catery.ItemClass
            })
            .ToList();
    }
}
