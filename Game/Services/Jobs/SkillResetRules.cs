using System;
using System.Collections.Generic;
using System.Linq;

namespace Navislamia.Game.Services.Jobs;

/// <summary>
/// A skill reset: the skills kept (at their new level), the ones taken away, the levels lowered, and the JP and talent
/// points given back.
/// </summary>
public sealed record SkillReset(IReadOnlyDictionary<int, byte> Remaining, IReadOnlyList<int> Removed,
    IReadOnlyList<(int SkillId, byte Level)> Lowered, long JpBack, int TpBack);

/// <summary>
/// The skill reset and the race change of the official server (<c>StructCreature::ResetSkill</c>,
/// <c>StructPlayer::ResetJob</c>, <c>StructPlayer::SetRace</c>, <c>onResetSkill</c>) and of the Epic 7 Lua
/// (<c>gold_skill_reset_check</c>, <c>jp_skill_reset_check</c>, <c>NPC_JobChange_set_race</c>). Pure, like
/// <see cref="JobChangeRules"/>; docs/packet-specs/socle-changement-metier.md §8.
/// </summary>
public static class SkillResetRules
{
    /// <summary>The race change stone the Lua asks for (<c>has_item(601100284)</c>), one taken per change.</summary>
    public const int RaceChangeItem = 601100284;

    /// <summary>The Lua's reset counter, a character flag (<c>get_flag('reset_count')</c>), capped at 9.</summary>
    public const string ResetCountFlag = "reset_count";

    public const int MaxResetCount = 9;

    /// <summary><c>reset_gold</c> and <c>reset_jp</c>, indexed by the resets already done.</summary>
    public static readonly long[] GoldCosts =
        { 10_000, 2_500_000, 5_000_000, 10_000_000, 25_000_000, 50_000_000, 100_000_000, 500_000_000, 1_000_000_000, 2_000_000_000 };

    public static readonly long[] JpCosts =
        { 2_000, 500_000, 1_000_000, 2_000_000, 5_000_000, 10_000_000, 20_000_000, 100_000_000, 200_000_000, 400_000_000 };

    /// <summary>The menu functions, offered by the job NPC to a master class and on the race change stone.</summary>
    public const string GoldReset = "gold_skill_reset_check";
    public const string JpReset = "jp_skill_reset_check";
    public const string ChangeRace = "NPC_JobChange_change_race";
    public const string SetRace = "NPC_JobChange_set_race";

    /// <summary>The races and their base job (<c>JobInfo::*_BASIC_JOB</c>): Deva 3, Asura 4, Gaia 5.</summary>
    public static int BaseJob(int race) => race switch { 3 => 100, 4 => 200, 5 => 300, _ => 0 };

    /// <summary><c>NPC_JobChange_get_resetcount</c>: an unset flag is 0.</summary>
    public static int ResetCount(IReadOnlyDictionary<string, string> flags) =>
        flags.TryGetValue(ResetCountFlag, out var value) && int.TryParse(value, out var count)
            ? Math.Clamp(count, 0, MaxResetCount)
            : 0;

    /// <summary>The page text of a master class at the job NPC, one per reset done (<c>@90604793</c> to <c>@90604802</c>).</summary>
    public static string ResetText(int resetCount) => "@" + (90604793 + Math.Clamp(resetCount, 0, MaxResetCount));

    /// <summary>
    /// <c>StructCreature::ResetSkill(jobDepth)</c>: a skill the trees below <paramref name="jobDepth"/> do not allow
    /// goes, with what each of its levels cost; one they allow below its level is lowered, and the levels above
    /// are given back too. Null when the depth is out of the character's reach.
    /// </summary>
    public static SkillReset Reset(SkillCatalog catalog, IReadOnlyList<(int Job, int JobLevel)> previousJobs,
        int currentJob, IReadOnlyDictionary<int, byte> learned, int jobDepth, double jpRate = 1)
    {
        previousJobs ??= Array.Empty<(int, int)>();
        var depth = previousJobs.Count;
        if (jobDepth < 0 || jobDepth > depth)
        {
            return null;
        }

        var jobs = previousJobs.Select(entry => entry.Job).Append(currentJob).ToArray();
        int Allowed(int skillId)
        {
            var allowed = 0;
            for (var d = 0; d <= Math.Min(depth, jobDepth - 1); d++)
            {
                allowed = Math.Max(allowed, catalog.MaxLevelIn(jobs[d], skillId));
            }

            return allowed;
        }

        var remaining = new Dictionary<int, byte>();
        var removed = new List<int>();
        var lowered = new List<(int, byte)>();
        long jp = 0;
        var tp = 0;
        void GiveBack(int skillId, int from, int to)
        {
            for (var level = to; level > from; level--)
            {
                var (levelJp, levelTp) = catalog.LevelCost(jobs[0], skillId, level);
                jp += Rates.RateMath.ScaleCost(levelJp, jpRate);
                tp += levelTp;
            }
        }

        foreach (var (skillId, level) in learned ?? new Dictionary<int, byte>())
        {
            var allowed = Allowed(skillId);
            if (allowed == 0)
            {
                removed.Add(skillId);
                GiveBack(skillId, 0, level);
            }
            else if (jobDepth > 0 && allowed < level)
            {
                // AdjustOverflowedSkillLevel: the level left is the highest the remaining trees allow.
                lowered.Add((skillId, (byte)allowed));
                remaining[skillId] = (byte)allowed;
                GiveBack(skillId, allowed, level);
            }
            else
            {
                remaining[skillId] = level;
            }
        }

        return new SkillReset(remaining, removed, lowered, jp, tp);
    }

    /// <summary><c>GetAllJobLevelJP</c>: what the job levels of a depth cost up to <paramref name="jobLevel"/>.</summary>
    public static long JobLevelJp(Func<int, int, long?> costOf, int depth, int jobLevel)
    {
        long jp = 0;
        for (var level = 1; level < jobLevel; level++)
        {
            jp += costOf(depth, level) ?? 0;
        }

        return jp;
    }
}
