using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Arcadia;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

public interface IWorldLocationRepository
{
    /// <summary>
    /// Every <c>WorldLocation</c> row, ordered by <c>id</c>, then <c>weather_id</c>, then <c>time_id</c>: the
    /// fold into one entry per location takes <c>location_type</c> and <c>weather_change_time</c> from the
    /// first row of an id, so that order has to be deterministic instead of whatever the table returns.
    /// </summary>
    IReadOnlyList<WorldLocationEntity> GetAll();
}
