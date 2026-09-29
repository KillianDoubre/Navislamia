using System;
using System.Linq;
using System.Reflection;
using FakeItEasy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// The `MixResource` row, its migration and the two catalogs built on the tables.
///
/// The row is read **positionally**: NGemity consumes the 109 columns in the order of the schema
/// (`ObjectMgr.cpp:1108-1146`), so a column declared out of order misaligns every sub-material group
/// downstream. The list below is the schema order of `ArcadiaSchemaPSQL.sql:473-581`, generated from
/// that file and not typed by hand; the first test replays it against the migration, the others against
/// the reading the matcher will use.
/// See docs/packet-specs/socle-artisanat-ressources.md §5.1 and §8 (L1a).
/// </summary>
[TestFixture]
public class MixResourceModelTests
{
    /// <summary>
    /// The 109 payload columns of <c>ArcadiaSchemaPSQL.sql:473-581</c>, in the order of the schema.
    /// </summary>
    private static readonly string[] PayloadColumns =
    {
        "Id",        "MixType",        "MixValue01",        "MixValue02",
        "MixValue03",        "MixValue04",        "MixValue05",        "MixValue06",
        "SubMaterialCount",        "MainType01",        "MainValue01",        "MainType02",
        "MainValue02",        "MainType03",        "MainValue03",        "MainType04",
        "MainValue04",        "MainType05",        "MainValue05",        "Sub01Type01",
        "Sub01Value01",        "Sub01Type02",        "Sub01Value02",        "Sub01Type03",
        "Sub01Value03",        "Sub01Type04",        "Sub01Value04",        "Sub01Type05",
        "Sub01Value05",        "Sub02Type01",        "Sub02Value01",        "Sub02Type02",
        "Sub02Value02",        "Sub02Type03",        "Sub02Value03",        "Sub02Type04",
        "Sub02Value04",        "Sub02Type05",        "Sub02Value05",        "Sub03Type01",
        "Sub03Value01",        "Sub03Type02",        "Sub03Value02",        "Sub03Type03",
        "Sub03Value03",        "Sub03Type04",        "Sub03Value04",        "Sub03Type05",
        "Sub03Value05",        "Sub04Type01",        "Sub04Value01",        "Sub04Type02",
        "Sub04Value02",        "Sub04Type03",        "Sub04Value03",        "Sub04Type04",
        "Sub04Value04",        "Sub04Type05",        "Sub04Value05",        "Sub05Type01",
        "Sub05Value01",        "Sub05Type02",        "Sub05Value02",        "Sub05Type03",
        "Sub05Value03",        "Sub05Type04",        "Sub05Value04",        "Sub05Type05",
        "Sub05Value05",        "Sub06Type01",        "Sub06Value01",        "Sub06Type02",
        "Sub06Value02",        "Sub06Type03",        "Sub06Value03",        "Sub06Type04",
        "Sub06Value04",        "Sub06Type05",        "Sub06Value05",        "Sub07Type01",
        "Sub07Value01",        "Sub07Type02",        "Sub07Value02",        "Sub07Type03",
        "Sub07Value03",        "Sub07Type04",        "Sub07Value04",        "Sub07Type05",
        "Sub07Value05",        "Sub08Type01",        "Sub08Value01",        "Sub08Type02",
        "Sub08Value02",        "Sub08Type03",        "Sub08Value03",        "Sub08Type04",
        "Sub08Value04",        "Sub08Type05",        "Sub08Value05",        "Sub09Type01",
        "Sub09Value01",        "Sub09Type02",        "Sub09Value02",        "Sub09Type03",
        "Sub09Value03",        "Sub09Type04",        "Sub09Value04",        "Sub09Type05",
        "Sub09Value05",
    };

    /// <summary>
    /// A row whose column n (1-based, schema order) holds the value n. Read positionally, `mix_value_01`
    /// is 3, `main_type_01` is 10, `sub09_value_05` is 109 — and every one of the nine sub groups has a
    /// non-zero first type, so `sub_material_count` reads 9.
    /// </summary>
    private static MixResourceEntity FormulaRow()
    {
        var mix = new MixResourceEntity();

        for (var column = 0; column < PayloadColumns.Length; column++)
        {
            var property = typeof(MixResourceEntity).GetProperty(PayloadColumns[column],
                BindingFlags.Public | BindingFlags.Instance);
            property!.SetValue(mix, column + 1);
        }

        return mix;
    }

    private static ArcadiaContext BuildContext()
    {
        // No connection is opened: building the model is offline work.
        var options = new DbContextOptionsBuilder<ArcadiaContext>()
            .UseNpgsql("Host=localhost;Database=arcadia;Username=navislamia;Password=navislamia")
            .Options;

        return new ArcadiaContext(options);
    }

    [Test]
    public void TheMigrationCreatesThe109PayloadColumnsInSchemaOrder()
    {
        using var context = BuildContext();

        var assembly = context.GetService<IMigrationsAssembly>();
        var migrationType = assembly.Migrations
            .Single(entry => entry.Key.EndsWith("AddMixResource", StringComparison.Ordinal)).Value;
        var migration = (Migration)Activator.CreateInstance(migrationType.AsType())!;

        var createTable = migration.UpOperations.OfType<CreateTableOperation>().Single();

        createTable.Name.Should().Be("MixResources");
        createTable.PrimaryKey!.Columns.Should().Equal("Id");

        createTable.Columns
            .Select(column => column.Name)
            .Where(name => name is not ("CreatedOn" or "ModifiedOn" or "DeletedOn"))
            .Should().Equal(PayloadColumns,
                "the column n of the table is the column n of ArcadiaSchemaPSQL.sql:473-581, in that order");
    }

    [Test]
    public void TheModelCarriesTheSame109PayloadColumns()
    {
        using var context = BuildContext();

        var entity = context.Model.FindEntityType(typeof(MixResourceEntity));

        entity.Should().NotBeNull();
        entity!.GetTableName().Should().Be("MixResources");
        entity.GetSchema().Should().BeNull();
        entity.FindPrimaryKey()!.Properties.Select(property => property.Name).Should().Equal("Id");

        entity.GetProperties()
            .Where(property => property.Name is not ("Id" or "CreatedOn" or "ModifiedOn" or "DeletedOn"))
            .Select(property => property.Name)
            .Should().BeEquivalentTo(PayloadColumns.Where(name => name != "Id"));

        entity.GetProperties()
            .Where(property => property.Name is not ("Id" or "CreatedOn" or "ModifiedOn" or "DeletedOn"))
            .Select(property => property.ClrType)
            .Should().OnlyContain(type => type == typeof(int),
                "the 109 columns of the reference schema are all integers");
    }

    [Test]
    public void AWholeRowIsReadPositionally()
    {
        var mix = FormulaRow();

        mix.MixType.Should().Be(2);
        mix.SubMaterialCount.Should().Be(9);
        mix.Sub09Value05.Should().Be(109);

        MixResourceRules.MixValues(mix).Should().Equal(3, 4, 5, 6, 7, 8);

        var main = MixResourceRules.MainMaterial(mix);
        main.Types.Should().Equal(10, 12, 14, 16, 18);
        main.Values.Should().Equal(11, 13, 15, 17, 19);

        var firstGroup = MixResourceRules.SubMaterial(mix, 0);
        firstGroup.Types.Should().Equal(20, 22, 24, 26, 28);
        firstGroup.Values.Should().Equal(21, 23, 25, 27, 29);

        var lastGroup = MixResourceRules.SubMaterial(mix, 8);
        lastGroup.Types.Should().Equal(100, 102, 104, 106, 108);
        lastGroup.Values.Should().Equal(101, 103, 105, 107, 109);
    }

    [Test]
    public void TheNineGroupsOfAFormulaRowAgreeWithItsCount()
    {
        var mix = FormulaRow();

        MixResourceRules.DeclaredSubMaterialGroups(mix).Should().Be(9);
        MixResourceRules.IsSubMaterialCountConsistent(mix).Should().BeTrue();
    }

    [Test]
    public void AnInconsistentSubMaterialCountIsReported()
    {
        var mix = new MixResourceEntity
        {
            Sub01Type01 = 1, Sub01Value01 = 4,
            Sub02Type01 = 3, Sub02Value01 = 700202
        };

        MixResourceRules.DeclaredSubMaterialGroups(mix).Should().Be(2);
        MixResourceRules.IsSubMaterialCountConsistent(mix).Should().BeFalse(
            "two groups are filled but the row declares one");

        mix.SubMaterialCount = 2;
        MixResourceRules.IsSubMaterialCountConsistent(mix).Should().BeTrue();
    }

    [Test]
    public void AGroupIsCountedOnlyWhenItsFirstTypeIsSet()
    {
        // The invariant measured on the 754 reference rows: a filled group always has a non-zero
        // `type_01`, and the groups beyond `sub_material_count` are zero in types and values.
        var mix = new MixResourceEntity
        {
            SubMaterialCount = 0,
            Sub02Type02 = 7, Sub02Value03 = 9
        };

        MixResourceRules.DeclaredSubMaterialGroups(mix).Should().Be(0);
        MixResourceRules.IsSubMaterialCountConsistent(mix).Should().BeTrue();
    }

    [Test]
    public void TheEnhanceCatalogKeepsTheSixRowsOfTheSameEnhanceId()
    {
        var repository = A.Fake<IEnhanceResourceRepository>();
        A.CallTo(() => repository.GetAll()).Returns(new[]
        {
            new EnhanceResourceEntity { Id = 240, LocalFlag = LocalFlag.Korea },
            new EnhanceResourceEntity { Id = 240, LocalFlag = LocalFlag.German },
            new EnhanceResourceEntity { Id = 240, LocalFlag = LocalFlag.France },
            new EnhanceResourceEntity { Id = 240, LocalFlag = LocalFlag.Russia },
            new EnhanceResourceEntity { Id = 240, LocalFlag = (LocalFlag)456 },
            new EnhanceResourceEntity { Id = 240, LocalFlag = LocalFlag.Mideast }
        });

        var catalog = new EnhanceResourceCatalog(repository);

        catalog.TryGetRow(240, LocalFlag.Russia, out var russia).Should().BeTrue();
        russia.LocalFlag.Should().Be(LocalFlag.Russia);
        catalog.TryGetRow(240, (LocalFlag)456, out var combined).Should().BeTrue();
        combined.LocalFlag.Should().Be((LocalFlag)456);
        catalog.TryGetRow(240, LocalFlag.Italy, out _).Should().BeFalse(
            "Italy is not one of the six rows of that enhance id");
        catalog.TryGetRow(241, LocalFlag.Korea, out _).Should().BeFalse();
    }

    [Test]
    public void TheMixCatalogWalksTheRulesInTableOrder()
    {
        var first = new MixResourceEntity { Id = 1016, MixType = 101 };
        var second = new MixResourceEntity { Id = 1154, MixType = 103 };

        var repository = A.Fake<IMixResourceRepository>();
        A.CallTo(() => repository.GetAll()).Returns(new[] { first, second });

        var catalog = new MixResourceCatalog(repository);

        catalog.Rules.Should().Equal(first, second);
        catalog.TryGetRule(1154, out var rule).Should().BeTrue();
        rule.MixType.Should().Be(103);
        catalog.TryGetRule(9999, out _).Should().BeFalse();
    }
}
