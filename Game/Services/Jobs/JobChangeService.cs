using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Party;
using Serilog;

namespace Navislamia.Game.Services.Jobs;

/// <summary>
/// What a job change function does: the page it shows (null for none), whether the NPC's quest offers go on top
/// of it (the Lua's <c>dlg_text</c>, not <c>dlg_text_without_quest_menu</c>), and the job to commit after showing it.
/// </summary>
public sealed record JobChangeStep(NpcDialogDefinition Page, bool WithQuests, int CommitJob, bool Tutorial)
{
    public static readonly JobChangeStep Nothing = new(null, false, 0, false);
}

public interface IJobChangeService
{
    /// <summary>
    /// The page a job change function shows (<see cref="JobChangeRules.Handles"/>), judged against the
    /// character's state now: a menu the character no longer qualifies for is not shown.
    /// </summary>
    Task<JobChangeStep> SelectAsync(GameClient client, int npcId, string function, string trigger);

    /// <summary>Changes the job (<c>Run_JobChange_common</c>): validation, database, then the properties.</summary>
    Task<bool> CommitAsync(GameClient client, int npcId, int job, bool tutorial);

    /// <summary>
    /// The GM command <c>/job</c>: any job of the character's race tree, at any depth, without the NPC, the level
    /// or the quest. The skills and JP stay as they are.
    /// </summary>
    Task<GmJobChange> ForceJobAsync(GameClient client, int job);

    /// <summary>
    /// The GM command <c>/race</c>: <c>StructPlayer::SetRace</c> without the stone — skills reset, the new race's
    /// base job, the JP of the skills and job levels given back.
    /// </summary>
    Task<GmJobChange> ForceRaceAsync(GameClient client, int race);
}

/// <summary>What <c>/job</c> or <c>/race</c> did.</summary>
public enum GmJobChange
{
    Done,
    AlreadyThere,
    NotInRaceTree,
    UnknownRace,
    Busy,
    NotSaved
}

/// <summary>
/// The job change of the official Epic 7 server (docs/packet-specs/socle-changement-metier.md). The dialog pages
/// are the Lua's own (<see cref="JobChangeRules"/>); the change sends what <c>set_value</c> and
/// <c>StructPlayer::onChangeProperty</c> send: <c>job_N</c>, <c>jlv_N</c>, then <c>job</c> to the region and the
/// job info, the stats, <c>job_level</c> and <c>job_depth</c>, and the system line of <c>message()</c>.
/// </summary>
public sealed partial class JobChangeService : IJobChangeService
{
    /// <summary><c>CHAT_NPC</c>, what the Lua's <c>message()</c> sends, from <c>@SCRIPT</c>.</summary>
    private const byte ChatNpc = 40;

    /// <summary><c>CHAT_ANNOUNCE</c>, what <c>announce()</c> sends to every player, from <c>@ANNOUNCE</c>.</summary>
    private const byte ChatAnnounce = 21;

    private readonly ILogger _logger = Log.ForContext<JobChangeService>();
    private readonly ICharacterService _characters;
    private readonly IStatService _stats;
    private readonly IQuestService _quests;
    private readonly IPartyService _parties;
    private readonly IPlayerVisibilityService _players;
    private readonly IWarpService _warp;

    public JobChangeService(ICharacterService characters, IStatService stats, IWarpService warp,
        IQuestService quests = null, IPartyService parties = null, IPlayerVisibilityService players = null,
        SkillCatalog skills = null, ILevelingService leveling = null, ISkillCastService casts = null,
        Creatures.ICreatureService creatures = null, Rates.IRateService rates = null)
    {
        _skills = skills;
        _leveling = leveling;
        _casts = casts;
        _creatures = creatures;
        _rates = rates;
        _characters = characters;
        _stats = stats;
        _warp = warp;
        _quests = quests;
        _parties = parties;
        _players = players;
    }

    public async Task<JobChangeStep> SelectAsync(GameClient client, int npcId, string function, string trigger)
    {
        var info = client.ConnectionInfo;
        var depth = JobChangeRules.Depth(info.PreviousJobs);
        switch (function)
        {
            case JobChangeRules.ChangeJob:
                return npcId == JobChangeRules.MasterNpcId ? JobChangeStep.Nothing : await ChangeJobPageAsync(client, npcId, depth);

            case JobChangeRules.MasterContact:
                return npcId == JobChangeRules.MasterNpcId
                    ? await MasterPageAsync(client, info, depth)
                    : JobChangeStep.Nothing;

            case JobChangeRules.TutorialChangeJob:
                return npcId == JobChangeRules.TutorialNpcId
                    ? await TutorialPageAsync(client, info, depth)
                    : JobChangeStep.Nothing;

            case JobChangeRules.TutorialMenu:
                return npcId == JobChangeRules.TutorialNpcId && depth == 0
                    ? new JobChangeStep(TutorialMenuPage(info), false, 0, true)
                    : JobChangeStep.Nothing;

            case JobChangeRules.GotoHector:
                _warp.Warp(client, JobChangeRules.HectorX, JobChangeRules.HectorY);
                return JobChangeStep.Nothing;

            case SkillResetRules.GoldReset:
            case SkillResetRules.JpReset:
                return await ResetSkillsAsync(client, npcId, function == SkillResetRules.GoldReset);

            case SkillResetRules.ChangeRace:
                return await ChangeRacePageAsync(client, npcId);

            case SkillResetRules.SetRace:
                return await SetRaceAsync(client, npcId, trigger);
        }

        if (!JobChangeRules.TryReadJobTrigger(trigger, out var jobFunction, out var job)
            || !await CanBecomeAsync(client, npcId, job, IsTutorial(jobFunction)))
        {
            _logger.Warning("{clientTag} job change trigger {trigger} refused", client.ClientTag, trigger);
            return JobChangeStep.Nothing;
        }

        return jobFunction switch
        {
            JobChangeRules.CheckCommon => new JobChangeStep(CheckPage(npcId, job), false, 0, false),
            JobChangeRules.TutorialCheck => new JobChangeStep(TutorialCheckPage(job), false, 0, true),
            JobChangeRules.Common => new JobChangeStep(CongratulationPage(npcId, job), false, job, false),
            JobChangeRules.Tutorial => new JobChangeStep(new NpcDialogDefinition
            {
                Title = "@90301901",
                Text = JobChangeRules.Sconv("@90301908", "#@job_name@#", JobChangeRules.JobName(job)),
                Menu = Menu((JobChangeRules.Close, string.Empty))
            }, false, job, true),
            _ => JobChangeStep.Nothing
        };
    }

    private static bool IsTutorial(string function) =>
        function is JobChangeRules.TutorialCheck or JobChangeRules.Tutorial;

    /// <summary>
    /// Whether the character may become <paramref name="job"/> at this NPC now: the requirement of its depth, the
    /// Lua's own menu, and the place — the base and first changes at the job NPCs (the base one also at the
    /// tutorial NPC), the master class only at its NPC once quest 3322 is completed.
    /// </summary>
    private async Task<bool> CanBecomeAsync(GameClient client, int npcId, int job, bool tutorial)
    {
        var info = client.ConnectionInfo;
        var depth = JobChangeRules.Depth(info.PreviousJobs);
        if (!JobChangeRules.MeetsRequirement(depth, info.CharacterLevel, info.CharacterJobLevel)
            || !JobChangeRules.NextJobs(info.CharacterRace, info.CharacterJob, depth).Contains(job))
        {
            return false;
        }

        if (tutorial)
        {
            return npcId == JobChangeRules.TutorialNpcId && depth == 0;
        }

        if (npcId == JobChangeRules.MasterNpcId)
        {
            return depth == 2 && _quests is not null
                && await _quests.GetQuestProgressAsync(client, JobChangeRules.MasterClassQuest) == JobChangeRules.QuestCompleted;
        }

        return depth is 0 or 1 && npcId != JobChangeRules.TutorialNpcId;
    }

    /// <summary><c>NPC_JobChange_change_job</c>.</summary>
    private async Task<JobChangeStep> ChangeJobPageAsync(GameClient client, int npcId, int depth)
    {
        // A master class gets the skill reset, priced by the resets already done (the Epic 7 trunk's branch).
        return depth >= JobChangeRules.MasterDepth && _skills is not null
            ? await MasterResetPageAsync(client, npcId)
            : ChangeJobPage(client.ConnectionInfo, npcId, depth);
    }

    private static JobChangeStep ChangeJobPage(ConnectionInfo info, int npcId, int depth)
    {
        var page = new NpcDialogDefinition { Title = JobChangeRules.Title(npcId) };
        if (depth == 0 && !JobChangeRules.MeetsRequirement(0, info.CharacterLevel, info.CharacterJobLevel))
        {
            page.Text = JobChangeRules.NpcString(npcId, 3);
            page.Menu = Menu((JobChangeRules.Close, string.Empty));
            return new JobChangeStep(page, true, 0, false);
        }

        if (depth == 1 && !JobChangeRules.MeetsRequirement(1, info.CharacterLevel, info.CharacterJobLevel))
        {
            // The Lua words it with is_able_to_jobchange(), which is false whenever this gate is.
            page.Text = JobChangeRules.NpcString(npcId,
                JobChangeRules.IsAbleToJobChange(1, info.CharacterLevel, info.CharacterJobLevel) ? 9 : 2);
            page.Menu = Menu((JobChangeRules.Close, string.Empty));
            return new JobChangeStep(page, true, 0, false);
        }

        if (depth >= 2)
        {
            // A second job is sent to Hector for the master class (@91002563). A master class gets the skill reset
            // text of its first reset (@90604793); the gold and JP resets themselves are not ported.
            page.Text = depth == 2 ? "@91002563" : "@90604793";
            page.Menu = Menu((JobChangeRules.Close, string.Empty));
            return new JobChangeStep(page, true, 0, false);
        }

        page.Text = depth == 0 ? "@90100404" : "@90700410";
        page.Menu = JobMenu(JobChangeRules.NextJobs(info.CharacterRace, info.CharacterJob, depth),
            JobChangeRules.CheckCommon, (JobChangeRules.Back, string.Empty));
        return new JobChangeStep(page, true, 0, false);
    }

    /// <summary><c>NPC_master_partdevil_contact</c>.</summary>
    private async Task<JobChangeStep> MasterPageAsync(GameClient client, ConnectionInfo info, int depth)
    {
        var page = new NpcDialogDefinition { Title = "@91002405" };
        var hector = (JobChangeRules.GoBack, JobChangeRules.GotoHector + "()");
        if (depth == 2)
        {
            if (!JobChangeRules.MeetsRequirement(2, info.CharacterLevel, info.CharacterJobLevel))
            {
                page.Text = JobChangeRules.IsAbleToJobChange(2, info.CharacterLevel, info.CharacterJobLevel)
                    ? "@91002407" : "@91002406";
                page.Menu = Menu(hector, (JobChangeRules.Close, string.Empty));
                return new JobChangeStep(page, true, 0, false);
            }

            var quest = _quests is null ? -1 : await _quests.GetQuestProgressAsync(client, JobChangeRules.MasterClassQuest);
            if (quest != JobChangeRules.QuestCompleted)
            {
                // "Congratulations on overcoming every trial" is the Lua's text for the not-yet case.
                return new JobChangeStep(new NpcDialogDefinition
                {
                    Title = "@90300401",
                    Text = "@91002486",
                    Menu = Menu(hector, (JobChangeRules.Close, " "))
                }, true, 0, false);
            }

            page.Text = "@91002409";
            page.Menu = JobMenu(JobChangeRules.NextJobs(info.CharacterRace, info.CharacterJob, 2),
                JobChangeRules.CheckCommon, hector, (JobChangeRules.Back, string.Empty));
            return new JobChangeStep(page, true, 0, false);
        }

        if (depth == JobChangeRules.MasterDepth)
        {
            page.Text = "@91002408";
            page.Menu = Menu(("@91002550", "tp_point_2()"), hector, (JobChangeRules.Close, string.Empty));
            return new JobChangeStep(page, true, 0, false);
        }

        page.Text = JobChangeRules.Sconv("@91002426", "#@job_name@#", JobChangeRules.ShortJobName(info.CharacterJob));
        page.Menu = Menu((JobChangeRules.Close, string.Empty));
        return new JobChangeStep(page, false, 0, false);
    }

    /// <summary><c>NPC_Tutorial_Change_job</c> (the trainees' island, NPC 3019).</summary>
    private async Task<JobChangeStep> TutorialPageAsync(GameClient client, ConnectionInfo info, int depth)
    {
        var level = info.CharacterLevel;
        var jobLevel = info.CharacterJobLevel;
        var reached = level >= 10 && jobLevel >= 10;
        var quest58 = await Progress(client, JobChangeRules.TutorialQuest58);
        var quest62 = await Progress(client, JobChangeRules.TutorialQuest62);
        var quest63 = await Progress(client, JobChangeRules.TutorialQuest63);

        var page = new NpcDialogDefinition { Title = "@90301901" };
        var withQuests = true;
        if (quest62 == 0)
        {
            page.Text = reached ? "@90301903" : "@90301911";
            withQuests = reached;
        }
        else if (depth == 1)
        {
            page.Text = "@90301910";
        }
        else if (!reached)
        {
            page.Text = "@90301902";
        }
        else if (quest58 is -1 or 0)
        {
            page.Text = "@90301922";
        }
        else
        {
            page.Text = "@90301910";
        }

        page.Menu = new List<NpcDialogMenuEntry>();
        if (depth == 0 && quest63 == JobChangeRules.QuestCompleted && reached)
        {
            page.Menu.Add(new NpcDialogMenuEntry { Label = "@90010060", Trigger = JobChangeRules.TutorialMenu + "()" });
        }

        page.Menu.Add(new NpcDialogMenuEntry { Label = JobChangeRules.Close, Trigger = " " });
        return new JobChangeStep(page, withQuests, 0, true);
    }

    private async Task<int> Progress(GameClient client, int code) =>
        _quests is null ? -1 : await _quests.GetQuestProgressAsync(client, code);

    /// <summary><c>Quest_Link_6_3</c>: the three first jobs of the race.</summary>
    private static NpcDialogDefinition TutorialMenuPage(ConnectionInfo info) => new()
    {
        Title = "@90301901",
        Text = "@90301906",
        Menu = JobMenu(JobChangeRules.NextJobs(info.CharacterRace, info.CharacterJob, 0), JobChangeRules.TutorialCheck,
            (JobChangeRules.Close, " "))
    };

    /// <summary><c>Run_JobChange_check_common</c>: what the job is, and "are you sure".</summary>
    private static NpcDialogDefinition CheckPage(int npcId, int job) => new()
    {
        Title = JobChangeRules.Title(npcId),
        Text = JobChangeRules.ConfirmText(job),
        Menu = Menu((JobChangeRules.Confirm, JobChangeRules.Trigger(JobChangeRules.Common, job)),
            (JobChangeRules.Back, string.Empty))
    };

    /// <summary><c>Run_JobChange_Tutorial_check</c>.</summary>
    private static NpcDialogDefinition TutorialCheckPage(int job) => new()
    {
        Title = "@90301901",
        Text = JobChangeRules.ConfirmText(job),
        Menu = Menu(("@90010195", JobChangeRules.Trigger(JobChangeRules.Tutorial, job)),
            ("@90010196", JobChangeRules.TutorialMenu + "()"))
    };

    /// <summary><c>Run_JobChange_common</c>'s dialog, shown before the values change, as the Lua does.</summary>
    private static NpcDialogDefinition CongratulationPage(int npcId, int job) => new()
    {
        Title = JobChangeRules.Title(npcId),
        Text = JobChangeRules.Sconv(npcId == JobChangeRules.MasterNpcId ? "@91002425" : JobChangeRules.NpcString(npcId, 6),
            "#@job_name@#", JobChangeRules.JobName(job)),
        Menu = Menu((JobChangeRules.Close, string.Empty))
    };

    private static List<NpcDialogMenuEntry> JobMenu(IEnumerable<int> jobs, string function,
        params (string Label, string Trigger)[] tail)
    {
        var menu = new List<NpcDialogMenuEntry>();
        foreach (var job in jobs)
        {
            menu.Add(new NpcDialogMenuEntry { Label = JobChangeRules.JobName(job), Trigger = JobChangeRules.Trigger(function, job) });
        }

        menu.AddRange(Menu(tail));
        return menu;
    }

    private static List<NpcDialogMenuEntry> Menu(params (string Label, string Trigger)[] entries)
    {
        var menu = new List<NpcDialogMenuEntry>(entries.Length);
        foreach (var (label, trigger) in entries)
        {
            menu.Add(new NpcDialogMenuEntry { Label = label, Trigger = trigger });
        }

        return menu;
    }

    public async Task<bool> CommitAsync(GameClient client, int npcId, int job, bool tutorial)
    {
        var info = client.ConnectionInfo;
        if (Interlocked.CompareExchange(ref info.JobChangeInProgress, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            // Judged again at the commit: the confirmation page may be old, and the trigger only proves that it
            // was offered, not that the character still qualifies.
            if (!await CanBecomeAsync(client, npcId, job, tutorial))
            {
                _logger.Warning("{clientTag} may not become job {job} at NPC {npcId}", client.ClientTag, job, npcId);
                return false;
            }

            var result = JobChangeRules.Apply(info.PreviousJobs, info.CharacterJob, info.CharacterJobLevel, job);
            var talentPoints = await _characters.ChangeJobAsync(info.CharacterName, job, result.PreviousJobs,
                result.TalentPoints);
            if (talentPoints is null)
            {
                _logger.Error("{clientTag} job change to {job} was not saved", client.ClientTag, job);
                return false;
            }

            info.PreviousJobs.Clear();
            info.PreviousJobs.AddRange(result.PreviousJobs);
            info.CharacterJob = result.Job;
            info.CharacterJobLevel = result.JobLevel;
            info.CharacterTalentPoint = talentPoints.Value;

            SendJobChange(client, result, talentPoints.Value);
            _parties?.OnJobChanged(client);
            if (_quests is not null)
            {
                await _quests.RefreshAsync(client);
            }

            var jobName = JobChangeRules.JobName(job);
            client.Connection.Send(GameChatPackets.BuildChat("@SCRIPT", ChatNpc,
                JobChangeRules.Sconv(tutorial ? "@90300408" : "@90010017", "#@job_name@#", jobName)));
            if (npcId == JobChangeRules.MasterNpcId)
            {
                Announce(JobChangeRules.Sconv("@246", "#@user_name@#", info.CharacterName, "#@job_name@#", jobName), client);
            }

            _logger.Information("{clientTag} changed job to {job} (depth {depth})", client.ClientTag, job, result.Depth);
            return true;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "{clientTag} job change to {job} failed", client.ClientTag, job);
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref info.JobChangeInProgress, 0);
        }
    }

    /// <summary>
    /// The frames of <c>set_value</c> in the Lua's order, with what <c>onChangeProperty("job")</c> adds:
    /// <c>job</c> to the region, <c>SendJobInfo</c>, the stats (<c>CalculateStat</c>), then <c>job_level</c> and
    /// <c>job_depth</c>, and the talent points of a master class.
    /// </summary>
    private void SendJobChange(GameClient client, JobChangeResult result, int talentPoints)
    {
        var info = client.ConnectionInfo;
        var handle = info.CharacterHandle;
        var left = result.Depth - 1;
        if (left >= 0)
        {
            // /job may set a base job, which leaves nothing behind.
            var (leftJob, leftJobLevel) = result.PreviousJobs[left];
            client.Connection.Send(GameStatPackets.BuildProperty(handle, $"job_{left}", leftJob));
            client.Connection.Send(GameStatPackets.BuildProperty(handle, $"jlv_{left}", leftJobLevel));
        }

        var jobFrame = GameStatPackets.BuildProperty(handle, "job", result.Job);
        if (_players is not null)
        {
            _players.SendToObservers(client, jobFrame, includeSelf: true);
        }
        else
        {
            client.Connection.Send(jobFrame);
        }

        client.Connection.Send(GameStatPackets.BuildProperty(handle, "job_level", result.JobLevel));
        for (var depth = 0; depth < 3; depth++)
        {
            var (job, jobLevel) = depth < result.PreviousJobs.Count ? result.PreviousJobs[depth] : (0, 0);
            client.Connection.Send(GameStatPackets.BuildProperty(handle, $"job_{depth}", job));
            client.Connection.Send(GameStatPackets.BuildProperty(handle, $"jlv_{depth}", jobLevel));
        }

        // The base stats follow the job (JobResource.stat_id) and the jobs left behind (JobLevelBonus).
        var stats = _stats.Compute(info);
        var maxHp = (int)stats.Total.MaxHp;
        var maxMp = (int)stats.Total.MaxMp;
        info.CharacterMaxHp = maxHp;
        info.CharacterHp = Math.Min(info.CharacterHp, maxHp);
        info.CharacterMp = Math.Min(info.CharacterMp, maxMp);
        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, stats.Total, StatInfoType.Total));
        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, stats.ByItem, StatInfoType.ByItem));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_hp", maxHp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_mp", maxMp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "job_level", result.JobLevel));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "job_depth", result.Depth));
        if (result.TalentPoints > 0)
        {
            client.Connection.Send(GameStatPackets.BuildProperty(handle, "tp", talentPoints));
        }
    }

    private void Announce(string text, GameClient self)
    {
        var frame = GameChatPackets.BuildChat("@ANNOUNCE", ChatAnnounce, text);
        var clients = _players?.Registry?.Clients;
        if (clients is null)
        {
            self.Connection.Send(frame);
            return;
        }

        foreach (var client in clients)
        {
            client.Connection.Send(frame);
        }
    }
}
