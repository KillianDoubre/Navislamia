using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Navislamia.Game.Services.Progression;

public sealed record TitleResource(int Id, int NameId, short[] Types, decimal[] Var1, decimal[] Var2,
    bool Periodic, string Begin, string End);
public sealed record TitleConditionType(int Id, int Category, int[] Values, bool Set);
public sealed record TitleCondition(int TitleId, int Group, int TypeId, long Count, bool Achieve);
public sealed record DungeonCell(int Id, int X, int Y);

public sealed class ProgressionResources
{
    public TitleResource[] Titles { get; set; } = Array.Empty<TitleResource>();
    public TitleConditionType[] ConditionTypes { get; set; } = Array.Empty<TitleConditionType>();
    public TitleCondition[] Conditions { get; set; } = Array.Empty<TitleCondition>();
    public DungeonCell[] DungeonCells { get; set; } = Array.Empty<DungeonCell>();
    public static ProgressionResources Official { get; } = Load();
    private static ProgressionResources Load()
    {
        using var stream = typeof(ProgressionResources).Assembly.GetManifestResourceStream("Navislamia.ProgressionResources.json")
            ?? throw new InvalidDataException("Missing embedded progression resources");
        return JsonSerializer.Deserialize<ProgressionResources>(stream)
            ?? throw new InvalidDataException("Invalid progression resources");
    }
}
