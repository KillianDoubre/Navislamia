using System.Collections.Generic;

namespace Navislamia.Configuration.Options;

public class NpcDialogOptions
{
    public Dictionary<int, string> Npcs { get; set; } = new();
    public Dictionary<string, NpcDialogDefinition> Dialogs { get; set; } = new();
}

public class NpcDialogDefinition
{
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public List<NpcDialogMenuEntry> Menu { get; set; } = new();
}

public class NpcDialogMenuEntry
{
    public string Label { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
}
