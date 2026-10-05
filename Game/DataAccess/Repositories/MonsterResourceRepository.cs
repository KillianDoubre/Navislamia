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
                Race = resource.Race,
                FirstAttack = resource.FirstAttack,
                VisibleRange = resource.VisibleRange,
                ChaseRange = resource.ChaseRange,
                AttackRange = resource.AttackRange,
                RunSpeed = resource.RunSpeed,
                Size = resource.Size,
                Scale = resource.Scale,
                StatId = resource.StatId,
                Mp = resource.Mp,
                AttackPoint = resource.AttackPoint,
                MagicPoint = resource.MagicPoint,
                Defence = resource.Defence,
                MagicDefence = resource.MagicDefence,
                AttackSpeed = resource.AttackSpeed,
                MagicSpeed = resource.MagicSpeed,
                Accuracy = resource.Accuracy,
                Avoid = resource.Avoid,
                MagicAccuracy = resource.MagicAccuracy,
                MagicAvoid = resource.MagicAvoid,
                MonsterSkillLinkId = resource.MonsterSkillLinkId,
                MonsterGroup = resource.MonsterGroup,
                Grp = resource.Grp,
                GroupFirstAttack = resource.GroupFirstAttack,
                // The reward columns of the death (docs/packet-specs/socle-recompenses-monstres.md §10.1):
                // everything absent from this projection is lost, since a projected entity is not tracked.
                Exp = resource.Exp,
                Jp = resource.Jp,
                GoldDropPercentage = resource.GoldDropPercentage,
                GoldMin = resource.GoldMin,
                GoldMax = resource.GoldMax,
                ChaosDropPercentage = resource.ChaosDropPercentage,
                ChaosMin = resource.ChaosMin,
                ChaosMax = resource.ChaosMax,
                // MonsterSpawns:UseSecondaryRewards (game.change_monster_drop_set) reads the *2 set.
                Exp2 = resource.Exp2,
                Jp2 = resource.Jp2,
                GoldMin2 = resource.GoldMin2,
                GoldMax2 = resource.GoldMax2,
                ChaosMin2 = resource.ChaosMin2,
                ChaosMax2 = resource.ChaosMax2,
                // TamingRules reads them through MonsterInstance; without them every monster is untamable.
                TamingId = resource.TamingId,
                TamingPercentage = resource.TamingPercentage
            })
            .ToList();
    }
}
