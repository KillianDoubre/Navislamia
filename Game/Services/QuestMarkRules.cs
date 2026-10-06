using System.Collections.Generic;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Services;

/// <summary>
/// The quest mark over an NPC's head, for one player (docs/packet-specs/socle-marqueurs-quete.md): the NPC's creature
/// status carries it (<c>TS_SC_ENTER</c> @26, <c>TS_SC_STATUS_CHANGE</c> 500), one bit at most, the first that holds of
/// <c>GetStatusCode</c> (<c>GameMessage.cpp:1843-1856</c>): a quest to hand in ("?"), else one to take ("!"), else one in
/// progress.
/// </summary>
public static class QuestMarkRules
{
    /// <summary>
    /// One <c>QuestLink</c> of the NPC seen by the player: the quest's type, the link's start/progress/end flags, and
    /// where the player stands with that quest.
    /// </summary>
    public readonly record struct Link(int Type, bool Start, bool Progress, bool End, bool Startable, bool InProgress,
        bool Finishable);

    /// <summary>The official random quests (<c>QUEST_RANDOM_KILL_INDIVIDUAL</c> 901, <c>QUEST_RANDOM_COLLECT</c> 902).</summary>
    public static bool IsRandom(int type) => type is 901 or 902;

    public static uint Mark(IEnumerable<Link> links)
    {
        var list = links as IReadOnlyCollection<Link> ?? new List<Link>(links);
        // StructNPC::HasFinishableQuest: an end link whose quest is finishable.
        foreach (var link in list)
            if (link.End && link.Finishable) return CreatureStatus.NpcHasFinishableQuest;
        if (HasStartable(list)) return CreatureStatus.NpcHasStartableQuest;
        // StructNPC::HasInProgressQuest: a progress link whose quest runs (a failed one counts, IsInProgressQuest).
        foreach (var link in list)
            if (link.Progress && link.InProgress) return CreatureStatus.NpcHasInProgressQuest;
        return 0;
    }

    /// <summary>
    /// <c>StructNPC::HasStartableQuest</c> (<c>StructNPC.cpp:127-154</c>): any startable quest of a start link — but the
    /// NPC's random contracts count only while none of them is running, so a contract giver shows no "!" while one of
    /// its contracts is taken.
    /// </summary>
    private static bool HasStartable(IReadOnlyCollection<Link> links)
    {
        bool startableRandom = false, runningRandom = false;
        foreach (var link in links)
        {
            if (!link.Start) continue;
            if (IsRandom(link.Type))
            {
                if (link.InProgress || link.Finishable) runningRandom = true;
                else if (link.Startable) startableRandom = true;
                continue;
            }

            if (link.Startable) return true;
        }

        return startableRandom && !runningRandom;
    }
}
