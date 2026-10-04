using FakeItEasy;
using System.Buffers.Binary;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Npgsql;

namespace Tests.Game;

[TestFixture]
public class CommunityPostgreSqlTests
{
    [Test, Explicit("Guild/ranking atomic writes and event resource migration in isolated PostgreSQL schemas.")]
    public async Task PostgreSql_donation_transaction_rolls_back_and_event_seed_round_trips()
    {
        var connectionString = Environment.GetEnvironmentVariable("NAVIS_GUILD_TEST_CONNECTION")
            ?? throw new InvalidOperationException("Set NAVIS_GUILD_TEST_CONNECTION for an isolated schema test.");
        var schema = "community_" + Guid.NewGuid().ToString("N"); var arcadia = schema + "_arcadia";
        var settings = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema, Pooling = false };
        await using var connection = new NpgsqlConnection(settings.ConnectionString); await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"; CREATE SCHEMA \"{arcadia}\"", connection)) await create.ExecuteNonQueryAsync();
        try
        {
            var options = new DbContextOptionsBuilder<TelecasterContext>().UseNpgsql(settings.ConnectionString,
                o => o.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options;
            await using (var db = new TelecasterContext(options)) await db.Database.MigrateAsync();
            var h = new GuildTests.Harness(options); var client = await h.Player(1); await h.Create(client, "Donors");
            var store = new DonationStore(options, h.Gate, A.Fake<IItemSellCatalog>(), h.Time);
            (await store.DonateAsync(client, new GameActionPackets.DonateItemRequest(100000, 0, null))).Should()
                .Be(Navislamia.Game.Network.Packets.ResultCode.Success);
            await using (var db = new TelecasterContext(options))
            {
                (await db.Guilds.SingleAsync()).DonationPoint.Should().Be(10);
                (await db.DonationScores.SingleAsync()).Score.Should().Be(10);
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"DonationScores\" ADD CONSTRAINT test_score_limit CHECK (\"Score\" <= 20)");
            }
            var before = StorageTestHarness.Session(client).CharacterGold;
            await FluentActions.Awaiting(() => store.DonateAsync(client, new GameActionPackets.DonateItemRequest(300000, 0, null)))
                .Should().ThrowAsync<DbUpdateException>();
            StorageTestHarness.Session(client).CharacterGold.Should().Be(before);
            await using (var db = new TelecasterContext(options))
            {
                (await db.Guilds.SingleAsync()).DonationPoint.Should().Be(10);
                (await db.DonationScores.SingleAsync()).Score.Should().Be(10);
                (await db.Characters.SingleAsync()).Gold.Should().Be(before);
            }
            var ranking = await new DonationRankingService(options, h.Time).GetAsync(1, 0);
            BinaryPrimitives.ReadUInt16LittleEndian(ranking.AsSpan(8)).Should().Be(1);
            BinaryPrimitives.ReadInt64LittleEndian(ranking.AsSpan(10)).Should().Be(100_000);
            BinaryPrimitives.ReadUInt16LittleEndian(ranking.AsSpan(18)).Should().Be(0, "the top starts at 100 points");
            var peer = await h.Player(2); await h.Join(client, peer);
            await Task.WhenAll(store.DonateAsync(client, new GameActionPackets.DonateItemRequest(100000, 0, null)),
                store.DonateAsync(peer, new GameActionPackets.DonateItemRequest(100000, 0, null)));
            await using (var db = new TelecasterContext(options))
            { (await db.Guilds.SingleAsync()).DonationPoint.Should().Be(30, "concurrent guild donors lose no update"); }
            var resourcesOptions = new DbContextOptionsBuilder<ArcadiaContext>()
                .UseNpgsql(new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = arcadia, Pooling = false }.ConnectionString,
                    o => o.MigrationsHistoryTable("__EFMigrationsHistory", arcadia)).Options;
            await using (var db = new ArcadiaContext(resourcesOptions))
            {
                await db.Database.MigrateAsync();
                (await db.EventAreaResources.CountAsync()).Should().Be(187);
                var area = await db.EventAreaResources.SingleAsync(r => r.Id == 10101);
                area.Conditions.Should().HaveCount(6); area.Values.Should().HaveCount(12);
                area.Conditions[0].Should().Be(2); area.Values.Take(2).Should().Equal(3217, 1);
                area.EnterHandler.Should().Be("mainquest2_region_espoir_level_10101()");
            }
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA \"{schema}\", \"{arcadia}\" CASCADE", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
