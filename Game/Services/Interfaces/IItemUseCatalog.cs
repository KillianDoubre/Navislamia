namespace Navislamia.Game.Services;

/// <summary>
/// The template levels that gate a use, read from <c>use_min_level</c> / <c>use_max_level</c>
/// of the item resource.
/// </summary>
public readonly record struct ItemUseLevels(int MinLevel, int MaxLevel);

public interface IItemUseCatalog
{
    /// <summary>
    /// The use levels of an item resource. Returns <c>false</c> when the resource is unknown to the
    /// catalog (no item resource table loaded): the caller must then leave the use ungated rather
    /// than refuse an item it cannot judge.
    /// </summary>
    bool TryGetLevels(int itemResourceId, out ItemUseLevels levels);

    bool TryGetUseFields(int itemResourceId, out Navislamia.Game.DataAccess.Repositories.Interfaces.ItemUseFields fields);

    /// <summary>Whether the item carries the <c>RenamePet</c> use effect (120): it opens the pet name box.</summary>
    bool RenamesPet(int itemResourceId);

    /// <summary>
    /// Whether a successful use takes one unit off the stack. Only a reusable resource
    /// (<c>ItemBaseType.Use</c>) is spared; an unknown resource is consumed, the reference default.
    /// </summary>
    bool IsConsumedOnUse(int itemResourceId);

    /// <summary>The resource's <c>item_use_flag</c> bitset; 0 for an unknown resource.</summary>
    int UseFlags(int itemResourceId) => 0;
}
