using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// The engine of <c>TM_CS_SOULSTONE_CRAFT</c> (260): it fills the chassis of an item with the soul stones
/// the client names, in the order and with the refusals of NGemity <c>WorldSession.cpp:1497-1588</c>.
///
/// The frame names one item to socket and four slots, each holding the handle of a stone or a zero; the
/// slots past the item's own chassis are ignored, not refused. Every refusal is a <c>TM_SC_RESULT</c> (0)
/// carrying the received id: <c>NotExist</c> (1) when the item being socketed is not one of the
/// character's, <c>AccessDenied</c> (6) when a stone's handle is not, <c>NotActable</c> (5) when a name
/// resolves but is no soul stone, <c>AlreadyExist</c> (9) when the two-stone rule is broken,
/// <c>InvalidArgument</c> (28) when the frame names no stone at all, and <c>NotEnoughMoney</c> (10) when
/// the character cannot pay the socketing. A success answers the paid gold (<c>TM_SC_GOLD_UPDATE</c>),
/// the spent stones (<c>TM_SC_UPDATE_ITEM_COUNT</c> or <c>TM_SC_DESTROY_ITEM</c>), the socketed item
/// (<c>TM_SC_INVENTORY</c>, 207) and then <c>Success</c> (0).
///
/// What the frame does not do: no endurance is written (the reference's durability loop reads a live
/// client object, docs/packet-specs/260-soulstone-craft.md §6.3), no contact with an NPC artisan is
/// required (§5.4) and no <c>TS_SC_ERR</c> (500) accompanies a refusal (§7.3). Those three await
/// Killian's arbitration.
/// </summary>
public class SoulstoneCraftService : ISoulstoneCraftService
{
    private const ushort SocketCapacity = 4;

    private readonly ILogger _logger = Log.ForContext<SoulstoneCraftService>();
    private readonly ICharacterService _characterService;
    private readonly ISoulstoneCraftCatalog _catalog;

    public SoulstoneCraftService(ICharacterService characterService, ISoulstoneCraftCatalog catalog)
    {
        _characterService = characterService;
        _catalog = catalog;
    }

    public async Task HandleAsync(GameClient client, byte[] packet)
    {
        const ushort requestId = (ushort)GamePackets.TM_CS_SOULSTONE_CRAFT;

        // Nothing to answer before the character is in the world: no inventory to socket into and no
        // handle to name. Same precedent as the socle and TM_CS_GET_REGION_INFO's handler.
        if (client.ConnectionInfo.CharacterHandle == 0)
        {
            _logger.Debug("Soulstone craft received from {clientTag} outside the world, dropped", client.ClientTag);
            return;
        }

        if (!GameActionPackets.TryReadSoulstoneCraft(packet, out var request))
        {
            _logger.Warning("Malformed soulstone craft frame from {clientTag} (Length: {length}), refused with InvalidArgument",
                client.ClientTag, packet.Length);
            client.SendResult(requestId, (ushort)ResultCode.InvalidArgument);
            return;
        }

        await CraftAsync(client, requestId, request);
    }

    private async Task CraftAsync(GameClient client, ushort requestId,
        GameActionPackets.SoulstoneCraftRequest request)
    {
        // The item being socketed comes first: NGemity refuses on its absence before it looks at a stone
        // (WorldSession.cpp:1503-1507, NOT_EXIST with the handle as value).
        ItemEntity item;
        try
        {
            item = await _characterService.GetItemByHandleAsync(client.ConnectionInfo.CharacterName,
                request.CraftItemHandle);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not read item {itemHandle} for {clientTag}", request.CraftItemHandle,
                client.ClientTag);
            client.SendResult(requestId, (ushort)ResultCode.DBError, unchecked((int)request.CraftItemHandle));
            return;
        }

        if (item is null)
        {
            _logger.Debug("Soulstone craft from {clientTag} names unknown item {itemHandle}, refused",
                client.ClientTag, request.CraftItemHandle);
            client.SendResult(requestId, (ushort)ResultCode.NotExist, unchecked((int)request.CraftItemHandle));
            return;
        }

        // The item's chassis count decides how many of the four slots can be filled at all; the reference
        // refuses anything outside 1..4 with ACCESS_DENIED and the handle as value (WorldSession.cpp:1508-1513).
        if (!_catalog.TryGetResource((int)item.ItemResourceId, out var crafted))
        {
            _logger.Warning("Item {itemHandle} of {clientTag} carries unknown resource {resourceId}: its chassis count cannot be read, refused with AccessDenied",
                request.CraftItemHandle, client.ClientTag, item.ItemResourceId);
            client.SendResult(requestId, (ushort)ResultCode.AccessDenied, unchecked((int)request.CraftItemHandle));
            return;
        }

        if (crafted.SocketCount < 1 || crafted.SocketCount > SocketCapacity)
        {
            _logger.Debug("Item {itemHandle} of {clientTag} has {socketCount} chassis, refused with AccessDenied",
                request.CraftItemHandle, client.ClientTag, crafted.SocketCount);
            client.SendResult(requestId, (ushort)ResultCode.AccessDenied, unchecked((int)request.CraftItemHandle));
            return;
        }

        var stones = new List<NamedStone>(crafted.SocketCount);
        foreach (var slot in CraftingSocleRules.FilledSlots(request.SoulstoneHandles, crafted.SocketCount))
        {
            var handle = request.SoulstoneHandles[slot];

            ItemEntity stone;
            try
            {
                stone = await _characterService.GetItemByHandleAsync(client.ConnectionInfo.CharacterName, handle);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Could not read item {itemHandle} for {clientTag}", handle, client.ClientTag);
                client.SendResult(requestId, (ushort)ResultCode.DBError, unchecked((int)handle));
                return;
            }

            // A stone's handle that resolves to none of the character's items is ACCESS_DENIED, not NOT_EXIST
            // (WorldSession.cpp:1521-1526): the slot is the reference's second refusal.
            if (stone is null)
            {
                _logger.Debug("Soulstone craft from {clientTag} names unknown stone {itemHandle} in slot {slot}, refused",
                    client.ClientTag, handle, slot);
                client.SendResult(requestId, (ushort)ResultCode.AccessDenied, unchecked((int)handle));
                return;
            }

            // A resource the catalog does not know cannot be judged: it is not provably a soul stone, and the
            // reference's test is a three-axis one on a resource it always has (WorldSession.cpp:1527-1528).
            if (!_catalog.TryGetResource((int)stone.ItemResourceId, out var resource) || !resource.IsSoulstone)
            {
                _logger.Warning(
                    "Soulstone craft from {clientTag} names item {itemHandle} (resource {resourceId}) in slot {slot}, which is no soul stone, refused with NotActable",
                    client.ClientTag, handle, stone.ItemResourceId, slot);
                client.SendResult(requestId, (ushort)ResultCode.NotActable, unchecked((int)handle));
                return;
            }

            stones.Add(new NamedStone(slot, handle, stone.ItemResourceId, resource.Profile, resource.Price));
        }

        // No stone at all: the reference's bIsValid stays false and the frame is answered INVALID_ARGUMENT
        // (WorldSession.cpp:1558-1561). A slot handle of zero names no stone, it is not a refusal of the frame
        // (CraftingSocleRules and spec §3.3).
        if (stones.Count == 0)
        {
            _logger.Debug("Soulstone craft from {clientTag} names no stone at all, refused with InvalidArgument",
                client.ClientTag);
            client.SendResult(requestId, (ushort)ResultCode.InvalidArgument);
            return;
        }

        var socketed = SocketedProfiles(item, crafted.SocketCount, client);
        var limit = SoulstoneCraftRules.DuplicationLimit(crafted.SocketCount);
        foreach (var stone in stones)
        {
            if (SoulstoneCraftRules.CountIdenticalSockets(stone.Profile, socketed, stone.Slot) < limit)
            {
                continue;
            }

            _logger.Debug(
                "Soulstone craft from {clientTag} would put a {limit}th identical stone in the chassis of item {itemHandle}, refused with AlreadyExist",
                client.ClientTag, limit, request.CraftItemHandle);
            client.SendResult(requestId, (ushort)ResultCode.AlreadyExist);
            return;
        }

        var prices = new int[stones.Count];
        for (var index = 0; index < stones.Count; index++)
        {
            prices[index] = stones[index].Price;
        }

        var cost = SoulstoneCraftRules.CraftCost(prices);
        var assignments = new SoulstoneSlotAssignment[stones.Count];
        for (var index = 0; index < stones.Count; index++)
        {
            assignments[index] = new SoulstoneSlotAssignment(stones[index].Slot, stones[index].Handle,
                stones[index].Code);
        }

        SoulstoneCraftResult result;
        try
        {
            result = await _characterService.SocketSoulstonesAsync(client.ConnectionInfo.CharacterName,
                request.CraftItemHandle, assignments, cost);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not socket {count} soul stone(s) into item {itemHandle} for {clientTag}",
                assignments.Length, request.CraftItemHandle, client.ClientTag);
            client.SendResult(requestId, (ushort)ResultCode.DBError, unchecked((int)request.CraftItemHandle));
            return;
        }

        switch (result.Outcome)
        {
            case SoulstoneCraftOutcome.ItemNotFound:
                client.SendResult(requestId, (ushort)ResultCode.NotExist, unchecked((int)request.CraftItemHandle));
                return;

            case SoulstoneCraftOutcome.StoneNotFound:
                client.SendResult(requestId, (ushort)ResultCode.AccessDenied, unchecked((int)result.MissingHandle));
                return;

            case SoulstoneCraftOutcome.NotEnoughMoney:
                _logger.Debug("Soulstone craft from {clientTag} costs {cost} gold, refused with NotEnoughMoney",
                    client.ClientTag, cost);
                client.SendResult(requestId, (ushort)ResultCode.NotEnoughMoney);
                return;
        }

        // The reference's order (WorldSession.cpp:1554-1586): the paid gold, then what the stones left behind,
        // then the socketed item through TM_SC_INVENTORY, then the acknowledgement.
        client.ConnectionInfo.CharacterGold = result.Gold;
        client.ConnectionInfo.CharacterChaos = result.Chaos;
        client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(result.Gold, result.Chaos));

        foreach (var (handle, remaining) in result.Consumed)
        {
            client.Connection.Send(remaining <= 0
                ? GameCharacterPackets.BuildDestroyItem(handle)
                : GameCharacterPackets.BuildUpdateItemCount(handle, remaining));
        }

        foreach (var inv in GameCharacterPackets.BuildInventory(new[] { result.Item }))
        {
            client.Connection.Send(inv);
        }

        client.SendResult(requestId, (ushort)ResultCode.Success);
        _logger.Debug(
            "{clientTag} socketed {count} soul stone(s) into item {itemHandle} for {cost} gold: {chassis}",
            client.ClientTag, stones.Count, request.CraftItemHandle, cost,
            string.Join(',', result.Item.SocketItemIds));
    }

    /// <summary>
    /// The chassis of <paramref name="item"/> that already hold a stone, as the profile the two-stone rule
    /// compares. A code the catalog does not know is left out instead of compared: the reference indexes its
    /// table with the code without checking (WorldSession.cpp:1536).
    /// </summary>
    private List<(int Slot, SoulstoneProfile Profile)> SocketedProfiles(ItemEntity item, int socketCount,
        GameClient client)
    {
        var socketed = new List<(int, SoulstoneProfile)>(socketCount);
        var codes = item.SocketItemIds;
        if (codes is null)
        {
            return socketed;
        }

        for (var slot = 0; slot < socketCount && slot < codes.Length; slot++)
        {
            if (codes[slot] == 0)
            {
                continue;
            }

            if (!_catalog.TryGetResource((int)codes[slot], out var resource))
            {
                _logger.Warning("Item {itemHandle} of {clientTag} sockets unknown resource {code}: not compared",
                    item.Id, client.ClientTag, codes[slot]);
                continue;
            }

            socketed.Add((slot, resource.Profile));
        }

        return socketed;
    }

    private readonly record struct NamedStone(int Slot, uint Handle, long Code, SoulstoneProfile Profile, int Price);
}
