using System.IO;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Migrations.Arcadia;
using Navislamia.Game.DataAccess.Repositories;

namespace Tests.DataAccess;

[TestFixture]
public class ItemWearCatalogueTests
{
    private const string PreviousMigration = "20261001161635_RandomQuestPools";
    private const string MigrationId = "20261002150000_BackfillItemWearRestrictions";

    private static ItemResourceEntity Item(long id) => new()
    {
        Id = id, WearType = ItemWearType.Armor, Rank = 7, UseMinLevel = 160, UseMaxLevel = 300,
        RaceRestriction = ItemRaceRestriction.Gaia, JobRestriction = ItemJobRestriction.Summoner, JobDepth = 8,
        Price = 4242, BaseTypes = new short[4], BaseVar1 = new decimal[4], BaseVar2 = new decimal[4],
        OptTypes = new short[4], OptVar1 = new decimal[4], OptVar2 = new decimal[4],
        EnhanceIds = Array.Empty<long>(), EnhanceValues = new decimal[2, 4]
    };

    [Test]
    public void Migration_is_discovered_and_its_data_is_embedded_in_the_game_assembly()
    {
        using var db = new ArcadiaContext(new DbContextOptionsBuilder<ArcadiaContext>()
            .UseNpgsql("Host=localhost;Database=arcadia;Username=test").Options);
        db.Database.GetMigrations().Should().Contain(MigrationId);
        var operation = new BackfillItemWearRestrictions().UpOperations.Should().ContainSingle().Subject;
        operation.Should().BeOfType<SqlOperation>();
        var sql = ((SqlOperation)operation).Sql;
        sql.Should().Contain("29647 item IDs, 31 whitelist/depth combinations");
        sql.Should().NotContain("DELETE ").And.NotContain("INSERT ").And.NotContain("ALTER TABLE");
        var script = db.GetService<IMigrator>().GenerateScript(PreviousMigration, MigrationId);
        script.Should().Contain(sql).And.Contain(MigrationId).And.Contain("COMMIT;");
    }

    [Test, Explicit("Requires an isolated PostgreSQL database named navis_equipment_test")]
    public async Task PostgreSql_migrates_existing_items_and_preserves_other_data_and_history()
    {
        var connection = Environment.GetEnvironmentVariable("NAVISLAMIA_ARCADIA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Ignore("Set NAVISLAMIA_ARCADIA_TEST_CONNECTION");
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connection);
        builder.Database.Should().Be("navis_equipment_test", "this test migrates a disposable database only");
        var options = new DbContextOptionsBuilder<ArcadiaContext>().UseNpgsql(builder.ConnectionString).Options;
        await using var db = new ArcadiaContext(options);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var master = Item(263701); master.RaceRestriction = 0; master.JobRestriction = 0; master.JobDepth = 0;
        var summonOnly = Item(560001);
        var unknown = Item(99999999);
        db.ItemResources.AddRange(master, summonOnly, unknown);
        db.JobResources.Add(new JobResourceEntity { Id = 123, JobClass = 4, JobDepth = 8, AvailableJobs = Array.Empty<short>() });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();

        await db.Database.MigrateAsync();
        var rows = await db.ItemResources.AsNoTracking().OrderBy(i => i.Id).ToArrayAsync();
        rows.Should().HaveCount(3);
        rows[0].RaceRestriction.Should().Be((ItemRaceRestriction)7);
        rows[0].JobRestriction.Should().Be(ItemJobRestriction.Magician); rows[0].JobDepth.Should().Be(8);
        rows[1].RaceRestriction.Should().Be(0); rows[1].JobRestriction.Should().Be(0); rows[1].JobDepth.Should().Be(15);
        rows[2].RaceRestriction.Should().Be(ItemRaceRestriction.Gaia);
        rows[2].JobRestriction.Should().Be(ItemJobRestriction.Summoner); rows[2].JobDepth.Should().Be(8);
        rows.Should().OnlyContain(i => i.Price == 4242 && i.Rank == 7 && i.UseMinLevel == 160 && i.UseMaxLevel == 300);
        (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(MigrationId);
        var projected = new ItemResourceRepository(options).GetWearFields().Single(i => i.Id == 263701);
        projected.RaceRestriction.Should().Be((ItemRaceRestriction)7);
        projected.JobRestriction.Should().Be(ItemJobRestriction.Magician); projected.JobDepth.Should().Be(8);
        projected.Rank.Should().Be(7); projected.UseMinLevel.Should().Be(160); projected.UseMaxLevel.Should().Be(300);
        var job = new JobResourceRepository(options).GetWearFields().Single();
        job.Job.Should().Be(123); job.JobClass.Should().Be(4); job.JobDepth.Should().Be(8);
        // A later administrator edit is not overwritten each time the server starts.
        await db.Database.ExecuteSqlRawAsync("UPDATE \"ItemResources\" SET \"RaceRestriction\" = 4 WHERE \"Id\" = 263701");
        await db.Database.MigrateAsync();
        (await db.ItemResources.AsNoTracking().SingleAsync(i => i.Id == 263701)).RaceRestriction.Should().Be(ItemRaceRestriction.Gaia);
    }
}
