using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Configuration.Options;
using System.Collections.Generic;

namespace Navislamia.Game.Services;

/// <summary>
/// The Epic 7.3 quest cycle: NPC dialogs, acceptance, objective updates, removal and rewards.
/// </summary>
public interface IQuestService
{
    /// <summary>
    /// Sends the complete <c>TM_SC_QUEST_LIST</c> (600) of the client's character. The client clears its
    /// own container on receive, so this frame is a full resynchronisation, never a delta.
    /// </summary>
    Task SendQuestListAsync(GameClient client);

    /// <summary>
    /// Handles <c>TM_CS_DROP_QUEST</c> (603): judge the code, erase the quest from the character's state,
    /// answer and resynchronise the list.
    /// </summary>
    Task DropQuestAsync(GameClient client, GameActionPackets.DropQuestRequest request);

    /// <summary>
    /// Handles <c>TM_CS_END_QUEST</c> (605): judge the frame, then the character's state, and answer a
    /// <c>TS_SC_RESULT</c> tagged 605. Consumption, rewards and completion commit atomically.
    /// </summary>
    Task EndQuestAsync(GameClient client, GameActionPackets.EndQuestRequest request);

    bool HasNpcQuests(int npcId);
    Task<IReadOnlyList<NpcDialogMenuEntry>> GetNpcOffersAsync(GameClient client, int npcId);
    Task<NpcDialogDefinition> GetQuestDialogAsync(GameClient client, int npcId, int code, string title);
    Task StartQuestAsync(GameClient client, int npcId, int code, int textId);
    Task OnMonsterKilledAsync(GameClient client, int monsterId, float x, float y, float z);
    Task LeaveWorldAsync(GameClient client);
    Task RefreshAsync(GameClient client);
}
