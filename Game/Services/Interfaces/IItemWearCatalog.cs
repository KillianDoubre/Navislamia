using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.Services;

/// <summary>
/// The port of an item resource: its wear slot (<c>wear_type</c> of the <c>ItemResource</c> table),
/// which is the only source for the destination of a <c>TM_CS_PUTON_ITEM_SET</c> (281) handle — unlike
/// <c>TM_CS_PUTON_ITEM</c> (200), that request carries no position of its own — and the level
/// requirements both requests judge before equipping (<c>rank</c>, <c>use_min_level</c>,
/// <c>use_max_level</c>, sheet §5.2).
/// </summary>
public interface IItemWearCatalog
{
    /// <summary>
    /// The port an item resource declares. Returns <c>false</c> when the resource is unknown to the
    /// catalog (no item resource table loaded): the caller cannot then place the item anywhere, and
    /// must not fall back on the index of the handle in the request, which the sheet leaves open.
    /// </summary>
    bool TryGetWearFields(long resourceId, out ItemWearFields fields);
}
