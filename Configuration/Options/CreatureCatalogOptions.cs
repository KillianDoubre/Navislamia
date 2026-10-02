using System.Collections.Generic;

namespace Navislamia.Configuration.Options;

/// <summary>
/// <c>DevConsole/creature-catalog.73.json</c> (<c>tools/export_creature_catalog.py</c>): every summon resource with
/// its base stats inlined, the summon name parts, and the names of the tamable monsters
/// (docs/packet-specs/socle-apprivoisement-invocation.md §15).
/// </summary>
public class CreatureCatalogOptions
{
    public List<SummonResourceOptions> Summons { get; set; } = new();
    public List<string> NamePrefixes { get; set; } = new();
    public List<string> NamePostfixes { get; set; } = new();
    public Dictionary<string, string> TamableMonsterNames { get; set; } = new();
}

public class SummonResourceOptions
{
    public int Id { get; set; }
    public int NameId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Type { get; set; }
    public int Rate { get; set; }
    public int Form { get; set; }
    public int EvolveTarget { get; set; }
    public int CardId { get; set; }
    public int StatId { get; set; }
    public int RunSpeed { get; set; }
    public float AttackRange { get; set; }
    public float Size { get; set; }
    public float Scale { get; set; }

    /// <summary>str, vit, dex, agi, int, men (wisdom), luk of <c>StatResource[stat_id]</c>; null when the row is missing.</summary>
    public float[] Stats { get; set; }
}
