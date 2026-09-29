using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Migrations.Arcadia;

namespace Tests.DataAccess;

/// <summary>
/// The mapping of the quest catalogue and its migration
/// (<c>docs/packet-specs/socle-cycle-quete.md</c> §5.5 lot b2): the two Arcadia tables <c>QuestResource</c>
/// and <c>QuestLinkResource</c> did not exist in any migrated Arcadia database, so the migration is part of
/// what the sheet delivers. These tests are the offline proof that the mirror, the migration and
/// <c>ArcadiaContextModelSnapshot</c> describe the same two tables — <c>dotnet ef</c> is not available in
/// this container, so nothing else would catch a hand-written drift.
/// <para>
/// The expected list below is transcribed from <c>ArcadiaSchemaPSQL.sql:1789-1882</c> (the 7.3 schema), in the
/// schema's own column order: it is the mirror's contract, and it is deliberately written out instead of
/// read from the entity, so that a change to the entity has to face the schema.
/// </para>
/// </summary>
[TestFixture]
public class ArcadiaQuestCatalogueModelTests
{
    /// <summary>Schema column order of <c>QuestResource</c>, with the model's column type and nullability.</summary>
    private static readonly string[] QuestResourceColumns =
    {
        "Id:integer:not null",
        "TextIdQuest:integer:not null",
        "TextIdSummary:integer:not null",
        "TextIdStatus:integer:not null",
        "LimitBeginTime:integer:not null",
        "LimitEndTime:integer:not null",
        "LimitLevel:integer:not null",
        "LimitJobLevel:integer:not null",
        "LimitMaxLevel:integer:not null",
        "LimitMaxJobLevel:integer:not null",
        "LimitDeva:character varying(1):not null",
        "LimitAsura:character varying(1):not null",
        "LimitGaia:character varying(1):not null",
        "LimitFighter:character varying(1):not null",
        "LimitHunter:character varying(1):not null",
        "LimitMagician:character varying(1):not null",
        "LimitSummoner:character varying(1):not null",
        "LimitJob:integer:not null",
        "LimitJobDepth:smallint:not null",
        "LimitFavorGroupId:integer:not null",
        "LimitFavor:integer:not null",
        "Repeatable:character varying(1):not null",
        "InvokeCondition:integer:not null",
        "InvokeValue:integer:not null",
        "TimeLimitType:character varying(10):not null",
        "TimeLimit:integer:not null",
        "Type:integer:not null",
        "Value1:integer:not null",
        "Value2:integer:not null",
        "Value3:integer:not null",
        "Value4:integer:not null",
        "Value5:integer:not null",
        "Value6:integer:not null",
        "Value7:integer:not null",
        "Value8:integer:not null",
        "Value9:integer:not null",
        "Value10:integer:not null",
        "Value11:integer:not null",
        "Value12:integer:not null",
        "DropGroupId:integer:not null",
        "QuestDifficulty:integer:not null",
        "FavorGroupId:integer:not null",
        "HateGroupId:integer:not null",
        "Favor:integer:not null",
        "Exp:bigint:not null",
        "Jp:integer:not null",
        "HolicPoint:integer:not null",
        "Ld:integer:not null",
        "DefaultRewardId:integer:not null",
        "DefaultRewardLevel:integer:not null",
        "DefaultRewardQuantity:integer:not null",
        "OptionalRewardId1:integer:not null",
        "OptionalRewardLevel1:integer:not null",
        "OptionalRewardQuantity1:integer:not null",
        "OptionalRewardId2:integer:not null",
        "OptionalRewardLevel2:integer:not null",
        "OptionalRewardQuantity2:integer:not null",
        "OptionalRewardId3:integer:not null",
        "OptionalRewardLevel3:integer:not null",
        "OptionalRewardQuantity3:integer:not null",
        "OptionalRewardId4:integer:not null",
        "OptionalRewardLevel4:integer:not null",
        "OptionalRewardQuantity4:integer:not null",
        "OptionalRewardId5:integer:not null",
        "OptionalRewardLevel5:integer:not null",
        "OptionalRewardQuantity5:integer:not null",
        "OptionalRewardId6:integer:not null",
        "OptionalRewardLevel6:integer:not null",
        "OptionalRewardQuantity6:integer:not null",
        "ForeQuest1:integer:not null",
        "ForeQuest2:integer:not null",
        "ForeQuest3:integer:not null",
        "OrFlag:character varying(1):not null",
        "IsAutoQuest:character varying(1):not null",
        "ScriptStartText:character varying(512):optional",
        "ScriptEndText:character varying(512):optional",
        "ScriptDropText:character varying(512):optional",
        "ShowTargetType:character varying(1):optional",
        "ShowTargetId:integer:optional",
        "MarkHide:character varying(1):optional",
        "CoolTime:integer:not null",
        "AcceptCoolTime:integer:not null"
    };

    /// <summary>Schema column order of <c>QuestLinkResource</c>, with the model's column type and nullability.</summary>
    private static readonly string[] QuestLinkResourceColumns =
    {
        "NpcId:integer:not null",
        "QuestId:integer:not null",
        "FlagStart:character varying(1):not null",
        "FlagProgress:character varying(1):not null",
        "FlagEnd:character varying(1):not null",
        "TextIdStart:integer:not null",
        "TextIdInProgress:integer:not null",
        "TextIdEnd:integer:not null"
    };

    private static ArcadiaContext BuildContext()
    {
        // No connection is opened: building the model is offline work.
        var options = new DbContextOptionsBuilder<ArcadiaContext>()
            .UseNpgsql("Host=localhost;Database=arcadia;Username=navislamia;Password=navislamia")
            .Options;

        return new ArcadiaContext(options);
    }

    private static IEnumerable<string> Describe(IEntityType entityType)
    {
        return entityType.GetProperties()
            .Select(property => $"{property.Name}:{property.GetColumnType()}:{(property.IsNullable ? "optional" : "not null")}");
    }

    [Test]
    public void QuestResourceMirrorsTheSchemaColumnForColumn()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(QuestResourceEntity));

        entityType.Should().NotBeNull();
        entityType!.GetTableName().Should().Be("QuestResources");
        entityType!.GetSchema().Should().BeNull();
        entityType!.FindPrimaryKey()!.Properties.Select(property => property.Name).Should().Equal("Id");

        Describe(entityType!).Should().BeEquivalentTo(QuestResourceColumns,
            "QuestResources must carry exactly the columns of ArcadiaSchemaPSQL.sql:1789-1882");
    }

    [Test]
    public void QuestResourceDeclaresTheColumnsInTheSchemaOrder()
    {
        // The entity keeps the schema's order, so a future edit cannot silently move a column: the mirror is
        // read against ArcadiaSchemaPSQL.sql, not against the C# file alone.
        var declared = typeof(QuestResourceEntity).GetProperties().Select(property => property.Name);

        declared.Should().Equal(QuestResourceColumns.Select(column => column.Split(':')[0]));
    }

    [Test]
    public void QuestLinkResourceMirrorsTheSchemaColumnForColumn()
    {
        using var context = BuildContext();

        var entityType = context.Model.FindEntityType(typeof(QuestLinkResourceEntity));

        entityType.Should().NotBeNull();
        entityType!.GetTableName().Should().Be("QuestLinkResources");
        entityType!.GetSchema().Should().BeNull();

        // The table declares no primary key; the model keys it on the (npc_id, quest_id) pair.
        entityType!.FindPrimaryKey()!.Properties.Select(property => property.Name)
            .Should().Equal("NpcId", "QuestId");

        Describe(entityType!).Should().BeEquivalentTo(QuestLinkResourceColumns,
            "QuestLinkResources must carry exactly the columns of ArcadiaSchemaPSQL.sql:906-916");
    }

    [Test]
    public void OneMigrationCreatesBothCatalogueTables()
    {
        var migration = new AddQuestCatalogue { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" };

        var created = migration.UpOperations.OfType<CreateTableOperation>().ToList();

        created.Select(operation => operation.Name)
            .Should().BeEquivalentTo("QuestResources", "QuestLinkResources");

        var questResources = created.Single(operation => operation.Name == "QuestResources");
        questResources.Columns
            .Select(column => $"{column.Name}:{column.ColumnType}:{(column.IsNullable ? "optional" : "not null")}")
            .Should().BeEquivalentTo(QuestResourceColumns);
        questResources.PrimaryKey!.Columns.Should().Equal("Id");

        var questLinkResources = created.Single(operation => operation.Name == "QuestLinkResources");
        questLinkResources.Columns
            .Select(column => $"{column.Name}:{column.ColumnType}:{(column.IsNullable ? "optional" : "not null")}")
            .Should().BeEquivalentTo(QuestLinkResourceColumns);
        questLinkResources.PrimaryKey!.Columns.Should().Equal("NpcId", "QuestId");

        migration.DownOperations.OfType<DropTableOperation>().Select(operation => operation.Name)
            .Should().BeEquivalentTo("QuestResources", "QuestLinkResources");
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
            "would drop the quest catalogue tables");
    }
}
