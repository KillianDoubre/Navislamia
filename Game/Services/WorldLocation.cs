using System;
using System.Threading;

namespace Navislamia.Game.Services;

/// <summary>
/// One weather location, folded from every <c>WorldLocation</c> row sharing the same id — the in-memory
/// equivalent of NGemity's <c>WorldLocationManager</c> entry.
/// </summary>
public sealed class WorldLocation
{
    private const int TimeCount = WorldLocationService.TimeCount;
    private const int WeatherCount = WorldLocationService.WeatherCount;

    private readonly short[] _weatherRatio = new short[WeatherCount * TimeCount];

    public WorldLocation(int id, short locationType, int x, int y, short weatherChangeTime)
    {
        Id = id;
        LocationType = locationType;
        X = x;
        Y = y;
        WeatherChangeTime = weatherChangeTime;
    }

    public int Id { get; }

    /// <summary><c>location_type</c> of the location's first row.</summary>
    public short LocationType { get; }

    /// <summary>Map cell the id is packed over (<c>x * 10000 + y * 100 + n</c>).</summary>
    public int X { get; }

    /// <inheritdoc cref="X"/>
    public int Y { get; }

    /// <summary>
    /// Raw <c>weather_change_time</c> of the location's first row, in minutes (the official <c>LocationLoader</c>
    /// multiplies it by 6000 ticks, like NGemity); see <see cref="WeatherChangeTicks"/>.
    /// </summary>
    public short WeatherChangeTime { get; }

    private int _currentWeather;
    private long _lastWeatherChange;

    /// <summary>
    /// The weather the 902 carries for this location (<c>StructWorldLocation::current_weather</c>), rolled by
    /// <see cref="PlayerLocationService"/> every <see cref="WeatherChangeTicks"/> like
    /// <c>WorldLocationManager::onProcess</c>; Clear (0) until the first roll.
    /// </summary>
    public ushort CurrentWeather
    {
        get => (ushort)Volatile.Read(ref _currentWeather);
        set => Volatile.Write(ref _currentWeather, value);
    }

    /// <summary><c>last_changed_time</c>: the tick of the last roll, 0 before the first one.</summary>
    public uint LastWeatherChange
    {
        get => (uint)Interlocked.Read(ref _lastWeatherChange);
        set => Interlocked.Exchange(ref _lastWeatherChange, value);
    }

    /// <summary>
    /// <c>weather_change_time</c> is in minutes: <c>LocationLoader</c> registers it multiplied by 6000 ar_time ticks.
    /// </summary>
    public uint WeatherChangeTicks => (uint)Math.Max(0, (int)WeatherChangeTime) * 6000u;

    /// <summary>
    /// <c>weather_ratio[weather_id][time_id]</c>: the ratio of the folded row for that pair, 0 when the
    /// location has no row for it.
    /// </summary>
    public short GetWeatherRatio(int weatherId, int timeId) => _weatherRatio[weatherId * TimeCount + timeId];

    /// <summary>
    /// Writes one folded row. Refuses a <paramref name="weatherId"/> or <paramref name="timeId"/> outside the
    /// matrix: NGemity's C array write would run out of bounds on such a row, which is not reproducible here —
    /// the value is dropped and reported by the caller instead.
    /// </summary>
    public bool TrySetWeatherRatio(int weatherId, int timeId, short ratio)
    {
        if (weatherId < 0 || weatherId >= WeatherCount || timeId < 0 || timeId >= TimeCount)
        {
            return false;
        }

        _weatherRatio[weatherId * TimeCount + timeId] = ratio;
        return true;
    }
}
