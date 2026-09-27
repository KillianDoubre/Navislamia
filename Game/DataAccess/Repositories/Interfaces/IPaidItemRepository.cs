using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Services;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

/// <summary>
/// The commercial storage (item shop) container: the rows of one character, and the real takeout that
/// leaves them (docs/packet-specs/socle-stockage-commercial-conteneur.md §5.4).
/// <para>
/// The character name is the entry point rather than an account id, exactly like
/// <see cref="IStorageRepository"/>: the account that may take from the container is the one of the
/// character in session, read from the character table instead of from the session state, so ownership has
/// a single source of truth (§5.6 point 6).
/// </para>
/// <para>
/// No method resolves a row from its uid alone: the owner conditions travel with every query (§5.2.3), so
/// a forged or foreign uid can only ever answer "no such row".
/// </para>
/// </summary>
public interface IPaidItemRepository
{
    /// <summary>
    /// The rows the character may see, ascending on the line id: those of its account that are neither
    /// cancelled nor empty and that are aimed at it or at no character at all.
    /// </summary>
    Task<PaidItemEntity[]> GetVisibleAsync(string characterName);

    /// <summary>
    /// One visible row by its uid, read only. <c>null</c> covers every refusal: unknown uid, foreign
    /// account, another character's delivery, cancelled row, empty row.
    /// </summary>
    Task<PaidItemEntity?> ResolveAsync(string characterName, uint uid);

    /// <summary>
    /// Takes <paramref name="count"/> units off the line: <c>rest_item_count</c> is lowered and the
    /// <c>taken_*</c> columns of the taker are stamped. Nothing else is written — the row is never deleted
    /// and <c>confirmed</c> is never touched (§5.6).
    /// </summary>
    Task<CommercialTakeoutResult> ConsumeAsync(string characterName, uint uid, ushort count);
}
