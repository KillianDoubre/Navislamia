using System;
using System.Linq;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

public class InventoryService : IInventoryService
{
    private const ushort ArrangeRequestId = (ushort)GamePackets.TM_CS_ARRANGE_ITEM;
    private static readonly TimeSpan ArrangeCooldown = TimeSpan.FromSeconds(3);

    private readonly ILogger _logger = Log.ForContext<InventoryService>();
    private readonly ICharacterService _characterService;
    private readonly IItemSortCatalog _catalog;

    public InventoryService(ICharacterService characterService, IItemSortCatalog catalog)
    {
        _characterService = characterService;
        _catalog = catalog;
    }

    public async Task SwapPositionsAsync(GameClient client, GameActionPackets.ChangeItemPositionRequest request)
    {
        const ushort requestId = (ushort)GamePackets.TM_CS_CHANGE_ITEM_POSITION;
        var target = unchecked((int)client.ConnectionInfo.CharacterHandle);
        if (request.IsStorage)
        {
            client.SendResult(requestId, (ushort)ResultCode.NotActable, target);
            return;
        }

        try
        {
            var swapped = await _characterService.SwapItemPositionsAsync(client.ConnectionInfo.CharacterName,
                request.ItemHandle1, request.ItemHandle2);
            if (swapped is null)
            {
                client.SendResult(requestId, (ushort)ResultCode.NotExist, target);
                return;
            }

            SendInventory(client, swapped);
            client.SendResult(requestId, (ushort)ResultCode.Success, target);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not swap items {first} and {second} for {clientTag}",
                request.ItemHandle1, request.ItemHandle2, client.ClientTag);
            client.SendResult(requestId, (ushort)ResultCode.DBError, target);
        }
    }

    public async Task ArrangeAsync(GameClient client, bool isStorage)
    {
        var info = client.ConnectionInfo;
        var target = unchecked((int)info.CharacterHandle);
        if (isStorage)
        {
            client.SendResult(ArrangeRequestId, (ushort)ResultCode.NotActable, target);
            return;
        }

        var now = DateTime.UtcNow;
        if (now < info.NextInventoryArrangeAt)
        {
            client.SendResult(ArrangeRequestId, (ushort)ResultCode.CoolTime, target);
            return;
        }

        try
        {
            var items = await _characterService.ArrangeInventoryAsync(info.CharacterName, _catalog);
            if (items is null)
            {
                client.SendResult(ArrangeRequestId, (ushort)ResultCode.NotExist, target);
                return;
            }

            info.NextInventoryArrangeAt = now + ArrangeCooldown;
            SendInventory(client, items);
            client.SendResult(ArrangeRequestId, (ushort)ResultCode.Success, target);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not arrange the inventory for {clientTag}", client.ClientTag);
            client.SendResult(ArrangeRequestId, (ushort)ResultCode.DBError, target);
        }
    }

    private static void SendInventory(GameClient client, ItemEntity[] items)
    {
        foreach (var packet in GameCharacterPackets.BuildInventory(items))
        {
            client.Connection.Send(packet);
        }
    }
}
