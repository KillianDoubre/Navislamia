using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Serilog;

namespace Navislamia.Game.Services.Auction;

/// <summary>The auction house (docs/packet-specs/socle-encheres-mecanique.md).</summary>
public interface IAuctionService
{
    Task OnNameChangedAsync(uint characterId, string name);
    Task<ResultCode> RenameCharacterAsync(uint characterId, string name, Func<Task<ResultCode>> rename);
    Task SearchAsync(GameClient client, AuctionSearchRequest request);
    Task SellingListAsync(GameClient client, int page);
    Task BiddedListAsync(GameClient client, int page);
    Task BidAsync(GameClient client, int auctionUid, long price);
    Task InstantPurchaseAsync(GameClient client, int auctionUid);
    Task RegisterAsync(GameClient client, uint itemHandle, long count, long startPrice, long instantPrice, byte durationType);
    Task CancelAsync(GameClient client, int auctionUid);
    Task KeepingListAsync(GameClient client, int page);
    Task TakeAsync(GameClient client, int keepingUid);
}

/// <summary>
/// The official <c>AuctionManager</c> and its <c>GameMessage</c> handlers: an in-memory index of the running auctions
/// and of the keeping box, written through <see cref="IAuctionStore"/> one transaction per change. Every gold that
/// moves between players goes through the keeping box, so only the actor's own balance ever changes at once.
/// </summary>
public sealed class AuctionService : IAuctionService
{
    private const ushort SearchId = (ushort)GamePackets.TM_CS_AUCTION_SEARCH;
    private const ushort SellingId = (ushort)GamePackets.TM_CS_AUCTION_SELLING_LIST;
    private const ushort BiddedId = (ushort)GamePackets.TM_CS_AUCTION_BIDDED_LIST;
    private const ushort BidId = (ushort)GamePackets.TM_CS_AUCTION_BID;
    private const ushort PurchaseId = (ushort)GamePackets.TM_CS_AUCTION_INSTANT_PURCHASE;
    private const ushort RegisterId = (ushort)GamePackets.TM_CS_AUCTION_REGISTER;
    private const ushort CancelId = (ushort)GamePackets.TM_CS_AUCTION_CANCEL;
    private const ushort KeepingListId = (ushort)GamePackets.TM_CS_ITEM_KEEPING_LIST;
    private const ushort TakeId = (ushort)GamePackets.TM_CS_ITEM_KEEPING_TAKE;

    private readonly ILogger _logger = Log.ForContext<AuctionService>();
    private readonly IAuctionStore _store;
    private readonly IAuctionCatalog _catalog;
    private readonly IItemSellCatalog _sellCatalog;
    private readonly IEquipmentService _equipment;
    private readonly IPlayerVisibilityService _players;
    private readonly Weight.ICarriedWeightService _weights;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<uint> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<long, Listing> _listings = new();
    private readonly Dictionary<long, Keeping> _keepings = new();
    private bool _loaded;
    private IReadOnlyDictionary<int, DateTime> _automaticRegistrations = new Dictionary<int, DateTime>();

    public AuctionService(IAuctionStore store, IAuctionCatalog catalog, IItemSellCatalog sellCatalog,
        IEquipmentService equipment = null, IPlayerVisibilityService players = null,
        Weight.ICarriedWeightService weights = null, Func<DateTime> utcNow = null, Func<uint> clock = null,
        bool runTicks = true)
    {
        _store = store;
        _catalog = catalog;
        _sellCatalog = sellCatalog;
        _equipment = equipment;
        _players = players;
        _weights = weights;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _clock = clock ?? (() => ServerClock.Now);
        foreach (var row in _catalog.AutomaticAuctions.Where(r => !_catalog.TryGetItem(r.ItemCode, out _)))
            _logger.Warning("Automatic auction {resourceId} skipped: item {itemCode} is absent from the client catalog", row.Id, row.ItemCode);
        if (runTicks)
        {
            _ = RunAsync();
        }
    }

    // ---- lists ----------------------------------------------------------------------------------------------

    public async Task OnNameChangedAsync(uint characterId, string name)
        => await RenameCharacterAsync(characterId, name, () => Task.FromResult(ResultCode.Success));

    public async Task<ResultCode> RenameCharacterAsync(uint characterId, string name, Func<Task<ResultCode>> rename)
    {
        // Official DaemonProc/AuctionManager.cpp:1602-1640: seller and current highest bidder only.
        await _gate.WaitAsync();
        try
        {
            await EnsureLoadedAsync();
            // Same lock order as bids/registration: auction gate then CharacterGate. A concurrent bid cannot
            // write an old cached name between the committed rename and its in-memory update.
            var result = await rename();
            if (result != ResultCode.Success) return result;
            foreach (var listing in _listings.Values)
            {
                if (listing.Entity.SellerId == characterId) listing.Entity.SellerName = name;
                if (listing.Entity.HighestBidderId == characterId) listing.Entity.HighestBidderName = name;
            }
            return result;
        }
        finally { _gate.Release(); }
    }

    /// <summary><c>onAuctionSearch</c> then <c>SearchAndSendAuctionList</c>; the next request waits 3 s.</summary>
    public async Task SearchAsync(GameClient client, AuctionSearchRequest request)
    {
        if (!Ready(client, SearchId)) return;
        await WithGateAsync(client, SearchId, () =>
        {
            var info = client.ConnectionInfo;
            var max = _catalog.MaxCategoryIndex;
            if (request.CategoryId < AuctionRules.CategorySpecial || request.CategoryId > max
                || request.SubCategoryId < AuctionRules.SubCategoryNone || request.PageNum < 1
                || request.PageNum > _listings.Count / AuctionRules.PerPage + 1
                || (request.CategoryId == AuctionRules.CategorySpecial && request.SubCategoryId != AuctionRules.SubCategoryAll
                    && request.SubCategoryId != AuctionRules.SubCategoryEtc))
            {
                client.SendResult(SearchId, (ushort)ResultCode.InvalidArgument);
                return Task.CompletedTask;
            }

            var category = request.CategoryId;
            var sub = request.SubCategoryId;
            if (category == AuctionRules.CategorySpecial && sub == AuctionRules.SubCategoryEtc)
            {
                category = max;
                sub = AuctionRules.SubCategoryAll;
            }

            Func<int, int, bool> takes = null;
            if (sub != AuctionRules.SubCategoryAll && !_catalog.TryGetNode(category, sub, out takes))
            {
                client.SendResult(SearchId, (ushort)ResultCode.InvalidArgument);
                return Task.CompletedTask;
            }

            var keyword = request.Keyword ?? string.Empty;
            var found = _listings.Values
                .Where(l => CanAccess(info, l.Entity))
                .Where(l => category == AuctionRules.CategorySpecial || l.Category == category)
                .Where(l => takes is null || takes(l.Group, l.Class))
                .Where(l => keyword.Length == 0 || l.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .Where(l => !request.IsEquipable || _equipment is null || _equipment.CanWear(info, l.Item.ItemResourceId))
                // addAuctionInfoToIndex keeps the reserved (secroute-only) sales ahead of the others in a category.
                .OrderBy(l => l.Category).ThenByDescending(l => l.Entity.SecrouteOnly).ThenBy(l => l.Entity.Id)
                .ToList();
            var page = found.Skip((request.PageNum - 1) * AuctionRules.PerPage).Take(AuctionRules.PerPage)
                .Select(l => new SearchedAuctionInfo(Wire(l), l.Entity.SellerName, SearchFlag(l, info.CharacterHandle)))
                .ToList();
            client.Connection.Send(GameAuctionPackets.BuildAuctionSearch(request.PageNum, AuctionRules.TotalPages(found.Count), page));
            return Task.CompletedTask;
        });
        client.ConnectionInfo.NextAuctionUsableTime = unchecked(_clock() + AuctionRules.SearchInterval);
    }

    private static byte SearchFlag(Listing listing, uint me) =>
        listing.Entity.HighestBidderId != 0 && listing.Entity.HighestBidderId == me ? AuctionRules.FlagIsHighestBidder
        : listing.Entity.HighestBidderId == 0 && listing.Entity.BidderIds.Length == 0 ? AuctionRules.FlagNoOtherBidder
        : (byte)0;

    /// <summary><c>SendRegisteredAuctionList</c>: the player's own auctions; none answers <c>NotExist</c>.</summary>
    public async Task SellingListAsync(GameClient client, int page)
    {
        if (!Ready(client, SellingId)) return;
        await WithGateAsync(client, SellingId, () =>
        {
            var mine = _listings.Values.Where(l => l.Entity.SellerId == client.ConnectionInfo.CharacterHandle)
                .OrderBy(l => l.Entity.Id).ToList();
            if (!CheckPage(client, SellingId, page, mine.Count)) return Task.CompletedTask;
            var entries = mine.Skip((page - 1) * AuctionRules.PerPage).Take(AuctionRules.PerPage)
                .Select(l => new RegisteredAuctionInfo(Wire(l),
                    l.Entity.HighestBidderId == 0 ? AuctionRules.StatusNoBidder : AuctionRules.StatusBidCalling)).ToList();
            client.Connection.Send(GameAuctionPackets.BuildAuctionSellingList(page, AuctionRules.TotalPages(mine.Count), entries));
            return Task.CompletedTask;
        });
        client.ConnectionInfo.NextAuctionUsableTime = unchecked(_clock() + AuctionRules.ProcessInterval);
    }

    /// <summary><c>SendBiddedAuctionList</c>: the auctions the player bid on, as highest bidder or overtaken.</summary>
    public async Task BiddedListAsync(GameClient client, int page)
    {
        if (!Ready(client, BiddedId)) return;
        await WithGateAsync(client, BiddedId, () =>
        {
            var me = (long)client.ConnectionInfo.CharacterHandle;
            var bidded = _listings.Values.Where(l => l.Entity.HighestBidderId == me || l.Entity.BidderIds.Contains(me))
                .Where(l => CanAccess(client.ConnectionInfo, l.Entity))
                .OrderBy(l => l.Entity.Id).ToList();
            if (!CheckPage(client, BiddedId, page, bidded.Count)) return Task.CompletedTask;
            var entries = bidded.Skip((page - 1) * AuctionRules.PerPage).Take(AuctionRules.PerPage)
                .Select(l => new BiddedAuctionInfo(Wire(l),
                    l.Entity.HighestBidderId == me ? AuctionRules.StatusMine : AuctionRules.StatusOthers)).ToList();
            client.Connection.Send(GameAuctionPackets.BuildAuctionBiddedList(page, AuctionRules.TotalPages(bidded.Count), entries));
            return Task.CompletedTask;
        });
        client.ConnectionInfo.NextAuctionUsableTime = unchecked(_clock() + AuctionRules.ProcessInterval);
    }

    /// <summary><c>SendKeepingItemList</c>: the keeping box, with the seconds each entry has left.</summary>
    public async Task KeepingListAsync(GameClient client, int page)
    {
        if (!Ready(client, KeepingListId)) return;
        await WithGateAsync(client, KeepingListId, () =>
        {
            var mine = _keepings.Values.Where(k => k.Entity.OwnerId == client.ConnectionInfo.CharacterHandle)
                .OrderBy(k => k.Entity.Id).ToList();
            if (!CheckPage(client, KeepingListId, page, mine.Count)) return Task.CompletedTask;
            var now = _utcNow();
            var entries = mine.Skip((page - 1) * AuctionRules.PerPage).Take(AuctionRules.PerPage)
                .Select(k => new ItemKeepingEntry((int)k.Entity.Id, WireItem(k),
                    (int)Math.Max(0, (k.Entity.ExpireTime - now).TotalSeconds), (byte)k.Entity.KeepingType,
                    k.Entity.RelatedItemCode, k.Entity.RelatedItemEnhance, k.Entity.RelatedItemLevel)).ToList();
            client.Connection.Send(GameAuctionPackets.BuildItemKeepingList(page, AuctionRules.TotalPages(mine.Count), entries));
            return Task.CompletedTask;
        });
        client.ConnectionInfo.NextAuctionUsableTime = unchecked(_clock() + AuctionRules.ProcessInterval);
    }

    // ---- actions --------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>RegisterItemToSell</c>, in its order: the item and count, the price against the shop price (<c>TooCheap</c>),
    /// the instant price against the start price, the tradable rules, the booth, worn, formed creature card, the tax in
    /// hand, the category. The tax is taken, the item leaves the bag (split from its stack if only part of it), the
    /// auction runs for 6/24/72 h. Result value = item code.
    /// </summary>
    public async Task RegisterAsync(GameClient client, uint itemHandle, long count, long startPrice, long instantPrice,
        byte durationType)
    {
        if (!Ready(client, RegisterId)) return;
        var info = client.ConnectionInfo;
        var duration = AuctionRules.Duration(durationType);
        var tax = AuctionRules.RegistrationTax(duration, startPrice);
        var itemCode = 0;

        await WithGateAsync(client, RegisterId, async () =>
        {
            if (itemHandle == 0 || count <= 0)
            {
                client.SendResult(RegisterId, (ushort)ResultCode.NotExist);
                return;
            }

            var listing = new AuctionListingEntity
            {
                SellerId = info.CharacterHandle, SellerName = info.CharacterName, EndTime = _utcNow() + duration,
                StartPrice = startPrice, InstantPurchasePrice = instantPrice, RegistrationTax = tax,
                HighestBiddingPrice = startPrice
            };
            var debited = false;
            ResultCode Check(ItemEntity item)
            {
                itemCode = (int)item.ItemResourceId;
                var verdict = CheckRegistrable(info, item, count, startPrice, instantPrice, tax);
                if (verdict != ResultCode.Success) return verdict;
                if (!info.TryDebitGold(tax)) return ResultCode.NotEnoughMoney;
                debited = true;
                return ResultCode.Success;
            }

            RegisterOutcome outcome;
            try
            {
                outcome = await _store.RegisterAsync(new GoldWrite(info.CharacterName, info.CharacterHandle, info.CharacterGold),
                    itemHandle, count, listing, Check, () => info.CharacterGold);
            }
            catch (Exception exception)
            {
                if (debited) info.AddGold(tax);
                _logger.Error(exception, "Could not register an auction for {clientTag}", client.ClientTag);
                client.SendResult(RegisterId, (ushort)ResultCode.DBError, itemCode);
                return;
            }

            if (outcome.Result != ResultCode.Success)
            {
                if (debited) info.AddGold(tax);
                client.SendResult(RegisterId, (ushort)outcome.Result, itemCode);
                return;
            }

            AddListing(outcome.Listing, outcome.AuctionItem);
            client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(info.CharacterGold, info.CharacterChaos));
            client.Connection.Send(outcome.BagItemRemoved
                ? GameCharacterPackets.BuildDestroyItem(itemHandle)
                : GameCharacterPackets.BuildUpdateItemCount(itemHandle, outcome.RemainingBagItem.Amount));
            client.SendResult(RegisterId, (ushort)ResultCode.Success, itemCode);
        });
    }

    private ResultCode CheckRegistrable(ConnectionInfo info, ItemEntity item, long count, long startPrice,
        long instantPrice, long tax)
    {
        var code = (int)item.ItemResourceId;
        if (_sellCatalog is not null && _sellCatalog.TryGetTemplate(code, out var template)
            && MarketSellPrice.TryComputeUnitPrice(template.Price, template.Rank, item.Level,
                MarketSellPrice.IsSamePriceForBuying(code), out var unit)
            && startPrice < unit * count)
        {
            return ResultCode.TooCheap;
        }

        if (instantPrice != 0 && instantPrice < startPrice) return ResultCode.TooCheap;
        if ((unchecked((uint)item.Flag) & (AuctionItemFlags.Failed | AuctionItemFlags.Taming)) != 0) return ResultCode.NotActable;
        if (tax < 0) return ResultCode.NotEnoughMoney;
        if (info.IsBoothOpen || info.WatchedBoothHandle is not null) return ResultCode.NotActableWhileUsingBooth;
        if (item.WearInfo != ItemWearType.None || item.EquippedBySummonId is not null) return ResultCode.NotActable;
        if (!Creatures.HeldItemRules.IsErasable(info, item.Id)) return ResultCode.NotActable;
        if (info.CharacterGold < tax) return ResultCode.NotEnoughMoney;
        if (!_catalog.TryGetItem(code, out var row)) return ResultCode.NotActable;
        var index = _catalog.CategoryIndexOf(row.Group, row.Class);
        if (index <= AuctionRules.CategorySpecial || index > _catalog.MaxCategoryIndex) return ResultCode.NotActable;
        return ResultCode.Success;
    }

    /// <summary>
    /// <c>BidForAuction</c>: not on one's own auction nor when already the highest bidder (<c>AccessDenied</c>), at
    /// least the current price (1.01 × once there is a bidder), never above the instant price. The bid is taken now;
    /// the bidder it overtakes gets their gold back in the keeping box (32) and the notice @713.
    /// </summary>
    public async Task BidAsync(GameClient client, int auctionUid, long price)
    {
        if (!Ready(client, BidId)) return;
        var info = client.ConnectionInfo;
        await WithGateAsync(client, BidId, async () =>
        {
            if (auctionUid == 0 || price <= 0)
            {
                client.SendResult(BidId, (ushort)ResultCode.InvalidArgument);
                return;
            }

            if (info.IsBoothOpen || info.WatchedBoothHandle is not null)
            {
                client.SendResult(BidId, (ushort)ResultCode.NotActableWhileUsingBooth);
                return;
            }

            if (price > info.CharacterGold)
            {
                client.SendResult(BidId, (ushort)ResultCode.NotEnoughMoney);
                return;
            }

            if (!_listings.TryGetValue(auctionUid, out var listing))
            {
                client.SendResult(BidId, (ushort)ResultCode.NotExist);
                return;
            }

            var code = (int)listing.Item.ItemResourceId;
            var me = (long)info.CharacterHandle;
            if (!CanAccess(info, listing.Entity))
            {
                client.SendResult(BidId, (ushort)ResultCode.NotExist);
                return;
            }
            if (listing.Entity.SellerId == me || (listing.Entity.HighestBidderId != 0 && listing.Entity.HighestBidderId == me))
            {
                client.SendResult(BidId, (ushort)ResultCode.AccessDenied, code);
                return;
            }

            var verdict = AuctionRules.CheckBid(price, listing.Entity.HighestBiddingPrice, listing.Entity.HighestBidderId != 0,
                listing.Entity.InstantPurchasePrice);
            if (verdict != ResultCode.Success)
            {
                client.SendResult(BidId, (ushort)verdict, code);
                return;
            }

            if (!info.TryDebitGold(price))
            {
                client.SendResult(BidId, (ushort)ResultCode.NotEnoughMoney, code);
                return;
            }

            var updated = Clone(listing.Entity);
            var change = new AuctionChange { Gold = new GoldWrite(info.CharacterName, me, info.CharacterGold) };
            var previous = listing.Entity.HighestBidderId;
            var previousName = listing.Entity.HighestBidderName;
            if (previous != 0)
            {
                change.AddedKeepings.Add(GoldKeeping(previous, listing.Entity.HighestBiddingPrice, StorageType.GoldByHigherBid, listing));
            }

            var bidders = updated.BidderIds.ToList();
            if (previous != 0) bidders.Add(previous);
            bidders.Remove(me);
            while (bidders.Count > AuctionRules.MaxBidderList) bidders.RemoveAt(0);
            updated.BidderIds = bidders.ToArray();
            updated.HighestBiddingPrice = price;
            updated.HighestBidderId = me;
            updated.HighestBidderName = info.CharacterName;
            change.UpdatedListings.Add(updated);

            if (!await CommitAsync(change))
            {
                info.AddGold(price);
                client.SendResult(BidId, (ushort)ResultCode.DBError, code);
                return;
            }

            listing.Entity = updated;
            AddKeepings(change);
            client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(info.CharacterGold, info.CharacterChaos));
            if (previous != 0) Notice(previous, 713, listing);
            client.SendResult(BidId, (ushort)ResultCode.Success, code);
        });
    }

    /// <summary>
    /// <c>InstantPurchase</c>: not one's own auction, an instant price set; the highest bidder pays the difference, any
    /// other highest bidder is refunded (34, notice @997). The item goes to the buyer's keeping box (2), the seller gets
    /// the price less 3 % (30) and the tax back (31), notice @717.
    /// </summary>
    public async Task InstantPurchaseAsync(GameClient client, int auctionUid)
    {
        if (!Ready(client, PurchaseId)) return;
        var info = client.ConnectionInfo;
        await WithGateAsync(client, PurchaseId, async () =>
        {
            if (auctionUid == 0)
            {
                client.SendResult(PurchaseId, (ushort)ResultCode.InvalidArgument);
                return;
            }

            if (info.IsBoothOpen || info.WatchedBoothHandle is not null)
            {
                client.SendResult(PurchaseId, (ushort)ResultCode.NotActableWhileUsingBooth);
                return;
            }

            if (!_listings.TryGetValue(auctionUid, out var listing))
            {
                client.SendResult(PurchaseId, (ushort)ResultCode.NotExist);
                return;
            }

            var entity = listing.Entity;
            var code = (int)listing.Item.ItemResourceId;
            if (!CanAccess(info, entity))
            {
                client.SendResult(PurchaseId, (ushort)ResultCode.NotExist);
                return;
            }
            var me = (long)info.CharacterHandle;
            if (entity.SellerId == me)
            {
                client.SendResult(PurchaseId, (ushort)ResultCode.AccessDenied, code);
                return;
            }

            if (entity.InstantPurchasePrice == 0)
            {
                client.SendResult(PurchaseId, (ushort)ResultCode.NotActable, code);
                return;
            }

            var required = entity.InstantPurchasePrice - (entity.HighestBidderId == me ? entity.HighestBiddingPrice : 0);
            if (required < 0)
            {
                client.SendResult(PurchaseId, (ushort)ResultCode.TooCheap, code);
                return;
            }

            if (info.CharacterGold < required || (required > 0 && !info.TryDebitGold(required)))
            {
                client.SendResult(PurchaseId, (ushort)ResultCode.NotEnoughMoney, code);
                return;
            }

            var change = new AuctionChange { Gold = new GoldWrite(info.CharacterName, me, info.CharacterGold) };
            var refunded = entity.HighestBidderId != 0 && entity.HighestBidderId != me;
            if (refunded)
                change.AddedKeepings.Add(GoldKeeping(entity.HighestBidderId, entity.HighestBiddingPrice, StorageType.GoldByItemSoldOut, listing));
            change.AddedKeepings.Add(ItemKeeping(me, StorageType.ItemByInstantPurchase, listing));
            AddSellerPayment(change, listing, entity.InstantPurchasePrice);
            change.RemovedListings.Add(entity.Id);

            if (!await CommitAsync(change))
            {
                if (required > 0) info.AddGold(required);
                client.SendResult(PurchaseId, (ushort)ResultCode.DBError, code);
                return;
            }

            _listings.Remove(entity.Id);
            AddKeepings(change, listing.Item);
            client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(info.CharacterGold, info.CharacterChaos));
            if (refunded) Notice(entity.HighestBidderId, 997, listing);
            Notice(entity.SellerId, 717, listing);
            client.SendResult(PurchaseId, (ushort)ResultCode.Success, code);
        });
    }

    /// <summary><c>CancelAuction</c>: the seller only (<c>AccessDenied</c>), and only without a bidder (<c>NotActable</c>).</summary>
    public async Task CancelAsync(GameClient client, int auctionUid)
    {
        if (!Ready(client, CancelId)) return;
        await WithGateAsync(client, CancelId, async () =>
        {
            if (!_listings.TryGetValue(auctionUid, out var listing))
            {
                client.SendResult(CancelId, (ushort)ResultCode.NotExist);
                return;
            }

            var code = (int)listing.Item.ItemResourceId;
            if (listing.Entity.SellerId != client.ConnectionInfo.CharacterHandle)
            {
                client.SendResult(CancelId, (ushort)ResultCode.AccessDenied, code);
                return;
            }

            if (listing.Entity.HighestBidderId != 0)
            {
                client.SendResult(CancelId, (ushort)ResultCode.NotActable, code);
                return;
            }

            var change = new AuctionChange();
            change.AddedKeepings.Add(ItemKeeping(listing.Entity.SellerId, StorageType.ItemByCancel, listing));
            change.RemovedListings.Add(listing.Entity.Id);
            if (!await CommitAsync(change))
            {
                client.SendResult(CancelId, (ushort)ResultCode.DBError, code);
                return;
            }

            _listings.Remove(listing.Entity.Id);
            AddKeepings(change, listing.Item);
            client.SendResult(CancelId, (ushort)ResultCode.Success, code);
        });
    }

    /// <summary>
    /// <c>TakeKeepedItem</c>: the owner only, the weight (<c>TooHeavy</c>), the gold ceiling (<c>TooMuchMoney</c>); the
    /// item joins the bag, the gold the balance.
    /// </summary>
    public async Task TakeAsync(GameClient client, int keepingUid)
    {
        if (!Ready(client, TakeId)) return;
        var info = client.ConnectionInfo;
        await WithGateAsync(client, TakeId, async () =>
        {
            if (keepingUid == 0)
            {
                client.SendResult(TakeId, (ushort)ResultCode.InvalidArgument);
                return;
            }

            if (info.IsBoothOpen || info.WatchedBoothHandle is not null)
            {
                client.SendResult(TakeId, (ushort)ResultCode.NotActableWhileUsingBooth);
                return;
            }

            if (!_keepings.TryGetValue(keepingUid, out var keeping))
            {
                client.SendResult(TakeId, (ushort)ResultCode.NotExist);
                return;
            }

            if (keeping.Entity.OwnerId != info.CharacterHandle)
            {
                client.SendResult(TakeId, (ushort)ResultCode.AccessDenied);
                return;
            }

            if (keeping.Item is { } carried && _weights is not null
                && !_weights.CanCarry(info, (int)carried.ItemResourceId, carried.Amount))
            {
                client.SendResult(TakeId, (ushort)ResultCode.TooHeavy);
                return;
            }

            var gold = keeping.Entity.Gold;
            if (gold > 0 && !info.TryCreditGold(gold, GoldRules.MaxCarried))
            {
                client.SendResult(TakeId, (ushort)ResultCode.TooMuchMoney);
                return;
            }

            TakeOutcome outcome;
            try
            {
                outcome = await _store.TakeAsync(new GoldWrite(info.CharacterName, info.CharacterHandle, info.CharacterGold),
                    keeping.Entity);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Could not take keeping {keeping} for {clientTag}", keepingUid, client.ClientTag);
                outcome = new TakeOutcome(ResultCode.DBError);
            }

            if (outcome.Result != ResultCode.Success)
            {
                if (gold > 0) info.AddGold(-gold);
                client.SendResult(TakeId, (ushort)outcome.Result);
                return;
            }

            _keepings.Remove(keeping.Entity.Id);
            if (gold > 0)
                client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(info.CharacterGold, info.CharacterChaos));
            if (outcome.Item is not null)
                foreach (var packet in GameCharacterPackets.BuildInventory(new[] { outcome.Item })) client.Connection.Send(packet);
            client.SendResult(TakeId, (ushort)ResultCode.Success);
        });
    }

    // ---- expiry ---------------------------------------------------------------------------------------------

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                await ProcessAsync();
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Auction tick failed");
            }
        }
    }

    /// <summary>
    /// <c>AuctionManager::onProcess</c>: expired keeping entries vanish (their item with them); an ended auction with a
    /// bidder goes to the bidder (1, notice @714) and pays the seller (30, 31, notice @717); without one the item and
    /// the tax go back to the seller (3, 31).
    /// </summary>
    public async Task ProcessAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await EnsureLoadedAsync();
            var now = _utcNow();
            await ProcessAutomaticAsync(now);
            foreach (var keeping in _keepings.Values.Where(k => k.Entity.ExpireTime <= now).ToList())
            {
                var change = new AuctionChange();
                change.RemovedKeepings.Add(keeping.Entity.Id);
                if (keeping.Entity.ItemId is { } itemId) change.DeletedItems.Add(itemId);
                if (await CommitAsync(change)) _keepings.Remove(keeping.Entity.Id);
            }

            foreach (var listing in _listings.Values.Where(l => l.Entity.EndTime <= now).ToList())
            {
                var entity = listing.Entity;
                var change = new AuctionChange();
                change.RemovedListings.Add(entity.Id);
                if (entity.HighestBidderId != 0)
                {
                    change.AddedKeepings.Add(ItemKeeping(entity.HighestBidderId, StorageType.ItemBySuccessfulBid, listing));
                    AddSellerPayment(change, listing, entity.HighestBiddingPrice);
                }
                else if (entity.SellerId != 0)
                {
                    change.AddedKeepings.Add(ItemKeeping(entity.SellerId, StorageType.ItemByExpiration, listing));
                    change.AddedKeepings.Add(GoldKeeping(entity.SellerId, entity.RegistrationTax, StorageType.GoldByRegTax, listing));
                }
                else change.DeletedItems.Add(listing.Item.Id);

                if (!await CommitAsync(change)) continue;
                _listings.Remove(entity.Id);
                AddKeepings(change, listing.Item);
                if (entity.HighestBidderId != 0)
                {
                    Notice(entity.SellerId, 717, listing);
                    Notice(entity.HighestBidderId, 714, listing);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---- helpers --------------------------------------------------------------------------------------------

    /// <summary><c>GetNextAuctionUsableTime() &gt; GetArTime()</c>: <c>CoolTime</c>.</summary>
    private bool Ready(GameClient client, ushort requestId)
    {
        var info = client.ConnectionInfo;
        if (info.CharacterHandle == 0) return false;
        if (info.NextAuctionUsableTime != 0 && unchecked((int)(info.NextAuctionUsableTime - _clock())) > 0)
        {
            client.SendResult(requestId, (ushort)ResultCode.CoolTime);
            return false;
        }

        return true;
    }

    private async Task WithGateAsync(GameClient client, ushort requestId, Func<Task> action)
    {
        await _gate.WaitAsync();
        try
        {
            await EnsureLoadedAsync();
            await action();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Auction request {request} of {clientTag} failed", requestId, client.ClientTag);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        foreach (var (listing, item) in await _store.LoadListingsAsync()) AddListing(listing, item);
        foreach (var (keeping, item) in await _store.LoadKeepingsAsync()) _keepings[keeping.Id] = new Keeping(keeping, item);
        _automaticRegistrations = await _store.LoadAutomaticRegistrationsAsync();
        _loaded = true;
    }

    private async Task ProcessAutomaticAsync(DateTime now)
    {
        foreach (var row in _catalog.AutomaticAuctions)
        {
            DateTime? previous = _automaticRegistrations.TryGetValue(row.Id, out var last) ? last : null;
            if (!_catalog.TryGetItem(row.ItemCode, out _) || !AutoAuctionSchedule.IsDue(row, previous, now)) continue;

            // onProcess logs a failed registration and goes on: one resource must not hold up the others, nor the
            // expirations that follow in the same tick.
            try
            {
                await RegisterAutomaticAsync(row, previous, now);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Automatic auction {resourceId} could not be registered", row.Id);
            }
        }
    }

    private async Task RegisterAutomaticAsync(Navislamia.Configuration.Options.AutoAuctionRow row, DateTime? previous,
        DateTime now)
    {
        var listing = new AuctionListingEntity
        {
            AutoAuctionResourceId = row.Id, SecrouteOnly = row.SecrouteOnly,
            SellerName = row.SellerName, EndTime = now + AuctionRules.Duration(row.DurationType),
            StartPrice = row.Price, HighestBiddingPrice = row.Price
        };
        var item = new ItemEntity
        {
            ItemResourceId = row.ItemCode, Amount = 1, Level = 1,
            GenerateBySource = ItemGenerateSource.Auction, WearInfo = ItemWearType.None, SocketItemIds = new long[4]
        };
        var outcome = await _store.RegisterAutomaticAsync(row.Id, previous, now, listing, item);
        if (outcome.Result == ResultCode.Success) AddListing(outcome.Listing, outcome.AuctionItem);
        _automaticRegistrations = await _store.LoadAutomaticRegistrationsAsync();
    }

    private bool CanAccess(ConnectionInfo info, AuctionListingEntity listing)
    {
        if (!listing.SecrouteOnly) return true;
        // StructState::GAIA_MEMBER_SHIP (9004), already persisted with the player's active buffs.
        lock (info.BuffLock)
            return info.ActiveBuffs.Any(b => b.StateId == 9004 && (b.EndTick == 0 || unchecked((int)(b.EndTick - _clock())) > 0));
    }

    private static bool CheckPage(GameClient client, ushort requestId, int page, int count)
    {
        if (page < 1)
        {
            client.SendResult(requestId, (ushort)ResultCode.InvalidArgument);
            return false;
        }

        if (count == 0)
        {
            client.SendResult(requestId, (ushort)ResultCode.NotExist);
            return false;
        }

        if (page > count / AuctionRules.PerPage + 1)
        {
            client.SendResult(requestId, (ushort)ResultCode.InvalidArgument);
            return false;
        }

        return true;
    }

    private async Task<bool> CommitAsync(AuctionChange change)
    {
        try
        {
            await _store.CommitAsync(change);
            return true;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not write an auction change");
            return false;
        }
    }

    private void AddListing(AuctionListingEntity entity, ItemEntity item)
    {
        _catalog.TryGetItem((int)item.ItemResourceId, out var row);
        var group = row?.Group ?? 0;
        var itemClass = row?.Class ?? 0;
        _listings[entity.Id] = new Listing(entity, item, _catalog.CategoryIndexOf(group, itemClass), group, itemClass,
            row?.Name ?? string.Empty, row?.NameId ?? 0);
    }

    /// <summary>The entries a committed change added, now with their ids; an item entry carries the auctioned item.</summary>
    private void AddKeepings(AuctionChange change, ItemEntity item = null)
    {
        foreach (var keeping in change.AddedKeepings)
            _keepings[keeping.Id] = new Keeping(keeping, keeping.ItemId is not null ? item : null);
    }

    private void AddSellerPayment(AuctionChange change, Listing listing, long price)
    {
        if (listing.Entity.SellerId == 0) return;
        change.AddedKeepings.Add(GoldKeeping(listing.Entity.SellerId, AuctionRules.SoldPrice(price), StorageType.GoldByItemSell, listing));
        if (listing.Entity.RegistrationTax != 0)
            change.AddedKeepings.Add(GoldKeeping(listing.Entity.SellerId, listing.Entity.RegistrationTax, StorageType.GoldByRegTax, listing));
    }

    private AuctionKeepingEntity GoldKeeping(long owner, long gold, StorageType type, Listing listing) => new()
    {
        OwnerId = owner, Gold = gold, KeepingType = (int)type, RelatedAuctionId = listing.Entity.Id,
        RelatedItemCode = (int)listing.Item.ItemResourceId, RelatedItemEnhance = (int)listing.Item.Enhance,
        RelatedItemLevel = (int)listing.Item.Level, ExpireTime = _utcNow() + AuctionRules.KeepDuration
    };

    private AuctionKeepingEntity ItemKeeping(long owner, StorageType type, Listing listing) => new()
    {
        OwnerId = owner, ItemId = listing.Item.Id, KeepingType = (int)type, RelatedAuctionId = listing.Entity.Id,
        ExpireTime = _utcNow() + AuctionRules.KeepDuration
    };

    private AuctionInfo Wire(Listing listing) => new((int)listing.Entity.Id, ItemFixedInfo.FromItem(listing.Item),
        AuctionRules.DurationTypeLeft(listing.Entity.EndTime - _utcNow()), (ulong)listing.Entity.HighestBiddingPrice,
        (ulong)listing.Entity.InstantPurchasePrice);

    /// <summary>A gold entry is the official gold item: code 0, its count the amount.</summary>
    private static ItemFixedInfo WireItem(Keeping keeping) => keeping.Item is { } item
        ? ItemFixedInfo.FromItem(item)
        : new ItemFixedInfo(0, 0, keeping.Entity.Id, keeping.Entity.Gold, 0, 0, 0, 0, 0, new long[4], 0, 0, 0, 0, 0, 0);

    /// <summary>The <c>@NOTICE</c> line an online player gets: <c>@&lt;id&gt;\v#@itemname@#\v@&lt;name id&gt;</c>.</summary>
    private void Notice(long characterId, int messageId, Listing listing)
    {
        if (characterId == 0 || _players?.Registry is null) return;
        if (!_players.Registry.TryResolve((uint)characterId, out var target)
            || target.ConnectionInfo.CharacterHandle != (uint)characterId)
        {
            return;
        }

        target.Connection.Send(GameChatPackets.BuildChat("@NOTICE", (byte)ChatType.Notice,
            $"@{messageId}\v#@itemname@#\v@{listing.NameId}"));
    }

    private static AuctionListingEntity Clone(AuctionListingEntity entity) => new()
    {
        AutoAuctionResourceId = entity.AutoAuctionResourceId, SecrouteOnly = entity.SecrouteOnly,
        Id = entity.Id, ItemId = entity.ItemId, SellerId = entity.SellerId, SellerName = entity.SellerName,
        EndTime = entity.EndTime, StartPrice = entity.StartPrice, InstantPurchasePrice = entity.InstantPurchasePrice,
        RegistrationTax = entity.RegistrationTax, HighestBiddingPrice = entity.HighestBiddingPrice,
        HighestBidderId = entity.HighestBidderId, HighestBidderName = entity.HighestBidderName,
        BidderIds = (long[])entity.BidderIds.Clone(), CreatedOn = entity.CreatedOn
    };

    private sealed class Listing
    {
        public Listing(AuctionListingEntity entity, ItemEntity item, int category, int group, int itemClass, string name,
            int nameId)
        {
            Entity = entity;
            Item = item;
            Category = category;
            Group = group;
            Class = itemClass;
            Name = name;
            NameId = nameId;
        }

        public AuctionListingEntity Entity { get; set; }
        public ItemEntity Item { get; }
        public int Category { get; }
        public int Group { get; }
        public int Class { get; }
        public string Name { get; }
        public int NameId { get; }
    }

    private sealed record Keeping(AuctionKeepingEntity Entity, ItemEntity Item);
}

/// <summary>The instance flags <c>RegisterItemToSell</c> refuses, as masks (<c>ITEM_FLAG_FAILED</c> 3, <c>ITEM_FLAG_TAMING</c> 29).</summary>
public static class AuctionItemFlags
{
    public const uint Failed = 1u << 3;
    public const uint Taming = 1u << 29;
}
