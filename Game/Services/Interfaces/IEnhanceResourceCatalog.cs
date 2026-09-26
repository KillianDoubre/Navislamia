using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;

namespace Navislamia.Game.Services;

/// <summary>
/// The enhancement chances, loaded once at startup and indexed by the composite key of the table
/// <c>(enhance_id, local_flag)</c> — 36 ids have six rows, one per region.
/// See docs/packet-specs/socle-artisanat-ressources.md §5.2.
/// </summary>
public interface IEnhanceResourceCatalog
{
    /// <summary>
    /// One row by its full key. The <c>local_flag</c> policy (which region a 7.3 server reads, and what
    /// to do with <c>456</c>) is <em>not</em> decided: the caller passes the flag it wants and gets the
    /// row for that flag, nothing else.
    /// </summary>
    bool TryGetRow(long enhanceId, LocalFlag localFlag, out EnhanceResourceEntity row);
}
