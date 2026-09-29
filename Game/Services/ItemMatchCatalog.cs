using System.Collections.Frozen;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

public class ItemMatchCatalog : IItemMatchCatalog
{
    private readonly ILogger _logger = Log.ForContext<ItemMatchCatalog>();
    private readonly FrozenDictionary<int, ItemMatchFields> _fields;

    public ItemMatchCatalog(IItemResourceRepository repository)
    {
        var rows = repository.GetMatchFields();
        var fields = new Dictionary<int, ItemMatchFields>(rows.Count);

        foreach (var row in rows)
        {
            fields[row.Id] = row;
        }

        _fields = fields.ToFrozenDictionary();
        _logger.Debug("Loaded the crafting match fields of {count} item resources", _fields.Count);
    }

    public bool TryGetFields(long resourceId, out ItemMatchFields fields)
    {
        return _fields.TryGetValue((int)resourceId, out fields);
    }
}
