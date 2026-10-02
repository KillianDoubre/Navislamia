using System;
using Navislamia.Configuration.Options;

namespace Navislamia.Game.Services.MonsterSkills;

/// <summary>CheckTriggerCondition / ResetTriggerCondition in the official MonsterAI.cpp.</summary>
public static class MonsterTriggerRules
{
    public static bool Check(MonsterTriggerOptions trigger, int hpPercentage, uint now, ref uint flag, ICombatRandom random)
    {
        var elapsed = unchecked((int)(now - flag));
        var delay = Math.Max(0, trigger.Value1 * 100);
        switch (trigger.Type)
        {
            case 1 when flag == 0 && hpPercentage <= trigger.Value1: flag = 1; return true;
            case 2 when hpPercentage <= trigger.Value1:
                return random.Next(10000) + 1 <= trigger.Value2 * 100;
            case 3 when flag != 0 && elapsed > delay: flag = 0; return true;
            case 4 when elapsed > delay: flag = now; return true;
            case 5 when flag == 0 || elapsed > delay: flag = now; return true;
            default: return false;
        }
    }
}
