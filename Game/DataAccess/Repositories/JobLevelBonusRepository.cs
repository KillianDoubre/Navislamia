using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.DataAccess.Repositories;

public class JobLevelBonusRepository : IJobLevelBonusRepository
{
    private readonly ArcadiaContext _context;

    public JobLevelBonusRepository(DbContextOptions<ArcadiaContext> options)
    {
        _context = new ArcadiaContext(options);
    }

    public IReadOnlyList<JobLevelBonusFields> GetAll()
    {
        return _context.JobLevelBonuses
            .AsNoTracking()
            .Select(bonus => new JobLevelBonusFields((int)bonus.Id,
                bonus.Strength, bonus.Vitality, bonus.Dexterity, bonus.Agility,
                bonus.Intelligence, bonus.Wisdom, bonus.Luck,
                bonus.DefaultStrength, bonus.DefaultVitality, bonus.DefaultDexterity, bonus.DefaultAgility,
                bonus.DefaultIntelligence, bonus.DefaultWisdom, bonus.DefaultLuck))
            .ToList();
    }
}
