using Navislamia.Game.DataAccess.Entities.Enums;

namespace Navislamia.Game.Services;

/// <summary>
/// The item group of an item resource, as the drop guard needs it (<c>group</c> column of the
/// <c>ItemResource</c> table, the <c>GetItemGroup()</c> of NGemity's item template).
/// </summary>
public interface IItemGroupCatalog
{
    /// <summary>
    /// The group of an item resource. Returns <c>false</c> when the resource is unknown to the
    /// catalog: the caller must then leave the group-gated rule ungated rather than refuse an item
    /// it cannot judge.
    /// </summary>
    bool TryGetGroup(long resourceId, out ItemGroup group);
}
