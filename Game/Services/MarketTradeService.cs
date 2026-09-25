using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// Buys one validated catalogue line: <c>TM_CS_BUY_ITEM</c> (251). Reference chain: the client's shop
/// window sends one 251 per line, <c>onBuyItem</c> resolves the last contact's market and the line
/// (<c>WorldSession.cpp:733-749</c>), debits the gold for the whole amount
/// (<c>::ChangeGold</c>, <c>:771-779</c>) before adding the item and echoing the transaction
/// (<c>Messages::SendResult</c> then <c>Messages::SendNpcTradeInfo</c>, <c>:804-812</c>).
/// <para>
/// Three things are deliberately <b>not</b> ported, because the catalogue they need does not exist here:
/// the container-weight check (the reference walks the catalogue four times to find a
/// <c>weight_id</c>/<c>max_count</c> column pair, <c>:749-769</c>, and this depot carries neither column),
/// the <c>flag</c> column that forces a count of one for non-stackable rows, and the int32 truncation
/// guard on the total. The limits that can be judged from what 250 announced <i>are</i> kept, so a
/// purchase cannot buy a line the client never saw. See docs/packet-specs/251-buy-item.md §6.
/// </para>
/// </summary>
public class MarketTradeService : IMarketTradeService
{
    private const ushort BuyItemId = (ushort)GamePackets.TM_CS_BUY_ITEM;

    private readonly ILogger _logger = Log.ForContext<MarketTradeService>();

    private readonly IMarketCatalog _catalog;
    private readonly ICharacterService _characterService;

    public MarketTradeService(IMarketCatalog catalog, ICharacterService characterService)
    {
        _catalog = catalog;
        _characterService = characterService;
    }

    public async Task BuyAsync(GameClient client, int itemCode, ushort buyCount)
    {
        var info = client.ConnectionInfo;

        // A zero count is refused before the market is even looked at (WorldSession.cpp:735-738); it would
        // otherwise reach the bag as one unit, the add being clamped to at least one.
        if (buyCount == 0)
        {
            client.SendResult(BuyItemId, (ushort)ResultCode.Unknown, 0);
            return;
        }

        // Where the frame was sent from: the market of the dialog that is still open. The reference
        // resolves it through its last contact and refuses with 7 when the name resolves to nothing
        // (:733, :740-744); here a dialog that ended (or moved on) has already dropped the market name
        // (ConnectionInfo.ClearNpcDialog), so a closed window cannot sell.
        var marketName = info.OpenMarketName;
        var merchantHandle = info.NpcDialogHandle;

        if (merchantHandle == 0 || string.IsNullOrEmpty(marketName)
            || !_catalog.TryGetMarket(marketName, out var lines))
        {
            _logger.Debug(
                "Refused a purchase of item {code} for {clientTag}: no open market (dialog {handle}, market '{market}')",
                itemCode, client.ClientTag, merchantHandle, marketName);
            client.SendResult(BuyItemId, (ushort)ResultCode.Unknown, 0);
            return;
        }

        // The line: the first catalogue entry carrying the code the client asked for. The reference scans
        // the whole catalogue without breaking and buys on every match (:749-814), a quirk of a table that
        // is expected to hold one row per (market, item): one 251 is one purchase here, so the first match
        // wins.
        if (!TryFindLine(lines, itemCode, out var line))
        {
            // A line the open market does not carry earns no answer at all: the reference's loop simply
            // ends with nothing sent (:749-814). Nothing was bought and nothing is owed.
            _logger.Debug("Item {code} is not on market {market}: no answer sent to {clientTag}",
                itemCode, marketName, client.ClientTag);
            return;
        }

        // The whole price of the transaction: the catalogue holds the absolute price, and the client buys
        // buy_count units of it. The reference refuses a total it cannot afford — and a negative one,
        // which its int32 arithmetic can produce — with 10 (:773-774).
        var total = buyCount * line.Price;

        if (total < 0 || total > info.CharacterGold)
        {
            client.SendResult(BuyItemId, (ushort)ResultCode.NotEnoughMoney, 0);
            return;
        }

        // The debit is taken here, synchronously, before the first await in this method: the receive loop
        // of one client runs this far without yielding, so two 251 frames coalesced in the same buffer
        // cannot both pass the check above and buy on the same gold. The reference behaves the same way —
        // its gold is the session's, checked then changed in one go (:771-779).
        var gold = info.CharacterGold - total;
        info.CharacterGold = gold;
        client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(gold, info.CharacterChaos));

        ItemEntity item;

        try
        {
            item = await _characterService.AddItemAsync(info.CharacterName, itemCode, buyCount);
        }
        catch (Exception exception)
        {
            _logger.Error(exception,
                "Could not add the bought item {code} to {character}: the gold is credited back",
                itemCode, info.CharacterName);
            item = null;
        }

        if (item is null)
        {
            // The only write of the gesture failed or found no character: nothing was bought, so the
            // debit is undone and the client is refused instead of paying for an item it never got. 8 is
            // the depot's own code for a failed write (ItemUseService), the reference having no store of
            // its own to fail on.
            info.CharacterGold += total;
            client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(info.CharacterGold, info.CharacterChaos));
            client.SendResult(BuyItemId, (ushort)ResultCode.DBError, 0);

            _logger.Warning("Purchase of item {code} for {character} was refused: the item was not added",
                itemCode, info.CharacterName);
            return;
        }

        // The gold update went out first, so what follows is the acknowledgement naming the bought item
        // and then the transaction echo carrying the whole amount and the merchant it was paid to
        // (:804-812). The line's announced huntaholic point is echoed as it was sent in the window it
        // came from.
        client.SendResult(BuyItemId, (ushort)ResultCode.Success, itemCode);
        client.Connection.Send(GameTradePackets.BuildNpcTradeInfo(false, itemCode, buyCount, total,
            line.HuntaholicPoint, merchantHandle));

        _logger.Debug("{clientTag} bought {count} × item {code} on market {market} for {total} gold",
            client.ClientTag, buyCount, itemCode, marketName, total);
    }

    private static bool TryFindLine(IReadOnlyList<MarketLine> lines, int itemCode, out MarketLine line)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].Code != itemCode)
            {
                continue;
            }

            line = lines[index];
            return true;
        }

        line = default;
        return false;
    }
}
