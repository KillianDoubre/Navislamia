using System;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// The sell gesture of a merchant window: <c>TM_CS_SELL_ITEM</c> (252) takes units off a stack and pays
/// their price. Reference chain: <c>WorldSession::onSellItem</c>
/// (<c>Chihiro/src/Network/GameNetwork/WorldSession.cpp:1021-1072</c>) resolves the item, prices it, erases
/// it (<c>Player::EraseItem</c> <c>:1308</c> then <c>Inventory::Erase</c> <c>Inventory.cpp:89</c>), pays it
/// and answers twice. See <c>docs/packet-specs/252-sell-item.md</c> §5.2.
/// <para>
/// The merchant handle of the <c>TM_SC_NPC_TRADE_INFO</c> (240) echo is the handle of the NPC dialog the
/// window was opened from: the reference reads it back through <c>GetLastContactLong("npc")</c>
/// (<c>:1070</c>), and <c>ConnectionInfo.NpcDialogHandle</c> is the session's equivalent — a dialog is
/// deliberately left current when a market opens (<c>NpcDialogService.cs</c>), so it survives the sale and
/// is cleared when the dialog ends. The provenance of that field remains to be arbitrated
/// (fiche A VERIFIER 4).
/// </para>
/// </summary>
public class MarketSellService : IMarketSellService
{
    private const ushort SellItemId = (ushort)GamePackets.TM_CS_SELL_ITEM;

    private readonly ILogger _logger = Log.ForContext<MarketSellService>();
    private readonly ICharacterService _characterService;
    private readonly IItemSellCatalog _catalog;

    public MarketSellService(ICharacterService characterService, IItemSellCatalog catalog)
    {
        _characterService = characterService;
        _catalog = catalog;
    }

    public async Task SellAsync(GameClient client, uint itemHandle, ushort sellCount)
    {
        var info = client.ConnectionInfo;

        ItemEntity item;
        try
        {
            item = await _characterService.GetItemByHandleAsync(info.CharacterName, itemHandle);
        }
        catch (Exception exception)
        {
            // The depot's own code for a store that did not answer, as in ItemUseService (the reference has
            // no failure path for a dead database).
            _logger.Error(exception, "Could not read item {itemHandle} for {clientTag}", itemHandle, client.ClientTag);
            client.SendResult(SellItemId, (ushort)ResultCode.DBError, 0);
            return;
        }

        // The reference refuses with value 0 before anything else: an unknown handle, an item that is not
        // the player's, one that is not in the bag, or one whose template does not resolve
        // (WorldSession.cpp:1026-1030). GetItemByHandleAsync resolves inside the character's own items, so a
        // foreign handle never gets this far.
        if (item is null || !_catalog.TryGetTemplate((int)item.ItemResourceId, out var template))
        {
            _logger.Debug("Sell of unknown item {itemHandle} by {clientTag}, refused with NotExist",
                itemHandle, client.ClientTag);
            client.SendResult(SellItemId, (ushort)ResultCode.NotExist, 0);
            return;
        }

        var itemCode = (int)item.ItemResourceId;

        if (sellCount == 0)
        {
            client.SendResult(SellItemId, (ushort)ResultCode.Unknown, 0);
            return;
        }

        // The price is computed before the "sellable" test in the reference (:1037 before :1042); both
        // refusals that can follow answer the same NotExist (1) with the item code, so their order is
        // unobservable.
        if (!MarketSellPrice.TryComputeUnitPrice(template.Price, template.Rank, item.Level,
                MarketSellPrice.IsSamePriceForBuying(itemCode), out var unitPrice))
        {
            // A rank outside 0-8 makes the reference read past its own scale: no price can be reproduced,
            // so the sale is refused with the code the reference answers a non-sellable item with. The
            // conduct for such an item is reserved (fiche §6 écart 2, A VERIFIER 6).
            _logger.Warning("Item {code} of {clientTag} carries the out-of-scale rank {rank}: the sale cannot be "
                            + "priced, refused with NotExist", itemCode, client.ClientTag, template.Rank);
            client.SendResult(SellItemId, (ushort)ResultCode.NotExist, itemCode);
            return;
        }

        var total = (long)sellCount * unitPrice;
        var remaining = item.Amount - sellCount;

        if (!IsSellable(item) || remaining < 0)
        {
            _logger.Debug("Item {code} (handle {itemHandle}) of {clientTag} is not sellable ({remaining} left "
                          + "after {count}), refused with NotExist", itemCode, itemHandle, client.ClientTag,
                remaining, sellCount);
            client.SendResult(SellItemId, (ushort)ResultCode.NotExist, itemCode);
            return;
        }

        // The gold cap of the reference, MAX_GOLD_FOR_INVENTORY (ItemTemplate.hpp:4), has no equivalent in
        // this repository: TooMuchMoney (53) is deliberately left without a producer rather than guessed
        // (fiche A VERIFIER 1).
        if (info.CharacterGold + total < 0)
        {
            // Ported as it reads (:1050-1053). The depot's gold is an int64, so where the reference guards
            // its int32 arithmetic against going negative this guard only catches an overflow.
            _logger.Warning("Paying {total} gold to {clientTag} would overflow the purse: refused with NotActable",
                total, client.ClientTag);
            client.SendResult(SellItemId, (ushort)ResultCode.NotActable, itemCode);
            return;
        }

        long? left;
        try
        {
            left = await _characterService.ConsumeItemAsync(info.CharacterName, itemHandle, sellCount);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not consume item {itemHandle} for {clientTag}", itemHandle,
                client.ClientTag);
            client.SendResult(SellItemId, (ushort)ResultCode.DBError, 0);
            return;
        }

        // The erase failed: the item left the bag between the read and the erase, the reference's own
        // failure of EraseItem (:1055-1058), answered with the handle.
        if (left is null)
        {
            _logger.Warning("Erase of item {itemHandle} of {clientTag} failed, refused with NotActable",
                itemHandle, client.ClientTag);
            client.SendResult(SellItemId, (ushort)ResultCode.NotActable, unchecked((int)itemHandle));
            return;
        }

        // NGemity erases the units before it pays for them (Inventory::Erase, :1055, then ChangeGold, :1059),
        // and the erase is what makes the client drop the units: the stack update leaves first, exactly as in
        // ItemUseService.
        client.Connection.Send(left == 0
            ? GameCharacterPackets.BuildDestroyItem(itemHandle)
            : GameCharacterPackets.BuildUpdateItemCount(itemHandle, left.Value));

        info.CharacterGold += total;
        client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(info.CharacterGold, info.CharacterChaos));

        client.SendResult(SellItemId, (ushort)ResultCode.Success, unchecked((int)itemHandle));

        // The success echo. The reference writes tradePct.code twice and never fills count
        // (:1067-1068): that slip is not ported — code carries the item code and count the sold quantity
        // (fiche §6 écart 3).
        client.Connection.Send(GameTradePackets.BuildNpcTradeInfo(true, itemCode, sellCount, total, 0,
            info.NpcDialogHandle));

        _logger.Debug("{clientTag} sold {count} x item {code} (handle {itemHandle}) for {total} gold, "
                      + "{gold} in the purse", client.ClientTag, sellCount, itemCode, itemHandle, total,
            info.CharacterGold);
    }

    /// <summary>
    /// The demonstrable subset of <c>Player::IsSellable</c> (<c>Player.cpp:3158-3166</c>): the item must be
    /// in the bag (<c>GetItemByHandleAsync</c> resolved it there), not worn, not stored and not equipped by
    /// a summon. The rest of the reference test — skill cards bound to a target, summon cards bound to a
    /// belt slot, the <c>ITEM_FLAG_TAMING</c> flag — has no readable equivalent in this repository (its
    /// <c>ItemFlag.Taming</c> is a bit index where NGemity compares a mask) and stays reserved
    /// (fiche §6 écart 5, A VERIFIER 2).
    /// </summary>
    private static bool IsSellable(ItemEntity item)
    {
        return item.WearInfo == ItemWearType.None
               && item.StorageId is null
               && item.EquippedBySummonId is null;
    }
}
