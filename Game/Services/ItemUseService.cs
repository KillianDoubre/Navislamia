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
/// Handles <c>TM_CS_USE_ITEM</c> (253). The scope is the one the fiche fixes: read the frame,
/// judge the item, answer. The effects of the item (base_type / opt_type) are not applied, the
/// amount is not consumed and no state is written: those belong to the later milestones of the
/// item system.
/// </summary>
public class ItemUseService : IItemUseService
{
    private const ushort UseItemRequestId = (ushort)GamePackets.TM_CS_USE_ITEM;

    private readonly ILogger _logger = Log.ForContext<ItemUseService>();
    private readonly ICharacterService _characterService;
    private readonly IItemUseCatalog _catalog;

    public ItemUseService(ICharacterService characterService, IItemUseCatalog catalog)
    {
        _characterService = characterService;
        _catalog = catalog;
    }

    public async Task UseAsync(GameClient client, GameActionPackets.UseItemRequest request)
    {
        var info = client.ConnectionInfo;
        var value = unchecked((int)request.ItemHandle);

        ItemEntity item;
        try
        {
            item = await _characterService.GetItemByHandleAsync(info.CharacterName, request.ItemHandle);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not read item {itemHandle} for {clientTag}", request.ItemHandle,
                client.ClientTag);
            client.SendResult(UseItemRequestId, (ushort)ResultCode.DBError, value);
            return;
        }

        // The handler only ever looks the handle up in the character's own items, so an item that
        // resolves is owned; anything else is unknown to this client.
        if (item is null)
        {
            client.SendResult(UseItemRequestId, (ushort)ResultCode.NotExist, value);
            return;
        }

        // A resource the catalog does not know cannot be judged: refusing it would be an unproven
        // refusal, so the use is left ungated (fiche §6).
        if (_catalog.TryGetLevels((int)item.ItemResourceId, out var levels))
        {
            var gate = ItemUseRules.CheckUseLevel(info.CharacterLevel, levels);
            if (gate != ResultCode.Success)
            {
                client.SendResult(UseItemRequestId, (ushort)gate, value);
                return;
            }
        }

        // A successful use answers twice, in the order of NGemity's WorldSession::onUseItem
        // (Chihiro/src/Network/GameNetwork/WorldSession.cpp:1336 then :1375): the generic
        // acknowledgement, then the use result echoing the item and target handles of the request.
        client.SendResult(UseItemRequestId, (ushort)ResultCode.Success, value);
        client.Connection.Send(GameCharacterPackets.BuildUseItemResult(request.ItemHandle, request.TargetHandle));
    }
}
