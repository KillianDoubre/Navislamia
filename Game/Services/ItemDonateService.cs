using System;
using System.Collections.Generic;
using System.Linq;
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
/// The gifts become moral points like the official <c>onDonateItem</c> (gold / 10 000, an item's base price /
/// 10 000 per unit), and <c>TM_CS_DONATE_REWARD</c> (259) spends them on the official reward items
/// (<see cref="RewardAsync"/>).
/// </summary>
public class ItemDonateService : IItemDonateService
{
    private const ushort DonateRequestId = (ushort)GamePackets.TM_CS_DONATE_ITEM;

    private readonly ILogger _logger = Log.ForContext<ItemDonateService>();
    private readonly ICharacterService _characterService;
    private readonly IItemSellCatalog _catalog;

    public ItemDonateService(ICharacterService characterService, IItemSellCatalog catalog = null)
    {
        _characterService = characterService;
        _catalog = catalog;
    }

    /// <summary><c>GameRule::DONATE_GOLD_UNIT_COUNT</c>: 10 000 gold (or 10 000 of an item's price) for one moral point.</summary>
    public const decimal DonateGoldUnitCount = 10000m;

    /// <summary><c>GameRule::DONATION_POINT_FOR_REWARD_ITEM</c>, the moral points each reward grade costs.</summary>
    public static readonly decimal[] RewardCost = { 1000m, 5000m, 10000m, 30000m };

    /// <summary><c>GameRule::DONATION_REWARD_ITEM_CODE</c>, the item each reward grade gives.</summary>
    public static readonly int[] RewardItem = { 3620026, 3620025, 3620024, 3620023 };

    /// <summary>
    /// <c>GameRule::GetDonationRewardMoralPoint</c>: the moral points an amount of gold (or an item's base price times
    /// its count) is worth, kept to the four decimals of <c>c_fixed10</c>.
    /// </summary>
    public static decimal MoralPoints(long goldValue) =>
        decimal.Truncate(Math.Max(0, goldValue) / DonateGoldUnitCount * 10000m) / 10000m;

    /// <summary>
    /// <c>onDonateReward</c>: the moral points (the negative of the immorality) buy reward items, the grades' costs
    /// summed first and refused as a whole (<c>NotOwn</c>) when they are not covered. The caller sends the result.
    /// </summary>
    public async Task<ResultCode> RewardAsync(GameClient client,
        IReadOnlyList<GameActionPackets.DonateRewardEntry> rewards)
    {
        var info = client.ConnectionInfo;
        if (rewards is null || rewards.Count == 0)
        {
            return ResultCode.Success;
        }

        if (rewards.Any(r => r.RewardType < 0 || r.RewardType >= RewardCost.Length))
        {
            return ResultCode.InvalidArgument;
        }

        var cost = rewards.Sum(r => RewardCost[r.RewardType] * r.Count);
        var moral = -info.ImmoralPoint;
        if (cost > moral)
        {
            // The official answer when the moral points do not cover the selection (string 223).
            return ResultCode.NotOwn;
        }

        var spent = 0m;
        try
        {
            foreach (var reward in rewards.Where(r => r.Count > 0))
            {
                var added = await _characterService.AddItemAsync(info.CharacterName, RewardItem[reward.RewardType],
                    reward.Count);
                if (added is null)
                {
                    continue;
                }

                spent += RewardCost[reward.RewardType] * reward.Count;
                foreach (var frame in GameCharacterPackets.BuildInventory(new[] { added }))
                {
                    client.Connection.Send(frame);
                }
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not give the donation rewards of {clientTag}", client.ClientTag);
        }

        if (spent > 0)
        {
            MoralityRules.Set(client, -(moral - spent));
        }

        return ResultCode.Success;
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

        // The gold is taken now, under GoldLock, and given back on every failure below: CheckAffordable read
        // the purse before the awaits, and a kill reward or a trade can change it meanwhile.
        if (!info.TryDebitGold(request.Gold))
        {
            client.SendResult(DonateRequestId, (ushort)ResultCode.NotEnoughMoney, value);
            return;
        }

        var items = request.Items ?? Array.Empty<GameActionPackets.DonateItemEntry>();
        var remaining = new long[items.Length];
        var prices = new long[items.Length];

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
                    info.AddGold(request.Gold);
                    client.SendResult(DonateRequestId, (ushort)ResultCode.NotExist, value);
                    return;
                }

                // The base price (ItemBase::nPrice) is what the altar weighs.
                prices[i] = _catalog is not null && _catalog.TryGetTemplate((int)item.ItemResourceId, out var template)
                    ? (long)template.Price
                    : 0;
            }

            for (var i = 0; i < items.Length; i++)
            {
                var left = await _characterService.ConsumeItemAsync(info.CharacterName, items[i].Handle,
                    items[i].Count);
                if (left is null)
                {
                    // The stack vanished between the resolution and the consumption.
                    info.AddGold(request.Gold);
                    client.SendResult(DonateRequestId, (ushort)ResultCode.NotExist, value);
                    return;
                }

                remaining[i] = left.Value;
            }
        }
        catch (Exception exception)
        {
            info.AddGold(request.Gold);
            _logger.Error(exception, "Could not take the donated items for {clientTag}", client.ClientTag);
            client.SendResult(DonateRequestId, (ushort)ResultCode.DBError, value);
            return;
        }

        // The offer was taken: the jp follows, the same way a job level up spends it (the gold already left).
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

        // onDonateItem: every gift lowers the immorality (raises the moral points) by its worth, a Bloody status
        // that clears is broadcast (MoralityRules.Set sends the property and the status).
        var reward = MoralPoints(request.Gold);
        for (var i = 0; i < items.Length; i++)
        {
            reward += MoralPoints(prices[i]) * items[i].Count;
        }

        if (reward > 0)
        {
            MoralityRules.Set(client, info.ImmoralPoint - reward);
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
                info.CharacterChaos, info.X, info.Y, info.PkMode, info.GetPvpProgress());
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not persist the donation of {clientTag}", client.ClientTag);
        }
    }
}
