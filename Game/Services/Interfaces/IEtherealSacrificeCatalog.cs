using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.Services;

/// <summary>
/// What an item resource can give when it is sacrificed to charge an Ethereal Stone
/// (<c>TM_CS_TRANSMIT_ETHEREAL_DURABILITY</c>, 263): its wear type, which decides whether the object is
/// an equipment at all, and the ethereal durability the resource can carry.
/// The catalog reads the two columns once; it interprets neither (§5.4 of the packet's fiche).
/// </summary>
public interface IEtherealSacrificeCatalog
{
    /// <summary>
    /// The sacrifice facts of an item resource. Returns <c>false</c> when the resource is unknown to the
    /// catalog: the caller must then leave the nature clause ungated rather than refuse an object it
    /// cannot judge, the convention <see cref="IItemGroupCatalog"/> documents.
    /// </summary>
    bool TryGet(long resourceId, out ItemEtherealFields fields);
}
