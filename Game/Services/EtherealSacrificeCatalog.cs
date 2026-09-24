using System.Collections.Frozen;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// The sacrifice facts of the item resources, read once from the <c>ItemResource</c> table
/// (<c>wear_type</c> and <c>ethereal_durability</c>) and indexed by resource id. The rows are kept as
/// they are read: which wear types and which durability floor the 7.3 client accepts is not established
/// here (docs/packet-specs/263-transmit-ethereal-durability.md §7).
/// </summary>
public class EtherealSacrificeCatalog : IEtherealSacrificeCatalog
{
    private readonly ILogger _logger = Log.ForContext<EtherealSacrificeCatalog>();
    private readonly FrozenDictionary<int, ItemEtherealFields> _fields;

    public EtherealSacrificeCatalog(IItemResourceRepository repository)
    {
        var fields = repository.GetEtherealFields();
        var byResourceId = new Dictionary<int, ItemEtherealFields>(fields.Count);

        foreach (var field in fields)
        {
            byResourceId[field.Id] = field;
        }

        _fields = byResourceId.ToFrozenDictionary();
        _logger.Debug("Loaded the ethereal durability of {count} item resources", _fields.Count);
    }

    public bool TryGet(long resourceId, out ItemEtherealFields fields)
    {
        if (resourceId is > 0 and <= int.MaxValue)
        {
            return _fields.TryGetValue((int)resourceId, out fields);
        }

        fields = default;
        return false;
    }
}
