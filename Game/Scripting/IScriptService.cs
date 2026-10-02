using System;

namespace Navislamia.Game.Scripting;

public interface IScriptService
{
    void Start();

    void RegisterFunction(string name, Func<object[], int> function);

    int RunString(string script);
    bool RunMonsterTrigger(string function, MonsterScriptContext context) => false;
}
