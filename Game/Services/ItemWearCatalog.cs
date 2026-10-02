using System.Collections.Frozen;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// The port of every item resource, frozen at startup for the equipment path: the slot a
/// <c>TM_CS_PUTON_ITEM_SET</c> (281) handle must be placed in, which that request does not carry, and
/// the level requirements <c>TM_CS_PUTON_ITEM</c> (200) and 281 judge before equipping.
/// </summary>
public class ItemWearCatalog : IItemWearCatalog
{
    private readonly ILogger _logger = Log.ForContext<ItemWearCatalog>();
    private readonly FrozenDictionary<int, ItemWearFields> _fields;

    public ItemWearCatalog(IItemResourceRepository repository)
    {
        var fields = repository.GetWearFields();
        var byId = new Dictionary<int, ItemWearFields>(fields.Count);

        foreach (var field in fields)
        {
            byId[field.Id] = field;
        }

        _fields = byId.ToFrozenDictionary();
        _logger.Debug("Loaded wear fields for {count} item resources", _fields.Count);
    }

    public bool TryGetWearFields(long resourceId, out ItemWearFields fields)
    {
        return _fields.TryGetValue((int)resourceId, out fields);
    }
}
