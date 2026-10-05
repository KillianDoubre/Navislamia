using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Jobs;

namespace Navislamia.Game.Services.GmCommands;

/// <summary>
/// <c>/job</c>, <c>/race</c> and <c>/maxskills</c>: the job and the race go through <see cref="IJobChangeService"/>,
/// the same saves and frames as the NPC's job change and race change; the skills through the save of a learnt skill.
/// </summary>
public partial class GmCommandService
{
    private readonly IJobChangeService _jobChange;

    private async Task<bool> RunCharacterAsync(GameClient client, GmCommandDefinition definition, GmCommandLine line)
    {
        switch (definition.Command)
        {
            case GmCommand.Job:
                if (!GmCommandRules.TryParseJob(line.Args, out var job)) { Usage(client, definition); return true; }
                if (_jobChange is null) { Reply(client, "The job change is not available."); return true; }
                Reply(client, await _jobChange.ForceJobAsync(client, job) switch
                {
                    GmJobChange.Done => $"Job {job}, job level 1. Skills and JP are unchanged; /maxskills fills the new tree.",
                    GmJobChange.AlreadyThere => $"Already job {job}.",
                    GmJobChange.NotInRaceTree => $"Job {job} is not in this race's tree; change the race first (/race).",
                    GmJobChange.Busy => "A job change is already running.",
                    _ => "The job change could not be saved."
                });
                return true;

            case GmCommand.Race:
                if (!GmCommandRules.TryParseRace(line.Args, out var race)) { Usage(client, definition); return true; }
                if (_jobChange is null) { Reply(client, "The race change is not available."); return true; }
                Reply(client, await _jobChange.ForceRaceAsync(client, race) switch
                {
                    GmJobChange.Done => "Race changed: base job, skills reset and their JP given back. Reconnect to see the new body.",
                    GmJobChange.AlreadyThere => "The character already has this race.",
                    GmJobChange.Busy => "A job change is already running.",
                    _ => "The race change could not be saved."
                });
                return true;

            case GmCommand.MaxSkills:
                await MaxSkillsAsync(client);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Every skill of the character's trees at its tree's maximum, without JP and whatever the level, job level or
    /// prerequisites: one save, then the raised levels in one <c>TS_SC_SKILL_LIST</c> and the stats, as <c>/learn</c>.
    /// </summary>
    private async Task MaxSkillsAsync(GameClient client)
    {
        var info = client.ConnectionInfo;
        Dictionary<int, byte> learned;
        lock (info.LearnedSkills) learned = new Dictionary<int, byte>(info.LearnedSkills);
        var jobs = info.PreviousJobs.Select(entry => entry.Job).Append(info.CharacterJob);
        var raised = GmCommandRules.MaxSkillLevels(_skillCatalog, jobs, learned);
        if (raised.Count == 0)
        {
            Reply(client, "Every skill of this job is already at its maximum.");
            return;
        }

        if (!await _characterService.SaveLearnedSkillsAsync(info.CharacterName, raised))
        {
            Reply(client, "The skills could not be saved.");
            return;
        }

        lock (info.LearnedSkills)
        {
            foreach (var (skillId, level) in raised) info.LearnedSkills[skillId] = level;
        }

        client.Connection.Send(GameCharacterPackets.BuildSkillList(info.CharacterHandle, raised.ToArray()));
        _statService.RefreshPassives(info);
        SendStats(client);
        Reply(client, $"{raised.Count} skills raised to their maximum level.");
    }
}
