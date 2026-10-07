using System;

namespace Navislamia.Game.Scripting;

public interface IScriptService
{
    void Start();

    void RegisterFunction(string name, Func<object[], int> function);

    int RunString(string script);
    bool RunMonsterTrigger(string function, MonsterScriptContext context) => false;
    int RunQuestScript(string script, QuestScriptContext context) => 0;

    /// <summary>
    /// Calls a global Lua function and returns what it returns as the string a caller compares — the shape
    /// the official server uses to read a chunk's verdict (<c>return NPC_Creature_Farm_nurse_handler()</c>,
    /// compared to <c>"1"</c>). Null when the function is absent or raises, which the caller reads as "no
    /// script" and answers <c>NO_REWARD</c> (docs/packet-specs/6006-nurse-creature.md §5.4).
    /// </summary>
    string CallGlobalFunction(string function) => null;
}
