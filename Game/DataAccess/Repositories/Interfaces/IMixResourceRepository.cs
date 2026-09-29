using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Arcadia;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

/// <summary>
/// The 754-ish <c>MixResource</c> recipes of the crafting engine. The row is the rule: the resolution
/// walks the rules in the order of the table and keeps the first one that matches, so the order of
/// <see cref="GetAll"/> is part of the contract (NGemity <c>MixManager.cpp:244-246</c>).
/// See docs/packet-specs/socle-artisanat-ressources.md §5.1 and §8 (L1a).
/// </summary>
public interface IMixResourceRepository
{
    /// <summary>
    /// Every recipe, ordered by <c>Id</c>. The reference table declares no order at all
    /// (<c>ArcadiaSchemaPSQL.sql:471-582</c>: no key, no index); <c>Id</c> is the only stable one, and
    /// it is also what a SQL Server dump returns for a heap it has never reordered.
    /// </summary>
    IReadOnlyList<MixResourceEntity> GetAll();
}
