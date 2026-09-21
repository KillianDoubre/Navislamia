using System;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// Handles <c>TM_CS_UNBIND_SKILLCARD</c> (285). The character is resolved by name, as the 253 does, so
/// the reference does not carry its session guard asymmetry over. The judgement and the socket write
/// happen inside the character service's database gate; this class only turns the verdict into frames.
/// </summary>
public class SkillCardService : ISkillCardService
{
    private const ushort UnbindSkillCardRequestId = (ushort)GamePackets.TM_CS_UNBIND_SKILLCARD;

    private readonly ILogger _logger = Log.ForContext<SkillCardService>();
    private readonly ICharacterService _characterService;
    private readonly IItemGroupCatalog _itemGroupCatalog;

    public SkillCardService(ICharacterService characterService, IItemGroupCatalog itemGroupCatalog)
    {
        _characterService = characterService;
        _itemGroupCatalog = itemGroupCatalog;
    }

    public async Task UnbindAsync(GameClient client, GameActionPackets.UnbindSkillCardRequest request)
    {
        var info = client.ConnectionInfo;
        SkillCardBindResult verdict;

        try
        {
            verdict = await _characterService.UnbindSkillCardAsync(info.CharacterName, request.ItemHandle,
                request.TargetHandle, _itemGroupCatalog);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not unbind skill card {itemHandle} for {clientTag}",
                request.ItemHandle, client.ClientTag);
            client.SendResult(UnbindSkillCardRequestId, (ushort)ResultCode.DBError,
                unchecked((int)request.ItemHandle));
            return;
        }

        if (!verdict.Succeeded)
        {
            client.SendResult(UnbindSkillCardRequestId, verdict.ResponseCode, verdict.Value);
            return;
        }

        // The only success answer: the 286 whose null target says the card left its bearer. NGemity
        // sends no TS_SC_RESULT there (Messages::SendSkillCardInfo, WorldSession.cpp:1719).
        client.Connection.Send(GameCharacterPackets.BuildSkillCardInfo(request.ItemHandle, 0));
    }
}
