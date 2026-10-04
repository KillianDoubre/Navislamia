using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MoonSharp.Interpreter;

namespace Navislamia.Game.Services;

/// <summary>Committed official functions, loaded into a fresh sandbox for every dialogue action.</summary>
public sealed class NpcScriptCatalog
{
    public IReadOnlyDictionary<string, string> Functions { get; }
    public string Program { get; }
    public string EventProgram { get; }
    public NpcScriptCatalog()
    {
        using var resource = typeof(NpcScriptCatalog).Assembly.GetManifestResourceStream("Navislamia.NpcDialogs.json");
        Functions = JsonSerializer.Deserialize<Dictionary<string, string>>(resource)
            ?? throw new InvalidDataException("NPC script resource is empty");
        Program = string.Join("\n", Functions.Values);
        using var events = typeof(NpcScriptCatalog).Assembly.GetManifestResourceStream("Navislamia.EventAreaFunctions.json");
        EventProgram = string.Join("\n", JsonSerializer.Deserialize<Dictionary<string,string>>(events).Values);
    }
    public bool Handles(string function) => Functions.ContainsKey(function);
    public Script Create(bool eventArea = false)
    {
        var script = new Script(CoreModules.Preset_HardSandbox);
        script.DoString(Program);
        if (eventArea) script.DoString(EventProgram);
        // These six official menu strings omit the suffix of their existing implementation.
        for (var i = 2; i <= 7; i++)
            script.Globals[$"NPC_all_2012_EnchantEvent_b_{i}"] = script.Globals.Get($"NPC_all_2012_EnchantEvent_b_{i}_n");
        return script;
    }
}
