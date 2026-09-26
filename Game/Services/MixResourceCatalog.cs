using System.Collections.Frozen;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

public class MixResourceCatalog : IMixResourceCatalog
{
    private readonly ILogger _logger = Log.ForContext<MixResourceCatalog>();
    private readonly FrozenDictionary<long, MixResourceEntity> _rulesById;
    private readonly IReadOnlyList<MixResourceEntity> _rules;

    public MixResourceCatalog(IMixResourceRepository repository)
    {
        var rows = repository.GetAll();
        var rules = new Dictionary<long, MixResourceEntity>(rows.Count);

        foreach (var row in rows)
        {
            rules[row.Id] = row;
        }

        _rulesById = rules.ToFrozenDictionary();
        _rules = rows;
        _logger.Debug("Loaded {count} mix resources", _rulesById.Count);
    }

    public IReadOnlyList<MixResourceEntity> Rules => _rules;

    public bool TryGetRule(long mixId, out MixResourceEntity rule)
    {
        return _rulesById.TryGetValue(mixId, out rule);
    }
}
