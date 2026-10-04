using Navislamia.Game.Maps.Entities;

namespace Navislamia.Game.Maps;

public interface IMapService
{
    /// <summary>
    /// A loaded event area (<c>.nfe</c>) by id. The id is what it is in the file: the format carries
    /// no map or layer, and the loader keys a single world-wide dictionary on it.
    /// </summary>
    public bool TryGetEventArea(int eventAreaId, out EventAreaInfo eventArea);
    public bool TryGetEventArea(int eventAreaId, int areaIndex, out EventAreaInfo eventArea)
    {
        eventArea = null;
        return areaIndex == 0 && TryGetEventArea(eventAreaId, out eventArea);
    }

    /// <summary>
    /// Immutable snapshot of the loaded event areas, for callers that have to test a position
    /// against every area. It is replaced on load, never mutated, so a reader takes no lock and sees
    /// either the previous or the new set.
    /// </summary>
    EventAreaInfo[] GetEventAreas();

    /// <summary>The containing .nfl polygon with the lowest priority, or 0 when no polygon is loaded.</summary>
    int GetLocationId(float x, float y);

    void Start(string directory);
}
