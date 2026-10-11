using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.Services.Interfaces;
using Serilog;

namespace Navislamia.Game.Services.ItemShop;

/// <summary>One article of the shop, validated.</summary>
public sealed record ItemShopEntry(int Id, string Category, int Code, int Count, long Price, string Name);

/// <summary>
/// The articles of <c>item-shop.73.json</c>, frozen at startup. An article is kept only with a positive price, a count
/// the Item Box line can carry (1..65535, the <c>uint16</c> of 10004) and an item the server knows
/// (<see cref="IItemSortCatalog"/>, the guard of <c>/item</c>); an id seen twice keeps its first article.
/// </summary>
public sealed class ItemShopCatalog
{
    private readonly ILogger _logger = Log.ForContext<ItemShopCatalog>();
    private readonly FrozenDictionary<int, ItemShopEntry> _byId;

    public ItemShopCatalog(IOptions<ItemShopOptions> options, IItemSortCatalog items)
        : this(options.Value.Entries, code => items.Contains(code))
    {
    }

    public ItemShopCatalog(IEnumerable<ItemShopEntryOptions> entries, Func<int, bool> knownItem)
    {
        var kept = new List<ItemShopEntry>();
        var seen = new HashSet<int>();
        foreach (var entry in entries ?? Enumerable.Empty<ItemShopEntryOptions>())
        {
            if (entry is null || entry.Id <= 0 || !seen.Add(entry.Id) || entry.Price <= 0
                || entry.Price > ItemShopRules.MaxPoints || entry.Count is < 1 or > ushort.MaxValue
                || !knownItem(entry.Code))
            {
                _logger.Warning("Item shop article {id} (item {code}) left out: invalid, duplicate or unknown item",
                    entry?.Id, entry?.Code);
                continue;
            }

            kept.Add(new ItemShopEntry(entry.Id, string.IsNullOrWhiteSpace(entry.Category) ? "Other" : entry.Category,
                entry.Code, entry.Count, entry.Price, string.IsNullOrWhiteSpace(entry.Name) ? $"Item {entry.Code}" : entry.Name));
        }

        Entries = kept;
        Categories = kept.Select(entry => entry.Category).Distinct(StringComparer.Ordinal).ToArray();
        _byId = kept.ToFrozenDictionary(entry => entry.Id);
    }

    /// <summary>Every article, in the file's order.</summary>
    public IReadOnlyList<ItemShopEntry> Entries { get; }

    /// <summary>The categories, in the order their first article appears.</summary>
    public IReadOnlyList<string> Categories { get; }

    public bool TryGet(int id, out ItemShopEntry entry) => _byId.TryGetValue(id, out entry);
}
