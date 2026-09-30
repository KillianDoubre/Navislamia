using Navislamia.Game.DataAccess.Entities.Enums;

namespace Navislamia.Game.Services;

/// <summary>
/// The wear slot of an item resource (<c>wear_type</c> of the <c>ItemResource</c> table). It is the
/// only source for the destination of a <c>TM_CS_PUTON_ITEM_SET</c> (281) handle: unlike
/// <c>TM_CS_PUTON_ITEM</c> (200), that request carries no position of its own.
/// </summary>
public interface IItemWearCatalog
{
    /// <summary>
    /// The wear type an item resource declares. Returns <c>false</c> when the resource is unknown to
    /// the catalog (no item resource table loaded): the caller cannot then place the item anywhere,
    /// and must not fall back on the index of the handle in the request, which the sheet leaves open.
    /// </summary>
    bool TryGetWearType(long resourceId, out ItemWearType wearType);
}
