using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Navislamia.Game.Services.Jobs;

/// <summary>
/// The job change of the official Epic 7 server, which lives in its Lua and not in its tables
/// (<c>NPC_JobChange.lua</c> and <c>NPC_Tutorial.lua</c> of the Epic 7 Part 4 trunk, read 2026-10-02;
/// docs/packet-specs/socle-changement-metier.md). Everything here is pure: the menus, the requirements, the
/// transitions and the triggers the dialog advertises, so the service only shows pages and commits.
/// </summary>
public static class JobChangeRules
{
    /// <summary>The master-class NPC ("the Satan girl", 11555), absent from the dialog catalogue's contact list.</summary>
    public const int MasterNpcId = 11555;

    /// <summary>The tutorial job supporter of the trainees' island (3019).</summary>
    public const int TutorialNpcId = 3019;

    /// <summary>The quest that opens the master class (<c>get_quest_progress(3322) == 255</c>).</summary>
    public const int MasterClassQuest = 3322;

    /// <summary>Tutorial quests 5-8, 6-2 and 6-3 (<c>NPC_Tutorial_Change_job</c>).</summary>
    public const int TutorialQuest58 = 1023, TutorialQuest62 = 1025, TutorialQuest63 = 1027;

    /// <summary>The Hector warp of <c>goto_hactor()</c>.</summary>
    public const int HectorX = 138645, HectorY = 75174;

    /// <summary><c>DEFAULT_TALENT_POINT</c>, granted on becoming a master class (<c>MIN_TALENT_POINT_JOB_DEPTH</c> 3).</summary>
    public const int MasterClassTalentPoints = 2;

    public const int MasterDepth = 3;

    /// <summary><c>get_quest_progress</c>: the quest is completed.</summary>
    public const int QuestCompleted = 255;

    public const string Close = "@90010002";
    public const string Back = "@90010001";
    public const string Confirm = "@90010007";
    public const string GoBack = "@90010003";

    public const string ChangeJob = "NPC_JobChange_change_job";
    public const string MasterContact = "NPC_master_partdevil_contact";
    public const string CheckCommon = "Run_JobChange_check_common";
    public const string Common = "Run_JobChange_common";
    public const string TutorialChangeJob = "NPC_Tutorial_Change_job";
    public const string TutorialMenu = "Quest_Link_6_3";
    public const string TutorialCheck = "Run_JobChange_Tutorial_check";
    public const string Tutorial = "Run_JobChange_Tutorial";
    public const string GotoHector = "goto_hactor";

    private static readonly HashSet<string> Functions = new(StringComparer.Ordinal)
    {
        ChangeJob, MasterContact, CheckCommon, Common, TutorialChangeJob, TutorialMenu, TutorialCheck, Tutorial,
        GotoHector
    };

    private static readonly Regex JobTrigger = new(
        @"\A(Run_JobChange_check_common|Run_JobChange_common|Run_JobChange_Tutorial_check|Run_JobChange_Tutorial)\( '(@[0-9]+)' , ([0-9]+) \)\z",
        RegexOptions.CultureInvariant);

    /// <summary>Whether a dialog function belongs to the job change.</summary>
    public static bool Handles(string function) => function is not null && Functions.Contains(function);

    /// <summary>The job depth of a character: the number of jobs it has left behind (0 base … 3 master).</summary>
    public static int Depth(IReadOnlyCollection<(int Job, int JobLevel)> previousJobs) => previousJobs?.Count ?? 0;

    /// <summary>
    /// <c>NPC_JobChange_change_job</c>: Lv 10 and JLv 10 to leave the base job, Lv 50 and JLv 40 to leave the first
    /// job; the master class (<c>NPC_master_partdevil_contact</c>) asks Lv 147 and JLv 49.
    /// </summary>
    public static bool MeetsRequirement(int depth, int level, int jobLevel) => depth switch
    {
        0 => level >= 10 && jobLevel >= 10,
        1 => level >= 50 && jobLevel >= 40,
        2 => level >= 147 && jobLevel >= 49,
        _ => false
    };

    /// <summary>
    /// <c>is_able_to_jobchange()</c> (<c>ETC_Script_Functions.lua</c>): the same gates, 145 at the second depth.
    /// The master NPC words its refusal with it.
    /// </summary>
    public static bool IsAbleToJobChange(int depth, int level, int jobLevel) => depth switch
    {
        0 => level >= 10 && jobLevel >= 10,
        1 => level >= 50 && jobLevel >= 40,
        2 => level >= 145 && jobLevel >= 49,
        _ => false
    };

    /// <summary>
    /// The jobs a character may become, hard-coded in the official Lua ("the table cannot be read, so the job
    /// change information is fixed here"). Depth 0 by race, depths 1 and 2 by the current job.
    /// </summary>
    public static IReadOnlyList<int> NextJobs(int race, int currentJob, int depth) => depth switch
    {
        0 => race switch
        {
            4 => new[] { 201, 202, 203 },
            5 => new[] { 301, 302, 303 },
            _ => new[] { 101, 102, 103 }
        },
        1 => currentJob switch
        {
            201 => new[] { 210, 211 },
            202 => new[] { 212, 213 },
            203 => new[] { 214 },
            301 => new[] { 310, 311 },
            302 => new[] { 312, 313 },
            303 => new[] { 314 },
            101 => new[] { 110, 111 },
            102 => new[] { 112, 113 },
            103 => new[] { 114 },
            _ => Array.Empty<int>()
        },
        2 => currentJob is >= 110 and <= 114 or >= 210 and <= 214 or >= 310 and <= 314
            ? new[] { currentJob + 10 }
            : Array.Empty<int>(),
        _ => Array.Empty<int>()
    };

    /// <summary>The job's name string (<c>"@10" .. job</c>, e.g. <c>@10201</c>).</summary>
    public static string JobName(int job) => "@10" + job.ToString(CultureInfo.InvariantCulture);

    /// <summary>The menu trigger of a job choice, exactly as the Lua concatenates it.</summary>
    public static string Trigger(string function, int job) =>
        $"{function}( '{JobName(job)}' , {job.ToString(CultureInfo.InvariantCulture)} )";

    /// <summary>Reads a job trigger back; the job name must be the job's own.</summary>
    public static bool TryReadJobTrigger(string trigger, out string function, out int job)
    {
        function = null;
        job = 0;
        var match = JobTrigger.Match(trigger ?? string.Empty);
        if (!match.Success || !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture,
                out job) || match.Groups[2].Value != JobName(job))
        {
            job = 0;
            return false;
        }

        function = match.Groups[1].Value;
        return true;
    }

    /// <summary>The NPC's title and text strings: <c>@90&lt;npc&gt;01</c>, <c>@90&lt;npc&gt;0x</c>.</summary>
    public static string NpcString(int npcId, int suffix) =>
        "@90" + npcId.ToString("D4", CultureInfo.InvariantCulture) + suffix.ToString("D2", CultureInfo.InvariantCulture);

    /// <summary>The title of a job change page: the master NPC has its own (<c>@91002405</c>).</summary>
    public static string Title(int npcId) => npcId == MasterNpcId ? "@91002405" : NpcString(npcId, 1);

    /// <summary>The "are you sure" text of <c>Run_JobChange_check_common</c>, per target job.</summary>
    public static string ConfirmText(int job) => job switch
    {
        >= 210 and <= 214 => "@907004" + (11 + job - 210).ToString(CultureInfo.InvariantCulture),
        >= 310 and <= 314 => "@907004" + (16 + job - 310).ToString(CultureInfo.InvariantCulture),
        >= 110 and <= 114 => "@907004" + (21 + job - 110).ToString(CultureInfo.InvariantCulture),
        >= 101 and <= 103 => "@903019" + (19 + job - 101).ToString(CultureInfo.InvariantCulture),
        >= 201 and <= 203 => "@903019" + (13 + job - 201).ToString(CultureInfo.InvariantCulture),
        >= 301 and <= 303 => "@903019" + (16 + job - 301).ToString(CultureInfo.InvariantCulture),
        >= 120 and <= 124 => "@910024" + (10 + job - 120).ToString(CultureInfo.InvariantCulture),
        >= 220 and <= 224 => "@910024" + (15 + job - 220).ToString(CultureInfo.InvariantCulture),
        >= 320 and <= 324 => "@910024" + (20 + job - 320).ToString(CultureInfo.InvariantCulture),
        _ => string.Empty
    };

    /// <summary>The base and first jobs' names the master NPC quotes when it turns one away (<c>@1342</c>…).</summary>
    public static string ShortJobName(int job) => job switch
    {
        >= 100 and <= 103 => "@" + (1342 + job - 100).ToString(CultureInfo.InvariantCulture),
        >= 200 and <= 203 => "@" + (1356 + job - 200).ToString(CultureInfo.InvariantCulture),
        >= 300 and <= 303 => "@" + (1372 + job - 300).ToString(CultureInfo.InvariantCulture),
        _ => string.Empty
    };

    /// <summary><c>sconv</c>: the format string and its key/value pairs joined by a vertical tab.</summary>
    public static string Sconv(string format, params string[] pairs) =>
        pairs.Length == 0 ? format : format + "\v" + string.Join("\v", pairs);

    /// <summary>
    /// The character after the change (<c>Run_JobChange_common</c>): the left job and its job level are kept at
    /// the old depth (<c>job_N</c>, <c>jlv_N</c>), the new job starts at job level 1, one depth deeper.
    /// </summary>
    public static JobChangeResult Apply(IReadOnlyList<(int Job, int JobLevel)> previousJobs, int currentJob,
        int currentJobLevel, int targetJob)
    {
        var history = new List<(int Job, int JobLevel)>(previousJobs ?? Array.Empty<(int, int)>())
        {
            (currentJob, currentJobLevel)
        };

        return new JobChangeResult(targetJob, 1, history, history.Count,
            history.Count == MasterDepth ? MasterClassTalentPoints : 0);
    }
}

/// <summary>A committed job change: the new job, its job level, the jobs left behind, the depth and the TP granted.</summary>
public sealed record JobChangeResult(int Job, int JobLevel, IReadOnlyList<(int Job, int JobLevel)> PreviousJobs,
    int Depth, int TalentPoints);
