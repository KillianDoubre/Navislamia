using System.Buffers.Binary;
using FakeItEasy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;

namespace Tests.Game;

[TestFixture]
public class SkillCardCraftTests
{
    private const string Crafter = "CardCrafter";

    // Imported rules 6837/6838: cube in slot 1, second card in slot 2;
    // main conditions 24/25 refer to slot 2. Different card codes may share a skill.
    private static MixResourceEntity Recipe(int cube = 700401) => new()
    {
        Id = cube == 700401 ? 6837 : 6838, MixType = 102,
        MixValue01 = cube == 700401 ? 400 : 560, SubMaterialCount = 2,
        MainType01 = 1, MainValue01 = 10,
        MainType02 = 24, MainValue02 = 2,
        MainType03 = 25, MainValue03 = 2,
        Sub01Type01 = 3, Sub01Value01 = cube,
        Sub02Type01 = 1, Sub02Value01 = 10,
        Sub02Type02 = 10, Sub02Value02 = 1
    };

    private static MixMaterial Card(uint handle = 100, int code = 400001, long enhance = 3, long skill = 1001) =>
        new(code, 10, 0, 1, -1, 1, enhance, 0, 1, handle, skill);

    private static MixMaterial Cube(int code = 700401) => new(code, 6, 0, 0, -1, 1, 0, 0, 1, 200);

    [TestCase(700401)]
    [TestCase(700402)]
    public void ImportedRecipes_MatchSkillAndEnhancementRatherThanItemCode(int cube)
    {
        MixResourceMatcher.TryResolve(new[] { Recipe(cube) }, Card(),
            new[] { Cube(cube), Card(300, 400002) }, out var resolution).Should().BeTrue();
        resolution.ConsumedCounts.Should().Equal(1L, 1L);
        resolution.Rule.MixValue01.Should().Be(cube == 700401 ? 400 : 560);
    }

    [TestCase(2, 1001)]
    [TestCase(3, 1002)]
    [TestCase(3, 0)]
    public void DifferentEnhancementOrSkill_IsRefused(long enhance, long skill)
    {
        MixResourceMatcher.TryResolve(new[] { Recipe() }, Card(),
            new[] { Cube(), Card(300, enhance: enhance, skill: skill) }, out _).Should().BeFalse();
    }

    [TestCase(19, -1)]
    [TestCase(19, 3)]
    [TestCase(24, -1)]
    [TestCase(24, 3)]
    [TestCase(25, -1)]
    [TestCase(25, 3)]
    public void ReferenceOutsideTheMaterialSlots_IsRefused(int condition, int slot)
    {
        var recipe = Recipe();
        recipe.MainType02 = condition;
        recipe.MainValue02 = slot;
        MixResourceMatcher.TryResolve(new[] { recipe }, Card(),
            new[] { Cube(), Card(300) }, out _).Should().BeFalse();
    }

    [TestCase(24)]
    [TestCase(25)]
    public void MaterialConditions_CanReferToTheMainSlot(int condition)
    {
        var recipe = Recipe();
        recipe.MainType02 = recipe.MainType03 = 0;
        recipe.Sub02Type03 = condition;
        recipe.Sub02Value03 = 0;
        MixResourceMatcher.TryResolve(new[] { recipe }, Card(),
            new[] { Cube(), Card(300) }, out _).Should().BeTrue();
        recipe.MainType01 = 0;
        MixResourceMatcher.TryResolve(new[] { recipe }, null,
            new[] { Cube(), Card(300) }, out _).Should().BeFalse();
    }

    private sealed record Harness(CharacterService Characters, CraftingSocleService Crafting,
        CharacterEntity Character, ItemEntity Main, ItemEntity Second, ItemEntity Cube,
        ICharacterRepository Repository, StorageTestHarness.FrameConnection Connection);

    private static Harness Build(uint enhance = 3, bool sameStack = false, long count = 4,
        int cubeCode = 700401, int roll = 0, FailResultType failResult = FailResultType.SkillCard)
    {
        var main = new ItemEntity
        {
            Id = 100, ItemResourceId = 400001, Level = 1, Enhance = enhance, Amount = count, Idx = 1,
            WearInfo = ItemWearType.None, SocketItemIds = new long[] { 42, 0, 17, 0 },
            Endurance = 71, EtherealDurability = 29, RemainingTime = 111,
            GenerateBySource = ItemGenerateSource.Monster
        };
        var second = sameStack ? main : new ItemEntity
        {
            Id = 300, ItemResourceId = 400002, Level = 1, Enhance = enhance, Amount = count,
            Idx = 3, WearInfo = ItemWearType.None
        };
        var cube = new ItemEntity
        {
            Id = 200, ItemResourceId = cubeCode, Level = 1, Amount = count, Idx = 2, WearInfo = ItemWearType.None
        };
        var character = new CharacterEntity
        {
            Id = 42, CharacterName = Crafter, Items = new List<ItemEntity> { main, cube }
        };
        if (!sameStack) character.Items.Add(second);
        var repository = A.Fake<ICharacterRepository>();
        A.CallTo(() => repository.GetCharacterByNameWithItemsAsync(Crafter)).Returns(character);
        A.CallTo(() => repository.SaveChangesAsync()).Invokes(() =>
        {
            foreach (var added in character.Items.Where(item => item.Id == 0)) added.Id = 900;
        });
        var factory = A.Fake<ICharacterRepositoryFactory>();
        A.CallTo(() => factory.Create()).Returns(repository);
        var characters = new CharacterService(A.Fake<IStarterItemsRepository>(), factory, new CharacterGate(),
            A.Fake<ILogger<CharacterService>>());
        var mixCatalog = A.Fake<IMixResourceCatalog>();
        A.CallTo(() => mixCatalog.Rules).Returns(new[] { Recipe(cubeCode) });
        var items = A.Fake<IItemMatchCatalog>();
        foreach (var item in character.Items)
        {
            var fields = new ItemMatchFields((int)item.ItemResourceId,
                (ItemGroup)(item == cube ? 6 : 10), (ItemType)0, 1, ItemWearType.None,
                item == cube ? 0 : 1001);
            A.CallTo(() => items.TryGetFields(item.ItemResourceId, out fields)).Returns(true)
                .AssignsOutAndRefParameters(fields);
        }
        var enhanceRow = new EnhanceResourceEntity
        {
            Id = cubeCode == 700401 ? 400 : 560, RequiredItemId = cubeCode, MaxEnhance = 10,
            FailResult = failResult, Percentage = Enumerable.Repeat(0.5m, 25).ToArray()
        };
        var enhances = A.Fake<IEnhanceResourceCatalog>();
        A.CallTo(() => enhances.TryGetForServer(enhanceRow.Id, 1, out enhanceRow)).Returns(true)
            .AssignsOutAndRefParameters(enhanceRow);
        var crafting = new CraftingSocleService(characters, mixCatalog, items, enhances) { Roll = (_, _) => roll };
        return new(characters, crafting, character, main, second, cube, repository,
            new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
    }

    private static async Task Send(Harness h)
    {
        var client = StorageTestHarness.NewGameClient(h.Connection);
        var session = StorageTestHarness.Session(client);
        session.CharacterName = Crafter;
        session.CharacterHandle = 42;
        var frame = new byte[27];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0), 27);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), 256);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(7), (uint)h.Main.Id);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(11), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(13), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(15), (uint)h.Cube.Id);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(19), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(21), (uint)h.Second.Id);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(25), 1);
        await h.Crafting.HandleAsync(client, 256, frame);
    }

    private static ushort Id(byte[] frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4));

    [TestCase(false, 700401)]
    [TestCase(true, 700401)]
    [TestCase(false, 700402)]
    [TestCase(true, 700402)]
    public async Task Success_ConsumesTwoUnitsAndReportsTheNewCardHandle(bool sameStack, int cube)
    {
        var h = Build(sameStack: sameStack, cubeCode: cube, roll: 50000);
        await Send(h);
        var added = h.Character.Items.Single(item => item.Id == 900);
        added.Enhance.Should().Be(4);
        added.Amount.Should().Be(1);
        added.ItemResourceId.Should().Be(h.Main.ItemResourceId);
        added.SocketItemIds.Should().Equal(h.Main.SocketItemIds);
        added.SocketItemIds.Should().NotBeSameAs(h.Main.SocketItemIds);
        added.Endurance.Should().Be(71);
        added.EtherealDurability.Should().Be(29);
        added.RemainingTime.Should().Be(111);
        added.GenerateBySource.Should().Be(h.Main.GenerateBySource);
        h.Main.Enhance.Should().Be(3);
        h.Main.Amount.Should().Be(sameStack ? 2 : 3);
        h.Second.Amount.Should().Be(sameStack ? 2 : 3);
        h.Cube.Amount.Should().Be(3);
        h.Connection.Sent.Select(Id).Should().Equal(sameStack
            ? new ushort[] { 255, 255, 207, 257 } : new ushort[] { 255, 255, 255, 207, 257 });
        var result = h.Connection.Sent.Last();
        BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(7)).Should().Be(1);
        BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(11)).Should().Be(900);
        A.CallTo(() => h.Repository.SaveChangesAsync()).MustHaveHappenedOnceExactly();
    }

    [TestCase(0u, false)]
    [TestCase(3u, false)]
    [TestCase(3u, true)]
    [TestCase(4u, false)]
    [TestCase(4u, true)]
    [TestCase(9u, true)]
    public async Task Failure_DestroysOneCardOrCreatesOneDowngradedUnit(uint enhance, bool sameStack)
    {
        var h = Build(enhance, sameStack, roll: 50001);
        await Send(h);
        h.Main.Amount.Should().Be(sameStack ? 2 : 3);
        h.Main.Enhance.Should().Be(enhance);
        h.Cube.Amount.Should().Be(3);
        h.Second.Amount.Should().Be(sameStack ? 2 : 3);
        var result = h.Connection.Sent.Last();
        Id(result).Should().Be(257);
        BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(7)).Should().Be(0);
        if (enhance > 3)
        {
            var downgraded = h.Character.Items.Single(item => item.Id == 900);
            downgraded.Enhance.Should().Be(enhance - 3);
            downgraded.Amount.Should().Be(1);
            h.Connection.Sent.Count(frame => Id(frame) == 207).Should().Be(1);
        }
        else
        {
            h.Character.Items.Should().NotContain(item => item.Id == 900);
            h.Connection.Sent.Should().NotContain(frame => Id(frame) == (ushort)GamePackets.TM_SC_DESTROY_ITEM);
        }
        A.CallTo(() => h.Repository.SaveChangesAsync()).MustHaveHappenedOnceExactly();
    }

    [TestCase(0)]
    [TestCase(100000)]
    public async Task ExhaustingASharedStack_RemovesItOnceAndCannotReplayTheRequest(int roll)
    {
        var h = Build(sameStack: true, count: 2, roll: roll);
        await Send(h);
        h.Character.Items.Should().NotContain(item => item.Id == 100);
        h.Connection.Sent.Count(frame => Id(frame) == (ushort)GamePackets.TM_SC_DESTROY_ITEM).Should().Be(1);
        A.CallTo(() => h.Repository.DeleteItem(h.Main)).MustHaveHappenedOnceExactly();
        await Send(h);
        A.CallTo(() => h.Repository.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        Id(h.Connection.Sent.Last()).Should().Be(0);
    }

    [TestCase(1, 3u)]
    [TestCase(4, 10u)]
    public async Task InsufficientSharedStackOrEnhancementCap_ConsumesNothing(long amount, uint enhance)
    {
        var h = Build(enhance, sameStack: true, count: amount);
        await Send(h);
        h.Main.Amount.Should().Be(amount);
        h.Main.Enhance.Should().Be(enhance);
        h.Cube.Amount.Should().Be(amount);
        h.Connection.Sent.Should().ContainSingle();
        Id(h.Connection.Sent.Single()).Should().Be(0);
        A.CallTo(() => h.Repository.SaveChangesAsync()).MustNotHaveHappened();
    }

    [TestCase(1, false)]
    [TestCase(1, true)]
    [TestCase(3, false)]
    [TestCase(3, true)]
    public async Task OtherFailurePolicies_ConsumeTheSecondCardAndUpdateTheSurvivingMain(int fail, bool sameStack)
    {
        var h = Build(enhance: 4, sameStack: sameStack, roll: 100000, failResult: (FailResultType)fail);
        await Send(h);
        h.Main.Amount.Should().Be(sameStack ? 3 : 4);
        h.Second.Amount.Should().Be(3);
        h.Main.Enhance.Should().Be(fail == 3 ? 1u : 4u);
        ((int)h.Main.Flag).Should().Be(fail == 1 ? CraftingEngine.FailedFlagMask : 0);
        h.Character.Items.Should().NotContain(item => item.Id == 900);
        Id(h.Connection.Sent.Last()).Should().Be(257);
        A.CallTo(() => h.Repository.SaveChangesAsync()).MustHaveHappenedOnceExactly();
    }

    [TestCase("enhance")]
    [TestCase("code")]
    [TestCase("flag")]
    [TestCase("wear")]
    public async Task SecondCardChangedAfterPlanning_RefusesTheWholeCommit(string changed)
    {
        var h = Build();
        var main = Card();
        var second = Card(300, 400002);
        MixResourceMatcher.TryResolve(new[] { Recipe() }, main, new[] { Cube(), second }, out var resolution)
            .Should().BeTrue();
        var plan = CraftingEngine.Plan(resolution, main, new EnhanceResourceEntity
        {
            MaxEnhance = 10, RequiredItemId = 700401, FailResult = FailResultType.SkillCard,
            Percentage = Enumerable.Repeat(1m, 25).ToArray()
        }, (_, _) => 0);
        switch (changed)
        {
            case "enhance": h.Second.Enhance++; break;
            case "code": h.Second.ItemResourceId++; break;
            case "flag": h.Second.Flag = (ItemFlag)1; break;
            case "wear": h.Second.WearInfo = ItemWearType.Weapon; break;
        }
        (await h.Characters.ApplyCraftAsync(Crafter, plan.Consumed, plan.Change)).Outcome
            .Should().Be(CraftCommitOutcome.TargetChanged);
        h.Main.Amount.Should().Be(4);
        h.Cube.Amount.Should().Be(4);
        A.CallTo(() => h.Repository.SaveChangesAsync()).MustNotHaveHappened();
    }

    [TestCase(false, 3u, 0)]
    [TestCase(true, 3u, 0)]
    [TestCase(false, 3u, 100000)]
    [TestCase(true, 3u, 100000)]
    [TestCase(false, 4u, 100000)]
    [TestCase(true, 4u, 100000)]
    public async Task RealRepository_PersistsTheSplitAndGeneratedHandle(bool sameStack, uint enhance, int roll)
    {
        var h = Build(enhance, sameStack, count: sameStack ? 2 : 1);
        var options = new DbContextOptionsBuilder<TelecasterContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), o => o.EnableNullChecks(false)).Options;
        using (var seed = new TelecasterContext(options))
        {
            seed.Characters.Add(h.Character);
            await seed.SaveChangesAsync();
        }
        var service = new CharacterService(A.Fake<IStarterItemsRepository>(), new CharacterRepositoryFactory(options),
            new CharacterGate(), A.Fake<ILogger<CharacterService>>());
        var main = Card(enhance: enhance);
        var second = sameStack ? main : Card(300, 400002, enhance);
        MixResourceMatcher.TryResolve(new[] { Recipe() }, main, new[] { Cube(), second }, out var resolution)
            .Should().BeTrue();
        var plan = CraftingEngine.Plan(resolution, main, new EnhanceResourceEntity
        {
            RequiredItemId = 700401, MaxEnhance = 10, FailResult = FailResultType.SkillCard,
            Percentage = Enumerable.Repeat(0.5m, 25).ToArray()
        }, (_, _) => roll);
        var committed = await service.ApplyCraftAsync(Crafter, plan.Consumed, plan.Change);
        committed.Outcome.Should().Be(CraftCommitOutcome.Success);
        using var read = new TelecasterContext(options);
        var saved = await read.Items.ToListAsync();
        saved.Should().NotContain(item => item.Id == 100);
        saved.Should().NotContain(item => item.Id == 300);
        if (roll == 0 || enhance > 3)
        {
            var added = saved.Single(item => item.Id != 200);
            added.Id.Should().BePositive().And.NotBe(100);
            committed.Target.Id.Should().Be(added.Id);
            added.CharacterId.Should().Be(42);
            added.Amount.Should().Be(1);
            added.Enhance.Should().Be(roll == 0 ? enhance + 1 : enhance - 3);
            added.SocketItemIds.Should().Equal(42L, 0L, 17L, 0L);
        }
        else
        {
            committed.Target.Should().BeNull();
            saved.Select(item => item.Id).Should().Equal(sameStack ? new long[] { 200 } : Array.Empty<long>());
        }
    }

    [Test, Explicit("Requires the disposable PostgreSQL database navis_equipment_test")]
    public async Task ItemResourceProjection_CarriesTheActualSkillId()
    {
        var connection = Environment.GetEnvironmentVariable("NAVISLAMIA_ARCADIA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Ignore("Set NAVISLAMIA_ARCADIA_TEST_CONNECTION");
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connection);
        builder.Database.Should().Be("navis_equipment_test");
        var schema = "skill_card_test_" + Guid.NewGuid().ToString("N");
        await using var control = new Npgsql.NpgsqlConnection(builder.ConnectionString);
        await control.OpenAsync();
        // A private table exercises the real provider and projection without touching imported resources.
        await using var create = new Npgsql.NpgsqlCommand($"""
            CREATE SCHEMA {schema};
            CREATE TABLE {schema}."ItemResources" (
                "Id" bigint PRIMARY KEY, "Group" integer, "ItemType" integer, "Rank" integer,
                "WearType" integer, "SkillId" bigint, "DeletedOn" timestamp with time zone);
            INSERT INTO {schema}."ItemResources" ("Id", "Group", "ItemType", "Rank", "WearType", "SkillId") VALUES
                (400001, 10, 0, 1, -1, 1001), (400002, 10, 0, 1, -1, NULL);
            """, control);
        await create.ExecuteNonQueryAsync();
        try
        {
            builder.SearchPath = schema;
            var options = new DbContextOptionsBuilder<ArcadiaContext>().UseNpgsql(builder.ConnectionString).Options;
            var catalog = new ItemMatchCatalog(new ItemResourceRepository(options));
            catalog.TryGetFields(400001, out var known).Should().BeTrue();
            known.SkillId.Should().Be(1001);
            catalog.TryGetFields(400002, out var missing).Should().BeTrue();
            missing.SkillId.Should().Be(0);
        }
        finally
        {
            await using var drop = new Npgsql.NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", control);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Test]
    public void MissingSkillIds_DoNotMakeUnrelatedCardsMatch()
    {
        MixResourceMatcher.TryResolve(new[] { Recipe() }, Card(skill: 0),
            new[] { Cube(), Card(300, skill: 0) }, out _).Should().BeFalse();
    }
}
