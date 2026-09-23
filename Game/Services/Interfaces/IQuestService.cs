using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

/// <summary>
/// The quest socle of Epic 7.3: the state a character carries (600 / 601) and its removal (603).
/// Acceptance and progression are out of scope — they need the quest catalogue and the quest scripts
/// (<c>docs/packet-specs/socle-quetes.md</c> §5.6, §7).
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
}
