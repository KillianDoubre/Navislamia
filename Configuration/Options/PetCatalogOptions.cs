using System.Collections.Generic;

namespace Navislamia.Configuration.Options;

/// <summary>
/// The pets the 7.3 client knows, exported from its own <c>db_pet.rdb</c> by
/// <c>tools/export_pet_catalog.py</c> into <c>DevConsole/pet-catalog.73.json</c>. Read with
/// System.Text.Json at startup like the other catalogues, never through the configuration provider.
/// </summary>
public class PetCatalogOptions
{
    public List<PetResourceRow> Pets { get; set; } = new();
}

/// <summary>
/// One pet of the client table: its id (the <c>pet_code</c> of the entry frame), the cage item that calls
/// it (<c>cage_id</c>), the model the client draws and the English name of its <c>name_id</c>.
/// </summary>
public class PetResourceRow
{
    public int Id { get; set; }
    public int Type { get; set; }
    public int NameId { get; set; }
    public int CageId { get; set; }
    public string Model { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Pickup radius in meters, from the pet's "Collect Items" skill (effect 10047, <c>var1</c>) in the 9.4
    /// tables — the client table does not carry it. 0: the pet collects nothing.
    /// </summary>
    public int CollectRadius { get; set; }
}
