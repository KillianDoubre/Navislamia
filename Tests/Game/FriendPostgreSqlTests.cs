using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Services.Friends;
using Npgsql;

namespace Tests.Game;

[TestFixture]
public class FriendPostgreSqlTests
{
    [Test, Explicit("Version0023_CharacterFriends and the friend store queries in an isolated PostgreSQL schema.")]
    public async Task PostgreSql_friend_lists_round_trip_and_skip_deleted_characters()
    {
        var connectionString = Environment.GetEnvironmentVariable("NAVIS_FRIEND_TEST_CONNECTION")
            ?? throw new InvalidOperationException("Set NAVIS_FRIEND_TEST_CONNECTION for an isolated schema test.");
        var schema = "friends_" + Guid.NewGuid().ToString("N");
        var settings = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema, Pooling = false };
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection)) await create.ExecuteNonQueryAsync();
        try
        {
            var options = new DbContextOptionsBuilder<TelecasterContext>().UseNpgsql(settings.ConnectionString,
                o => o.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options;
            long alice, bobby, carol;
            await using (var db = new TelecasterContext(options))
            {
                await db.Database.MigrateAsync();
                var characters = new[] { "Alice", "Bobby", "Carol" }
                    .Select(name => new CharacterEntity { CharacterName = name, AccountName = "test" }).ToArray();
                db.Characters.AddRange(characters);
                await db.SaveChangesAsync();
                (alice, bobby, carol) = (characters[0].Id, characters[1].Id, characters[2].Id);
            }

            var store = new FriendStore(options);
            (await store.FindCharacterAsync("bOBBY")).Should().Be(new FriendEntry(bobby, "Bobby"));
            (await store.FindCharacterAsync("Nobody")).Should().BeNull();

            await store.AddAsync(alice, bobby, false);
            await store.AddAsync(alice, carol, false);
            await store.AddAsync(alice, carol, true);
            var lists = await store.LoadAsync(alice);
            lists.Friends.Should().Equal(new FriendEntry(bobby, "Bobby"), new FriendEntry(carol, "Carol"));
            lists.Denials.Should().Equal(new FriendEntry(carol, "Carol"));

            await FluentActions.Awaiting(() => store.AddAsync(alice, bobby, false)).Should()
                .ThrowAsync<DbUpdateException>("a pair is listed once");

            await store.RemoveAsync(alice, carol, true);
            await store.AddAsync(alice, carol, true);
            (await store.LoadAsync(alice)).Denials.Should().ContainSingle("a removed row is soft-deleted and may come back");

            await using (var db = new TelecasterContext(options))
            {
                db.Characters.Remove(await db.Characters.SingleAsync(c => c.Id == bobby));
                await db.SaveChangesAsync();
            }

            (await store.LoadAsync(alice)).Friends.Should().Equal(new[] { new FriendEntry(carol, "Carol") },
                "a deleted character leaves the lists");
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
