using System.Collections.Frozen;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// The <c>(rank, price)</c> pair of every item resource, read once from the item resource table and kept as
/// a frozen dictionary, like the other item catalogs (<see cref="ItemUseCatalog"/>,
/// <see cref="ItemGroupCatalog"/>). <c>IItemResourceRepository</c> exposed no price projection before this
/// packet; <c>GetSellPriceFields()</c> is that new, single-column read
/// (docs/packet-specs/252-sell-item.md §10).
/// </summary>
public class ItemSellCatalog : IItemSellCatalog
{
    private readonly ILogger _logger = Log.ForContext<ItemSellCatalog>();
    private readonly FrozenDictionary<int, ItemSellTemplate> _templates;

    public ItemSellCatalog(IItemResourceRepository repository)
    {
        var fields = repository.GetSellPriceFields();
        var templates = new Dictionary<int, ItemSellTemplate>(fields.Count);

        foreach (var field in fields)
        {
            templates[field.Id] = new ItemSellTemplate(field.Rank, field.Price);
        }

        _templates = templates.ToFrozenDictionary();
        _logger.Debug("Loaded sell templates for {count} item resources", _templates.Count);
    }

    public bool TryGetTemplate(int itemResourceId, out ItemSellTemplate template)
    {
        return _templates.TryGetValue(itemResourceId, out template);
    }
}
