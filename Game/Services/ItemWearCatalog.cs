using System.Collections.Frozen;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// The <c>wear_type</c> of every item resource, frozen at startup for the equipment path that has to
/// resolve a destination slot from the item itself (<c>TM_CS_PUTON_ITEM_SET</c>, 281).
/// </summary>
public class ItemWearCatalog : IItemWearCatalog
{
    private readonly ILogger _logger = Log.ForContext<ItemWearCatalog>();
    private readonly FrozenDictionary<int, ItemWearType> _wearTypes;

    public ItemWearCatalog(IItemResourceRepository repository)
    {
        var fields = repository.GetWearFields();
        var wearTypes = new Dictionary<int, ItemWearType>(fields.Count);

        foreach (var field in fields)
        {
            wearTypes[field.Id] = field.WearType;
        }

        _wearTypes = wearTypes.ToFrozenDictionary();
        _logger.Debug("Loaded wear types for {count} item resources", _wearTypes.Count);
    }

    public bool TryGetWearType(long resourceId, out ItemWearType wearType)
    {
        return _wearTypes.TryGetValue((int)resourceId, out wearType);
    }
}
