using System.Collections.Frozen;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

public class ItemSortCatalog : IItemSortCatalog
{
    private readonly ILogger _logger = Log.ForContext<ItemSortCatalog>();
    private readonly FrozenDictionary<int, ulong> _resourceKeys;

    public ItemSortCatalog(IItemResourceRepository repository)
    {
        var fields = repository.GetSortFields();
        var keys = new Dictionary<int, ulong>(fields.Count);
        foreach (var field in fields)
        {
            keys[field.Id] = InventoryArrange.BuildResourceKey(field.Category, field.Group, field.Rank, field.Id);
        }

        _resourceKeys = keys.ToFrozenDictionary();
        _logger.Debug("Loaded {count} item sort keys", _resourceKeys.Count);
    }

    public ulong GetResourceKey(long resourceId)
    {
        return _resourceKeys.TryGetValue((int)resourceId, out var key)
            ? key
            : InventoryArrange.BuildUnknownResourceKey(resourceId);
    }
}
