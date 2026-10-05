using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Creatures;

namespace Navislamia.Game.Services.Jobs;

/// <summary>
/// The master class skill reset (<c>gold_skill_reset_check</c>, <c>jp_skill_reset_check</c>) and the race change
/// (<c>NPC_JobChange_change_race</c>, <c>NPC_JobChange_set_race</c>) of the Epic 7 Lua, on the official
/// <c>ResetSkill</c>/<c>ResetJob</c>/<c>SetRace</c> (docs/packet-specs/socle-changement-metier.md §8).
/// </summary>
public sealed partial class JobChangeService
{
    private readonly SkillCatalog _skills;
    private readonly ILevelingService _leveling;
    private readonly ISkillCastService _casts;
    private readonly ICreatureService _creatures;
    private readonly Rates.IRateService _rates;

    private static readonly int[] Races = { 4, 5, 3 };

    /// <summary>The master class's page at a job NPC: the reset text of the resets done, and the two resets.</summary>
    private async Task<JobChangeStep> MasterResetPageAsync(GameClient client, int npcId)
    {
        var count = SkillResetRules.ResetCount(await _characters.GetFlagsAsync(client.ConnectionInfo.CharacterName));
        return new JobChangeStep(new NpcDialogDefinition
        {
            Title = JobChangeRules.Title(npcId),
            Text = SkillResetRules.ResetText(count),
            Menu = Menu(("@90604803", SkillResetRules.GoldReset + "(npc_id)"), ("@90604804", SkillResetRules.JpReset + "(npc_id)"),
                (JobChangeRules.Close, string.Empty))
        }, true, 0, false);
    }

    /// <summary><c>gold_skill_reset_check</c> / <c>jp_skill_reset_check</c>: pay, then <c>reset_skill(3)</c>.</summary>
    private async Task<JobChangeStep> ResetSkillsAsync(GameClient client, int npcId, bool byGold)
    {
        var info = client.ConnectionInfo;
        if (_skills is null || !JobChangeRules.JobNpcs.Contains(npcId)
            || JobChangeRules.Depth(info.PreviousJobs) != JobChangeRules.MasterDepth
            || Interlocked.CompareExchange(ref info.JobChangeInProgress, 1, 0) != 0)
        {
            return JobChangeStep.Nothing;
        }

        try
        {
            var count = SkillResetRules.ResetCount(await _characters.GetFlagsAsync(info.CharacterName));
            var cost = (byGold ? SkillResetRules.GoldCosts : SkillResetRules.JpCosts)[count];
            var page = new NpcDialogDefinition { Title = JobChangeRules.Title(npcId), Menu = Menu((JobChangeRules.Close, string.Empty)) };

            // The price first: gold through the wallet lock, JP through the progress lock.
            var paid = byGold ? info.TryDebitGold(cost) : TrySpendJp(info, cost);
            if (!paid)
            {
                page.Text = "@90604806";
                return new JobChangeStep(page, true, 0, false);
            }

            var plan = PlanReset(info, JobChangeRules.MasterDepth);
            var committed = await CommitResetAsync(client, plan, byGold ? info.CharacterGold : null,
                Math.Min(count + 1, SkillResetRules.MaxResetCount), null);
            if (!committed)
            {
                if (byGold) info.AddGold(cost);
                else lock (info.ProgressLock) info.CharacterJp += cost;
                return JobChangeStep.Nothing;
            }

            if (byGold)
            {
                client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "gold", info.CharacterGold));
            }

            page.Text = "@90604805";
            return new JobChangeStep(page, true, 0, false);
        }
        finally
        {
            Interlocked.Exchange(ref info.JobChangeInProgress, 0);
        }
    }

    /// <summary><c>NPC_JobChange_change_race</c>: the two other races, with the stone in the bag.</summary>
    private async Task<JobChangeStep> ChangeRacePageAsync(GameClient client, int npcId)
    {
        var info = client.ConnectionInfo;
        if (!IsRaceChangeNpc(npcId))
        {
            return JobChangeStep.Nothing;
        }

        var page = new NpcDialogDefinition { Title = RaceTitle(npcId), Menu = new List<NpcDialogMenuEntry>() };
        if (await HasStoneAsync(info))
        {
            page.Text = "@90010256";
            foreach (var race in Races.Where(race => race != info.CharacterRace))
            {
                page.Menu.Add(new NpcDialogMenuEntry { Label = "@" + (90010253 + Array.IndexOf(Races, race)), Trigger = $"{SkillResetRules.SetRace}({race})" });
            }
        }
        else
        {
            page.Text = "@90010257";
            page.Menu.Add(new NpcDialogMenuEntry { Label = JobChangeRules.Close, Trigger = " " });
        }

        return new JobChangeStep(page, true, 0, false);
    }

    /// <summary>
    /// <c>NPC_JobChange_set_race(race)</c> on <c>StructPlayer::SetRace</c>: <c>ResetSkill(0)</c>, <c>ResetJob(0)</c>, the
    /// new race's base job, the race, and one stone taken.
    /// </summary>
    private async Task<JobChangeStep> SetRaceAsync(GameClient client, int npcId, string trigger)
    {
        var info = client.ConnectionInfo;
        var argument = trigger?.Trim();
        var race = argument is not null && argument.StartsWith(SkillResetRules.SetRace + "(", StringComparison.Ordinal)
                   && argument.EndsWith(')') && int.TryParse(argument[(SkillResetRules.SetRace.Length + 1)..^1], out var parsed)
            ? parsed
            : 0;
        if (_skills is null || !IsRaceChangeNpc(npcId) || SkillResetRules.BaseJob(race) == 0
            || Interlocked.CompareExchange(ref info.JobChangeInProgress, 1, 0) != 0)
        {
            return JobChangeStep.Nothing;
        }

        try
        {
            var page = new NpcDialogDefinition { Title = RaceTitle(npcId), Menu = Menu((JobChangeRules.Close, " ")) };
            if (!await HasStoneAsync(info))
            {
                page.Text = "@90010257";
                return new JobChangeStep(page, true, 0, false);
            }

            // SetRace refuses the race already held (success ~= 1).
            if (race == info.CharacterRace)
            {
                page.Text = "@90010259";
                return new JobChangeStep(page, true, 0, false);
            }

            var plan = PlanReset(info, 0);
            var committed = await CommitResetAsync(client, plan, null, null,
                new RaceChangeWrite(race, SkillResetRules.BaseJob(race), SkillResetRules.RaceChangeItem));
            page.Text = committed ? "@90010258" : "@90010259";
            return new JobChangeStep(page, true, 0, false);
        }
        finally
        {
            Interlocked.Exchange(ref info.JobChangeInProgress, 0);
        }
    }

    public async Task<GmJobChange> ForceRaceAsync(GameClient client, int race)
    {
        var info = client.ConnectionInfo;
        var baseJob = SkillResetRules.BaseJob(race);
        if (baseJob == 0)
        {
            return GmJobChange.UnknownRace;
        }

        if (race == info.CharacterRace)
        {
            return GmJobChange.AlreadyThere;
        }

        if (_skills is null || Interlocked.CompareExchange(ref info.JobChangeInProgress, 1, 0) != 0)
        {
            return GmJobChange.Busy;
        }

        try
        {
            // SetRace's own path (ResetSkill(0), ResetJob(0), the base job of the race): only the stone is left out.
            var committed = await CommitResetAsync(client, PlanReset(info, 0), null, null,
                new RaceChangeWrite(race, baseJob, 0));
            return committed ? GmJobChange.Done : GmJobChange.NotSaved;
        }
        finally
        {
            Interlocked.Exchange(ref info.JobChangeInProgress, 0);
        }
    }

    public async Task<GmJobChange> ForceJobAsync(GameClient client, int job)
    {
        var info = client.ConnectionInfo;
        var path = JobChangeRules.PathTo(info.CharacterRace, job);
        if (path is null)
        {
            return GmJobChange.NotInRaceTree;
        }

        if (info.CharacterJob == job)
        {
            return GmJobChange.AlreadyThere;
        }

        if (Interlocked.CompareExchange(ref info.JobChangeInProgress, 1, 0) != 0)
        {
            return GmJobChange.Busy;
        }

        try
        {
            // The jobs left behind keep the job level they had, at least what the official change asks to leave them.
            var known = new Dictionary<int, int>();
            foreach (var (previousJob, previousJobLevel) in info.PreviousJobs) known[previousJob] = previousJobLevel;
            known[info.CharacterJob] = info.CharacterJobLevel;
            var previous = new List<(int Job, int JobLevel)>();
            for (var depth = 0; depth < path.Count - 1; depth++)
            {
                previous.Add((path[depth], JobChangeRules.LeftJobLevel(depth, known.GetValueOrDefault(path[depth]))));
            }

            // Becoming a master class grants its talent points once, as the official change does.
            var talentGrant = previous.Count == JobChangeRules.MasterDepth
                              && JobChangeRules.Depth(info.PreviousJobs) < JobChangeRules.MasterDepth
                ? JobChangeRules.MasterClassTalentPoints
                : 0;
            var talentPoints = await _characters.ChangeJobAsync(info.CharacterName, job, previous, talentGrant);
            if (talentPoints is null)
            {
                return GmJobChange.NotSaved;
            }

            info.PreviousJobs.Clear();
            info.PreviousJobs.AddRange(previous);
            info.CharacterJob = job;
            info.CharacterJobLevel = 1;
            info.CharacterTalentPoint = talentPoints.Value;

            SendJobChange(client, new JobChangeResult(job, 1, previous, previous.Count, talentGrant), talentPoints.Value);
            _parties?.OnJobChanged(client);
            if (_quests is not null)
            {
                await _quests.RefreshAsync(client);
            }

            _logger.Information("{clientTag} GM job change to {job} (depth {depth})", client.ClientTag, job, previous.Count);
            return GmJobChange.Done;
        }
        finally
        {
            Interlocked.Exchange(ref info.JobChangeInProgress, 0);
        }
    }

    private static bool IsRaceChangeNpc(int npcId) => JobChangeRules.JobNpcs.Contains(npcId) || npcId == JobChangeRules.TutorialNpcId;

    private static string RaceTitle(int npcId) => npcId == JobChangeRules.TutorialNpcId ? "@90300401" : JobChangeRules.Title(npcId);

    private async Task<bool> HasStoneAsync(ConnectionInfo info) =>
        (await _characters.GetCarriedItemsAsync(info.CharacterName)).Any(item =>
            item.ItemResourceId == SkillResetRules.RaceChangeItem && item.Amount > 0 && ItemWearRules.IsWornByPlayer(item) == false
            && item.EquippedBySummonId is null);

    private static bool TrySpendJp(ConnectionInfo info, long cost)
    {
        lock (info.ProgressLock)
        {
            if (info.CharacterJp < cost) return false;
            info.CharacterJp -= cost;
            return true;
        }
    }

    /// <summary>What a reset to <paramref name="jobDepth"/> gives back, from the session (the JP still to be added).</summary>
    private sealed record ResetPlan(SkillReset Skills, long JpBack, int TalentPoint, bool ToBaseJob);

    private ResetPlan PlanReset(ConnectionInfo info, int jobDepth)
    {
        Dictionary<int, byte> learned;
        lock (info.LearnedSkills) learned = new Dictionary<int, byte>(info.LearnedSkills);
        var reset = SkillResetRules.Reset(_skills, info.PreviousJobs, info.CharacterJob, learned, jobDepth,
            _rates?.SkillJpCost ?? 1);
        var depth = JobChangeRules.Depth(info.PreviousJobs);
        long? JobLevelCost(int d, int level) =>
            _leveling is not null && _leveling.TryGetNextJobLevelCost(d, level, out var cost) ? cost : null;

        // onResetSkill: the current job's job levels are paid back and it starts again at JLv 1.
        var jp = reset.JpBack + SkillResetRules.JobLevelJp(JobLevelCost, depth, info.CharacterJobLevel);
        var talentPoint = info.CharacterTalentPoint + reset.TpBack;
        if (jobDepth == 0 && depth > 0)
        {
            // ResetJob(0) (getJPAfterJobReset): every job left behind pays its job levels back too, and the master
            // class's own talent points go (getTPAfterJobReset).
            for (var d = 0; d < depth; d++)
            {
                jp += SkillResetRules.JobLevelJp(JobLevelCost, d, info.PreviousJobs[d].JobLevel);
            }

            if (depth >= JobChangeRules.MasterDepth)
            {
                talentPoint = Math.Max(0, talentPoint - JobChangeRules.MasterClassTalentPoints);
            }
        }

        return new ResetPlan(reset, jp, talentPoint, jobDepth == 0);
    }

    /// <summary>Saves the reset, then applies it to the session and tells the client and its observers.</summary>
    private async Task<bool> CommitResetAsync(GameClient client, ResetPlan plan, long? gold, int? resetCount,
        RaceChangeWrite race)
    {
        var info = client.ConnectionInfo;
        long jp;
        lock (info.ProgressLock)
        {
            info.CharacterJp += plan.JpBack;
            jp = info.CharacterJp;
        }

        SkillResetCommit commit;
        try
        {
            commit = await _characters.ApplySkillResetAsync(info.CharacterName,
                new SkillResetWrite(plan.Skills.Remaining, jp, plan.TalentPoint, 1, gold, resetCount, race));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "{clientTag} skill reset was not saved", client.ClientTag);
            commit = new SkillResetCommit(false);
        }

        if (!commit.Saved)
        {
            lock (info.ProgressLock) info.CharacterJp -= plan.JpBack;
            return false;
        }

        // RemoveSkill: the cast in progress and the auras of the skills taken away stop first.
        _casts?.TurnOffAurasOf(client, plan.Skills.Removed.Concat(plan.Skills.Lowered.Select(entry => entry.SkillId)).ToArray());
        var creatureControl = info.LearnedSkills.GetValueOrDefault(CreatureRules.CreatureControlSkill);
        lock (info.LearnedSkills)
        {
            info.LearnedSkills.Clear();
            foreach (var (skillId, level) in plan.Skills.Remaining) info.LearnedSkills[skillId] = level;
        }

        info.CharacterTalentPoint = plan.TalentPoint;
        info.CharacterJobLevel = 1;
        var handle = info.CharacterHandle;
        if (race is not null)
        {
            info.CharacterRace = race.Race;
            info.CharacterJob = race.Job;
            info.PreviousJobs.Clear();
            if (info.Appearance is { } appearance)
            {
                info.Appearance = new PlayerAppearance
                {
                    Race = (byte)race.Race, Sex = appearance.Sex, SkinColor = appearance.SkinColor, FaceId = appearance.FaceId,
                    FaceTextureId = appearance.FaceTextureId, HairId = appearance.HairId,
                    HairColorIndex = appearance.HairColorIndex, HairColorRgb = appearance.HairColorRgb,
                    HideEquipFlag = appearance.HideEquipFlag
                };
            }

            if (commit.Stone is { } stone)
            {
                client.Connection.Send(stone.Amount > 0
                    ? GameCharacterPackets.BuildUpdateItemCount((uint)stone.Id, stone.Amount)
                    : GameCharacterPackets.BuildDestroyItem((uint)stone.Id));
            }

            // onAfterResetRace and onAfterResetJob: race and job to the region, then the job info.
            Broadcast(client, GameStatPackets.BuildProperty(handle, "race", race.Race));
            Broadcast(client, GameStatPackets.BuildProperty(handle, "job", race.Job));
            for (var depth = 0; depth < 3; depth++)
            {
                client.Connection.Send(GameStatPackets.BuildProperty(handle, $"job_{depth}", 0));
                client.Connection.Send(GameStatPackets.BuildProperty(handle, $"jlv_{depth}", 0));
            }

            client.Connection.Send(GameStatPackets.BuildProperty(handle, "job_depth", 0));
            _parties?.OnJobChanged(client);
        }

        // onResetSkill: the whole skill list again (REFRESH), the JP and the job level.
        client.Connection.Send(GameCharacterPackets.BuildSkillList(handle,
            plan.Skills.Remaining.Select(entry => new SkillListEntry(entry.Key, entry.Value, 0, 0)).ToArray(), refresh: true));
        client.Connection.Send(GameCharacterPackets.BuildExpUpdate(handle, info.CharacterExp, info.CharacterJp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "job_level", 1));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "tp", info.CharacterTalentPoint));

        // CalculateStat: the job, its job level and the passives feed the base stats.
        _stats.RefreshPassives(info);
        var stats = _stats.Compute(info);
        var maxHp = (int)stats.Total.MaxHp;
        info.CharacterMaxHp = maxHp;
        info.CharacterHp = Math.Min(info.CharacterHp, maxHp);
        info.CharacterMp = Math.Min(info.CharacterMp, (int)stats.Total.MaxMp);
        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, stats.Total, StatInfoType.Total));
        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, stats.ByItem, StatInfoType.ByItem));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_hp", maxHp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_mp", (int)stats.Total.MaxMp));

        // onResetSkill: a creature beyond the Creature Control left goes back to its card and leaves the formation.
        if (_creatures is not null && info.LearnedSkills.GetValueOrDefault(CreatureRules.CreatureControlSkill) < creatureControl)
        {
            CreatureCard[] outside;
            lock (info.SummonLock) outside = info.CreatureCards.Values.Where(card => card.SummonHandle != 0).ToArray();
            foreach (var card in outside) _creatures.Unsummon(client, (uint)card.ItemId);
            await _creatures.EquipAsync(client, info.SummonSlots.Select(slot => (uint)slot).ToArray(), false);
        }

        if (_quests is not null)
        {
            await _quests.RefreshAsync(client);
        }

        _logger.Information("{clientTag} skill reset to depth {depth}: {removed} removed, {lowered} lowered, {jp} JP back",
            client.ClientTag, plan.ToBaseJob ? 0 : JobChangeRules.MasterDepth, plan.Skills.Removed.Count,
            plan.Skills.Lowered.Count, plan.JpBack);
        return true;
    }

    private void Broadcast(GameClient client, byte[] frame)
    {
        if (_players is not null) _players.SendToObservers(client, frame, includeSelf: true);
        else client.Connection.Send(frame);
    }
}
