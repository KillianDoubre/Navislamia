using System.Collections.Generic;

namespace Navislamia.Configuration.Options;

/// <summary>
/// The ticket-cost table of the creature farm (<c>CreatureFarmResource</c>), exported from the 7.3 client's own
/// <c>db_creaturefarm.rdb</c> by <c>tools/export_creature_farm_costs.py</c> into
/// <c>DevConsole/creature-farm-costs.73.json</c>. Read with System.Text.Json at startup like the other
/// catalogues, never through the configuration provider.
/// <para>
/// The table is not on the wire: it is the <c>(rate, form, enhance_level)</c> line of the card being deposited,
/// and the official server answers <b>0</b> for a key it does not carry, which refuses the deposition. No row
/// may therefore cost 0 tickets, and an absent file leaves <see cref="Rows"/> empty rather than failing the
/// startup.
/// </para>
/// </summary>
public class CreatureFarmTicketCostOptions
{
    public List<CreatureFarmTicketCostRowOptions> Rows { get; set; } = new();
}

/// <summary>
/// One row of the client table, in the column order the 7.3 binary compares: <c>rate</c>, <c>form</c>,
/// <c>enhance_level</c>, then the <c>ticket_count</c> that key costs.
/// </summary>
public class CreatureFarmTicketCostRowOptions
{
    public int Rate { get; set; }
    public int Form { get; set; }
    public int EnhanceLevel { get; set; }
    public int TicketCount { get; set; }
}
