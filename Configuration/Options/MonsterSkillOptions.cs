using System.Collections.Generic;

namespace Navislamia.Configuration.Options;

/// <summary>One <c>MonsterSkillResource</c> slot: a skill, its level and the chance it is rolled at.</summary>
public class MonsterSkillEntryOptions
{
    public int SkillId { get; set; }
    public int Level { get; set; }

    /// <summary>A probability in <c>[0, 1]</c> per attack opportunity, not a percentage.</summary>
    public double Probability { get; set; }
}

/// <summary>
/// <c>DevConsole/monster-skills.73.json</c> (<c>tools/export_monster_skills.py</c>): monster skill link id
/// (<c>MonsterResource.monster_skill_link_id</c>) to its entries, in the order the AI rolls them.
/// </summary>
public class MonsterSkillOptions
{
    public Dictionary<int, List<MonsterSkillEntryOptions>> Links { get; set; } = new();
    public Dictionary<int, List<MonsterTriggerOptions>> Triggers { get; set; } = new();
}

public class MonsterTriggerOptions
{
    public int Type { get; set; }
    public double Value1 { get; set; }
    public double Value2 { get; set; }
    public string Function { get; set; }
}
