using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Enums;
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

    public IReadOnlyList<ItemEffectFields> GetInstantSkillItems()
    {
        const short skill = (short)ItemEffectInstant.Skill;

        return _context.ItemResources
            .AsNoTracking()
            .Where(item => item.BaseTypes.Contains(skill) || item.OptTypes.Contains(skill))
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

    public IReadOnlyList<ItemWeightFields> GetWeightFields()
    {
        return _context.ItemResources
            .AsNoTracking()
            .Select(item => new ItemWeightFields((int)item.Id, item.Weight))
            .ToList();
    }

    public IReadOnlyList<ItemMatchFields> GetMatchFields()
    {
        return _context.ItemResources
            .AsNoTracking()
            .Select(item => new ItemMatchFields((int)item.Id, item.Group, item.ItemType, item.Rank,
                item.WearType))
            .ToList();
    }

    public IReadOnlyList<ItemUseFields> GetUseFields()
    {
        return _context.ItemResources
            .AsNoTracking()
            .Select(item => new { item.Id, item.UseMinLevel, item.UseMaxLevel, item.ItemBaseType,
                item.CoolTime, item.CoolTimeGroup, item.BaseTypes, item.BaseVar1, item.BaseVar2,
                item.OptTypes, item.OptVar1, item.OptVar2, item.StateId, item.StateLevel, item.StateTime })
            .AsEnumerable()
            .Select(item => new ItemUseFields((int)item.Id, item.UseMinLevel, item.UseMaxLevel, item.ItemBaseType,
                item.OptTypes != null && Array.IndexOf(item.OptTypes, (short)ItemEffectInstant.RenamePet) >= 0,
                item.CoolTime, item.CoolTimeGroup, item.BaseTypes, item.BaseVar1, item.OptTypes,
                item.OptVar1, item.StateId, item.StateLevel, item.StateTime, item.BaseVar2,
                item.OptVar2))
            .ToList();
    }

    public IReadOnlyList<ItemSellFields> GetSellPriceFields()
    {
        return _context.ItemResources
            .AsNoTracking()
            .Select(item => new ItemSellFields((int)item.Id, item.Rank, item.Price))
            .ToList();
    }

    public IReadOnlyList<ItemEtherealFields> GetEtherealFields()
    {
        return _context.ItemResources
            .AsNoTracking()
            .Select(item => new ItemEtherealFields((int)item.Id, item.WearType, item.EtherealDurability))
            .ToList();
    }

    public IReadOnlyList<ItemSoulstoneCraftFields> GetSoulstoneCraftFields()
    {
        return _context.ItemResources
            .AsNoTracking()
            .Select(item => new ItemSoulstoneCraftFields((int)item.Id, item.ItemBaseType, item.Group,
                item.ItemType, item.SocketCount, item.Price, item.BaseTypes, item.BaseVar1, item.OptTypes,
                item.OptVar1))
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

    public IReadOnlyList<ItemWearFields> GetWearFields()
    {
        return _context.ItemResources
            .AsNoTracking()
            .Select(item => new ItemWearFields((int)item.Id, item.WearType, item.Rank, item.UseMinLevel,
                item.UseMaxLevel))
            .ToList();
    }
}
