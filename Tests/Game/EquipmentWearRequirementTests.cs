using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;

namespace Tests.Game;

/// <summary>
/// The requirements <c>TM_CS_PUTON_ITEM</c> (200) and <c>TM_CS_PUTON_ITEM_SET</c> (281) judge before
/// equipping — the level floor of the official Epic 7 Part 4 server
/// (<c>StructCreature::TranslateWearPosition</c>, sheet
/// <c>docs/packet-specs/socle-exigences-equipement.md</c> §5.2). Race, class and job depth are the
/// sheet's lot 2: they are not judged here, and their columns are empty in this server's data.
/// </summary>
[TestFixture]
public class EquipmentWearRequirementTests
{
    private const string Character = "Guardsman";
    private const ushort CharacterHandle = 42;
    private const long ArmorResource = 100201;
    private const long HeadyResource = 100202;
    private const long UnknownResource = 999999;
    private const uint ArmorHandle = 0x80000001u;
    private const uint HeadyHandle = 0x80000002u;
    private const uint UnknownHandle = 0x80000003u;

    // ---- the rank table of the official server ------------------------------------------------

    [TestCase(0, 0)]
    [TestCase(1, 0)]
    [TestCase(2, 20)]
    [TestCase(3, 50)]
    [TestCase(4, 80)]
    [TestCase(5, 100)]
    [TestCase(6, 120)]
    [TestCase(7, 150)]
    [TestCase(8, 170)]
    public void RankLevelFloor_IsTheTableOfTheOfficialServer(int rank, int expected)
    {
        // 0x1404f7fd8, read by StructItem::GetLevelLimit (0x1400ad250). NGemity says 180 at rank 8
        // (GameRule.cpp:102-103): the official server decides, and it says 170.
        ItemWearRules.RankLevelFloor(rank).Should().Be(expected);
    }

    [Test]
    public void RankLevelFloor_ReadsTheNearestEndOutsideTheTable()
    {
        // GetLevelLimit clamps the rank to 1..8 before reading the eight integers: a rank outside the
        // table reads an end instead of running off the array.
        ItemWearRules.RankLevelFloor(-1).Should().Be(0);
        ItemWearRules.RankLevelFloor(9).Should().Be(170);
        ItemWearRules.RankLevelFloor(int.MaxValue).Should().Be(170);
    }

    // ---- the predicate table: rank floor, use_min_level, use_max_level -------------------------

    [TestCase(0, 0, 0, 0, true)]
    [TestCase(0, 0, 0, 300, true)]
    [TestCase(1, 0, 0, 0, true)]
    [TestCase(2, 0, 0, 19, false)]
    [TestCase(2, 0, 0, 20, true)]
    [TestCase(3, 0, 0, 49, false)]
    [TestCase(3, 0, 0, 50, true)]
    [TestCase(4, 0, 0, 79, false)]
    [TestCase(4, 0, 0, 80, true)]
    [TestCase(5, 0, 0, 99, false)]
    [TestCase(5, 0, 0, 100, true)]
    [TestCase(6, 0, 0, 119, false)]
    [TestCase(6, 0, 0, 120, true)]
    [TestCase(7, 0, 0, 149, false)]
    [TestCase(7, 0, 0, 150, true)]
    [TestCase(8, 0, 0, 169, false)]
    [TestCase(8, 0, 0, 170, true)]
    [TestCase(8, 0, 0, 300, true)]
    [TestCase(0, 1, 0, 0, false)]
    [TestCase(0, 1, 0, 1, true)]
    [TestCase(0, 20, 0, 19, false)]
    [TestCase(0, 20, 0, 20, true)]
    [TestCase(0, 155, 0, 154, false)]
    [TestCase(0, 155, 0, 155, true)]
    [TestCase(0, 160, 0, 159, false)]
    [TestCase(0, 160, 0, 160, true)]
    [TestCase(2, 155, 0, 154, false)]
    [TestCase(2, 155, 0, 200, true)]
    [TestCase(0, 0, 300, 300, true)]
    [TestCase(0, 0, 300, 301, false)]
    [TestCase(0, 0, 1, 2, false)]
    [TestCase(8, 0, 300, 170, true)]
    [TestCase(8, 0, 300, 301, false)]
    [TestCase(7, 160, 0, 155, false)]
    [TestCase(7, 160, 0, 160, true)]
    [TestCase(7, 100, 0, 155, true)]
    [TestCase(6, 100, 120, 120, true)]
    [TestCase(6, 100, 120, 121, false)]
    public void IsWearAllowed_JudgesTheFloorAndTheWindow(int rank, int useMinLevel, int useMaxLevel,
        int level, bool expected)
    {
        var fields = new ItemWearFields(100201, ItemWearType.Armor, rank, useMinLevel, useMaxLevel);

        ItemWearRules.IsWearAllowed(fields, level).Should().Be(expected,
            "rank {0} (floor {1}), use_min_level {2}, use_max_level {3} at level {4}", rank,
            ItemWearRules.RankLevelFloor(rank), useMinLevel, useMaxLevel, level);
    }

    [Test]
    public void IsWearAllowed_RefusesAnItemThatDeclaresNoPort()
    {
        // §5.2-1: wear_type -1 (ItemWearType.CantWear) is refused before anything else, whatever the
        // level is: 3 967 rows of the Epic 7.3 data carry it.
        var fields = Wear(700001, ItemWearType.CantWear);

        ItemWearRules.IsWearAllowed(fields, 300).Should().BeFalse();
    }

    [Test]
    public void IsWearAllowed_KeepsTheFloorWhenTheMinimumLevelIsLower()
    {
        // The floor is max(rank floor, use_min_level): a rank the character cannot wear yet is not
        // rescued by a use_min_level below it.
        var byRank = Wear(100201, ItemWearType.Armor, rank: 8, useMinLevel: 1);
        var byMinimum = Wear(100202, ItemWearType.Armor, rank: 1, useMinLevel: 160);

        ItemWearRules.IsWearAllowed(byRank, 160).Should().BeFalse();
        ItemWearRules.IsWearAllowed(byMinimum, 160).Should().BeTrue();
        ItemWearRules.IsWearAllowed(byMinimum, 159).Should().BeFalse();
    }

    [Test]
    public void Wear_KeepsTheFieldsInTheOrderOfTheRecord()
    {
        // Guards the positional record: swapping rank and use_min_level would move every floor.
        var fields = Wear(100201, ItemWearType.Weapon, rank: 7, useMinLevel: 160, useMaxLevel: 300);

        fields.Rank.Should().Be(7);
        fields.UseMinLevel.Should().Be(160);
        fields.UseMaxLevel.Should().Be(300);
    }

    // ---- 200: the branch in EquipmentService.EquipAsync ---------------------------------------

    [Test]
    public async Task EquipAsync_RefusesAnItemBelowItsRankFloorWithNotActable()
    {
        var harness = Build(5, Items((HeadyHandle, HeadyResource)),
            Wear(HeadyResource, ItemWearType.Armor, rank: 8));

        await harness.Service.EquipAsync(harness.Client, new GameActionPackets.PutonItemRequest(2, HeadyHandle, 0));

        var result = SingleResult(harness);
        result.RequestMsgID.Should().Be((ushort)GamePackets.TM_CS_PUTON_ITEM);
        result.Result.Should().Be((ushort)ResultCode.NotActable);
        A.CallTo(() => harness.Characters.EquipItemAsync(Character, HeadyHandle, A<ItemWearType>._))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task EquipAsync_RefusesAnItemAboveItsMaxLevelWithNotActable()
    {
        var harness = Build(150, Items((ArmorHandle, ArmorResource)),
            Wear(ArmorResource, ItemWearType.Armor, rank: 0, useMinLevel: 0, useMaxLevel: 100));

        await harness.Service.EquipAsync(harness.Client, new GameActionPackets.PutonItemRequest(2, ArmorHandle, 0));

        SingleResult(harness).Result.Should().Be((ushort)ResultCode.NotActable);
        A.CallTo(() => harness.Characters.EquipItemAsync(Character, ArmorHandle, A<ItemWearType>._))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task EquipAsync_RefusesAnItemThatDeclaresNoPort()
    {
        var harness = Build(300, Items((ArmorHandle, ArmorResource)),
            Wear(ArmorResource, ItemWearType.CantWear, rank: 0));

        await harness.Service.EquipAsync(harness.Client, new GameActionPackets.PutonItemRequest(2, ArmorHandle, 0));

        SingleResult(harness).Result.Should().Be((ushort)ResultCode.NotActable);
        A.CallTo(() => harness.Characters.EquipItemAsync(Character, ArmorHandle, A<ItemWearType>._))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task EquipAsync_EquipsAnItemThatMeetsItsRequirements()
    {
        var harness = Build(170, Items((HeadyHandle, HeadyResource)),
            Wear(HeadyResource, ItemWearType.Armor, rank: 8));

        await harness.Service.EquipAsync(harness.Client, new GameActionPackets.PutonItemRequest(2, HeadyHandle, 0));

        A.CallTo(() => harness.Characters.EquipItemAsync(Character, HeadyHandle, ItemWearType.Armor))
            .MustHaveHappenedOnceExactly();
        // The answer is the equip path's own (the fake answers NotFound → AccessDenied): the gate let
        // the request through instead of refusing it itself.
        SingleResult(harness).Result.Should().Be((ushort)ResultCode.AccessDenied);
    }

    [Test]
    public async Task EquipAsync_LeavesAHandleTheCharacterDoesNotOwnToTheEquipPath()
    {
        // An unknown handle belongs to the equip path (AccessDenied): the requirements gate must not
        // turn it into NotActable, and it cannot judge an item it cannot read.
        var harness = Build(5, Items(), Wear(ArmorResource, ItemWearType.Armor, rank: 8));

        await harness.Service.EquipAsync(harness.Client, new GameActionPackets.PutonItemRequest(2, UnknownHandle, 0));

        A.CallTo(() => harness.Characters.EquipItemAsync(Character, UnknownHandle, A<ItemWearType>._))
            .MustHaveHappenedOnceExactly();
        SingleResult(harness).Result.Should().Be((ushort)ResultCode.AccessDenied);
    }

    [Test]
    public async Task EquipAsync_LeavesAResourceNoCatalogKnowsToTheEquipPath()
    {
        // An item the catalog cannot judge is left to the equip path rather than refused by a rule
        // that could not be read — the same choice as ICharacterService.UnbindSkillCardAsync.
        var harness = Build(5, Items((UnknownHandle, UnknownResource)),
            Wear(ArmorResource, ItemWearType.Armor, rank: 8));

        await harness.Service.EquipAsync(harness.Client, new GameActionPackets.PutonItemRequest(2, UnknownHandle, 0));

        A.CallTo(() => harness.Characters.EquipItemAsync(Character, UnknownHandle, A<ItemWearType>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task EquipAsync_AnswersAPositionOutsideTheWearInfoBeforeReadingTheItem()
    {
        // The established order of the depot, kept as it is (§7.4 of the sheet): the position is judged
        // before the item is read, and the code is InvalidArgument, not the official's 5.
        var harness = Build(300, Items((HeadyHandle, HeadyResource)),
            Wear(HeadyResource, ItemWearType.Armor, rank: 0));

        await harness.Service.EquipAsync(harness.Client, new GameActionPackets.PutonItemRequest(24, HeadyHandle, 0));

        SingleResult(harness).Result.Should().Be((ushort)ResultCode.InvalidArgument);
        A.CallTo(() => harness.Characters.GetItemByHandleAsync(Character, A<uint>._)).MustNotHaveHappened();
    }

    // ---- 281: the branch in EquipmentService.EquipSetAsync ------------------------------------

    [Test]
    public async Task EquipSetAsync_RefusesAHandleBelowItsFloorWithNotActable()
    {
        var harness = Build(5, Items((HeadyHandle, HeadyResource)),
            Wear(HeadyResource, ItemWearType.Armor, rank: 8));

        await harness.Service.EquipSetAsync(harness.Client, new[] { HeadyHandle });

        var result = SingleResult(harness);
        result.RequestMsgID.Should().Be((ushort)GamePackets.TM_CS_PUTON_ITEM_SET);
        result.Result.Should().Be((ushort)ResultCode.NotActable);
        A.CallTo(() => harness.Characters.EquipItemAsync(Character, HeadyHandle, A<ItemWearType>._))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task EquipSetAsync_KeepsJudgingTheHandlesAfterARefusedOne()
    {
        // A refused handle does not stop the following ones (§5.6-3): the expensive piece is refused,
        // the cheap one is still handed to the equip path, and the answer reports the refusal.
        var harness = Build(5, Items((HeadyHandle, HeadyResource), (ArmorHandle, ArmorResource)),
            Wear(HeadyResource, ItemWearType.Armor, rank: 8), Wear(ArmorResource, ItemWearType.Armor, rank: 0));

        await harness.Service.EquipSetAsync(harness.Client, new[] { HeadyHandle, ArmorHandle });

        A.CallTo(() => harness.Characters.EquipItemAsync(Character, HeadyHandle, A<ItemWearType>._))
            .MustNotHaveHappened();
        A.CallTo(() => harness.Characters.EquipItemAsync(Character, ArmorHandle, ItemWearType.Armor))
            .MustHaveHappenedOnceExactly();
        SingleResult(harness).Result.Should().Be((ushort)ResultCode.NotActable);
    }

    [Test]
    public async Task EquipSetAsync_JudgesEveryHandleOfTheSet()
    {
        var harness = Build(170, Items((HeadyHandle, HeadyResource)),
            Wear(HeadyResource, ItemWearType.Armor, rank: 8, useMinLevel: 0, useMaxLevel: 300));

        await harness.Service.EquipSetAsync(harness.Client, new[] { 0u, HeadyHandle });

        A.CallTo(() => harness.Characters.EquipItemAsync(Character, HeadyHandle, ItemWearType.Armor))
            .MustHaveHappenedOnceExactly();
        SingleResult(harness).RequestMsgID.Should().Be((ushort)GamePackets.TM_CS_PUTON_ITEM_SET);
    }

    // ---- the harness --------------------------------------------------------------------------

    private static ItemWearFields Wear(long resourceId, ItemWearType wearType, int rank = 0,
        int useMinLevel = 0, int useMaxLevel = 0)
    {
        return new ItemWearFields((int)resourceId, wearType, rank, useMinLevel, useMaxLevel);
    }

    private static Dictionary<uint, ItemEntity> Items(params (uint Handle, long ResourceId)[] items)
    {
        var dictionary = new Dictionary<uint, ItemEntity>();
        foreach (var (handle, resourceId) in items)
        {
            dictionary[handle] = new ItemEntity { Id = (int)(handle & 0xFFFF), ItemResourceId = resourceId };
        }

        return dictionary;
    }

    private static TS_SC_RESULT SingleResult(Harness harness)
    {
        harness.Connection.Sent.Should().ContainSingle("the port answers its own request id");
        return new Packet<TS_SC_RESULT>(harness.Connection.Sent[0]).GetDataStruct<TS_SC_RESULT>();
    }

    private static Harness Build(int characterLevel, IReadOnlyDictionary<uint, ItemEntity> items,
        params ItemWearFields[] fields)
    {
        var characters = A.Fake<ICharacterService>();
        A.CallTo(() => characters.GetItemByHandleAsync(Character, A<uint>._))
            .ReturnsLazily((string _, uint handle) =>
                Task.FromResult(items.TryGetValue(handle, out var item) ? item : null));
        A.CallTo(() => characters.EquipItemAsync(Character, A<uint>._, A<ItemWearType>._))
            .Returns(new EquipItemResult(EquipItemOutcome.NotFound, null, null, null));

        var repository = A.Fake<IItemResourceRepository>();
        A.CallTo(() => repository.GetWearFields()).Returns(fields);

        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection, characterService: characters);
        var session = StorageTestHarness.Session(client);
        session.CharacterName = Character;
        session.CharacterHandle = CharacterHandle;
        session.CharacterLevel = characterLevel;

        var service = new EquipmentService(characters, A.Fake<IStatService>(), new ItemWearCatalog(repository),
            A.Fake<IPlayerVisibilityService>());

        return new Harness(service, connection, characters, client);
    }

    private sealed record Harness(EquipmentService Service, StorageTestHarness.FrameConnection Connection,
        ICharacterService Characters, GameClient Client);
}
