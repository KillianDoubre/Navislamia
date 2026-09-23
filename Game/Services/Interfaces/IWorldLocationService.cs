namespace Navislamia.Game.Services;

/// <summary>
/// The weather locations of the world, for the 902/903 pair. Read-only and loaded once at startup.
/// </summary>
public interface IWorldLocationService
{
    /// <summary>Number of distinct locations (ids), not of <c>WorldLocation</c> rows.</summary>
    int Count { get; }

    /// <summary>
    /// Resolves a location id. The id travels as a <c>uint32</c> in TM_CS_GET_WEATHER_INFO (903), while
    /// <c>WorldLocation.id</c> is an <c>int32</c>: a value above <see cref="int.MaxValue"/> resolves to
    /// nothing rather than wrapping.
    /// </summary>
    bool TryGet(int locationId, out WorldLocation location);
}
