using Navislamia.Game.Maps.Entities;

namespace Navislamia.Game.Maps;

public interface IMapService
{
    /// <summary>
    /// A loaded event area (<c>.nfe</c>) by id. The id is what it is in the file: the format carries
    /// no map or layer, and the loader keys a single world-wide dictionary on it.
    /// </summary>
    public bool TryGetEventArea(int eventAreaId, out EventAreaInfo eventArea);

    /// <summary>
    /// Immutable snapshot of the loaded event areas, for callers that have to test a position
    /// against every area. It is replaced on load, never mutated, so a reader takes no lock and sees
    /// either the previous or the new set.
    /// </summary>
    EventAreaInfo[] GetEventAreas();

    void Start(string directory);
}
