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
/// Handles <c>TM_CS_BIND_SKILLCARD</c> (284) and <c>TM_CS_UNBIND_SKILLCARD</c> (285). The character is resolved by name, as the 253 does, so
/// the reference does not carry its session guard asymmetry over. The judgement and the socket write
/// happen inside the character service's database gate; this class only turns the verdict into frames.
/// </summary>
public class SkillCardService : ISkillCardService
{
    private const ushort BindSkillCardRequestId = (ushort)GamePackets.TM_CS_BIND_SKILLCARD;
    private const ushort UnbindSkillCardRequestId = (ushort)GamePackets.TM_CS_UNBIND_SKILLCARD;

    private readonly ILogger _logger = Log.ForContext<SkillCardService>();
    private readonly ICharacterService _characterService;
    private readonly IItemGroupCatalog _itemGroupCatalog;

    public SkillCardService(ICharacterService characterService, IItemGroupCatalog itemGroupCatalog)
    {
        _characterService = characterService;
        _itemGroupCatalog = itemGroupCatalog;
    }

    public async Task BindAsync(GameClient client, GameActionPackets.BindSkillCardRequest request)
    {
        var info = client.ConnectionInfo;
        var itemValue = unchecked((int)request.ItemHandle);
        SkillCardBindAttempt result;

        try
        {
            result = await _characterService.BindSkillCardAsync(info.CharacterName, request.ItemHandle,
                request.TargetHandle, _itemGroupCatalog);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not bind skill card {itemHandle} for {clientTag}", request.ItemHandle,
                client.ClientTag);
            client.SendResult(BindSkillCardRequestId, (ushort)ResultCode.DBError, itemValue);
            return;
        }

        switch (result.Outcome)
        {
            case SkillCardBindOutcome.NotFound:
                client.SendResult(BindSkillCardRequestId, (ushort)ResultCode.NotExist, itemValue);
                return;
            case SkillCardBindOutcome.NotActable:
                client.SendResult(BindSkillCardRequestId, (ushort)ResultCode.NotActable,
                    unchecked((int)request.TargetHandle));
                return;
            case SkillCardBindOutcome.AccessDenied:
                client.SendResult(BindSkillCardRequestId, (ushort)ResultCode.AccessDenied, itemValue);
                return;
        }

        // The only success answer: the 286 naming the bearer. NGemity sends no TS_SC_RESULT there.
        _logger.Debug("Skill card {itemHandle} bound for {clientTag}", request.ItemHandle, client.ClientTag);
        client.Connection.Send(GameCharacterPackets.BuildSkillCardInfo(request.ItemHandle, info.CharacterHandle));
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
