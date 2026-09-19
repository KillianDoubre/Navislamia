using System.Collections.Frozen;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

public class ItemUseCatalog : IItemUseCatalog
{
    private readonly ILogger _logger = Log.ForContext<ItemUseCatalog>();
    private readonly FrozenDictionary<int, ItemUseLevels> _levels;
    private readonly FrozenSet<int> _reusable;

    public ItemUseCatalog(IItemResourceRepository repository)
    {
        var fields = repository.GetUseFields();
        var levels = new Dictionary<int, ItemUseLevels>(fields.Count);
        var reusable = new HashSet<int>();

        foreach (var field in fields)
        {
            levels[field.Id] = new ItemUseLevels(field.UseMinLevel, field.UseMaxLevel);
            if (!ItemUseRules.IsConsumedOnUse(field.BaseType))
            {
                reusable.Add(field.Id);
            }
        }

        _levels = levels.ToFrozenDictionary();
        _reusable = reusable.ToFrozenSet();
        _logger.Debug("Loaded use levels for {count} item resources, {reusable} reusable", _levels.Count,
            _reusable.Count);
    }

    public bool TryGetLevels(int itemResourceId, out ItemUseLevels levels)
    {
        return _levels.TryGetValue(itemResourceId, out levels);
    }

    public bool IsConsumedOnUse(int itemResourceId)
    {
        return !_reusable.Contains(itemResourceId);
    }
}
