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
            .Select(item => new ItemSortFields((int)item.Id, (int)item.ItemBaseType, (int)item.Group, item.Rank))
            .ToList();
    }

    public IReadOnlyList<ItemEffectFields> GetEffectFields()
    {
        return _context.ItemResources
            .AsNoTracking()
            .Select(item => new ItemEffectFields((int)item.Id, item.ItemType, item.BaseTypes, item.BaseVar1,
                item.BaseVar2, item.OptTypes, item.OptVar1, item.OptVar2))
            .ToList();
    }

    public IReadOnlyList<ItemGroupFields> GetGroupFields()
    {
        return _context.ItemResources
            .AsNoTracking()
            .Select(item => new ItemGroupFields((int)item.Id, item.Group))
            .ToList();
    }

    public IReadOnlyList<ItemUseFields> GetUseFields()
    {
        return _context.ItemResources
            .AsNoTracking()
            .Select(item => new ItemUseFields((int)item.Id, item.UseMinLevel, item.UseMaxLevel,
                item.ItemBaseType))
            .ToList();
    }

    public IReadOnlyList<ItemSocketFields> GetSocketFields()
    {
        return _context.ItemResources
            .AsNoTracking()
            .Select(item => new ItemSocketFields((int)item.Id, item.SocketCount, item.ItemBaseType,
                item.ItemType, item.Group))
            .ToList();
    }
}
