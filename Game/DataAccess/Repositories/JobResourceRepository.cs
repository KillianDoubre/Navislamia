using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.DataAccess.Repositories;

public class JobResourceRepository : IJobResourceRepository
{
    private readonly ArcadiaContext _context;

    public JobResourceRepository(DbContextOptions<ArcadiaContext> options)
    {
        _context = new ArcadiaContext(options);
    }

    public IReadOnlyList<JobStatFields> GetJobStatIds()
    {
        return _context.JobResources
            .AsNoTracking()
            .Select(job => new JobStatFields((int)job.Id, job.StatId))
            .ToList();
    }
}
