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

    /// <summary>Uses before the prop leaves the world; 0 is unlimited (<c>StructFieldProp::m_nUseCount</c>).</summary>
    public int UseCount { get; set; }

    /// <summary>Ticks before it comes back, or first appears after the start (<c>FieldPropManager</c>).</summary>
    public int RegenTime { get; set; }

    /// <summary>Ticks it stays in the world once there; 0 is forever.</summary>
    public int LifeTime { get; set; }

    public List<FieldPropDropOptions> Drops { get; set; } = new();

    /// <summary>The Epic 7 script of a prop whose 9.4 script has no action (<c>quest_prop_*</c>).</summary>
    public string LuaScript { get; set; } = string.Empty;
}

public class FieldPropDropOptions
{
    public int ItemId { get; set; }

    /// <summary>The chance out of 100 000 000 (<c>XRandom(1, 100000000) &lt;= ratio</c>).</summary>
    public int Ratio { get; set; }
    public int CountMin { get; set; }
    public int CountMax { get; set; }
    public int LevelMin { get; set; }
    public int LevelMax { get; set; }
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
