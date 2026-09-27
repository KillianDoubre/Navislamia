using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Navislamia.Game.Services;

/// <summary>
/// Every decision the commercial storage (item shop container) has to take, without a database — the
/// only seam this repository can test, since the test suite builds no <c>TelecasterContext</c>
/// (docs/packet-specs/socle-stockage-commercial-conteneur.md §5.3).
/// <para>
/// The container is the table <c>PaidItems</c> (<see cref="PaidItemEntity"/>), read from the official
/// dump and not from any implementation: neither rzu nor NGemity models it, so nothing here is a ported
/// rule — each decision is derived and written down in the fiche (§5.1, §5.2, §5.3).
/// </para>
/// </summary>
public static class CommercialStorageRules
{
    /// <summary>
    /// The largest number of lines the container can announce: <c>count</c> of
    /// <c>TM_SC_COMMERCIAL_STORAGE_LIST</c> is a <c>uint16</c> (10004, §3.2). Beyond it the first
    /// <see cref="MaxEntries"/> rows by id are emitted, the rest is logged and left out — the frame
    /// builder raises past this bound, and an exception in the world entry sequence is worse than a
    /// bounded list (§5.3).
    /// </summary>
    public const int MaxEntries = ushort.MaxValue;

    /// <summary>
    /// The largest quantity one line can carry or one takeout can ask for: <c>rest_item_count</c> travels
    /// as the <c>uint16 count</c> of a 10004 line (§3.2) and the requested quantity as the <c>uint16
    /// count</c> of a 10005 (§3.3).
    /// </summary>
    public const int MaxCount = ushort.MaxValue;

    /// <summary>
    /// Whether a row belongs in the list of the given character (§5.2.3): not cancelled, still holding
    /// something to take, bought by the account of the reader and either aimed at that character or at no
    /// character at all. Never the uid alone — that is what makes a forged or foreign uid harmless.
    /// <para>
    /// The repository applies the same four conditions in SQL (<c>PaidItemRepository.VisibleRows</c>): a
    /// query cannot call this method, the two have to be read together.
    /// </para>
    /// </summary>
    public static bool IsVisible(PaidItemEntity row, long characterId, long accountId)
        => row is not null
           && !row.IsCancel
           && row.RestItemCount > 0
           && row.AccountId == accountId
           && (row.CharacterId is null || row.CharacterId == characterId);

    /// <summary>
    /// Whether the row can be named on the wire at all: <c>commercial_item_uid</c> is a <c>uint32</c>
    /// (§3.2), so a row whose identity key does not fit is <b>left out</b> instead of truncated — a
    /// truncation would produce a uid that may name another row (§5.2.1).
    /// </summary>
    public static bool IsAddressable(PaidItemEntity row) => row is not null && row.Id >= 0 && row.Id <= uint.MaxValue;

    /// <summary>
    /// Whether the row can be rendered: <c>code</c> is read as an item code by the client and against the
    /// item catalogue by the server, so a non-positive code would persist nothing readable (§5.3).
    /// </summary>
    public static bool IsRenderable(PaidItemEntity row) => row is not null && row.ItemCode > 0;

    /// <summary>One line of <c>TM_SC_COMMERCIAL_STORAGE_LIST</c>: <c>uid</c>, <c>code</c>, <c>count</c>.</summary>
    public static bool TryToEntry(PaidItemEntity row, out (uint Uid, int Code, ushort Count) entry)
    {
        entry = default;
        if (!IsAddressable(row) || !IsRenderable(row))
        {
            return false;
        }

        entry = ((uint)row.Id, row.ItemCode, ClampCount(row.RestItemCount));
        return true;
    }

    /// <summary>
    /// The rows this emission actually carries, in the order the frame writes them — ascending on the line
    /// id, so two emissions of the same content are byte-identical, and capped at <see cref="MaxEntries"/>
    /// (§3.2, §5.3). The counters and the line list are both computed from this single selection: that is
    /// what keeps <c>total_item_count</c> equal to the <c>count</c> of the 10004 sent with it.
    /// </summary>
    public static PaidItemEntity[] EmittedRows(IEnumerable<PaidItemEntity> rows)
        => (rows ?? Enumerable.Empty<PaidItemEntity>())
            .Where(row => row is not null && IsAddressable(row) && IsRenderable(row))
            .OrderBy(row => row.Id)
            .Take(MaxEntries)
            .ToArray();

    /// <summary>
    /// The lines of <c>TM_SC_COMMERCIAL_STORAGE_LIST</c>, one per emitted row, in the same order (§5.3).
    /// </summary>
    public static (uint Uid, int Code, ushort Count)[] BuildEntries(IEnumerable<PaidItemEntity> rows)
    {
        var entries = new List<(uint Uid, int Code, ushort Count)>();
        foreach (var row in EmittedRows(rows))
        {
            if (TryToEntry(row, out var entry))
            {
                entries.Add(entry);
            }
        }

        return entries.ToArray();
    }

    /// <summary>
    /// The two counters of <c>TM_SC_COMMERCIAL_STORAGE_INFO</c> (10003) for the rows about to be listed:
    /// <c>total_item_count</c> is the number of emitted rows, <c>new_item_count</c> the number of those
    /// whose <c>Confirmed</c> is zero. The second one is an hypothesis of the fiche (§7b): it reads a real
    /// column of the dump, and nothing in this repository ever writes it.
    /// </summary>
    public static (ushort Total, ushort New) BuildCounters(IReadOnlyList<PaidItemEntity> visibleRows)
    {
        if (visibleRows is null || visibleRows.Count == 0)
        {
            return (0, 0);
        }

        var total = Math.Min(visibleRows.Count, MaxEntries);
        var fresh = 0;
        for (var index = 0; index < total; index++)
        {
            if (visibleRows[index] is not null && visibleRows[index].Confirmed == 0)
            {
                fresh++;
            }
        }

        return ((ushort)total, (ushort)fresh);
    }

    /// <summary>
    /// Whether a takeout of <paramref name="count"/> units can be served from
    /// <paramref name="row"/>, and how many units the line could serve. A quantity of zero is refused, and
    /// so is anything above <paramref name="takeable"/> — the count of a 10005 carries no bound of its own
    /// (§3.3, §5.3).
    /// </summary>
    public static bool TryTakeable(PaidItemEntity row, ushort count, out ushort takeable)
    {
        takeable = row is null || row.RestItemCount <= 0 ? (ushort)0 : ClampCount(row.RestItemCount);
        return count > 0 && count <= takeable;
    }

    /// <summary>The units a line announces: <c>min(rest_item_count, 65535)</c> (§5.3).</summary>
    private static ushort ClampCount(int restItemCount)
        => (ushort)Math.Clamp(restItemCount, 0, MaxCount);
}
