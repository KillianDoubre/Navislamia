using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.DataAccess.Repositories;

/// <summary>
/// The reading surface of the quest catalogue: the two Arcadia tables the reference splits it into
/// (<c>ObjectMgr::LoadQuestResource</c>, <c>LoadQuestLinkResource</c>), carried across and never
/// interpreted — which is what the columns of the definition judge is not established
/// (docs/packet-specs/socle-cycle-quete.md §5.1, §7.4). Both tables belong to one catalogue and are read
/// together by the same caller, so they share a repository, as <see cref="WorldRepository"/> carries the
/// several tables of the world.
/// </summary>
public class QuestCatalogueRepository : IQuestCatalogueRepository
{
    private readonly ArcadiaContext _context;

    public QuestCatalogueRepository(DbContextOptions<ArcadiaContext> options)
    {
        _context = new ArcadiaContext(options);
    }

    public IReadOnlyList<QuestResourceEntity> GetResources()
    {
        return _context.QuestResources
            .AsNoTracking()
            .OrderBy(resource => resource.Id)
            .ToList();
    }

    public IReadOnlyList<QuestLinkResourceEntity> GetLinks()
    {
        return _context.QuestLinkResources
            .AsNoTracking()
            .OrderBy(link => link.NpcId)
            .ThenBy(link => link.QuestId)
            .ToList();
    }
}
