using System.Collections.Generic;

namespace Navislamia.Configuration.Options;

/// <summary>
/// The merchant catalogue: one row per (market name, item) pair, grouped by name at load time exactly
/// like the reference server groups its <c>MarketResource</c> rows (<c>ObjectMgr.cpp</c>, SELECT ...
/// FROM MarketResource ORDER BY name, sort_id).
/// <para>
/// The reference reads that table from SQL Server, which has no equivalent here: the rows come from a
/// versioned JSON catalogue, the pattern the other base lots already use
/// (<c>monster-drops.73.json</c>, <c>npc-dialogs.73.json</c>). No row is available in the repository,
/// so the file ships empty and the market window stays closed until an export is supplied.
/// </para>
/// </summary>
public class MarketCatalogOptions
{
    public List<MarketResourceRow> Markets { get; set; } = new();
}

/// <summary>
/// One catalogue line, named after the columns it comes from. <see cref="Price"/> is the <b>absolute</b>
/// gold price, not the database's <c>price_ratio</c>: the reference multiplies the ratio by the item base
/// price at load time (<c>ObjectMgr.cpp:851</c>), and the client only ever receives the product
/// (<c>TM_SC_MARKET</c> carries no ratio and the client knows only its own item names).
/// <see cref="HuntaholicPoint"/> is expected to be 0: the reference forces the loaded
/// <c>huntaholic_ratio</c> to 0 (<c>ObjectMgr.cpp:852</c>). The field is carried because the packet
/// serialises it from <c>EPIC_5_2</c> on, i.e. in 7.3.
/// </summary>
public class MarketResourceRow
{
    public string Name { get; set; } = string.Empty;

    public int SortId { get; set; }

    public int Code { get; set; }

    public long Price { get; set; }

    public int HuntaholicPoint { get; set; }
}
