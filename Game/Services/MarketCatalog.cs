using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// One <c>TM_SC_MARKET</c> line: item code, absolute gold price and huntaholic point, in packet order.
/// </summary>
public readonly record struct MarketLine(int Code, long Price, int HuntaholicPoint);

/// <summary>
/// The merchant catalogues, grouped by market name and looked up by name — the equivalent of the
/// reference's <c>_marketResourceStore</c> / <c>GetMarketInfo(szKey)</c> (<c>ObjectMgr.cpp</c>), fed from
/// a versioned JSON file instead of the SQL Server <c>MarketResource</c> table.
/// </summary>
public class MarketCatalog : IMarketCatalog
{
    private static readonly MarketLine[] NoLines = Array.Empty<MarketLine>();

    private readonly ILogger _logger = Log.ForContext<MarketCatalog>();
    private readonly FrozenDictionary<string, MarketLine[]> _markets;

    public MarketCatalog(IOptions<MarketCatalogOptions> options) : this(options.Value)
    {
    }

    public MarketCatalog(MarketCatalogOptions options)
    {
        var rows = new Dictionary<string, List<MarketResourceRow>>(StringComparer.Ordinal);

        foreach (var row in options.Markets ?? new List<MarketResourceRow>())
        {
            // A nameless market cannot be looked up and a zero code is not an item: both are dropped
            // rather than carried as a line the client would render as garbage.
            if (string.IsNullOrWhiteSpace(row.Name) || row.Code == 0)
            {
                continue;
            }

            if (!rows.TryGetValue(row.Name, out var market))
            {
                rows[row.Name] = market = new List<MarketResourceRow>();
            }

            market.Add(row);
        }

        var lines = 0;
        _markets = rows.ToFrozenDictionary(
            market => market.Key,
            market =>
            {
                // Same order as the reference query (ORDER BY name, sort_id); rows sharing a sort_id
                // keep the file order, since the table has no unique key and the client's own display
                // order is not established.
                var ordered = market.Value.OrderBy(row => row.SortId).ToArray();
                var compiled = new MarketLine[ordered.Length];
                for (var index = 0; index < ordered.Length; index++)
                {
                    compiled[index] = new MarketLine(
                        ordered[index].Code, ordered[index].Price, ordered[index].HuntaholicPoint);
                }

                lines += compiled.Length;
                return compiled;
            },
            StringComparer.Ordinal);

        _logger.Information("Loaded {markets} Markettemplates over {lines} catalogue lines",
            _markets.Count, lines);

        if (_markets.Count == 0)
        {
            // The reference logs "Loaded 0 Markettemplates" and starts anyway: an empty merchant catalogue
            // is a missing export, not a reason to refuse to boot.
            _logger.Warning("Loaded 0 Markettemplates: no market-catalog.73.json export, no merchant window "
                            + "can open");
        }
    }

    public int MarketCount => _markets.Count;

    public bool TryGetMarket(string name, out IReadOnlyList<MarketLine> lines)
    {
        if (!string.IsNullOrEmpty(name) && _markets.TryGetValue(name, out var market))
        {
            lines = market;
            return true;
        }

        lines = NoLines;
        return false;
    }
}
