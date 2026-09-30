using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

public class ItemUseCatalog : IItemUseCatalog
{
    private readonly ILogger _logger = Log.ForContext<ItemUseCatalog>();
    private readonly FrozenDictionary<int, ItemUseLevels> _levels;
    private readonly FrozenDictionary<int, ItemUseFields> _fields;
    private readonly FrozenSet<int> _reusable;
    private readonly FrozenSet<int> _renamesPet;

    public ItemUseCatalog(IItemResourceRepository repository, string retailPath = null)
    {
        var fields = new List<ItemUseFields>(repository.GetUseFields());
        if (retailPath is not null && File.Exists(retailPath))
        {
            var retail = JsonSerializer.Deserialize<ItemUseFields[]>(File.ReadAllText(retailPath));
            if (retail is not null)
            {
                var merged = new Dictionary<int, ItemUseFields>(fields.Count);
                foreach (var field in fields) merged[field.Id] = field;
                foreach (var field in retail) merged[field.Id] = field;
                fields = new List<ItemUseFields>(merged.Values);
                _logger.Information("Loaded {count} Epic 7.x consumable use profiles", retail.Length);
            }
        }
        var levels = new Dictionary<int, ItemUseLevels>(fields.Count);
        var byId = new Dictionary<int, ItemUseFields>(fields.Count);
        var reusable = new HashSet<int>();
        var renamesPet = new HashSet<int>();

        foreach (var field in fields)
        {
            levels[field.Id] = new ItemUseLevels(field.UseMinLevel, field.UseMaxLevel);
            byId[field.Id] = field;
            if (!ItemUseRules.IsConsumedOnUse(field.BaseType))
            {
                reusable.Add(field.Id);
            }

            if (field.RenamesPet)
            {
                renamesPet.Add(field.Id);
            }
        }

        _levels = levels.ToFrozenDictionary();
        _fields = byId.ToFrozenDictionary();
        _reusable = reusable.ToFrozenSet();
        _renamesPet = renamesPet.ToFrozenSet();
        _logger.Debug("Loaded use levels for {count} item resources, {reusable} reusable", _levels.Count,
            _reusable.Count);
    }

    public bool TryGetLevels(int itemResourceId, out ItemUseLevels levels)
    {
        return _levels.TryGetValue(itemResourceId, out levels);
    }

    public bool TryGetUseFields(int itemResourceId, out ItemUseFields fields) =>
        _fields.TryGetValue(itemResourceId, out fields);

    public bool RenamesPet(int itemResourceId) => _renamesPet.Contains(itemResourceId);

    public bool IsConsumedOnUse(int itemResourceId)
    {
        return !_reusable.Contains(itemResourceId);
    }
}
