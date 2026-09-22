using System.Linq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;

namespace Tests.DataAccess;

/// <summary>
/// The `WorldLocations` mapping and its migration. The fiche 902/903 asks for a DbSet nobody had mapped
/// before, so the table does not exist in any migrated Arcadia database yet: the migration is part of the
/// packet's delivery, and an unmigrated table makes `WorldRepository.LoadWorldIntoMemory` fail at startup.
/// These two tests are the offline proof that the model, the migration target model (the `.Designer.cs`)
/// and `ArcadiaContextModelSnapshot` describe the same table — `dotnet ef` is not available here.
/// </summary>
[TestFixture]
public class ArcadiaWorldLocationModelTests
{
    private static ArcadiaContext BuildContext()
    {
        // No connection is opened: building the model is offline work.
        var options = new DbContextOptionsBuilder<ArcadiaContext>()
            .UseNpgsql("Host=localhost;Database=arcadia;Username=navislamia;Password=navislamia")
            .Options;

        return new ArcadiaContext(options);
    }

    [Test]
    public void WorldLocationIsMappedToTheMigratedTable()
    {
        using var context = BuildContext();

        var entity = context.Model.FindEntityType(typeof(WorldLocationEntity));

        entity.Should().NotBeNull();
        entity!.GetTableName().Should().Be("WorldLocations");
        entity.GetSchema().Should().BeNull();
        entity.FindPrimaryKey()!.Properties.Select(property => property.Name)
            .Should().Equal("Id", "WeatherId", "TimeId");
        entity.GetProperties().Select(property => $"{property.Name}:{property.GetColumnType()}")
            .Should().BeEquivalentTo(new[]
            {
                "Id:integer", "X:integer", "Y:integer", "LocationType:smallint", "TimeId:integer",
                "WeatherId:integer", "WeatherRatio:smallint", "WeatherChangeTime:smallint"
            });
    }

    [Test]
    public void ModelSnapshotDescribesTheSameModelSoNoMigrationIsPending()
    {
        using var context = BuildContext();

        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot;
        var differ = context.GetService<IMigrationsModelDiffer>();

        var snapshotModel = snapshot?.Model;
        if (snapshotModel is IMutableModel mutableModel)
        {
            snapshotModel = mutableModel.FinalizeModel();
        }

        if (snapshotModel != null)
        {
            snapshotModel = context.GetService<IModelRuntimeInitializer>().Initialize(snapshotModel, designTime: true);
        }

        var differences = differ.GetDifferences(snapshotModel?.GetRelationalModel(),
            context.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        differences.Should().BeEmpty(
            "ArcadiaContextModelSnapshot must describe the migrated model, otherwise the next migration " +
            "would undo the WorldLocations table");
    }
}
