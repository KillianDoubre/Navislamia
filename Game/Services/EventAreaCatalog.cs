using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;

namespace Navislamia.Game.Services;

public sealed class EventAreaCatalog
{
    public IReadOnlyDictionary<int, EventAreaResourceEntity> Rows { get; }
    public EventAreaCatalog(DbContextOptions<ArcadiaContext> options)
    {
        using var db = new ArcadiaContext(options);
        Rows = db.EventAreaResources.AsNoTracking().ToArray().ToDictionary(r => checked((int)r.Id));
    }
    public EventAreaCatalog(IEnumerable<EventAreaResourceEntity> rows) => Rows = rows.ToDictionary(r => checked((int)r.Id));
}
