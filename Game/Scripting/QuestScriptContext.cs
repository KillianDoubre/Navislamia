using System;

namespace Navislamia.Game.Scripting;

public sealed class QuestScriptContext
{
    public uint PlayerHandle { get; init; }
    public Func<int, int> GetProgress { get; init; }
    public Func<int, int, int, bool> SetStatus { get; init; }
    public Func<int, long, bool> SetTitleCondition { get; init; }
}
