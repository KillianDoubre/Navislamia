using System;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// Handles <c>TM_CS_DONATE_ITEM</c> (258). The scope is the one the fiche fixes (§5.3): read the
/// frame, judge the offer, take the gold / jp / item units out of the character, acknowledge.
/// <b>Nothing is credited in exchange</b> — the moral points of the 7.3 donation altar are not
/// modelled in this repository and no conversion rate is established (fiche §5.4, §7.1), and the
/// sibling packet <c>TM_CS_DONATE_REWARD</c> (259) is out of scope.
/// </summary>
public class ItemDonateService : IItemDonateService
{
    private const ushort DonateRequestId = (ushort)GamePackets.TM_CS_DONATE_ITEM;

    private readonly ILogger _logger = Log.ForContext<ItemDonateService>();
    private readonly ICharacterService _characterService;

    public ItemDonateService(ICharacterService characterService)
    {
        _characterService = characterService;
    }

    public async Task DonateAsync(GameClient client, GameActionPackets.DonateItemRequest request)
    {
        var info = client.ConnectionInfo;

        // No reference fills TM_SC_RESULT.value for this packet (fiche §7.2), so it stays at the
        // default of SendResult rather than carrying a guessed handle.
        const int value = 0;

        var shape = DonateRules.CheckShape(request);
        if (shape != ResultCode.Success)
        {
            client.SendResult(DonateRequestId, (ushort)shape, value);
            return;
        }

        var affordable = DonateRules.CheckAffordable(info.CharacterGold, info.CharacterJp, request);
        if (affordable != ResultCode.Success)
        {
            client.SendResult(DonateRequestId, (ushort)affordable, value);
            return;
        }

        var items = request.Items ?? Array.Empty<GameActionPackets.DonateItemEntry>();
        var remaining = new long[items.Length];

        try
        {
            // Every handle is resolved before anything is taken: a frame naming one stack the
            // character does not own is refused as a whole, so a partial offer is never debited.
            // The lookup is the character's own items, which is how possession is proven here.
            for (var i = 0; i < items.Length; i++)
            {
                var item = await _characterService.GetItemByHandleAsync(info.CharacterName, items[i].Handle);
                if (item is null)
                {
                    client.SendResult(DonateRequestId, (ushort)ResultCode.NotExist, value);
                    return;
                }
            }

            for (var i = 0; i < items.Length; i++)
            {
                var left = await _characterService.ConsumeItemAsync(info.CharacterName, items[i].Handle,
                    items[i].Count);
                if (left is null)
                {
                    // The stack vanished between the resolution and the consumption.
                    client.SendResult(DonateRequestId, (ushort)ResultCode.NotExist, value);
                    return;
                }

                remaining[i] = left.Value;
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not take the donated items for {clientTag}", client.ClientTag);
            client.SendResult(DonateRequestId, (ushort)ResultCode.DBError, value);
            return;
        }

        // The offer was taken: the session's gold and jp follow, the same way a job level up spends
        // its jp. Both are already bounded by CheckAffordable.
        info.CharacterGold -= request.Gold;
        info.CharacterJp -= request.Jp;

        for (var i = 0; i < items.Length; i++)
        {
            client.Connection.Send(remaining[i] == 0
                ? GameCharacterPackets.BuildDestroyItem(items[i].Handle)
                : GameCharacterPackets.BuildUpdateItemCount(items[i].Handle, remaining[i]));
        }

        if (request.Gold > 0)
        {
            client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(info.CharacterGold, info.CharacterChaos));
        }

        if (request.Jp > 0)
        {
            client.Connection.Send(GameCharacterPackets.BuildExpUpdate(info.CharacterHandle, info.CharacterExp,
                info.CharacterJp));
        }

        client.SendResult(DonateRequestId, (ushort)ResultCode.Success, value);

        // Gold and jp are only written back by the disconnect save otherwise; a donation removes
        // value for good, so it is persisted right away. A failing write is logged rather than
        // turned into a refusal: the items are already gone and the next save would recover the
        // rest, so answering DBError here would report a failure the player cannot act on.
        try
        {
            await _characterService.SaveProgressAsync(info.CharacterName, info.CharacterLevel,
                info.CharacterJobLevel, info.CharacterExp, info.CharacterJp, info.CharacterGold,
                info.CharacterChaos, info.X, info.Y);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not persist the donation of {clientTag}", client.ClientTag);
        }
    }
}
