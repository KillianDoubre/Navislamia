using FakeItEasy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Progression;
using Navislamia.Game.Services.Stats;
using Npgsql;

namespace Tests.Game;

[TestFixture]
public class TitleProgressionTests
{
    [Test, Explicit("Runs title migrations and array round trips in a temporary PostgreSQL schema.")]
    public async Task PostgreSql_title_migration_persists_arrays_and_selection()
    {
        var connectionString = Environment.GetEnvironmentVariable("NAVIS_QUEST_TEST_CONNECTION")
            ?? throw new InvalidOperationException("Set NAVIS_QUEST_TEST_CONNECTION to an isolated PostgreSQL server.");
        var schema = "title_test_" + Guid.NewGuid().ToString("N");
        var settings = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema, Pooling = false };
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection)) await create.ExecuteNonQueryAsync();
        try
        {
            var options = new DbContextOptionsBuilder<TelecasterContext>()
                .UseNpgsql(settings.ConnectionString, o => o.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options;
            await using (var db = new TelecasterContext(options))
            {
                await db.Database.MigrateAsync();
                (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(m => m.EndsWith("_CharacterTitles"));
                var character = new CharacterEntity { Id = 1, CharacterName = "Titles", MainTitleId = 2 };
                foreach (var property in typeof(CharacterEntity).GetProperties().Where(p => p.CanWrite))
                {
                    if (property.PropertyType == typeof(string) && property.GetValue(character) is null) property.SetValue(character, "");
                    if (property.PropertyType.IsArray) property.SetValue(character, Array.CreateInstance(property.PropertyType.GetElementType()!, 4));
                }
                db.Characters.Add(character);
                db.CharacterTitleStates.Add(new CharacterTitleStateEntity { CharacterId = 1,
                    OpenedTitleIds = new[] { 1, 2 }, OwnedTitleIds = new[] { 2 },
                    ConditionIds = new[] { 101 }, ConditionCounts = new[] { 3_000_000_000L } });
                await db.SaveChangesAsync();
            }
            await using (var db = new TelecasterContext(options))
            {
                (await db.Characters.SingleAsync()).MainTitleId.Should().Be(2);
                var state = await db.CharacterTitleStates.SingleAsync();
                state.OpenedTitleIds.Should().Equal(1, 2); state.OwnedTitleIds.Should().Equal(2);
                state.ConditionCounts.Should().Equal(3_000_000_000L);
                db.Characters.Remove(await db.Characters.SingleAsync()); await db.SaveChangesAsync();
                (await db.CharacterTitleStates.CountAsync()).Should().Be(0, "the foreign key cascades character deletion");
            }
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
    private static TitleResource Resource(int id) => new(id, id, new short[] { 96 },
        new decimal[] { 512 }, new decimal[] { 40 }, false, "", "");

    [Test]
    public void Title_effects_use_all_eight_options()
    {
        var types = new short[8]; var masks = new decimal[8]; var values = new decimal[8];
        types[7] = 96; masks[7] = 512; values[7] = 40;
        var resource = Resource(1) with { Types = types, Var1 = masks, Var2 = values };
        var catalog = new TitleCatalog(new ProgressionResources { Titles = new[] { resource } });
        catalog.GetEffects(1).Should().Equal(new StatEffect(StatTarget.Defence, 40, false));
    }

    [Test]
    public async Task Quest_and_monster_titles_unlock_persist_select_and_change_real_stats()
    {
        var options = new DbContextOptionsBuilder<TelecasterContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), o => o.EnableNullChecks(false)).Options;
        var resources = new ProgressionResources
        {
            Titles = new[] { Resource(1), Resource(2), Resource(3) },
            ConditionTypes = new[]
            {
                new TitleConditionType(10, 4002, new[] { 1005, 0, 0 }, true),
                new TitleConditionType(11, 1001, new[] { 2101, 0, 0 }, false),
                new TitleConditionType(12, 8001, new[] { 2, 0, 0 }, true)
            },
            Conditions = new[]
            {
                new TitleCondition(1, 0, 10, 1, true),
                new TitleCondition(2, 0, 10, 1, false),
                new TitleCondition(2, 0, 11, 2, true),
                new TitleCondition(3, 0, 12, 1, true)
            }
        };
        var catalog = new TitleCatalog(resources);
        var stats = new StatService(StatCatalogTestFactory.Create(), A.Fake<IItemStatCatalog>(),
            A.Fake<ISkillPassiveCatalog>(), A.Fake<IStateCatalog>(), catalog);
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        StorageTestHarness.Session(client).CharacterHandle = 1; StorageTestHarness.Session(client).CharacterName = "Titles";
        StorageTestHarness.Session(client).CharacterJob = StatCatalogTestFactory.KnownJob; StorageTestHarness.Session(client).CharacterLevel = 5;
        await using (var db = new TelecasterContext(options))
        {
            db.Characters.Add(new CharacterEntity { Id = 1, CharacterName = "Titles", Lv = 5,
                CurrentJob = (Navislamia.Game.DataAccess.Entities.Enums.Job)StatCatalogTestFactory.KnownJob });
            await db.SaveChangesAsync();
        }
        var service = new TitleService(options, new CharacterGate(), catalog, stats);
        (await service.GetOwnedAsync(client)).Should().BeEmpty();
        (await service.SelectAsync(client, 2)).Should().BeFalse();
        var before = stats.Compute(StorageTestHarness.Session(client)).Total.Defence;
        await using (var db = new TelecasterContext(options))
        {
            db.CharacterQuestCompletions.Add(new CharacterQuestCompletionEntity
                { CharacterId = 1, Code = 1005, CompletedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        await service.RefreshAsync(client);
        StorageTestHarness.Session(client).MainTitleId.Should().Be(1);
        stats.Compute(StorageTestHarness.Session(client)).Total.Defence.Should().Be(before + 40);
        stats.Compute(StorageTestHarness.Session(client)).ByItem.Defence.Should().Be(0);
        var world = MonsterProgressionTests.World(); world.TryGetInstance(0, out var monster).Should().BeTrue();
        await service.RecordMonsterKillAsync(client, monster);
        (await service.GetOwnedAsync(client)).Should().Equal(1);
        // A new service instance sees the saved first kill and opened title.
        service = new TitleService(options, new CharacterGate(), catalog, stats);
        await service.RecordMonsterKillAsync(client, monster);
        (await service.GetOwnedAsync(client)).Should().Equal(1, 2, 3);
        (await service.SelectAsync(client, 2)).Should().BeTrue();
        await using (var db = new TelecasterContext(options))
        {
            var character = await db.Characters.SingleAsync(); character.MainTitleId.Should().Be(2);
            StorageTestHarness.Session(client).ClearCharacterSession();
            stats.Seed(StorageTestHarness.Session(client), character);
            StorageTestHarness.Session(client).MainTitleId.Should().Be(2);
            stats.Compute(character).Total.Defence.Should().Be(before + 40);
        }
        StorageTestHarness.Session(client).CharacterHandle = 1; StorageTestHarness.Session(client).CharacterName = "Titles";
        (await service.SelectAsync(client, 0)).Should().BeTrue();
        StorageTestHarness.Session(client).TitleEffects.Should().BeEmpty();
        (await service.SelectAsync(client, 999)).Should().BeFalse();
        (await service.SetConditionAsync(client, 999, 1)).Should().BeFalse();
        (await service.SetConditionAsync(client, 10, -1)).Should().BeFalse();
    }

    [Test]
    public void Condition_groups_require_all_members_of_any_group_and_unknown_positive_conditions_block()
    {
        var conditions = new[] { new TitleCondition(1, 0, 1, 2, true), new TitleCondition(1, 0, 2, 1, true),
            new TitleCondition(1, 1, 3, 5, true) };
        TitleCatalog.Satisfied(conditions, new Dictionary<int, long> { [1] = 2 }).Should().BeFalse();
        TitleCatalog.Satisfied(conditions, new Dictionary<int, long> { [1] = 2, [2] = 1 }).Should().BeTrue();
        TitleCatalog.Satisfied(conditions, new Dictionary<int, long> { [3] = 5 }).Should().BeTrue();
        TitleCatalog.Satisfied(Array.Empty<TitleCondition>(), new Dictionary<int, long>()).Should().BeTrue();
    }

    [Test]
    public void Expired_titles_have_no_effect_and_resource_export_has_all_tables()
    {
        var expired = Resource(1) with { Periodic = true, Begin = "2000-01-01", End = "2001-01-01" };
        var catalog = new TitleCatalog(new ProgressionResources { Titles = new[] { expired } });
        catalog.GetEffects(1).Should().BeEmpty(); catalog.GetEffects(999).Should().BeEmpty();
        ProgressionResources.Official.Titles.Should().HaveCount(258);
        ProgressionResources.Official.ConditionTypes.Should().HaveCount(292);
        ProgressionResources.Official.Conditions.Should().HaveCount(601);
        ProgressionResources.Official.DungeonCells.Should().HaveCount(81);
    }
}
