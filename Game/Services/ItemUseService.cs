using System;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Pets;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// Handles <c>TM_CS_USE_ITEM</c> (253). The scope is the one the fiche fixes: read the frame,
/// judge the item, consume one unit, answer. The effects of the item (base_type / opt_type) are not
/// applied yet: they belong to the later milestones of the item system — except a pet cage
/// (<c>SummonPet</c>), which calls its pet through <see cref="IPetSummonService"/>.
/// </summary>
public class ItemUseService : IItemUseService
{
    private const ushort UseItemRequestId = (ushort)GamePackets.TM_CS_USE_ITEM;

    private readonly ILogger _logger = Log.ForContext<ItemUseService>();
    private readonly ICharacterService _characterService;
    private readonly IItemUseCatalog _catalog;
    private readonly IPetSummonService _petSummon;

    public ItemUseService(ICharacterService characterService, IItemUseCatalog catalog,
        IPetSummonService petSummon)
    {
        _characterService = characterService;
        _catalog = catalog;
        _petSummon = petSummon;
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

        // A pet rename item (effect RenamePet) needs a pet out; refused before anything is consumed.
        if (_catalog.RenamesPet((int)item.ItemResourceId) && !_petSummon.HasPetOut(client))
        {
            _logger.Debug("{clientTag} used pet rename item {resourceId} with no pet out: refused",
                client.ClientTag, item.ItemResourceId);
            client.SendResult(UseItemRequestId, (ushort)ResultCode.NotActable, value);
            return;
        }

        // NGemity erases the unit inside Player::UseItem, so the stack update (TS_SC_UPDATE_ITEM_COUNT,
        // or TS_SC_DESTROY_ITEM for the last unit) leaves before the result.
        if (_catalog.IsConsumedOnUse((int)item.ItemResourceId))
        {
            long? remaining;
            try
            {
                remaining = await _characterService.ConsumeItemAsync(info.CharacterName, request.ItemHandle, 1);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Could not consume item {itemHandle} for {clientTag}",
                    request.ItemHandle, client.ClientTag);
                client.SendResult(UseItemRequestId, (ushort)ResultCode.DBError, value);
                return;
            }

            // The item can vanish between the read and the consumption (a concurrent erase).
            if (remaining is null)
            {
                client.SendResult(UseItemRequestId, (ushort)ResultCode.NotExist, value);
                return;
            }

            client.Connection.Send(remaining == 0
                ? GameCharacterPackets.BuildDestroyItem(request.ItemHandle)
                : GameCharacterPackets.BuildUpdateItemCount(request.ItemHandle, remaining.Value));
        }

        // A successful use answers twice, in the order of NGemity's WorldSession::onUseItem
        // (Chihiro/src/Network/GameNetwork/WorldSession.cpp:1336 then :1375): the generic
        // acknowledgement, then the use result echoing the item and target handles of the request.
        client.SendResult(UseItemRequestId, (ushort)ResultCode.Success, value);
        client.Connection.Send(GameCharacterPackets.BuildUseItemResult(request.ItemHandle, request.TargetHandle));
        _logger.Debug("{clientTag} used item {resourceId} (handle {itemHandle}, target {targetHandle})",
            client.ClientTag, item.ItemResourceId, request.ItemHandle, request.TargetHandle);

        // A cage is a reusable item (type Use): the use is acknowledged like any other, then the pet comes
        // out, goes away or is swapped. The pet frames follow the acknowledgement.
        if (await _petSummon.TryUseCageAsync(client, item.ItemResourceId, request.ItemHandle))
        {
            return;
        }

        if (_catalog.RenamesPet((int)item.ItemResourceId))
        {
            _petSummon.OfferRename(client);
        }
    }
}
