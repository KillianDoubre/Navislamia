using System.Collections.Frozen;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

public class ItemGroupCatalog : IItemGroupCatalog
{
    private readonly ILogger _logger = Log.ForContext<ItemGroupCatalog>();
    private readonly FrozenDictionary<int, ItemGroup> _groups;

    public ItemGroupCatalog(IItemResourceRepository repository)
    {
        var fields = repository.GetGroupFields();
        var groups = new Dictionary<int, ItemGroup>(fields.Count);

        foreach (var field in fields)
        {
            groups[field.Id] = field.Group;
        }

        _groups = groups.ToFrozenDictionary();
        _logger.Debug("Loaded item groups for {count} item resources", _groups.Count);
    }

    public bool TryGetGroup(long resourceId, out ItemGroup group)
    {
        return _groups.TryGetValue((int)resourceId, out group);
    }
}
