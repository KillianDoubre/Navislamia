using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Serilog;

namespace Navislamia.Game.Services.Trade;

/// <summary>
/// The trade between two players, <c>TS_TRADE</c> (280) in both directions. The rules are the official
/// server's (<c>onTrade</c> and its <c>onRequestTrade</c>…<c>onModifyCount</c>, <c>CaptainHerlockServer.exe</c>
/// of the Epic 7 Part 4 dump); see <c>docs/packet-specs/280-trade.md</c>.
/// </summary>
public interface IPlayerTradeService
{
    Task HandleAsync(GameClient client, PlayerTradePackets.TradeRequest request);

    /// <summary>Leaving the world ends any trade: the partner's window closes (<c>TM_CANCEL_TRADE</c>).</summary>
    void CancelFor(GameClient client);
}

public sealed class PlayerTradeService : IPlayerTradeService
{
    private const ushort TradeId = (ushort)GamePackets.TM_TRADE;

    /// <summary><c>ITEM_FLAG_SUMMON</c>: a bound creature card stays with its tamer.</summary>
    private const uint BoundSummonCard = 0x8000_0000u;

    private readonly ILogger _logger = Log.ForContext<PlayerTradeService>();
    private readonly ICharacterService _characters;
    private readonly IPlayerVisibilityService _players;
    private readonly object _gate = new();
    private readonly Dictionary<uint, TradeSide> _trades = new();
    private readonly HashSet<(uint From, uint To)> _requests = new();

    public PlayerTradeService(ICharacterService characters, IPlayerVisibilityService players)
    {
        _characters = characters;
        _players = players;
    }

    public async Task HandleAsync(GameClient client, PlayerTradePackets.TradeRequest request)
    {
        var info = client.ConnectionInfo;
        if (info.CharacterHandle == 0)
        {
            return;
        }

        // onTrade: naming oneself ends whatever trade is open.
        if (request.TargetPlayer == info.CharacterHandle)
        {
            CancelFor(client);
            return;
        }

        switch (request.Mode)
        {
            case TradeMode.Request:
                Request(client, request.TargetPlayer);
                break;
            case TradeMode.Accept:
                Accept(client, request.TargetPlayer);
                break;
            case TradeMode.Cancel:
                CancelFor(client);
                break;
            case TradeMode.Reject:
                Reject(client, request.TargetPlayer);
                break;
            case TradeMode.AddItem:
            case TradeMode.ModifyCount:
                await OfferItemAsync(client, request);
                break;
            case TradeMode.RemoveItem:
                await RemoveItemAsync(client, request);
                break;
            case TradeMode.AddGold:
                AddGold(client, request.Count);
                break;
            case TradeMode.Freeze:
                Freeze(client);
                break;
            case TradeMode.Confirm:
                await ConfirmAsync(client);
                break;
        }
    }

    public void CancelFor(GameClient client)
    {
        lock (_gate)
        {
            var handle = client.ConnectionInfo.CharacterHandle;
            _requests.RemoveWhere(pair => pair.From == handle || pair.To == handle);
            if (!_trades.TryGetValue(handle, out var side))
            {
                return;
            }

            End(side, notify: true);
        }
    }

    private void Request(GameClient client, uint target)
    {
        lock (_gate)
        {
            if (!TryValidTarget(client, target, out var partner))
            {
                return;
            }

            if (_trades.ContainsKey(client.ConnectionInfo.CharacterHandle) || _trades.ContainsKey(target))
            {
                client.SendResult(TradeId, (ushort)ResultCode.AccessDenied, unchecked((int)target));
                return;
            }

            _requests.Add((client.ConnectionInfo.CharacterHandle, target));
            partner.Connection.Send(PlayerTradePackets.Build(client.ConnectionInfo.CharacterHandle, TradeMode.Request));
        }
    }

    private void Accept(GameClient client, uint target)
    {
        lock (_gate)
        {
            if (!TryValidTarget(client, target, out var partner))
            {
                return;
            }

            // Only an accept answering the partner's own request opens a trade: the official server takes
            // any accept, which lets a player open a trade window on someone who never asked.
            var me = client.ConnectionInfo.CharacterHandle;
            if (_trades.ContainsKey(me) || _trades.ContainsKey(target) || !_requests.Remove((target, me)))
            {
                client.SendResult(TradeId, (ushort)ResultCode.AccessDenied);
                return;
            }

            var mine = new TradeSide(client);
            var theirs = new TradeSide(partner);
            mine.Partner = theirs;
            theirs.Partner = mine;
            _trades[me] = mine;
            _trades[target] = theirs;

            client.Connection.Send(PlayerTradePackets.Build(target, TradeMode.Begin));
            partner.Connection.Send(PlayerTradePackets.Build(me, TradeMode.Begin));
        }
    }

    private void Reject(GameClient client, uint target)
    {
        lock (_gate)
        {
            if (!TryValidTarget(client, target, out var partner))
            {
                return;
            }

            _requests.Remove((target, client.ConnectionInfo.CharacterHandle));
            partner.Connection.Send(PlayerTradePackets.Build(client.ConnectionInfo.CharacterHandle, TradeMode.Reject));
        }
    }

    /// <summary>
    /// <c>onAddItem</c> and <c>onModifyCount</c>: the item is named by its uid, the count must be positive and
    /// within the stack, the item tradable; the echo carries the item with the count traded.
    /// </summary>
    private async Task OfferItemAsync(GameClient client, PlayerTradePackets.TradeRequest request)
    {
        if (!IsOpenAndUnfrozen(client))
        {
            return;
        }

        var item = await FindItemAsync(client, request.ItemUid);
        lock (_gate)
        {
            if (!TryOpenUnfrozen(client, out var side))
            {
                return;
            }

            if (item is null || request.Count <= 0 || request.Count > item.Amount)
            {
                client.SendResult(TradeId, (ushort)ResultCode.NotExist);
                return;
            }

            if (!IsTradable(item))
            {
                client.SendResult(TradeId, (ushort)ResultCode.NotActable);
                return;
            }

            var offered = side.Items.ContainsKey(item.Id);
            if (request.Mode == TradeMode.AddItem ? offered : !offered)
            {
                client.SendResult(TradeId, (ushort)ResultCode.NotExist);
                return;
            }

            side.Items[item.Id] = request.Count;
            Echo(side, request.Mode, item, request.Count);
        }
    }

    private async Task RemoveItemAsync(GameClient client, PlayerTradePackets.TradeRequest request)
    {
        if (!IsOpenAndUnfrozen(client))
        {
            return;
        }

        var item = await FindItemAsync(client, request.ItemUid);
        lock (_gate)
        {
            if (!TryOpenUnfrozen(client, out var side))
            {
                return;
            }

            if (item is null || !side.Items.Remove(item.Id))
            {
                client.SendResult(TradeId, (ushort)ResultCode.NotExist);
                return;
            }

            Echo(side, TradeMode.RemoveItem, item, request.Count);
        }
    }

    /// <summary><c>onAddGold</c>: the amount offered, never negative, never above what the player holds.</summary>
    private void AddGold(GameClient client, long gold)
    {
        lock (_gate)
        {
            if (!TryOpenUnfrozen(client, out var side))
            {
                return;
            }

            if (gold < 0 || gold > client.ConnectionInfo.CharacterGold)
            {
                client.SendResult(TradeId, (ushort)ResultCode.NotExist);
                return;
            }

            side.Gold = gold;
            Echo(side, TradeMode.AddGold, null, gold);
        }
    }

    private void Freeze(GameClient client)
    {
        lock (_gate)
        {
            if (!_trades.TryGetValue(client.ConnectionInfo.CharacterHandle, out var side))
            {
                return;
            }

            side.Frozen = true;
            Echo(side, TradeMode.Freeze, null, 0);
        }
    }

    /// <summary>
    /// <c>onConfirmTrade</c>: both windows frozen, then each side confirms; the second confirmation carries
    /// the trade out — gold under each session's lock, items and both balances in one save.
    /// </summary>
    private async Task ConfirmAsync(GameClient client)
    {
        TradeSide mine;
        lock (_gate)
        {
            if (!_trades.TryGetValue(client.ConnectionInfo.CharacterHandle, out mine))
            {
                return;
            }

            if (!mine.Frozen || !mine.Partner.Frozen)
            {
                End(mine, notify: true);
                return;
            }

            if (mine.Confirmed || mine.Processing)
            {
                return;
            }

            mine.Confirmed = true;
            Echo(mine, TradeMode.Confirm, null, 0);
            if (!mine.Partner.Confirmed)
            {
                return;
            }

            mine.Processing = mine.Partner.Processing = true;
        }

        await ProcessAsync(mine);
    }

    private async Task ProcessAsync(TradeSide first)
    {
        var second = first.Partner;
        var a = first.Client.ConnectionInfo;
        var b = second.Client.ConnectionInfo;

        // TooMuchMoney names the side whose balance would overflow, to both (the official answer).
        var aOver = a.CharacterGold - first.Gold + second.Gold > BoothTradeRules.MaxGold;
        var bOver = b.CharacterGold - second.Gold + first.Gold > BoothTradeRules.MaxGold;
        if (aOver || bOver)
        {
            lock (_gate)
            {
                End(first, notify: false);
            }

            foreach (var over in new[] { (aOver, a.CharacterHandle), (bOver, b.CharacterHandle) }.Where(o => o.Item1))
            {
                first.Client.SendResult(TradeId, (ushort)ResultCode.TooMuchMoney, unchecked((int)over.CharacterHandle));
                second.Client.SendResult(TradeId, (ushort)ResultCode.TooMuchMoney, unchecked((int)over.CharacterHandle));
            }

            return;
        }

        var result = await SettleAsync(first, second);
        lock (_gate)
        {
            End(first, notify: false);
        }

        if (result is null)
        {
            first.Client.SendResult(TradeId, (ushort)ResultCode.AccessDenied);
            second.Client.SendResult(TradeId, (ushort)ResultCode.AccessDenied);
            return;
        }

        foreach (var (side, other) in new[] { (first, second), (second, first) })
        {
            side.Client.Connection.Send(PlayerTradePackets.Build(side.Client.ConnectionInfo.CharacterHandle,
                TradeMode.Process));
            side.Client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(side.Client.ConnectionInfo.CharacterGold,
                side.Client.ConnectionInfo.CharacterChaos));
        }

        SendItemFrames(first.Client, second.Client, result.FirstGave);
        SendItemFrames(second.Client, first.Client, result.SecondGave);
        _logger.Information("Trade {first} <-> {second}: {firstItems} and {secondItems} stack(s), {firstGold} and {secondGold} gold",
            a.CharacterName, b.CharacterName, result.FirstGave.Count, result.SecondGave.Count, first.Gold, second.Gold);
    }

    /// <summary>The gold first, each move judged under its session's lock, then the items; any failure undoes the gold.</summary>
    private async Task<ItemExchangeResult> SettleAsync(TradeSide first, TradeSide second)
    {
        var a = first.Client.ConnectionInfo;
        var b = second.Client.ConnectionInfo;
        if (!a.TryDebitGold(first.Gold))
        {
            return null;
        }

        if (!b.TryDebitGold(second.Gold))
        {
            a.AddGold(first.Gold);
            return null;
        }

        a.AddGold(second.Gold);
        b.AddGold(first.Gold);

        ItemExchangeResult result;
        try
        {
            result = await _characters.ExchangeItemsAsync(new ItemExchange(a.CharacterName, b.CharacterName,
                Lines(first), Lines(second), a.CharacterGold, b.CharacterGold));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Trade between {first} and {second} failed in the database",
                a.CharacterName, b.CharacterName);
            result = null;
        }

        if (result?.Outcome == ItemTransferOutcome.Success)
        {
            return result;
        }

        a.AddGold(first.Gold - second.Gold);
        b.AddGold(second.Gold - first.Gold);
        return null;
    }

    private static IReadOnlyList<ItemTransferLine> Lines(TradeSide side) =>
        side.Items.Select(pair => new ItemTransferLine((uint)pair.Key, pair.Value)).ToArray();

    /// <summary>The giver's stacks shrink or leave (255 or 254); the receiver's new items arrive (207).</summary>
    private static void SendItemFrames(GameClient giver, GameClient receiver, IReadOnlyList<ItemTransferred> moved)
    {
        foreach (var line in moved)
        {
            giver.Connection.Send(line.GiverRemaining == 0
                ? GameCharacterPackets.BuildDestroyItem(line.GiverHandle)
                : GameCharacterPackets.BuildUpdateItemCount(line.GiverHandle, line.GiverRemaining));
        }

        if (moved.Count > 0)
        {
            foreach (var frame in GameCharacterPackets.BuildInventory(moved.Select(line => line.Received).ToArray()))
            {
                receiver.Connection.Send(frame);
            }
        }
    }

    /// <summary>Both windows see every change of one side, named by that side's handle.</summary>
    private static void Echo(TradeSide side, TradeMode mode, ItemEntity item, long count)
    {
        var frame = PlayerTradePackets.Build(side.Client.ConnectionInfo.CharacterHandle, mode, item, count);
        side.Partner.Client.Connection.Send(frame);
        side.Client.Connection.Send(frame);
    }

    /// <summary>
    /// <c>CancelTrade</c> on both sides; with <paramref name="notify"/> each window is told
    /// (<c>TM_CANCEL_TRADE</c> naming the partner), otherwise the trade ended on its own terms.
    /// </summary>
    private void End(TradeSide side, bool notify)
    {
        foreach (var (one, other) in new[] { (side, side.Partner), (side.Partner, side) })
        {
            _trades.Remove(one.Client.ConnectionInfo.CharacterHandle);
            if (notify)
            {
                one.Client.Connection.Send(PlayerTradePackets.Build(other.Client.ConnectionInfo.CharacterHandle,
                    TradeMode.Cancel));
            }
        }
    }

    /// <summary>
    /// The official validity check (<c>0x1401422a0</c>): the target is a player in the world, else
    /// <c>NotExist</c>; within <c>g_nRegionSize</c> in the plane, else <c>TooFar</c>.
    /// </summary>
    private bool TryValidTarget(GameClient client, uint target, out GameClient partner)
    {
        partner = _players.Registry.TryResolve(target, out var resolved)
                  && resolved.ConnectionInfo.CharacterHandle == target
            ? resolved
            : null;
        if (partner is null)
        {
            client.SendResult(TradeId, (ushort)ResultCode.NotExist);
            return false;
        }

        var me = client.ConnectionInfo;
        var them = partner.ConnectionInfo;
        var dx = me.X - them.X;
        var dy = me.Y - them.Y;
        if (me.Layer != them.Layer || dx * dx + dy * dy > WorldVisibility.RegionSize * WorldVisibility.RegionSize)
        {
            client.SendResult(TradeId, (ushort)ResultCode.TooFar);
            return false;
        }

        return true;
    }

    private bool IsOpenAndUnfrozen(GameClient client)
    {
        lock (_gate)
        {
            return TryOpenUnfrozen(client, out _);
        }
    }

    private bool TryOpenUnfrozen(GameClient client, out TradeSide side) =>
        _trades.TryGetValue(client.ConnectionInfo.CharacterHandle, out side) && !side.Frozen && !side.Processing;

    private async Task<ItemEntity> FindItemAsync(GameClient client, long uid)
    {
        if (uid <= 0 || uid > uint.MaxValue)
        {
            return null;
        }

        try
        {
            return await _characters.GetItemByHandleAsync(client.ConnectionInfo.CharacterName, (uint)uid);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not read item {uid} for {clientTag}'s trade", uid, client.ClientTag);
            return null;
        }
    }

    /// <summary>
    /// Worn items and bound creature cards stay; <c>StructPlayer::IsTradable</c>'s other rules (item flags,
    /// <c>item_use_flag</c>) are not read.
    /// </summary>
    private static bool IsTradable(ItemEntity item) =>
        item.WearInfo == ItemWearType.None && (unchecked((uint)item.Flag) & BoundSummonCard) == 0;

    private sealed class TradeSide
    {
        public TradeSide(GameClient client) => Client = client;

        public GameClient Client { get; }
        public TradeSide Partner { get; set; }
        public Dictionary<long, long> Items { get; } = new();
        public long Gold { get; set; }
        public bool Frozen { get; set; }
        public bool Confirmed { get; set; }
        public bool Processing { get; set; }
    }
}
