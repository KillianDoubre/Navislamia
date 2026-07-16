using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.DataAccess.Repositories;

public class ItemResourceRepository : IItemResourceRepository
{
    private readonly ArcadiaContext _context;

    public ItemResourceRepository(DbContextOptions<ArcadiaContext> options)
    {
        _context = new ArcadiaContext(options);
    }

    public IReadOnlyList<ItemSortFields> GetSortFields()
    {
        return _context.ItemResources
            .AsNoTracking()
            .Select(item => new ItemSortFields((int)item.Id, (int)item.Group, (int)item.ItemType, item.Rank))
            .ToList();
    }
}
