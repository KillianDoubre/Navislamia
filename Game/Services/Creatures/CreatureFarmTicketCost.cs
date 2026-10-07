using System.Collections.Generic;

namespace Navislamia.Game.Services.Creatures;

/// <summary>
/// The number of tickets a deposition costs, keyed on the summon's <c>(rate, form, enhance_level)</c> — the
/// official <c>GameContent::GetCreatureFarmTicketCount</c> (<c>GameContent.cpp:2910-2925</c>, called with
/// <c>GetRate()</c>, <c>GetTransformLevel()</c> then <c>GetEnhance()</c>, <c>GameMessage.cpp:11866</c>).
/// <para>
/// The table itself (72 rows, <c>db_creaturefarm.rdb</c>) is not loaded by this lot: its provenance is a data
/// decision reserved to Killian (docs/packet-specs/6002-foster-creature.md §5.5, A VERIFIER 1). The seam exists
/// so the deposit can be validated without it, and <b>0</b> is the value it owes for an absent key — the
/// reference's own answer (<c>:2924</c>) — which refuses the deposition instead of accepting an unpriced frame.
/// </para>
/// </summary>
public interface ICreatureFarmTicketCost
{
    /// <summary>The tickets a deposition of that key costs; 0 when the key has no row.</summary>
    int GetTicketCount(int rate, int form, int enhanceLevel);
}

/// <summary>
/// One row of the cost table, in the column order the 7.3 binary compares: <c>rate</c>, <c>form</c>,
/// <c>enhance_level</c>, then the <c>ticket_count</c> it returns.
/// </summary>
public readonly record struct CreatureFarmTicketCostRow(int Rate, int Form, int EnhanceLevel, int TicketCount);

/// <summary>
/// The cost table as loaded rows: empty until the socle lot feeds it, in which case every lookup answers 0 —
/// the reference's "no row" answer — and every deposition is refused. A row may not lower a cost below 1: the
/// decoded table's smallest value is 1, and 0 must stay the reserved "absent key" answer.
/// </summary>
public sealed class CreatureFarmTicketCost : ICreatureFarmTicketCost
{
    private readonly Dictionary<(int Rate, int Form, int EnhanceLevel), int> _costs = new();

    /// <param name="rows">
    /// The 72 decoded rows (docs/packet-specs/6002-foster-creature.md §5.5). Null and empty are equivalent: no
    /// row matches, so every key answers 0.
    /// </param>
    public CreatureFarmTicketCost(IReadOnlyList<CreatureFarmTicketCostRow> rows = null)
    {
        foreach (var row in rows ?? System.Array.Empty<CreatureFarmTicketCostRow>())
        {
            _costs[(row.Rate, row.Form, row.EnhanceLevel)] = row.TicketCount;
        }
    }

    public int GetTicketCount(int rate, int form, int enhanceLevel) =>
        _costs.TryGetValue((rate, form, enhanceLevel), out var cost) ? cost : 0;
}
