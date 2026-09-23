using System.Collections.Frozen;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Serilog;

namespace Navislamia.Game.Services.Pets;

/// <summary>A pet the client knows: the <c>pet_code</c> of its entry frame and the name it enters with.</summary>
public sealed record PetDefinition(int PetId, int CageItemId, string Name);

/// <summary>The cage item → pet link of the 7.3 client table, frozen at startup.</summary>
public interface IPetCatalog
{
    /// <summary>The pet a cage item calls, false when the item is not a known cage.</summary>
    bool TryGetByCage(long cageItemId, out PetDefinition pet);
}

public class PetCatalog : IPetCatalog
{
    private readonly ILogger _logger = Log.ForContext<PetCatalog>();
    private readonly FrozenDictionary<long, PetDefinition> _byCage;

    public PetCatalog(IOptions<PetCatalogOptions> options)
    {
        var byCage = new Dictionary<long, PetDefinition>();
        foreach (var row in options.Value?.Pets ?? new List<PetResourceRow>())
        {
            // The exporter already refuses a shared cage; the first row wins if a hand edit adds one.
            if (row.CageId > 0 && row.Id > 0)
            {
                byCage.TryAdd(row.CageId, new PetDefinition(row.Id, row.CageId, row.Name ?? string.Empty));
            }
        }

        _byCage = byCage.ToFrozenDictionary();
        _logger.Debug("Loaded {count} pet cages", _byCage.Count);
    }

    public bool TryGetByCage(long cageItemId, out PetDefinition pet) => _byCage.TryGetValue(cageItemId, out pet);
}
