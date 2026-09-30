using System.Collections.Frozen;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

public class EnhanceResourceCatalog : IEnhanceResourceCatalog
{
    private readonly ILogger _logger = Log.ForContext<EnhanceResourceCatalog>();
    private readonly FrozenDictionary<(long, LocalFlag), EnhanceResourceEntity> _rows;

    public EnhanceResourceCatalog(IEnhanceResourceRepository repository)
    {
        var loaded = repository.GetAll();
        var rows = new Dictionary<(long, LocalFlag), EnhanceResourceEntity>(loaded.Count);

        foreach (var row in loaded)
        {
            rows[(row.Id, row.LocalFlag)] = row;
        }

        _rows = rows.ToFrozenDictionary();
        _logger.Debug("Loaded {count} enhance resources", _rows.Count);
    }

    public bool TryGetRow(long enhanceId, LocalFlag localFlag, out EnhanceResourceEntity row)
    {
        return _rows.TryGetValue((enhanceId, localFlag), out row);
    }

    public bool TryGetForServer(long enhanceId, int serverLocalFlag, out EnhanceResourceEntity row)
    {
        // With the default server flag each id has exactly one matching row; should a flag match several,
        // the narrowest local_flag wins, so the answer never depends on the dictionary's order.
        row = null;
        foreach (var candidate in _rows.Values)
        {
            if (candidate.Id == enhanceId && ((int)candidate.LocalFlag & serverLocalFlag) != 0
                && (row is null || (int)candidate.LocalFlag < (int)row.LocalFlag))
            {
                row = candidate;
            }
        }

        return row is not null;
    }
}
