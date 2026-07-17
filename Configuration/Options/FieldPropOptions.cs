using System.Collections.Generic;

namespace Navislamia.Configuration.Options;

public class FieldPropActivationOptions
{
    public int Condition { get; set; }
    public int Value1 { get; set; }
    public int Value2 { get; set; }
}

public class FieldPropTemplateOptions
{
    public int Id { get; set; }
    public int ActivateSkillId { get; set; }
    public int CastingTime { get; set; }
    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
    public int Limit { get; set; }
    public int LimitJobId { get; set; }
    public string Script { get; set; } = string.Empty;
    public List<FieldPropActivationOptions> Activations { get; set; } = new();
}

public class FieldPropSpawnOptions
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

public class DungeonStartOptions
{
    public int Id { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}

public class FieldPropOptions
{
    public List<FieldPropTemplateOptions> Templates { get; set; } = new();
    public List<FieldPropSpawnOptions> Spawns { get; set; } = new();
    public List<DungeonStartOptions> Dungeons { get; set; } = new();
}
