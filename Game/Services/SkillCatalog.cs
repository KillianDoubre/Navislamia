using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Packets;

namespace Navislamia.Game.Services;

public readonly record struct SkillLearnEvaluation(ResultCode Result, long Cost)
{
    public bool IsSuccess => Result == ResultCode.Success;
}

public class SkillCatalog
{
    private readonly FrozenDictionary<int, FrozenDictionary<int, LearnableSkill>> _jobs;

    public SkillCatalog(IOptions<SkillCatalogOptions> options) : this(options.Value)
    {
    }

    public SkillCatalog(SkillCatalogOptions options)
    {
        _jobs = options.Jobs
            .GroupBy(job => job.JobId)
            .ToFrozenDictionary(
                group => group.Key,
                group => group.SelectMany(job => job.Skills)
                    .GroupBy(skill => skill.SkillId)
                    .ToFrozenDictionary(skill => skill.Key, skill => skill.First()));
    }

    public int JobCount => _jobs.Count;

    public SkillLearnEvaluation Evaluate(int jobId, int characterLevel, int jobLevel, int skillId,
        byte currentLevel, byte targetLevel, IReadOnlyDictionary<int, byte> learnedSkills, long availableJp)
    {
        if (targetLevel == 0 || targetLevel != currentLevel + 1)
        {
            return new SkillLearnEvaluation(ResultCode.InvalidArgument, 0);
        }

        if (!_jobs.TryGetValue(jobId, out var jobSkills) || !jobSkills.TryGetValue(skillId, out var skill))
        {
            return new SkillLearnEvaluation(ResultCode.LimitJob, 0);
        }

        var hasTargetRule = false;
        var hasCharacterLevel = false;
        var hasJobLevel = false;
        SkillUnlockRule rule = null;
        foreach (var candidate in skill.Rules)
        {
            if (targetLevel < candidate.MinSkillLevel || targetLevel > candidate.MaxSkillLevel)
            {
                continue;
            }

            hasTargetRule = true;
            if (characterLevel < candidate.RequiredLevel)
            {
                continue;
            }

            hasCharacterLevel = true;
            if (jobLevel < candidate.RequiredJobLevel)
            {
                continue;
            }

            hasJobLevel = true;
            if (candidate.Prerequisites.All(prerequisite => prerequisite.SkillId == 0 ||
                    learnedSkills.GetValueOrDefault(prerequisite.SkillId) >= prerequisite.Level))
            {
                rule = candidate;
                break;
            }
        }

        if (!hasTargetRule)
        {
            return new SkillLearnEvaluation(ResultCode.LimitMax, 0);
        }

        if (!hasCharacterLevel)
        {
            return new SkillLearnEvaluation(ResultCode.NotEnoughLevel, 0);
        }

        if (!hasJobLevel)
        {
            return new SkillLearnEvaluation(ResultCode.NotEnoughJobLevel, 0);
        }

        if (rule is null)
        {
            return new SkillLearnEvaluation(ResultCode.NotEnoughSkill, 0);
        }

        if (targetLevel > skill.JpCosts.Count)
        {
            return new SkillLearnEvaluation(ResultCode.NotActable, 0);
        }

        var ratio = rule.JpRatio > 0 ? rule.JpRatio : 1;
        var cost = checked((long)Math.Ceiling(skill.JpCosts[targetLevel - 1] * ratio));
        if (cost < 0)
        {
            return new SkillLearnEvaluation(ResultCode.NotActable, 0);
        }

        return availableJp < cost
            ? new SkillLearnEvaluation(ResultCode.NotEnoughJP, cost)
            : new SkillLearnEvaluation(ResultCode.Success, cost);
    }
}
