using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.Services;

/// <summary>
/// The four item-resource columns the crafting conditions compare, for every item resource. Loading the
/// whole table once is what NGemity does with its item templates (<c>ObjectMgr.cpp:1108-1146</c>), and the
/// conditions are read per material on the resolution path, so the lookup has to be in memory.
/// See docs/packet-specs/socle-artisanat-ressources.md §6.1 (L1b).
/// </summary>
public interface IItemMatchCatalog
{
    /// <summary>
    /// The template columns of an item resource. Returns <c>false</c> when the resource is unknown to the
    /// catalog: the caller must then refuse the frame rather than judge conditions on a zeroed row.
    /// </summary>
    bool TryGetFields(long resourceId, out ItemMatchFields fields);
}
