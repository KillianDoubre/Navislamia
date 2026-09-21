using System;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// Handles <c>TM_CS_BIND_SKILLCARD</c> (284): a player binds a skill card of their inventory to
/// themselves. The judgement (handle, target, admission of the card, write of the bound state) is the
/// gated <see cref="ICharacterService.BindSkillCardAsync"/>; this service turns its verdict into the
/// wire answer.
/// </summary>
/// <remarks>
/// The reference for the answer is NGemity's <c>WorldSession::onBindSkillCard</c>
/// (Chihiro/src/Network/GameNetwork/WorldSession.cpp:1678-1701) followed by
/// <c>Player::BindSkillCard</c> / <c>Messages.cpp:1010-1017</c>: a refusal sends <c>TS_SC_RESULT</c>
/// carrying the request id and the code, a success sends <c>TM_SC_SKILLCARD_INFO</c> (286) <em>only</em>
/// — no generic result, no inventory refresh. <c>TS_RESULT_NOT_EXIST</c> names the unknown item
/// handle, <c>TS_RESULT_NOT_ACTABLE</c> the target that is not the character,
/// <c>TS_RESULT_ACCESS_DENIED</c> the card itself (group, worn, already bound).
/// </remarks>
public class SkillCardService : ISkillCardService
{
    private const ushort BindRequestId = (ushort)GamePackets.TM_CS_BIND_SKILLCARD;

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

        SkillCardBindResult result;
        try
        {
            result = await _characterService.BindSkillCardAsync(info.CharacterName, request.ItemHandle,
                request.TargetHandle, _itemGroupCatalog);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not bind skill card {itemHandle} for {clientTag}", request.ItemHandle,
                client.ClientTag);
            client.SendResult(BindRequestId, (ushort)ResultCode.DBError, itemValue);
            return;
        }

        switch (result.Outcome)
        {
            case SkillCardBindOutcome.NotFound:
                client.SendResult(BindRequestId, (ushort)ResultCode.NotExist, itemValue);
                return;
            case SkillCardBindOutcome.NotActable:
                client.SendResult(BindRequestId, (ushort)ResultCode.NotActable,
                    unchecked((int)request.TargetHandle));
                return;
            case SkillCardBindOutcome.AccessDenied:
                client.SendResult(BindRequestId, (ushort)ResultCode.AccessDenied, itemValue);
                return;
        }

        _logger.Debug("Skill card {itemHandle} bound for {clientTag}", request.ItemHandle, client.ClientTag);

        // The state sent back is the one after the bind: the item handle, and as target the bearer —
        // the character itself, since only a self target is accepted above.
        client.Connection.Send(GameCharacterPackets.BuildSkillCardInfo(request.ItemHandle, info.CharacterHandle));
    }
}
