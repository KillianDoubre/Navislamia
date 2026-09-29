using System.Linq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Tests.DataAccess;

/// <summary>
/// The <c>PaidItems</c> mapping and its migration. The fiche asks for a container nobody had mapped
/// before, so the table does not exist in any migrated Telecaster database yet: the migration is part of
/// the packet's delivery, and an unmigrated table makes every world entry fail on the read
/// (docs/packet-specs/socle-stockage-commercial-conteneur.md §5.1).
/// <para>
/// These two tests are the offline proof that the model, the migration target model (the
/// <c>.Designer.cs</c>) and <c>TelecasterContextModelSnapshot</c> describe the same table —
/// <c>dotnet ef</c> is not available in this container.
/// </para>
/// </summary>
[TestFixture]
public class TelecasterPaidItemModelTests
{
    private static TelecasterContext BuildContext()
    {
        // No connection is opened: building the model is offline work.
        var options = new DbContextOptionsBuilder<TelecasterContext>()
            .UseNpgsql("Host=localhost;Database=telecaster;Username=navislamia;Password=navislamia")
            .Options;

        return new TelecasterContext(options);
    }

    [Test]
    public void PaidItemIsMappedToTheMigratedTable()
    {
        using var context = BuildContext();

        var entity = context.Model.FindEntityType(typeof(PaidItemEntity));

        entity.Should().NotBeNull();
        entity!.GetTableName().Should().Be("PaidItems");
        entity.GetSchema().Should().BeNull();
        entity.FindPrimaryKey()!.Properties.Select(property => property.Name).Should().Equal("Id");

        // Column by column, the official dump of the table (reference/ngemity/Database/Telecaster.sql:419-439):
        // 18 columns of the dump plus the three timestamps every entity of this repository carries.
        entity.GetProperties().Select(property => $"{property.Name}:{property.GetColumnType()}")
            .Should().BeEquivalentTo(new[]
            {
                "Id:bigint", "AccountId:bigint", "CharacterId:bigint", "CharacterName:text",
                "ItemCode:integer", "ItemCount:integer", "RestItemCount:integer",
                "BoughtTime:timestamp with time zone", "ValidTime:timestamp with time zone", "ServerName:text",
                "TakenCharacterId:bigint", "TakenCharacterName:text", "TakenServerName:text",
                "TakenTime:timestamp with time zone", "TakenAccountId:bigint",
                "Confirmed:integer", "ConfirmedTime:timestamp with time zone", "IsCancel:boolean",
                "CreatedOn:timestamp with time zone", "ModifiedOn:timestamp with time zone",
                "DeletedOn:timestamp with time zone"
            });
    }

    [Test]
    public void PaidItemCarriesTheOwnerIndexTheReadPathFiltersOn()
    {
        using var context = BuildContext();

        var entity = context.Model.FindEntityType(typeof(PaidItemEntity))!;

        entity.GetIndexes().Select(index => string.Join(",", index.Properties.Select(property => property.Name)))
            .Should().Contain("AccountId,CharacterId");
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
            "TelecasterContextModelSnapshot must describe the migrated model, otherwise the next migration " +
            "would undo the PaidItems table");
    }
}
