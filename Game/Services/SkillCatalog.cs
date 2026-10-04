using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Packets;

namespace Navislamia.Game.Services;

/// <summary>
/// What learning a level costs: JP, or talent points for a talent skill, whose <c>need_jp</c> is negative
/// (<c>SkillBase::GetNeedTalentPoint</c>: the opposite of a negative cost is a TP cost, and its JP cost is 0).
/// </summary>
public readonly record struct SkillLearnEvaluation(ResultCode Result, long Cost, int TalentCost = 0)
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

    /// <summary>
    /// The highest level any job's tree allows for <paramref name="skillId"/>, used by the GM command
    /// <c>/learn</c>, which ignores the job restriction. False when no job knows the skill.
    /// </summary>
    public bool TryGetMaxLevel(int skillId, out byte maxLevel)
    {
        maxLevel = 0;
        foreach (var jobSkills in _jobs.Values)
        {
            if (!jobSkills.TryGetValue(skillId, out var skill))
            {
                continue;
            }

            foreach (var rule in skill.Rules)
            {
                if (rule.MaxSkillLevel > maxLevel)
                {
                    maxLevel = (byte)Math.Min(rule.MaxSkillLevel, byte.MaxValue);
                }
            }
        }

        return maxLevel > 0;
    }

    /// <summary>
    /// <c>StructPlayer::IsLearnableSkill</c>: the trees of the jobs left behind first, each with the job level it
    /// reached there, then the current job's tree with the current job level. The search goes on only while the
    /// answer is "not in this tree", "beyond this tree's maximum" or "job level too low" — any other answer is
    /// final, as in the official loop.
    /// </summary>
    public SkillLearnEvaluation EvaluateAcrossJobs(IReadOnlyList<(int Job, int JobLevel)> previousJobs, int jobId,
        int characterLevel, int jobLevel, int skillId, byte currentLevel, byte targetLevel,
        IReadOnlyDictionary<int, byte> learnedSkills, long availableJp, double costRate = 1, int cardEnhance = 0,
        int availableTp = 0)
    {
        var evaluation = new SkillLearnEvaluation(ResultCode.LimitJob, 0);
        foreach (var (previousJob, previousJobLevel) in previousJobs ?? Array.Empty<(int, int)>())
        {
            if (previousJob == 0)
            {
                continue;
            }

            evaluation = Evaluate(previousJob, characterLevel, previousJobLevel, skillId, currentLevel, targetLevel,
                learnedSkills, availableJp, costRate, cardEnhance, availableTp);
            if (!KeepsSearching(evaluation.Result))
            {
                return evaluation;
            }
        }

        return Evaluate(jobId, characterLevel, jobLevel, skillId, currentLevel, targetLevel, learnedSkills,
            availableJp, costRate, cardEnhance, availableTp);
    }

    private static bool KeepsSearching(ResultCode result) =>
        result is ResultCode.LimitJob or ResultCode.LimitMax or ResultCode.NotEnoughJobLevel;

    /// <summary>Whether a job id has a tree (a classic job, or a summon resource at Epic 7).</summary>
    public bool HasTree(int jobId) => _jobs.ContainsKey(jobId);

    /// <summary>The skills of a tree, for a summon's skill list.</summary>
    public IReadOnlyCollection<int> SkillsOf(int jobId) =>
        _jobs.TryGetValue(jobId, out var skills) ? skills.Keys : Array.Empty<int>();

    /// <summary><c>GameContent::GetAllowedMaxSkillLevel(tree, skill)</c>: the highest level a job's tree allows, 0 outside it.</summary>
    public int MaxLevelIn(int jobId, int skillId) =>
        _jobs.TryGetValue(jobId, out var skills) && skills.TryGetValue(skillId, out var skill)
            ? skill.Rules.Select(rule => rule.MaxSkillLevel).DefaultIfEmpty(0).Max()
            : 0;

    /// <summary>
    /// <c>GetSumOfSkillLearningCost</c> for one level: what learning it cost, given back by a reset. The JP is the
    /// skill's need times the <c>jp_ratio</c> of the first tree tried — the depth-0 job's, whose last entry reaching
    /// the level wins, 1 otherwise (<c>GameContent::GetNeedJpForSkillLevelUp</c>) — and a negative need is TP.
    /// </summary>
    public (long Jp, int Tp) LevelCost(int baseJobId, int skillId, int level)
    {
        // need_jp belongs to the skill (SkillBase), not to a tree: the longest list a tree carries is the skill's.
        var costs = _jobs.Values.Select(skills => skills.GetValueOrDefault(skillId)?.JpCosts)
            .Where(list => list is not null).MaxBy(list => list.Count);
        if (costs is null || level < 1 || level > costs.Count)
        {
            return (0, 0);
        }

        var need = costs[level - 1];
        if (need < 0)
        {
            return (0, -need);
        }

        var ratio = 1d;
        if (_jobs.TryGetValue(baseJobId, out var tree) && tree.TryGetValue(skillId, out var entry))
        {
            foreach (var rule in entry.Rules.Where(rule => rule.MaxSkillLevel >= level))
            {
                ratio = rule.JpRatio > 0 ? rule.JpRatio : 1;
            }
        }

        return ((long)(need * ratio), 0);
    }

    public SkillLearnEvaluation Evaluate(int jobId, int characterLevel, int jobLevel, int skillId,
        byte currentLevel, byte targetLevel, IReadOnlyDictionary<int, byte> learnedSkills, long availableJp,
        double costRate = 1, int cardEnhance = 0, int availableTp = 0)
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
        var hasEnhance = false;
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
            // GameContent::isLearnableSkill: a summon's rule bounds its card's enhance; a player counts as 0.
            if (cardEnhance < candidate.MinCardEnhance || cardEnhance > candidate.MaxCardEnhance)
            {
                continue;
            }

            hasEnhance = true;
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

        if (!hasEnhance)
        {
            return new SkillLearnEvaluation(ResultCode.EnhanceLimit, 0);
        }

        if (rule is null)
        {
            return new SkillLearnEvaluation(ResultCode.NotEnoughSkill, 0);
        }

        if (targetLevel > skill.JpCosts.Count)
        {
            return new SkillLearnEvaluation(ResultCode.NotActable, 0);
        }

        // GameContent::isLearnableSkill: a talent skill (negative need_jp) costs talent points, no JP, no ratio.
        var need = skill.JpCosts[targetLevel - 1];
        if (need < 0)
        {
            return availableTp < -need
                ? new SkillLearnEvaluation(ResultCode.NotEnoughTP, 0, -need)
                : new SkillLearnEvaluation(ResultCode.Success, 0, -need);
        }

        var ratio = rule.JpRatio > 0 ? rule.JpRatio : 1;
        var cost = checked((long)Math.Ceiling(need * ratio));

        // The server's SkillJpCost rate, after the job's own ratio.
        cost = Rates.RateMath.ScaleCost(cost, costRate);

        return availableJp < cost
            ? new SkillLearnEvaluation(ResultCode.NotEnoughJP, cost)
            : new SkillLearnEvaluation(ResultCode.Success, cost);
    }
}
