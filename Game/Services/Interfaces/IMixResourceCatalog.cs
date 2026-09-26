using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Arcadia;

namespace Navislamia.Game.Services;

/// <summary>
/// The recipes of the crafting engine, loaded once at startup.
/// See docs/packet-specs/socle-artisanat-ressources.md §5.1 and §8 (L1a).
/// </summary>
public interface IMixResourceCatalog
{
    /// <summary>
    /// Every recipe in the order of the table (<c>Id</c> ascending). The order is meaningful: the
    /// resolution keeps the first rule that matches (NGemity <c>MixManager.cpp:244-246</c>).
    /// </summary>
    IReadOnlyList<MixResourceEntity> Rules { get; }

    /// <summary>
    /// One recipe by its <c>id</c>. Returns <c>false</c> for an unknown id — a recipe body that names a
    /// rule the import never carried.
    /// </summary>
    bool TryGetRule(long mixId, out MixResourceEntity rule);
}
