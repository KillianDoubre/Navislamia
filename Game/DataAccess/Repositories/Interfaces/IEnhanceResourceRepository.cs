using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Arcadia;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

/// <summary>
/// The <c>EnhanceResource</c> rows: the enhancement chances the <c>mix_type</c> 101/103 recipes name
/// through <c>mix_value_01</c> (the <c>enhance_id</c>). The entity already exists; this repository only
/// makes the table reachable. See docs/packet-specs/socle-artisanat-ressources.md §5.2.
/// </summary>
public interface IEnhanceResourceRepository
{
    /// <summary>
    /// Every row, ordered by the composite key <c>(Id, LocalFlag)</c> the table carries
    /// (<c>EnhanceResources</c> PK, migration <c>Version0001_TheBeginning</c>): one <c>enhance_id</c>
    /// has up to six rows, one per <c>local_flag</c>.
    /// </summary>
    IReadOnlyList<EnhanceResourceEntity> GetAll();
}
