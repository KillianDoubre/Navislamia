namespace Navislamia.Tools.ExportFieldProps;

internal sealed class PropSpawn
{
    public int PropId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float ZOffset { get; set; }
    public float RotateX { get; set; }
    public float RotateY { get; set; }
    public float RotateZ { get; set; }
    public float ScaleX { get; set; }
    public float ScaleY { get; set; }
    public float ScaleZ { get; set; }
}

internal sealed class PropActivation
{
    public int Condition { get; set; }
    public int Value1 { get; set; }
    public int Value2 { get; set; }
}

internal sealed class PropTemplate
{
    public int Id { get; set; }
    public int ActivateSkillId { get; set; }

    /// <summary>Cast delay in ar_time ticks: the source column is in seconds, hence the x100.</summary>
    public int CastingTime { get; set; }

    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
    public int Limit { get; set; }
    public int LimitJobId { get; set; }
    public string Script { get; set; }
    public List<PropActivation> Activations { get; set; } = new();
}

internal sealed class DungeonStart
{
    public int Id { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}

internal sealed class CatalogMetadata
{
    public string ClientEpic { get; set; }
    public string Source { get; set; }
    public int SpawnCount { get; set; }
    public int TemplateCount { get; set; }
    public int DungeonCount { get; set; }
}

internal sealed class FieldPropCatalogBody
{
    public List<PropTemplate> Templates { get; set; }
    public List<PropSpawn> Spawns { get; set; }
    public List<DungeonStart> Dungeons { get; set; }
}

internal sealed class FieldPropCatalogFile
{
    public CatalogMetadata Metadata { get; set; }
    public FieldPropCatalogBody FieldPropCatalog { get; set; }
}
