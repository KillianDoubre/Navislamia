using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Services;
using Npgsql;

namespace Tests.Game;

[TestFixture]
public class GuildPostgreSqlTests
{
    [Test, Explicit("Migrates and exercises guild foreign keys and transactions in an isolated PostgreSQL schema.")]
    public async Task PostgreSql_guild_creation_alliance_taxes_and_restart_use_real_constraints()
    {
        var connectionString = Environment.GetEnvironmentVariable("NAVIS_GUILD_TEST_CONNECTION")
            ?? throw new InvalidOperationException("Set NAVIS_GUILD_TEST_CONNECTION for an isolated schema test.");
        var schema = "guild_test_" + Guid.NewGuid().ToString("N");
        var settings = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema, Pooling = false };
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection)) await create.ExecuteNonQueryAsync();
        try
        {
            var options = new DbContextOptionsBuilder<TelecasterContext>().UseNpgsql(settings.ConnectionString,
                o => o.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options;
            await using (var db = new TelecasterContext(options)) await db.Database.MigrateAsync();
            var h = new GuildTests.Harness(options);
            var one = await h.Player(1); var two = await h.Player(2); var three = await h.Player(3);
            (await h.Create(one, "First")).Should().BeTrue();
            (await h.Create(two, "Second")).Should().BeTrue();
            (await h.Create(three, "Third")).Should().BeTrue();
            (await h.Create(one, "RealAlliance", true)).Should().BeTrue();
            await h.JoinAlliance(one, two); await h.JoinAlliance(one, three);
            await using (var db = h.Db())
            {
                db.Dungeons.Add(new DungeonEntity { Id = 130000, OwnerGuildId = StorageTestHarness.Session(one).GuildId, TaxRate = 10 });
                await db.SaveChangesAsync();
                (await db.Guilds.CountAsync(g => g.AllianceId != null)).Should().Be(3);
            }
            var definition = h.Catalog.Dungeons[130000];
            var monster = new MonsterInstance(999, 12, definition.X, definition.Y, 0, 50, 100, 1, 0, false, 100, 100, 100, 1, 1, 0, 0);
            var reward = await h.Guilds.OnMonsterKilledAsync(two, monster, 999, new MonsterKillReward(1000, 100, 101, 109));
            reward.Gold.Should().Be(91); reward.Chaos.Should().Be(99);
            (await h.Guilds.ExecuteCommandAsync(one, "/gwithdraw gold")).Should().BeTrue();
            h.Guilds = h.Service(); await h.Guilds.OnWorldEntryAsync(two);
            await using (var db = h.Db())
            {
                (await db.Guilds.SingleAsync(g => g.Name == "First")).Gold.Should().Be(0);
                (await db.Characters.FindAsync(1L))!.Gold.Should().Be(400010);
                (await db.Guilds.SingleAsync(g => g.Name == "First")).Chaos.Should().Be(10);
                StorageTestHarness.Session(two).AllianceId.Should().Be((await db.Alliances.SingleAsync()).Id);
            }
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
