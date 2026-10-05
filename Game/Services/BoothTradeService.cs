using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// The trade half of the player booth: what other players see of a booth and what they do with it
/// (docs/packet-specs/705-buy-from-booth.md).
/// <list type="bullet">
/// <item><description>an open booth is published in the owner's status mask
/// (<see cref="CreatureStatus.PlayerSellBooth"/> or <see cref="CreatureStatus.PlayerBuyBooth"/>) to the
/// owner and to every client that sees it, and cleared the same way when it closes, when its watchers
/// also receive <c>TM_SC_BOOTH_CLOSED</c> (709);</description></item>
/// <item><description><c>TM_CS_GET_BOOTHS_NAME</c> (707) is answered with <c>TM_SC_GET_BOOTHS_NAME</c>
/// (708), the names of the named booths that are open;</description></item>
/// <item><description><c>TM_CS_BUY_FROM_BOOTH</c> (705) buys from a sell booth and
/// <c>TM_CS_SELL_TO_BOOTH</c> (706) sells to a buy booth. A trade reserves the booth's units, moves the
/// gold under each session's gold lock, then moves the items and writes both balances in one save
/// (<see cref="ICharacterService.TransferItemsAsync"/>); any failure puts everything back.</description></item>
/// </list>
/// A customer must be watching the booth (a 702 answered) to trade with it: the 7.3 client only offers
/// the trade from that window, and it keeps the customer within sight of the booth.
/// </summary>
public class BoothTradeService : IBoothTradeService
{
    private readonly ILogger _logger = Log.ForContext<BoothTradeService>();
    private readonly ICharacterService _characters;
    private readonly IPlayerVisibilityService _visibility;
    private readonly IBoothWatchService _watch;

    public BoothTradeService(ICharacterService characters, IPlayerVisibilityService visibility,
        IBoothWatchService watch)
    {
        _characters = characters;
        _visibility = visibility;
        _watch = watch;
    }

    public void PublishBoothStatus(GameClient owner)
    {
        var info = owner.ConnectionInfo;
        if (info.CharacterHandle == 0)
        {
            return;
        }

        _visibility.SendToObservers(owner,
            GameCharacterPackets.BuildStatusChange(info.CharacterHandle, ActorStatus.ForPlayer(info)),
            includeSelf: true);
    }

    public void CloseBooth(GameClient owner)
    {
        var info = owner.ConnectionInfo;
        if (!info.CloseBooth())
        {
            return;
        }

        PublishBoothStatus(owner);

        var closed = BoothPackets.BuildBoothClosed(info.CharacterHandle);
        foreach (var watcher in Watchers(owner))
        {
            watcher.ConnectionInfo.StopWatchingBooth();
            watcher.Connection.Send(closed);
        }
    }

    public void HandleGetBoothsName(GameClient client, byte[] packet)
    {
        if (!BoothPackets.TryReadGetBoothsName(packet, out var handles))
        {
            _logger.Warning("Malformed TM_CS_GET_BOOTHS_NAME from {clientTag} (Length: {length}), dropped",
                client.ClientTag, packet.Length);
            return;
        }

        var names = new List<BoothName>(handles.Length);
        foreach (var handle in handles.Distinct())
        {
            if (_visibility.Registry.TryResolve(handle, out var owner)
                && owner.ConnectionInfo.Booth is { } booth)
            {
                names.Add(new BoothName(handle, booth.Name));
            }
        }

        client.Connection.Send(BoothPackets.BuildGetBoothsName(names));
    }

    public async Task HandleBuyAsync(GameClient buyer, byte[] packet)
    {
        const ushort requestId = (ushort)GamePackets.TM_CS_BUY_FROM_BOOTH;
        if (!BoothPackets.TryReadBuyFromBooth(packet, out var request))
        {
            _logger.Warning("Malformed TM_CS_BUY_FROM_BOOTH from {clientTag} (Length: {length})", buyer.ClientTag,
                packet.Length);
            buyer.SendResult(requestId, (ushort)ResultCode.InvalidArgument);
            return;
        }

        await RunExclusiveAsync(buyer, requestId, request.Target, async () =>
        {
            if (!TryResolveBooth(buyer, request.Target, out var ownerClient, out var refusal))
            {
                return refusal;
            }

            var owner = ownerClient.ConnectionInfo;

            // The motif the buyer sent must still describe the owner's item: a stale window (the stack
            // was split, sold or swapped since the 703) is refused rather than sold under another code.
            foreach (var line in request.Lines)
            {
                var item = await _characters.GetItemByHandleAsync(owner.CharacterName, line.ItemHandle);
                if (item is null || item.ItemResourceId != line.Code)
                {
                    return ResultCode.NotExist;
                }

                // onBuyFromBooth: pTarget->IsTradable (GameMessage.cpp:9181) — a formed card, a card whose summon is
                // out or the cage of the pet out stays with the owner.
                if (!Creatures.HeldItemRules.IsErasable(owner, item.Id))
                {
                    return ResultCode.NotActable;
                }
            }

            var (reserved, reservation) = owner.UpdateBooth(booth =>
            {
                var (next, result) = BoothTradeRules.ReservePurchase(booth, request.Lines);
                return (next, (next, result));
            });

            return await SettleAsync(reservation, reserved, ownerClient, customer: buyer, giver: ownerClient,
                receiver: buyer, isSell: true, requestId);
        });
    }

    public async Task HandleSellAsync(GameClient seller, byte[] packet)
    {
        const ushort requestId = (ushort)GamePackets.TM_CS_SELL_TO_BOOTH;
        if (!BoothPackets.TryReadSellToBooth(packet, out var request))
        {
            _logger.Warning("Malformed TM_CS_SELL_TO_BOOTH from {clientTag} (Length: {length})", seller.ClientTag,
                packet.Length);
            seller.SendResult(requestId, (ushort)ResultCode.InvalidArgument);
            return;
        }

        await RunExclusiveAsync(seller, requestId, request.Target, async () =>
        {
            if (!TryResolveBooth(seller, request.Target, out var ownerClient, out var refusal))
            {
                return refusal;
            }

            var owner = ownerClient.ConnectionInfo;
            var offered = await _characters.GetItemByHandleAsync(seller.ConnectionInfo.CharacterName,
                request.ItemHandle);
            if (offered is null)
            {
                return ResultCode.NotExist;
            }

            if (offered.WearInfo != DataAccess.Entities.Enums.ItemWearType.None
                || !Creatures.HeldItemRules.IsErasable(seller.ConnectionInfo, offered.Id))
            {
                return ResultCode.NotActable;
            }

            if (request.Count < 1 || request.Count > offered.Amount)
            {
                return ResultCode.NotEnoughItem;
            }

            // A buy booth names what it wants by sample items of its owner's bag: their codes are what an
            // offer is matched against.
            var codeOf = new Dictionary<uint, int>();
            foreach (var declared in owner.Booth?.Items ?? Array.Empty<BoothOpenItem>())
            {
                var sample = await _characters.GetItemByHandleAsync(owner.CharacterName, declared.ItemHandle);
                if (sample is not null)
                {
                    codeOf[declared.ItemHandle] = (int)sample.ItemResourceId;
                }
            }

            var (reserved, reservation) = owner.UpdateBooth(booth =>
            {
                var (next, result) = BoothTradeRules.ReserveSale(booth, request.ItemHandle,
                    (int)offered.ItemResourceId, request.Count, codeOf);
                return (next, (next, result));
            });

            return await SettleAsync(reservation, reserved, ownerClient, customer: seller, giver: seller,
                receiver: ownerClient, isSell: false, requestId);
        });
    }

    /// <summary>
    /// Carries a reservation out: gold first (judged and moved under each session's lock), then the items
    /// and both balances in one save. Every failure gives the gold and the reserved units back. The items
    /// go from <paramref name="giver"/> to <paramref name="receiver"/>, the gold the other way.
    /// </summary>
    private async Task<ResultCode> SettleAsync(BoothReservation reservation, StartBoothRequest reserved,
        GameClient ownerClient, GameClient customer, GameClient giver, GameClient receiver, bool isSell,
        ushort requestId)
    {
        var payer = receiver;
        var payee = giver;

        if (reservation.Result != ResultCode.Success)
        {
            return reservation.Result;
        }

        var owner = ownerClient.ConnectionInfo;
        var total = reservation.Total;

        if (!payer.ConnectionInfo.TryDebitGold(total))
        {
            Release(owner, reserved, reservation);
            return ResultCode.NotEnoughMoney;
        }

        if (!payee.ConnectionInfo.TryCreditGold(total, BoothTradeRules.MaxGold))
        {
            payer.ConnectionInfo.AddGold(total);
            Release(owner, reserved, reservation);
            return ResultCode.TooMuchMoney;
        }

        ItemTransferResult transfer;
        try
        {
            transfer = await _characters.TransferItemsAsync(new ItemTransfer(
                giver.ConnectionInfo.CharacterName,
                receiver.ConnectionInfo.CharacterName,
                reservation.Lines.Select(line => new ItemTransferLine(line.ItemHandle, line.Count)).ToArray(),
                giver.ConnectionInfo.CharacterGold,
                receiver.ConnectionInfo.CharacterGold));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Booth trade between {giver} and {receiver} failed in the database",
                giver.ClientTag, receiver.ClientTag);
            transfer = null;
        }

        if (transfer?.Outcome != ItemTransferOutcome.Success)
        {
            payee.ConnectionInfo.AddGold(-total);
            payer.ConnectionInfo.AddGold(total);
            Release(owner, reserved, reservation);
            return transfer is null ? ResultCode.DBError : RefusalOf(transfer.Outcome);
        }

        SendTradeFrames(giver, receiver, ownerClient, customer, isSell, reservation, transfer);
        customer.SendResult(requestId, (ushort)ResultCode.Success, unchecked((int)owner.CharacterHandle));

        _logger.Information("Booth trade {kind}: {giver} -> {receiver}, {lines} line(s), {total} gold",
            isSell ? "purchase" : "sale", giver.ClientTag, receiver.ClientTag, reservation.Lines.Count, total);

        await RefreshWatchersAsync(ownerClient);
        if (BoothTradeRules.IsExhausted(owner.Booth))
        {
            CloseBooth(ownerClient);
        }

        return ResultCode.Success;
    }

    /// <summary>
    /// What each side sees of a trade that went through: both gold balances, the giver's stacks shrinking
    /// or leaving (255 or 254), the receiver's new items (207), and the owner's <c>TM_SC_BOOTH_TRADE_INFO</c>
    /// (710) naming the customer and the traded items at their unit price.
    /// </summary>
    private static void SendTradeFrames(GameClient giver, GameClient receiver, GameClient ownerClient,
        GameClient customer, bool isSell, BoothReservation reservation, ItemTransferResult transfer)
    {
        foreach (var side in new[] { giver, receiver })
        {
            side.Connection.Send(GameCharacterPackets.BuildGoldUpdate(side.ConnectionInfo.CharacterGold,
                side.ConnectionInfo.CharacterChaos));
        }

        foreach (var moved in transfer.Moved)
        {
            giver.Connection.Send(moved.GiverRemaining == 0
                ? GameCharacterPackets.BuildDestroyItem(moved.GiverHandle)
                : GameCharacterPackets.BuildUpdateItemCount(moved.GiverHandle, moved.GiverRemaining));
        }

        foreach (var frame in GameCharacterPackets.BuildInventory(transfer.Moved.Select(moved => moved.Received).ToArray()))
        {
            receiver.Connection.Send(frame);
        }

        var unitPrices = reservation.Lines.ToDictionary(line => line.ItemHandle, line => line.UnitPrice);
        var traded = transfer.Moved.Select(moved => new BoothWatchItem(
            ItemFixedInfo.FromItem(moved.Received), unitPrices[moved.GiverHandle])).ToArray();
        ownerClient.Connection.Send(BoothPackets.BuildBoothTradeInfo(customer.ConnectionInfo.CharacterHandle,
            isSell, traded));
    }

    private async Task RefreshWatchersAsync(GameClient ownerClient)
    {
        var watchers = Watchers(ownerClient);
        if (watchers.Count == 0)
        {
            return;
        }

        var frame = await _watch.BuildWatchFrameAsync(ownerClient.ConnectionInfo);
        if (frame is null)
        {
            return;
        }

        foreach (var watcher in watchers)
        {
            watcher.Connection.Send(frame);
        }
    }

    /// <summary>The clients that see the owner and have its booth window open.</summary>
    private List<GameClient> Watchers(GameClient ownerClient)
    {
        var handle = ownerClient.ConnectionInfo.CharacterHandle;
        return _visibility.Observers(ownerClient)
            .Where(observer => observer.ConnectionInfo.WatchedBoothHandle == handle)
            .ToList();
    }

    /// <summary>
    /// The customer-side checks every trade shares: not one's own booth, the booth's window open, the
    /// owner connected and its booth open.
    /// </summary>
    private bool TryResolveBooth(GameClient customer, uint target, out GameClient ownerClient,
        out ResultCode refusal)
    {
        ownerClient = null;
        var info = customer.ConnectionInfo;

        if (target == info.CharacterHandle)
        {
            refusal = ResultCode.NotActable;
            return false;
        }

        if (info.WatchedBoothHandle != target)
        {
            refusal = ResultCode.NotActable;
            return false;
        }

        if (!_visibility.Registry.TryResolve(target, out ownerClient) || !ownerClient.ConnectionInfo.IsBoothOpen)
        {
            refusal = ResultCode.NotExist;
            return false;
        }

        refusal = ResultCode.Success;
        return true;
    }

    /// <summary>
    /// One trade per customer at a time, and every failure answered with the request id and the booth
    /// handle: a second request while the first awaits the database is dropped, as a double click would
    /// otherwise spend twice.
    /// </summary>
    private async Task RunExclusiveAsync(GameClient customer, ushort requestId, uint target,
        Func<Task<ResultCode>> trade)
    {
        var info = customer.ConnectionInfo;
        if (info.CharacterHandle == 0)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref info.BoothTradeInProgress, 1, 0) != 0)
        {
            _logger.Debug("Booth request {id} from {clientTag} dropped: a trade is already in progress", requestId,
                customer.ClientTag);
            return;
        }

        try
        {
            var result = await trade();
            if (result != ResultCode.Success)
            {
                _logger.Debug("Booth request {id} from {clientTag} on booth {target} refused: {result}", requestId,
                    customer.ClientTag, target, result);
                customer.SendResult(requestId, (ushort)result, unchecked((int)target));
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Booth request {id} from {clientTag} failed", requestId, customer.ClientTag);
            customer.SendResult(requestId, (ushort)ResultCode.DBError, unchecked((int)target));
        }
        finally
        {
            Interlocked.Exchange(ref info.BoothTradeInProgress, 0);
        }
    }

    private static void Release(ConnectionInfo owner, StartBoothRequest reserved, BoothReservation reservation)
    {
        owner.UpdateBooth(booth => (BoothTradeRules.Release(booth, reserved, reservation.Lines), 0));
    }

    private static ResultCode RefusalOf(ItemTransferOutcome outcome) => outcome switch
    {
        ItemTransferOutcome.ItemMissing => ResultCode.NotExist,
        ItemTransferOutcome.NotEnough => ResultCode.NotEnoughItem,
        ItemTransferOutcome.Worn => ResultCode.NotActable,
        _ => ResultCode.NotExist
    };
}
