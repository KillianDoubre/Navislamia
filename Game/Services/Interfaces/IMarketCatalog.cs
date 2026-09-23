using System.Collections.Generic;

namespace Navislamia.Game.Services;

public interface IMarketCatalog
{
    /// <summary>Number of markets with at least one line; a market with no line is not a market.</summary>
    int MarketCount { get; }

    /// <summary>
    /// The lines of the market named <paramref name="name"/>, in catalogue order. False when no such
    /// market carries a line: an empty result would be a <c>TM_SC_MARKET</c> with <c>n = 0</c>, which has
    /// no known producer.
    /// </summary>
    bool TryGetMarket(string name, out IReadOnlyList<MarketLine> lines);
}
