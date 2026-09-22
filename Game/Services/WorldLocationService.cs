using System.Collections.Frozen;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// The world's weather locations, built once from the Arcadia <c>WorldLocation</c> table.
/// <para>
/// The table has <b>one row per (id, weather_id, time_id)</b>, so a flat dictionary keyed by id would keep
/// only one of them. The fold reproduces NGemity's <c>WorldLocationManager::RegisterWorldLocation</c>
/// (<c>Chihiro/src/Map/WorldLocation.cpp:107-126</c>): the <b>first</b> row of an id fixes
/// <c>location_type</c> and <c>weather_change_time</c>, and every row writes
/// <c>weather_ratio[weather_id][time_id]</c>. In the 7.3 data that first row is the
/// <c>weather_id = 0, time_id = 0</c> one, which is why the repository hands the rows over ordered by
/// (id, weather_id, time_id).
/// </para>
/// </summary>
public class WorldLocationService : IWorldLocationService
{
    /// <summary>Size of NGemity's <c>weather_ratio[7][4]</c>.</summary>
    public const int WeatherCount = 7;

    /// <inheritdoc cref="WeatherCount"/>
    public const int TimeCount = 4;

    private readonly ILogger _logger = Log.ForContext<WorldLocationService>();
    private readonly FrozenDictionary<int, WorldLocation> _locations;

    public WorldLocationService(IWorldLocationRepository repository)
    {
        var rows = repository.GetAll();
        var locations = new Dictionary<int, WorldLocation>(rows.Count);

        foreach (var row in rows)
        {
            if (!locations.TryGetValue(row.Id, out var location))
            {
                location = new WorldLocation(row.Id, row.LocationType, row.X, row.Y, row.WeatherChangeTime);
                locations[row.Id] = location;
            }

            if (!location.TrySetWeatherRatio(row.WeatherId, row.TimeId, row.WeatherRatio))
            {
                _logger.Warning(
                    "WorldLocation {id}: row outside weather_id 0..{weatherCount} / time_id 0..{timeCount} (weather_id={weatherId}, time_id={timeId}), its ratio is ignored",
                    row.Id, WeatherCount - 1, TimeCount - 1, row.WeatherId, row.TimeId);
            }
        }

        _locations = locations.ToFrozenDictionary();
        _logger.Debug("Loaded {count} world locations out of {rows} WorldLocation rows", _locations.Count,
            rows.Count);
    }

    public int Count => _locations.Count;

    public bool TryGet(int locationId, out WorldLocation location) =>
        _locations.TryGetValue(locationId, out location);
}
