using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Auction;
using Navislamia.Game.Services.Interfaces;

namespace Tests.Game;

/// <summary>The auction house mechanics (docs/packet-specs/socle-encheres-mecanique.md), on an in-memory store.</summary>
[TestFixture]
public class AuctionHouseTests
{
    private const int Sword = 101100;
    private const int Potion = 990001;

    private sealed class MemoryStore : IAuctionStore
    {
        private long _nextId = 1000;
        public readonly Dictionary<long, List<ItemEntity>> Bags = new();
        public readonly Dictionary<long, AuctionListingEntity> Listings = new();
        public readonly Dictionary<long, AuctionKeepingEntity> Keepings = new();
        public readonly Dictionary<long, long> Gold = new();
        public readonly Dictionary<long, ItemEntity> Items = new();
        public readonly Dictionary<int, DateTime> Registrations = new();

        public Task<IReadOnlyDictionary<int, DateTime>> LoadAutomaticRegistrationsAsync() =>
            Task.FromResult<IReadOnlyDictionary<int, DateTime>>(new Dictionary<int, DateTime>(Registrations));

        /// <summary>Resources whose registration fails in the database.</summary>
        public readonly HashSet<int> FailingAutomatic = new();

        public Task<RegisterOutcome> RegisterAutomaticAsync(int resourceId, DateTime? previousRegistration,
            DateTime registeredAt, AuctionListingEntity listing, ItemEntity item)
        {
            if (FailingAutomatic.Contains(resourceId)) throw new InvalidOperationException("database unavailable");
            DateTime? last = Registrations.TryGetValue(resourceId, out var value) ? value : null;
            if (last != previousRegistration) return Task.FromResult(new RegisterOutcome(ResultCode.AlreadyExist));
            item.Id = _nextId++;
            Items[item.Id] = item;
            listing.Id = _nextId++;
            listing.ItemId = item.Id;
            Listings[listing.Id] = listing;
            Registrations[resourceId] = registeredAt;
            return Task.FromResult(new RegisterOutcome(ResultCode.Success, listing, item));
        }

        public ItemEntity Give(long owner, int code, long count)
        {
            var item = new ItemEntity { Id = _nextId++, CharacterId = owner, ItemResourceId = code, Amount = count, SocketItemIds = new long[4], WearInfo = ItemWearType.None };
            if (!Bags.TryGetValue(owner, out var bag)) Bags[owner] = bag = new List<ItemEntity>();
            bag.Add(item);
            Items[item.Id] = item;
            return item;
        }

        public Task<IReadOnlyList<(AuctionListingEntity Listing, ItemEntity Item)>> LoadListingsAsync() =>
            Task.FromResult<IReadOnlyList<(AuctionListingEntity, ItemEntity)>>(Listings.Values.Select(l => (l, Items[l.ItemId])).ToList());

        public Task<IReadOnlyList<(AuctionKeepingEntity Keeping, ItemEntity Item)>> LoadKeepingsAsync() =>
            Task.FromResult<IReadOnlyList<(AuctionKeepingEntity, ItemEntity)>>(Keepings.Values.Select(k => (k, k.ItemId is { } id ? Items[id] : null)).ToList());

        public Task<RegisterOutcome> RegisterAsync(GoldWrite gold, uint itemHandle, long count, AuctionListingEntity listing,
            Func<ItemEntity, ResultCode> check, Func<long> goldAfterCheck)
        {
            var bag = Bags.GetValueOrDefault(gold.CharacterId) ?? new List<ItemEntity>();
            var item = bag.FirstOrDefault(i => i.Id == itemHandle);
            if (item is null || item.Amount < count) return Task.FromResult(new RegisterOutcome(ResultCode.NotExist));
            var verdict = check(item);
            if (verdict != ResultCode.Success) return Task.FromResult(new RegisterOutcome(verdict));
            ItemEntity auctioned;
            var removed = item.Amount == count;
            if (removed)
            {
                bag.Remove(item);
                item.CharacterId = null;
                auctioned = item;
            }
            else
            {
                item.Amount -= count;
                auctioned = new ItemEntity { Id = _nextId++, ItemResourceId = item.ItemResourceId, Amount = count, SocketItemIds = new long[4] };
                Items[auctioned.Id] = auctioned;
            }

            Gold[gold.CharacterId] = goldAfterCheck();
            listing.Id = _nextId++;
            listing.ItemId = auctioned.Id;
            Listings[listing.Id] = listing;
            return Task.FromResult(new RegisterOutcome(ResultCode.Success, listing, auctioned, removed ? null : item, removed));
        }

        public Task CommitAsync(AuctionChange change)
        {
            if (change.Gold is { } gold) Gold[gold.CharacterId] = gold.Gold;
            foreach (var listing in change.UpdatedListings) Listings[listing.Id] = listing;
            foreach (var id in change.RemovedListings) Listings.Remove(id);
            foreach (var id in change.RemovedKeepings) Keepings.Remove(id);
            foreach (var id in change.DeletedItems) Items.Remove(id);
            foreach (var keeping in change.AddedKeepings)
            {
                keeping.Id = _nextId++;
                Keepings[keeping.Id] = keeping;
            }

            return Task.CompletedTask;
        }

        public Task<TakeOutcome> TakeAsync(GoldWrite gold, AuctionKeepingEntity keeping)
        {
            if (!Keepings.Remove(keeping.Id)) return Task.FromResult(new TakeOutcome(ResultCode.NotExist));
            Gold[gold.CharacterId] = gold.Gold;
            ItemEntity item = null;
            if (keeping.ItemId is { } id)
            {
                item = Items[id];
                item.CharacterId = gold.CharacterId;
                if (!Bags.TryGetValue(gold.CharacterId, out var bag)) Bags[gold.CharacterId] = bag = new List<ItemEntity>();
                bag.Add(item);
            }

            return Task.FromResult(new TakeOutcome(ResultCode.Success, item));
        }
    }

    private sealed class SellCatalog : IItemSellCatalog
    {
        public bool TryGetTemplate(int itemResourceId, out ItemSellTemplate template)
        {
            template = new ItemSellTemplate(1, 1000);
            return itemResourceId == Sword;
        }
    }

    private MemoryStore _store;
    private PlayerVisibilityService _players;
    private AuctionService _service;
    private DateTime _now;
    private uint _tick;

    [SetUp]
    public void SetUp()
    {
        _store = new MemoryStore();
        _players = new PlayerVisibilityService(A.Fake<ILogger<PlayerVisibilityService>>());
        var catalog = new AuctionCatalog(Options.Create(new AuctionCatalogOptions
        {
            Categories =
            {
                new AuctionCategoryRow { CategoryId = 0, SubCategoryId = -1, ItemGroup = 1, ItemClass = -1 },
                new AuctionCategoryRow { CategoryId = 0, SubCategoryId = 0, ItemGroup = 1, ItemClass = 101 },
                new AuctionCategoryRow { CategoryId = 18, SubCategoryId = -1, ItemGroup = 99, ItemClass = -1 }
            },
            Items =
            {
                new AuctionItemRow { Code = Sword, NameId = 7001, Name = "Iron Sword", Group = 1, Class = 101 },
                new AuctionItemRow { Code = Potion, NameId = 7002, Name = "Red Potion", Group = 99, Class = 0 }
            }
        }));
        var sell = new SellCatalog();
        _now = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
        _tick = 1_000_000;
        _service = new AuctionService(_store, catalog, sell, players: _players, utcNow: () => _now, clock: () => _tick,
            runTicks: false);
    }

    private GameClient Player(uint handle, string name, long gold)
    {
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()),
            playerVisibilityService: _players);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = handle;
        info.CharacterName = name;
        info.CharacterLevel = 50;
        info.CharacterGold = gold;
        _players.Registry.Register(handle, client);
        return client;
    }

    private static List<byte[]> Frames(GameClient client, GamePackets id) =>
        ((StorageTestHarness.FrameConnection)client.Connection).Sent
            .Where(frame => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)) == (ushort)id).ToList();

    private static (ushort Request, ushort Result, int Value) LastResult(GameClient client)
    {
        var frame = Frames(client, GamePackets.TM_SC_RESULT).Last();
        return (BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(7)), BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(9)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(11)));
    }

    private async Task<long> Sell(GameClient seller, long start, long instant, byte duration = 1, long count = 1)
    {
        var item = _store.Give(StorageTestHarness.Session(seller).CharacterHandle, Sword, count);
        await _service.RegisterAsync(seller, (uint)item.Id, count, start, instant, duration);
        return _store.Listings.Keys.Max();
    }

    private IEnumerable<AuctionKeepingEntity> KeepingsOf(GameClient client) =>
        _store.Keepings.Values.Where(k => k.OwnerId == StorageTestHarness.Session(client).CharacterHandle);

    [Test]
    public void TheRulesAreTheOfficialRates()
    {
        AuctionRules.RegistrationTax(AuctionRules.Duration(1), 10_000).Should().Be(300);
        AuctionRules.RegistrationTax(AuctionRules.Duration(2), 10_000).Should().Be(400);
        AuctionRules.RegistrationTax(AuctionRules.Duration(3), 10_000).Should().Be(500);
        AuctionRules.Duration(0).Should().Be(TimeSpan.FromHours(6), "anything but mid and long is short");
        AuctionRules.SoldPrice(10_000).Should().Be(9_700);
        AuctionRules.CheckBid(10_000, 10_000, false, 0).Should().Be(ResultCode.Success, "the first bid may equal the start price");
        AuctionRules.CheckBid(10_099, 10_000, true, 0).Should().Be(ResultCode.TooCheap);
        AuctionRules.CheckBid(10_100, 10_000, true, 0).Should().Be(ResultCode.Success);
        AuctionRules.CheckBid(20_001, 10_000, false, 20_000).Should().Be(ResultCode.TooMuchMoney);
        AuctionRules.DurationTypeLeft(TimeSpan.FromHours(30)).Should().Be(3);
        AuctionRules.DurationTypeLeft(TimeSpan.FromHours(7)).Should().Be(2);
        AuctionRules.DurationTypeLeft(TimeSpan.FromHours(1)).Should().Be(1);
    }

    [Test]
    public void TheKeepingListIsThreeThousandEightHundredFiftyNineBytes()
    {
        var info = new ItemFixedInfo(0, 0, 9, 5000, 0, 0, 0, 0, 0, new long[4], 0, 0, 0, 0, 0, 0);
        var packet = GameAuctionPackets.BuildItemKeepingList(1, 1, new[] { new ItemKeepingEntry(9, info, 3600, 32, Sword, 2, 3) });
        packet.Length.Should().Be(3859);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(19)).Should().Be(9);
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(19 + 4 + 16)).Should().Be(5000, "the gold item's count");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(19 + 79)).Should().Be(3600);
        packet[19 + 83].Should().Be(32);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(19 + 84)).Should().Be(Sword);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(19 + 92)).Should().Be(3);
    }

    [Test]
    public async Task RegisteringTakesTheTaxAndTheItem()
    {
        var seller = Player(1, "Ana", 1_000);

        await Sell(seller, 10_000, 20_000);

        LastResult(seller).Should().Be(((ushort)1309, (ushort)ResultCode.Success, Sword));
        StorageTestHarness.Session(seller).CharacterGold.Should().Be(700, "3 % of the start price for six hours");
        _store.Gold[1].Should().Be(700, "written with the auction");
        Frames(seller, GamePackets.TM_SC_DESTROY_ITEM).Should().ContainSingle("the whole stack left the bag");
        _store.Bags[1].Should().BeEmpty();
    }

    [Test]
    public async Task RegisteringPartOfAStackSplitsIt()
    {
        var seller = Player(1, "Ana", 1_000);
        var item = _store.Give(1, Sword, 5);

        await _service.RegisterAsync(seller, (uint)item.Id, 2, 10_000, 0, 1);

        item.Amount.Should().Be(3);
        Frames(seller, GamePackets.TM_SC_UPDATE_ITEM_COUNT).Should().ContainSingle();
        _store.Items[_store.Listings.Values.Single().ItemId].Amount.Should().Be(2);
    }

    [Test]
    public async Task RegisterRefusesInTheOfficialOrder()
    {
        var seller = Player(1, "Ana", 1_000);
        var item = _store.Give(1, Sword, 2);

        await _service.RegisterAsync(seller, (uint)item.Id, 2, 1, 0, 1);
        LastResult(seller).Result.Should().Be((ushort)ResultCode.TooCheap, "below the shop price of the two units");

        await _service.RegisterAsync(seller, (uint)item.Id, 1, 10_000, 9_999, 1);
        LastResult(seller).Result.Should().Be((ushort)ResultCode.TooCheap, "an instant price under the start price");

        item.WearInfo = ItemWearType.Weapon;
        await _service.RegisterAsync(seller, (uint)item.Id, 1, 10_000, 0, 1);
        LastResult(seller).Result.Should().Be((ushort)ResultCode.NotActable, "a worn item");
        item.WearInfo = ItemWearType.None;

        await _service.RegisterAsync(seller, (uint)item.Id, 1, 100_000, 0, 3);
        LastResult(seller).Result.Should().Be((ushort)ResultCode.NotEnoughMoney, "5 000 of tax");
        StorageTestHarness.Session(seller).CharacterGold.Should().Be(1_000, "nothing was taken");
        _store.Listings.Should().BeEmpty();
    }

    [Test]
    public async Task ABidTakesTheGoldAndRefundsTheOvertakenBidderInTheKeepingBox()
    {
        var seller = Player(1, "Ana", 1_000);
        var bo = Player(2, "Bo", 50_000);
        var cy = Player(3, "Cy", 50_000);
        var auction = await Sell(seller, 10_000, 0);

        await _service.BidAsync(seller, (int)auction, 10_000);
        LastResult(seller).Result.Should().Be((ushort)ResultCode.NotEnoughMoney, "the gold in hand is judged first");
        await _service.BidAsync(seller, (int)auction, 500);
        LastResult(seller).Result.Should().Be((ushort)ResultCode.AccessDenied, "one's own auction");

        await _service.BidAsync(bo, (int)auction, 10_000);
        LastResult(bo).Should().Be(((ushort)1306, (ushort)ResultCode.Success, Sword));
        StorageTestHarness.Session(bo).CharacterGold.Should().Be(40_000);

        await _service.BidAsync(bo, (int)auction, 20_000);
        LastResult(bo).Result.Should().Be((ushort)ResultCode.AccessDenied, "already the highest bidder");

        await _service.BidAsync(cy, (int)auction, 10_099);
        LastResult(cy).Result.Should().Be((ushort)ResultCode.TooCheap);
        await _service.BidAsync(cy, (int)auction, 10_100);
        LastResult(cy).Result.Should().Be((ushort)ResultCode.Success);

        var refund = KeepingsOf(bo).Single();
        refund.Gold.Should().Be(10_000);
        refund.KeepingType.Should().Be((int)StorageType.GoldByHigherBid);
        Frames(bo, GamePackets.TM_SC_CHAT).Should().ContainSingle("the @713 notice");
        _store.Listings[auction].BidderIds.Should().Equal(2L);
    }

    [Test]
    public async Task TheBiddedListShowsWhoLeads()
    {
        var seller = Player(1, "Ana", 1_000);
        var bo = Player(2, "Bo", 50_000);
        var cy = Player(3, "Cy", 50_000);
        var auction = await Sell(seller, 10_000, 0);
        await _service.BidAsync(bo, (int)auction, 10_000);
        await _service.BidAsync(cy, (int)auction, 10_100);

        await _service.BiddedListAsync(bo, 1);

        var list = Frames(bo, GamePackets.TM_SC_AUCTION_BIDDED_LIST).Single();
        BinaryPrimitives.ReadInt32LittleEndian(list.AsSpan(15)).Should().Be(1);
        list[19 + 96].Should().Be(AuctionRules.StatusOthers);
    }

    [Test]
    public async Task AnInstantPurchasePaysTheSellerThroughTheKeepingBox()
    {
        var seller = Player(1, "Ana", 1_000);
        var bo = Player(2, "Bo", 50_000);
        var cy = Player(3, "Cy", 50_000);
        var auction = await Sell(seller, 10_000, 20_000);
        await _service.BidAsync(bo, (int)auction, 12_000);

        await _service.InstantPurchaseAsync(cy, (int)auction);

        LastResult(cy).Should().Be(((ushort)1308, (ushort)ResultCode.Success, Sword));
        StorageTestHarness.Session(cy).CharacterGold.Should().Be(30_000);
        KeepingsOf(cy).Single().KeepingType.Should().Be((int)StorageType.ItemByInstantPurchase);
        KeepingsOf(bo).Single().Should().Match<AuctionKeepingEntity>(k => k.Gold == 12_000
            && k.KeepingType == (int)StorageType.GoldByItemSoldOut);
        KeepingsOf(seller).Select(k => (k.KeepingType, k.Gold)).Should().BeEquivalentTo(new[]
        {
            ((int)StorageType.GoldByItemSell, 19_400L), ((int)StorageType.GoldByRegTax, 300L)
        });
        _store.Listings.Should().BeEmpty();
    }

    [Test]
    public async Task TheHighestBidderPaysOnlyTheDifference()
    {
        var seller = Player(1, "Ana", 1_000);
        var bo = Player(2, "Bo", 50_000);
        var auction = await Sell(seller, 10_000, 20_000);
        await _service.BidAsync(bo, (int)auction, 12_000);

        await _service.InstantPurchaseAsync(bo, (int)auction);

        StorageTestHarness.Session(bo).CharacterGold.Should().Be(30_000, "12 000 for the bid and 8 000 for the rest");
    }

    [Test]
    public async Task OnlyTheSellerCancelsAndOnlyWithoutABidder()
    {
        var seller = Player(1, "Ana", 1_000);
        var bo = Player(2, "Bo", 50_000);
        var first = await Sell(seller, 10_000, 0);
        var second = await Sell(seller, 10_000, 0);
        await _service.BidAsync(bo, (int)second, 10_000);

        await _service.CancelAsync(bo, (int)first);
        LastResult(bo).Result.Should().Be((ushort)ResultCode.AccessDenied);
        await _service.CancelAsync(seller, (int)second);
        LastResult(seller).Result.Should().Be((ushort)ResultCode.NotActable);

        await _service.CancelAsync(seller, (int)first);
        LastResult(seller).Result.Should().Be((ushort)ResultCode.Success);
        KeepingsOf(seller).Single().KeepingType.Should().Be((int)StorageType.ItemByCancel);
    }

    [Test]
    public async Task AnEndedAuctionGoesToItsBidderOrBackToItsSeller()
    {
        var seller = Player(1, "Ana", 1_000);
        var bo = Player(2, "Bo", 50_000);
        var sold = await Sell(seller, 10_000, 0);
        await Sell(seller, 10_000, 0);
        await _service.BidAsync(bo, (int)sold, 15_000);

        _now = _now.AddHours(6).AddSeconds(1);
        await _service.ProcessAsync();

        _store.Listings.Should().BeEmpty();
        KeepingsOf(bo).Single().KeepingType.Should().Be((int)StorageType.ItemBySuccessfulBid);
        KeepingsOf(seller).Select(k => (k.KeepingType, k.Gold)).Should().BeEquivalentTo(new[]
        {
            ((int)StorageType.GoldByItemSell, 14_550L), ((int)StorageType.GoldByRegTax, 300L),
            ((int)StorageType.ItemByExpiration, 0L), ((int)StorageType.GoldByRegTax, 300L)
        });
        Frames(bo, GamePackets.TM_SC_CHAT).Should().ContainSingle("the @714 notice");
    }

    [Test]
    public async Task TakingFromTheKeepingBoxCreditsGoldOrGivesTheItem()
    {
        var seller = Player(1, "Ana", 1_000);
        var auction = await Sell(seller, 10_000, 0);
        await _service.CancelAsync(seller, (int)auction);
        var bo = Player(2, "Bo", 0);

        var entry = KeepingsOf(seller).Single();
        _tick += 1000;
        await _service.TakeAsync(bo, (int)entry.Id);
        LastResult(bo).Result.Should().Be((ushort)ResultCode.AccessDenied);

        await _service.TakeAsync(seller, (int)entry.Id);
        LastResult(seller).Result.Should().Be((ushort)ResultCode.Success);
        Frames(seller, GamePackets.TM_SC_INVENTORY).Should().NotBeEmpty();
        _store.Bags[1].Should().ContainSingle(i => i.ItemResourceId == Sword);

    }

    [Test]
    public async Task ARefundIsTakenUnderTheGoldCeiling()
    {
        var seller = Player(1, "Ana", 1_000);
        var bo = Player(2, "Bo", 50_000);
        var cy = Player(3, "Cy", 50_000);
        var auction = await Sell(seller, 10_000, 0);
        await _service.BidAsync(bo, (int)auction, 10_000);
        await _service.BidAsync(cy, (int)auction, 10_100);
        var refund = KeepingsOf(bo).Single();

        StorageTestHarness.Session(bo).CharacterGold = GoldRules.MaxCarried;
        await _service.TakeAsync(bo, (int)refund.Id);
        LastResult(bo).Result.Should().Be((ushort)ResultCode.TooMuchMoney);

        StorageTestHarness.Session(bo).CharacterGold = 0;
        await _service.TakeAsync(bo, (int)refund.Id);
        LastResult(bo).Result.Should().Be((ushort)ResultCode.Success);
        StorageTestHarness.Session(bo).CharacterGold.Should().Be(10_000);
        _store.Gold[2].Should().Be(10_000);
        KeepingsOf(bo).Should().BeEmpty();
    }

    [Test]
    public async Task TheSearchFiltersByCategoryAndKeywordAndWaitsThreeSeconds()
    {
        var seller = Player(1, "Ana", 100_000);
        await Sell(seller, 10_000, 0);
        var potion = _store.Give(1, Potion, 3);
        await _service.RegisterAsync(seller, (uint)potion.Id, 3, 100, 0, 1);
        var bo = Player(2, "Bo", 0);

        await _service.SearchAsync(bo, new AuctionSearchRequest(0, 0, "iron", 1, false));
        var search = Frames(bo, GamePackets.TM_SC_AUCTION_SEARCH).Single();
        BinaryPrimitives.ReadInt32LittleEndian(search.AsSpan(15)).Should().Be(1);
        search[19 + 96 + 31].Should().Be(AuctionRules.FlagNoOtherBidder);

        await _service.SearchAsync(bo, new AuctionSearchRequest(-1, -1, "", 1, false));
        LastResult(bo).Result.Should().Be((ushort)ResultCode.CoolTime, "three seconds between searches");

        _tick += 300;
        await _service.SearchAsync(bo, new AuctionSearchRequest(-1, 0, "", 1, false));
        var etc = Frames(bo, GamePackets.TM_SC_AUCTION_SEARCH).Last();
        BinaryPrimitives.ReadInt32LittleEndian(etc.AsSpan(15)).Should().Be(1, "the potion is in the etc category");
    }

    private AuctionService AutomaticService(params AutoAuctionRow[] rows)
    {
        var catalog = new AuctionCatalog(Options.Create(new AuctionCatalogOptions
        {
            AutomaticAuctions = rows.ToList(),
            Categories = { new AuctionCategoryRow { CategoryId = 0, SubCategoryId = -1, ItemGroup = 1, ItemClass = -1 } },
            Items = { new AuctionItemRow { Code = Sword, Name = "Iron Sword", Group = 1, Class = 101 } }
        }));
        return new AuctionService(_store, catalog, new SellCatalog(), players: _players, utcNow: () => _now,
            clock: () => _tick, runTicks: false);
    }

    private AutoAuctionRow AutomaticRow(int id = 1, bool repeat = true, bool premium = false) => new()
    {
        Id = id, ItemCode = Sword, SellerName = "Auctioneer", Price = 10_000, EnrollmentTime = _now,
        Repeat = repeat, RepeatDays = 7, DurationType = 3, SecrouteOnly = premium
    };

    [Test]
    public async Task AutomaticAuctionsRespectCalendarAndRestartWithoutDuplicatesOrBacklog()
    {
        var row = AutomaticRow();
        row.EnrollmentTime = _now.AddHours(1);
        var service = AutomaticService(row);
        await service.ProcessAsync();
        _store.Listings.Should().BeEmpty();
        _now = _now.AddHours(1);
        await service.ProcessAsync();
        var first = _store.Listings.Values.Single();
        first.Should().Match<AuctionListingEntity>(l => l.SellerId == 0 && l.RegistrationTax == 0
            && l.InstantPurchasePrice == 0 && l.HighestBiddingPrice == 10000 && l.AutoAuctionResourceId == 1);
        first.EndTime.Should().Be(_now.AddHours(72));
        _store.Items[first.ItemId].GenerateBySource.Should().Be(ItemGenerateSource.Auction);
        service = AutomaticService(row);
        await service.ProcessAsync();
        _store.Listings.Should().ContainSingle();
        _now = _now.AddDays(3);
        await service.ProcessAsync();
        _store.Listings.Should().BeEmpty();
        _store.Items.Should().BeEmpty("unsold server items are destroyed");
        _store.Keepings.Should().BeEmpty("there is no player with id zero");
        _now = _now.AddDays(100);
        await service.ProcessAsync();
        await AutomaticService(row).ProcessAsync();
        _store.Listings.Should().ContainSingle("missed weeks do not flood the auction house");
        _store.Registrations[1].Should().Be(_now);
    }

    [Test]
    public async Task AFailedAutomaticRegistrationHoldsUpNeitherTheOthersNorTheExpirations()
    {
        _store.FailingAutomatic.Add(1);
        var service = AutomaticService(AutomaticRow(1), AutomaticRow(2));
        await service.ProcessAsync();
        _store.Listings.Values.Should().ContainSingle(l => l.AutoAuctionResourceId == 2);

        // The listing of resource 2 still expires, unsold, while resource 1 keeps failing.
        _now = _now.AddHours(72);
        await service.ProcessAsync();
        _store.Listings.Values.Should().NotContain(l => l.AutoAuctionResourceId == 2 && l.EndTime <= _now);

        _store.FailingAutomatic.Clear();
        await service.ProcessAsync();
        _store.Listings.Values.Should().Contain(l => l.AutoAuctionResourceId == 1, "the resource registers once the database answers");
    }

    [Test]
    public async Task AOneOffAuctionIsNotRecreatedAfterItIsWonAndTaken()
    {
        var row = AutomaticRow(repeat: false);
        var service = AutomaticService(row);
        await service.ProcessAsync();
        var listing = _store.Listings.Values.Single();
        var buyer = Player(2, "Bo", 50_000);
        await service.BidAsync(buyer, (int)listing.Id, 10_000);
        LastResult(buyer).Result.Should().Be((ushort)ResultCode.Success);
        _now = _now.AddHours(72);
        await service.ProcessAsync();
        var keeping = _store.Keepings.Values.Single();
        keeping.OwnerId.Should().Be(2);
        keeping.KeepingType.Should().Be((int)StorageType.ItemBySuccessfulBid);
        await service.TakeAsync(buyer, (int)keeping.Id);
        _store.Bags[2].Should().ContainSingle();
        await AutomaticService(row).ProcessAsync();
        _store.Listings.Should().BeEmpty();
    }

    [Test]
    public async Task RegionalUnsupportedAndInvalidRepeatRowsAreNotPublished()
    {
        var regional = AutomaticRow(1); regional.LocalFlag = 1;
        var unsupported = AutomaticRow(2); unsupported.ItemCode = 2016027;
        var invalid = AutomaticRow(3); invalid.RepeatDays = 0;
        await AutomaticService(regional, unsupported, invalid).ProcessAsync();
        _store.Listings.Should().BeEmpty();
        _store.Registrations.Should().BeEmpty();
    }

    [Test]
    public async Task ReservedAuctionsRequireAnUnexpiredHiddenVillagePassEvenWithAKnownId()
    {
        var service = AutomaticService(AutomaticRow(premium: true));
        await service.ProcessAsync();
        var id = (int)_store.Listings.Keys.Single();
        var buyer = Player(2, "Bo", 50_000);
        await service.SearchAsync(buyer, new AuctionSearchRequest(-1, -1, "", 1, false));
        BinaryPrimitives.ReadInt32LittleEndian(Frames(buyer, GamePackets.TM_SC_AUCTION_SEARCH).Last().AsSpan(15)).Should().Be(0);
        _tick += 300;
        await service.BidAsync(buyer, id, 10_000);
        LastResult(buyer).Result.Should().Be((ushort)ResultCode.NotExist);
        await service.InstantPurchaseAsync(buyer, id);
        LastResult(buyer).Result.Should().Be((ushort)ResultCode.NotExist);
        StorageTestHarness.Session(buyer).ActiveBuffs.Add(new Navislamia.Game.Services.Buffs.ActiveBuff(1, 9004, 0, 1, _tick, _tick + 1000));
        await service.SearchAsync(buyer, new AuctionSearchRequest(-1, -1, "", 1, false));
        BinaryPrimitives.ReadInt32LittleEndian(Frames(buyer, GamePackets.TM_SC_AUCTION_SEARCH).Last().AsSpan(15)).Should().Be(1);
        _tick += 300;
        await service.InstantPurchaseAsync(buyer, id);
        LastResult(buyer).Result.Should().Be((ushort)ResultCode.NotActable, "automatic auctions have no instant price");
        await service.BidAsync(buyer, id, 10_000);
        LastResult(buyer).Result.Should().Be((ushort)ResultCode.Success);
        _tick += 1000;
        await service.BiddedListAsync(buyer, 1);
        LastResult(buyer).Result.Should().Be((ushort)ResultCode.NotExist, "an expired pass also hides the bid list");
    }

    [Test]
    public void TheResourceTimeUsesTheConfiguredServerZone()
    {
        var row = AutomaticRow(); row.EnrollmentTime = new DateTime(2015, 11, 4, 22, 30, 0);
        var catalog = new AuctionCatalog(Options.Create(new AuctionCatalogOptions { AutomaticAuctions = { row }, TimeZone = "Europe/Paris" }));
        catalog.AutomaticAuctions.Single().EnrollmentTime.Should().Be(new DateTime(2015, 11, 4, 21, 30, 0, DateTimeKind.Utc));
    }

    [Test]
    public void OnlyTheDefinitionsOfItemsTheClientKnowsAreImported_AndOnlyCompatibleRegionalItemsCanBePublished()
    {
        var root = new System.IO.DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root is not null && !System.IO.File.Exists(System.IO.Path.Combine(root.FullName, "DevConsole", "auction-catalog.73.json")))
            root = root.Parent;
        root.Should().NotBeNull();
        using var json = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(root!.FullName, "DevConsole", "auction-catalog.73.json")));
        var options = System.Text.Json.JsonSerializer.Deserialize<AuctionCatalogOptions>(json.RootElement.GetProperty("AuctionCatalog").GetRawText())!;
        // 39 rows in AutoAuctionResource; the 35 whose item db_item.rdb lacks are left out (filtre-ressources-73.md).
        options.AutomaticAuctions.Select(r => r.Id).Should().Equal(1, 14, 15, 39);
        var catalog = new AuctionCatalog(Options.Create(options));
        catalog.AutomaticAuctions.Where(r => catalog.TryGetItem(r.ItemCode, out _)).Select(r => r.Id).Should().Equal(14, 15, 39);
    }
}
