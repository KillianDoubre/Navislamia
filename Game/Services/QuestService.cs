using System;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// The quest socle of Epic 7.3. The state it reads lives in the character's own rows
/// (<c>CharacterQuests</c>), because the packets impose it: 600 exposes exactly the fields 603 erases,
/// and no other writer exists yet (<c>docs/packet-specs/socle-quetes.md</c> §5.6, §8.4).
/// </summary>
public class QuestService : IQuestService
{
    private const ushort DropQuestRequestId = (ushort)GamePackets.TM_CS_DROP_QUEST;

    private readonly ILogger _logger = Log.ForContext<QuestService>();
    private readonly ICharacterService _characterService;

    public QuestService(ICharacterService characterService)
    {
        _characterService = characterService;
    }

    public async Task SendQuestListAsync(GameClient client)
    {
        CharacterQuestEntity[] quests;
        try
        {
            quests = await _characterService.GetQuestsAsync(client.ConnectionInfo.CharacterName);
        }
        catch (Exception exception)
        {
            // An empty 600 is not a harmless default: the client resets its container on receive, so a
            // frame built from an unreadable state would erase quests the character really carries.
            _logger.Error(exception, "Could not read the quest list of {clientTag}", client.ClientTag);
            return;
        }

        client.Connection.Send(GameQuestPackets.BuildQuestList(quests));
    }

    public async Task DropQuestAsync(GameClient client, GameActionPackets.DropQuestRequest request)
    {
        var verdict = QuestDropRules.CheckRequest(request.Code);
        if (verdict != ResultCode.Success)
        {
            client.SendResult(DropQuestRequestId, (ushort)verdict);
            return;
        }

        bool dropped;
        try
        {
            dropped = await _characterService.DropQuestAsync(client.ConnectionInfo.CharacterName, request.Code);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not drop quest {code} for {clientTag}", request.Code, client.ClientTag);
            client.SendResult(DropQuestRequestId, (ushort)ResultCode.DBError);
            return;
        }

        // Success only when a row was actually erased; NGemity answers NotActable for a quest the player
        // does not carry (WorldSession.cpp:2039-2042).
        client.SendResult(DropQuestRequestId, (ushort)(dropped ? ResultCode.Success : ResultCode.NotActable));

        if (dropped)
        {
            // The result carries no list, and 600 is the only frame the client rebuilds its state from.
            await SendQuestListAsync(client);
        }
    }
}
