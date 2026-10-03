using System.Collections.Generic;

namespace Navislamia.Configuration.Options;

public class SkillCatalogOptions
{
    public List<JobSkillCatalog> Jobs { get; set; } = new();
}

public class JobSkillCatalog
{
    public int JobId { get; set; }
    public int SkillTreeId { get; set; }
    public List<LearnableSkill> Skills { get; set; } = new();
}

public class LearnableSkill
{
    public int SkillId { get; set; }
    public List<int> JpCosts { get; set; } = new();
    public List<SkillUnlockRule> Rules { get; set; } = new();
}

public class SkillUnlockRule
{
    public int MinSkillLevel { get; set; }
    public int MaxSkillLevel { get; set; }
    public int RequiredLevel { get; set; }
    public int RequiredJobLevel { get; set; }
    public double JpRatio { get; set; } = 1;
    public List<SkillPrerequisite> Prerequisites { get; set; } = new();

    /// <summary>The card enhance range of a summon's rule (<c>cenhance_min/max</c>); a player counts as enhance 0.</summary>
    public int MinCardEnhance { get; set; }
    public int MaxCardEnhance { get; set; } = 5;
}

public class SkillPrerequisite
{
    public int SkillId { get; set; }
    public int Level { get; set; }
}
