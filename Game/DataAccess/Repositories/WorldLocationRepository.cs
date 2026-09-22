using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.DataAccess.Repositories;

public class WorldLocationRepository : IWorldLocationRepository
{
    private readonly DbContextOptions<ArcadiaContext> _options;

    public WorldLocationRepository(DbContextOptions<ArcadiaContext> options)
    {
        _options = options;
    }

    public IReadOnlyList<WorldLocationEntity> GetAll()
    {
        using var arcadiaContext = new ArcadiaContext(_options);

        return arcadiaContext.WorldLocations
            .AsNoTracking()
            .OrderBy(location => location.Id)
            .ThenBy(location => location.WeatherId)
            .ThenBy(location => location.TimeId)
            .Select(location => new WorldLocationEntity
            {
                Id = location.Id,
                X = location.X,
                Y = location.Y,
                LocationType = location.LocationType,
                TimeId = location.TimeId,
                WeatherId = location.WeatherId,
                WeatherRatio = location.WeatherRatio,
                WeatherChangeTime = location.WeatherChangeTime
            })
            .ToList();
    }
}
