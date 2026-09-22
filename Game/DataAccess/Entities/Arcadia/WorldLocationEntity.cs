namespace Navislamia.Game.DataAccess.Entities.Arcadia;

/// <summary>
/// One row of the Arcadia <c>WorldLocation</c> table. The table holds <b>one row per
/// (id, weather_id, time_id)</b> — the 7.3 data has four rows per weather for every location, grouped by
/// <c>id</c> then <c>weather_id</c> then <c>time_id</c> — so <see cref="Id"/> alone is not unique and the
/// rows must be folded into one entry per location before use (see
/// <see cref="Navislamia.Game.Services.WorldLocationService"/>).
/// <para>
/// <see cref="X"/> and <see cref="Y"/> are the map cell of the location: the id is packed over them as
/// <c>x * 10000 + y * 100 + n</c>. No reference reads them, but any future position → location mapping
/// needs them.
/// </para>
/// <para>
/// The table also carries the sky/light columns and the unused apply_* flags; they are left unmapped until a
/// card needs them.
/// </para>
/// </summary>
public class WorldLocationEntity
{
    public int Id { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public short LocationType { get; set; }

    public int TimeId { get; set; }

    public int WeatherId { get; set; }

    /// <summary>Raw ratio of this (weather_id, time_id) pair. Its unit is not established anywhere.</summary>
    public short WeatherRatio { get; set; }

    /// <summary>
    /// Kept as the raw value of the column: NGemity multiplies it by 6000, but no document or client code
    /// confirms that unit, so the server gives it no temporal meaning.
    /// </summary>
    public short WeatherChangeTime { get; set; }
}
