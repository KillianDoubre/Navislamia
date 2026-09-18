using System.Collections.Frozen;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

public class ItemUseCatalog : IItemUseCatalog
{
    private readonly ILogger _logger = Log.ForContext<ItemUseCatalog>();
    private readonly FrozenDictionary<int, ItemUseLevels> _levels;

    public ItemUseCatalog(IItemResourceRepository repository)
    {
        var fields = repository.GetUseFields();
        var levels = new Dictionary<int, ItemUseLevels>(fields.Count);

        foreach (var field in fields)
        {
            levels[field.Id] = new ItemUseLevels(field.UseMinLevel, field.UseMaxLevel);
        }

        _levels = levels.ToFrozenDictionary();
        _logger.Debug("Loaded use levels for {count} item resources", _levels.Count);
    }

    public bool TryGetLevels(int itemResourceId, out ItemUseLevels levels)
    {
        return _levels.TryGetValue(itemResourceId, out levels);
    }
}
