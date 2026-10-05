using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Services.Party;
using Navislamia.Game.Services;
using Npgsql;

namespace Tests.Game;

/// <summary>The parties across restarts (socle-groupe.md, *Persistance*, Trello 6nctY154).</summary>
public partial class PartyServiceTests
{
    private sealed class RecordingStore : IPartyStore
    {
        public readonly List<PartySnapshot> Saved = new();
        public IReadOnlyList<StoredParty> Stored = Array.Empty<StoredParty>();
        public int MaxId;

        public Task<(IReadOnlyList<StoredParty> Parties, int MaxId)> LoadAsync() => Task.FromResult((Stored, MaxId));

        public Task SaveAsync(PartySnapshot snapshot)
        {
            lock (Saved) Saved.Add(snapshot);
            return Task.CompletedTask;
        }
    }

    private PartyService WithStore(RecordingStore store) =>
        _parties = new PartyService(_visibility, A.Fake<IStatService>(), _bannedWords, store: store);

    [Test]
    public async Task Every_change_of_an_ordinary_party_is_stored_in_order_and_the_last_member_deletes_it()
    {
        var store = new RecordingStore();
        WithStore(store);
        var ana = Player(1, "Ana");
        var bo = Player(2, "Bo");

        _parties.TryHandleCommand(ana, "/pcreate Wolves");
        Join(ana, bo);
        _parties.TryHandleCommand(ana, "/ppromote Bo");
        _parties.TryHandleCommand(bo, "/pshare random");
        _parties.TryHandleCommand(ana, "/pleave");
        _parties.TryHandleCommand(bo, "/pdestroy");
        await _parties.FlushAsync();

        store.Saved.Select(s => (s.LeaderId, s.ShareMode, string.Join(",", s.Members))).Should().Equal(
            (1L, PartyShareMode.Monopoly, "1"),
            (1L, PartyShareMode.Monopoly, "1,2"),
            (2L, PartyShareMode.Monopoly, "1,2"),
            (2L, PartyShareMode.Random, "1,2"),
            (2L, PartyShareMode.Random, "2"),
            (2L, PartyShareMode.Random, ""));
        store.Saved.Should().OnlyContain(s => s.Id == 1 && s.Name == "Wolves" && s.Type == 0);
    }

    [Test]
    public async Task A_HuntaHolic_party_is_never_stored()
    {
        var store = new RecordingStore();
        WithStore(store);
        _parties.CreateHuntaholicParty(Player(1, "Ana"), "Room");
        await _parties.FlushAsync();
        store.Saved.Should().BeEmpty();
    }

    [Test]
    public async Task Stored_parties_come_back_and_a_member_entering_gets_login_then_party_info()
    {
        StoredPartyMember Member(long id, string name) => new(id, name, 30, 110, 4);
        var store = new RecordingStore
        {
            MaxId = 20,
            Stored = new[]
            {
                new StoredParty(5, "Wolves", 1, PartyShareMode.Linear, 0, new[] { Member(1, "Ana"), Member(2, "Bo") }),
                new StoredParty(6, "Hunt", 3, PartyShareMode.Monopoly, 3, new[] { Member(3, "Cy") }),
                new StoredParty(7, "Empty", 4, PartyShareMode.Monopoly, 0, Array.Empty<StoredPartyMember>()),
                new StoredParty(8, "Headless", 9, PartyShareMode.Monopoly, 0, new[] { Member(4, "Di") })
            }
        };
        WithStore(store);
        await _parties.LoadAsync();

        var ana = Player(1, "Ana");
        _parties.OnWorldEntry(ana);
        Info(ana).PartyId.Should().Be(5);
        var lines = Lines(ana);
        lines[0].Should().Be("LOGIN|Wolves|Ana|");
        lines.Last().Should().StartWith("PINFO|5|Wolves|Ana|2|").And.Contain("|0|Bo|30|110|0|0|0|0|0|",
            "Bo is offline, his entry comes from the stored row");

        var cy = Player(3, "Cy");
        _parties.OnWorldEntry(cy);
        Info(cy).PartyId.Should().BeNull("PartyManager::loadPartyList destroys a HuntaHolic party");
        _parties.TryHandleCommand(cy, "/pcreate Fresh");
        Info(cy).PartyId.Should().Be(21, "new ids continue after the highest stored one");

        await _parties.FlushAsync();
        store.Saved.Where(s => s.Members.Count == 0).Select(s => s.Id).Should().BeEquivalentTo(new[] { 6, 7, 8 },
            "a destroyed party is deleted from the table, its members' party_id cleared");
    }

    [Test]
    public async Task Attack_party_loader_rejects_missing_guild_dungeon_head_wrong_type_and_capacity()
    {
        StoredParty Row(int id, int type, long guild, int dungeon, long head, int max = 2) =>
            new(id, "Party" + id, id, PartyShareMode.Monopoly, type,
                new[] { new StoredPartyMember(id, "P" + id, 30, 110, 4) }, guild, dungeon, head, max);
        var store = new RecordingStore { Stored = new[]
        {
            Row(10, 1, 7, 130000, 10), Row(11, 1, 7, 130000, 10), Row(12, 1, 7, 130000, 10),
            Row(13, 2, 7, 130000, 10), Row(14, 1, 0, 130000, 14), Row(15, 1, 8, 0, 15),
            Row(16, 1, 8, 130000, 99), Row(17, 1, 9, 130300, 10), Row(18, 1, 7, 130000, 18)
        }, MaxId = 18 };
        WithStore(store); await _parties.LoadAsync();
        _parties.AttackParties().Select(p => p.Id).Should().BeEquivalentTo(new long[] { 10, 11 });
        await _parties.FlushAsync();
        store.Saved.Select(p => p.Id).Should().BeEquivalentTo(new[] { 12, 13, 14, 15, 16, 17, 18 });
        store.Saved.Should().OnlyContain(p => p.Members.Count == 0);
    }

    [Test, Explicit("Runs the Telecaster migrations in an isolated schema on the configured local PostgreSQL server.")]
    public async Task PostgreSql_party_rows_and_members_survive_a_restart()
    {
        var settingsPath = Path.GetFullPath("../../../../DevConsole/appsettings.json", TestContext.CurrentContext.TestDirectory);
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        var options = settings.RootElement.GetProperty("Database").Deserialize<DatabaseOptions>()!;
        var schema = "party_test_" + Guid.NewGuid().ToString("N");
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = options.DataSource, Port = options.Port, Username = options.User, Password = options.Password,
            Database = options.TelecasterCatalog, SearchPath = schema, Pooling = false
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection)) await create.ExecuteNonQueryAsync();
        try
        {
            var dbOptions = new DbContextOptionsBuilder<TelecasterContext>()
                .UseNpgsql(connectionString, o => o.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options;
            await using (var db = new TelecasterContext(dbOptions))
            {
                await db.Database.MigrateAsync();
                foreach (var (id, name) in new[] { (1L, "Ana"), (2L, "Bo") })
                {
                    var character = new CharacterEntity { Id = id, CharacterName = name, Lv = 30 };
                    foreach (var property in typeof(CharacterEntity).GetProperties().Where(p => p.CanWrite))
                    {
                        if (property.PropertyType == typeof(string) && property.GetValue(character) is null) property.SetValue(character, "");
                        if (property.PropertyType.IsArray) property.SetValue(character, Array.CreateInstance(property.PropertyType.GetElementType()!, 4));
                    }

                    db.Characters.Add(character);
                }

                await db.SaveChangesAsync();
            }

            var store = new PartyStore(dbOptions);
            await store.SaveAsync(new PartySnapshot(5, "Wolves", 1, PartyShareMode.Random, 0, new long[] { 1, 2 }));
            var (parties, maxId) = await store.LoadAsync();
            maxId.Should().Be(5);
            parties.Should().ContainSingle();
            parties[0].ShareMode.Should().Be(PartyShareMode.Random);
            parties[0].Members.Select(m => (m.CharacterId, m.Name, m.Level)).Should().Equal((1L, "Ana", 30), (2L, "Bo", 30));

            // Bo leaves, then the party ends: party_id is cleared and the row soft-deleted.
            await store.SaveAsync(new PartySnapshot(5, "Wolves", 1, PartyShareMode.Random, 0, new long[] { 1 }));
            await store.SaveAsync(new PartySnapshot(5, "Wolves", 1, PartyShareMode.Random, 0, Array.Empty<long>()));
            (parties, maxId) = await store.LoadAsync();
            parties.Should().BeEmpty();
            maxId.Should().Be(5, "a deleted id is never handed out again");

            // The same leader leads a new party: LeaderId is no longer unique.
            await store.SaveAsync(new PartySnapshot(6, "Again", 1, PartyShareMode.Monopoly, 0, new long[] { 1, 2 }));
            (parties, _) = await store.LoadAsync();
            parties.Single().Id.Should().Be(6);
            await using (var db = new TelecasterContext(dbOptions))
            {
                (await db.Characters.Select(c => c.PartyId).ToListAsync()).Should().AllBeEquivalentTo(6L);
            }

            // The restart itself: a new service reads the table back.
            var restarted = new PartyService(_visibility, A.Fake<IStatService>(), _bannedWords, store: store);
            await restarted.LoadAsync();
            var bo = Player(2, "Bo");
            restarted.OnWorldEntry(bo);
            Info(bo).PartyId.Should().Be(6);
            await restarted.FlushAsync();

            // Attack teams (merge review of bbbf6fb): a head team links to itself, a second team to the head, through
            // the real LeadPartyId foreign key; the head can end while the linked team still points at it (soft delete).
            await store.SaveAsync(new PartySnapshot(6, "Again", 1, PartyShareMode.Monopoly, 0, Array.Empty<long>()));
            await store.SaveAsync(new PartySnapshot(7, "Head", 1, PartyShareMode.Monopoly, 1, new long[] { 1 }, 7));
            await store.SaveAsync(new PartySnapshot(8, "Second", 2, PartyShareMode.Monopoly, 1, new long[] { 2 }, 7));
            await using (var db = new TelecasterContext(dbOptions))
            {
                (await db.Parties.Where(p => p.Id >= 7).OrderBy(p => p.Id).Select(p => p.LeadPartyId).ToListAsync())
                    .Should().Equal(7L, 7L);
            }

            await store.SaveAsync(new PartySnapshot(7, "Head", 1, PartyShareMode.Monopoly, 1, Array.Empty<long>(), 7));
            await store.SaveAsync(new PartySnapshot(8, "Second", 2, PartyShareMode.Monopoly, 1, Array.Empty<long>(), 7));
        }
        finally
        {
            // The schema is this test's alone (fixed prefix and GUID).
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
