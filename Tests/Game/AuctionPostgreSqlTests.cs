using FakeItEasy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Auction;
using Navislamia.Game.Services.Interfaces;
using Npgsql;

namespace Tests.Game;

[TestFixture]
public class AuctionPostgreSqlTests
{
    private const int Sword = 101100;
    private DbContextOptions<TelecasterContext> _options = null!;
    private NpgsqlConnection _connection = null!;
    private string _schema = "";
    private DateTime _now;
    private readonly CharacterGate _gate = new();
    private TelecasterContext Db() => new(_options);
    private AuctionStore Store() => new(_options, _gate);

    [SetUp]
    public async Task CreateIsolatedSchema()
    {
        var connection = Environment.GetEnvironmentVariable("NAVIS_AUCTION_TEST_CONNECTION")
            ?? throw new InvalidOperationException("Set NAVIS_AUCTION_TEST_CONNECTION to run the PostgreSQL auction test.");
        _schema = "auction_test_" + Guid.NewGuid().ToString("N");
        var settings = new NpgsqlConnectionStringBuilder(connection) { SearchPath = _schema, Pooling = false };
        _connection = new NpgsqlConnection(settings.ConnectionString);
        await _connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{_schema}\"", _connection)) await create.ExecuteNonQueryAsync();
        _options = new DbContextOptionsBuilder<TelecasterContext>().UseNpgsql(settings.ConnectionString,
            o => o.MigrationsHistoryTable("__EFMigrationsHistory", _schema)).Options;
        await using var db = Db();
        await db.Database.MigrateAsync();
        _now = DateTime.UtcNow;
    }

    [TearDown]
    public async Task RemoveIsolatedSchema()
    {
        if (_connection is null || _connection.State != System.Data.ConnectionState.Open) return;
        try
        {
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA \"{_schema}\" CASCADE", _connection);
            await cleanup.ExecuteNonQueryAsync();
        }
        finally { await _connection.DisposeAsync(); }
    }

    private async Task<GameClient> Player(uint id)
    {
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = id; info.CharacterName = "AuctionP" + id; info.CharacterGold = 100_000;
        var row = new CharacterEntity { Id = id, CharacterName = info.CharacterName, Lv = 50, Gold = info.CharacterGold };
        foreach (var property in typeof(CharacterEntity).GetProperties().Where(p => p.CanWrite))
        {
            if (property.PropertyType == typeof(string) && property.GetValue(row) is null) property.SetValue(row, "");
            if (property.PropertyType.IsArray && property.GetValue(row) is null)
                property.SetValue(row, Array.CreateInstance(property.PropertyType.GetElementType()!, 0));
        }
        await using var db = Db(); db.Characters.Add(row); await db.SaveChangesAsync();
        return client;
    }

    private AuctionService Service(params AutoAuctionRow[] rows)
    {
        var catalog = new AuctionCatalog(Options.Create(new AuctionCatalogOptions
        {
            AutomaticAuctions = rows.ToList(),
            Categories = { new AuctionCategoryRow { CategoryId = 0, SubCategoryId = -1, ItemGroup = 1, ItemClass = -1 } },
            Items = { new AuctionItemRow { Code = Sword, Name = "Sword", Group = 1, Class = 101 } }
        }));
        var sell = A.Fake<IItemSellCatalog>();
        ItemSellTemplate template = new(1, 100);
        A.CallTo(() => sell.TryGetTemplate(Sword, out template)).Returns(true).AssignsOutAndRefParameters(template);
        return new AuctionService(Store(), catalog, sell, utcNow: () => _now, clock: () => 1_000_000, runTicks: false);
    }

    private ItemEntity Item(long? owner = null, long count = 1) => new()
    {
        CharacterId = owner, ItemResourceId = Sword, Amount = count, Level = 1, SocketItemIds = new long[4], WearInfo = ItemWearType.None
    };

    [Test, Explicit("Exercises real PostgreSQL migrations, escrow, refunds, keeping withdrawals and rollback in an isolated schema.")]
    public async Task PlayerAuctionsSurviveReloadAndTransferItemsAndGoldAtomically()
    {
        var seller = await Player(1); var bidder = await Player(2); var buyer = await Player(3);
        var item = Item(1, 5);
        await using (var db = Db()) { db.Items.Add(item); await db.SaveChangesAsync(); }
        var service = Service();
        await service.RegisterAsync(seller, (uint)item.Id, 2, 1_000, 2_000, 1);
        AuctionListingEntity listing;
        await using (var db = Db())
        {
            listing = await db.AuctionListings.SingleAsync();
            (await db.Items.SingleAsync(i => i.Id == item.Id)).Amount.Should().Be(3);
            var escrow = await db.Items.SingleAsync(i => i.Id == listing.ItemId);
            escrow.CharacterId.Should().BeNull(); escrow.AccountId.Should().BeNull(); escrow.Amount.Should().Be(2);
            (await db.Characters.SingleAsync(c => c.Id == 1)).Gold.Should().Be(99_970);
        }
        await service.BidAsync(bidder, (int)listing.Id, 1_000);
        await service.BidAsync(buyer, (int)listing.Id, 1_010);
        service = Service(); // Recreate all in-memory indexes from PostgreSQL.
        await service.InstantPurchaseAsync(buyer, (int)listing.Id);
        AuctionKeepingEntity refund, won;
        List<AuctionKeepingEntity> payments;
        await using (var db = Db())
        {
            (await db.AuctionListings.CountAsync()).Should().Be(0);
            (await db.Characters.SingleAsync(c => c.Id == 3)).Gold.Should().Be(98_000);
            refund = await db.AuctionKeepings.SingleAsync(k => k.OwnerId == 2);
            refund.Gold.Should().Be(1_000);
            won = await db.AuctionKeepings.SingleAsync(k => k.OwnerId == 3);
            won.ItemId.Should().Be(listing.ItemId);
            payments = await db.AuctionKeepings.Where(k => k.OwnerId == 1).ToListAsync();
            payments.Select(k => k.Gold).Should().BeEquivalentTo(new long[] { 1_940, 30 });
        }
        (await Store().TakeAsync(new GoldWrite("AuctionP3", 3, 100_000), refund)).Result.Should().Be(ResultCode.AccessDenied);
        await service.TakeAsync(bidder, (int)refund.Id);
        await service.TakeAsync(buyer, (int)won.Id);
        foreach (var payment in payments) await service.TakeAsync(seller, (int)payment.Id);
        (await Store().TakeAsync(new GoldWrite("AuctionP2", 2, 100_000), refund)).Result.Should().Be(ResultCode.NotExist);
        await using (var db = Db())
        {
            (await db.Characters.SingleAsync(c => c.Id == 1)).Gold.Should().Be(101_940);
            (await db.Characters.SingleAsync(c => c.Id == 2)).Gold.Should().Be(100_000);
            (await db.Items.SingleAsync(i => i.Id == listing.ItemId)).CharacterId.Should().Be(3);
            (await db.AuctionKeepings.CountAsync()).Should().Be(0);
        }

        // The item split and fee save succeed before the deliberately invalid listing timestamp fails.
        var invalid = new AuctionListingEntity { SellerId = 1, SellerName = "AuctionP1", EndTime = DateTime.SpecifyKind(_now, DateTimeKind.Unspecified) };
        Func<Task> register = () => Store().RegisterAsync(new GoldWrite("AuctionP1", 1, 101_940), (uint)item.Id, 1,
            invalid, _ => ResultCode.Success, () => 101_910);
        await register.Should().ThrowAsync<DbUpdateException>();
        await using (var db = Db())
        {
            (await db.Items.CountAsync()).Should().Be(2, "a failed registration must not leave a split item behind");
            (await db.Items.SingleAsync(i => i.Id == item.Id)).Amount.Should().Be(3);
            (await db.Characters.SingleAsync(c => c.Id == 1)).Gold.Should().Be(101_940);
        }

        // An unsold player auction returns to keeping; after 15 days its unclaimed item is deleted.
        await service.RegisterAsync(seller, (uint)item.Id, 1, 500, 0, 1);
        _now = _now.AddHours(6);
        await service.ProcessAsync();
        (await Store().LoadKeepingsAsync()).Should().HaveCount(2);
        _now = _now.AddDays(15);
        await Service().ProcessAsync();
        (await Store().LoadKeepingsAsync()).Should().BeEmpty();
        await using (var db = Db())
        {
            (await db.Items.CountAsync()).Should().Be(2);
            (await db.Items.SingleAsync(i => i.Id == item.Id)).Amount.Should().Be(2);
        }
    }

    [Test, Explicit("Exercises automatic auction history, concurrent registration, reload, expiration and rollback on PostgreSQL.")]
    public async Task AutomaticRegistrationSurvivesRestartAndConcurrentTicksWithoutDuplicates()
    {
        var row = new AutoAuctionRow { Id = 10, ItemCode = Sword, Price = 1_000, EnrollmentTime = _now,
            Repeat = true, RepeatDays = 7, DurationType = 1 };
        await Service(row).ProcessAsync();
        await Service(row).ProcessAsync();
        await using (var db = Db())
        {
            (await db.AuctionListings.CountAsync()).Should().Be(1);
            (await db.AutoAuctionRegistrations.SingleAsync()).ResourceId.Should().Be(10);
        }
        var buyer = await Player(1);
        var service = Service(row);
        var loaded = (await Store().LoadListingsAsync()).Single();
        await service.BidAsync(buyer, (int)loaded.Listing.Id, 1_000);
        _now = _now.AddHours(6);
        await service.ProcessAsync();
        service = Service(row);
        var keeping = (await Store().LoadKeepingsAsync()).Single();
        keeping.Keeping.OwnerId.Should().Be(1);
        await service.TakeAsync(buyer, (int)keeping.Keeping.Id);
        await service.ProcessAsync();
        (await Store().LoadListingsAsync()).Should().BeEmpty();
        _now = _now.AddDays(100);
        await Service(row).ProcessAsync();
        await Service(row).ProcessAsync();
        (await Store().LoadListingsAsync()).Should().ContainSingle();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Store().RegisterAutomaticAsync(99, null, _now,
            new AuctionListingEntity { AutoAuctionResourceId = 99, SellerName = "Auctioneer", EndTime = _now.AddHours(6) }, Item())));
        outcomes.Select(o => o.Result).Should().BeEquivalentTo(new[] { ResultCode.Success, ResultCode.AlreadyExist });
        var invalid = new AuctionListingEntity { AutoAuctionResourceId = 90, EndTime = DateTime.SpecifyKind(_now, DateTimeKind.Unspecified) };
        Func<Task> register = () => Store().RegisterAutomaticAsync(90, null, _now, invalid, Item());
        await register.Should().ThrowAsync<DbUpdateException>();
        (await Store().LoadAutomaticRegistrationsAsync()).Keys.Should().BeEquivalentTo(new[] { 10, 99 });
        _now = _now.AddHours(6);
        await Service(row).ProcessAsync();
        (await Store().LoadListingsAsync()).Should().BeEmpty();
        (await Store().LoadKeepingsAsync()).Should().BeEmpty("server-owned unsold items have no owner keeping");
        await using (var db = Db())
            (await db.Items.CountAsync()).Should().Be(1, "only the buyer's already withdrawn item remains");
    }
}
