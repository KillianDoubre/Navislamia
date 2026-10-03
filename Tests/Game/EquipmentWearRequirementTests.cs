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
/// <c>docs/packet-specs/socle-exigences-equipement.md</c> §5.2-5.3), including class, race and job depth.
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
        var fields = Wear(100201, ItemWearType.Armor, rank, useMinLevel, useMaxLevel);

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
        // before the item is read, and the code is InvalidArgument, not the official's 5. 24..27 are the
        // spare slots now, so the first position outside the wear info is 28.
        var harness = Build(300, Items((HeadyHandle, HeadyResource)),
            Wear(HeadyResource, ItemWearType.Armor, rank: 0));

        await harness.Service.EquipAsync(harness.Client, new GameActionPackets.PutonItemRequest(28, HeadyHandle, 0));

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

    [TestCase(3, 4, true)] [TestCase(3, 1, false)] [TestCase(3, 0, false)]
    [TestCase(4, 1, true)] [TestCase(4, 2, false)]
    [TestCase(5, 2, true)] [TestCase(5, 4, false)]
    [TestCase(0, 7, false)] [TestCase(6, 7, false)]
    public void Race_whitelist_requires_the_players_race_bit(int race, int mask, bool allowed)
    {
        var item = Wear(ArmorResource, ItemWearType.Armor) with { RaceRestriction = (ItemRaceRestriction)mask };
        ItemWearRules.IsWearAllowed(item, 300, race, 1, 1).Should().Be(allowed);
    }

    [TestCase(1, 1024, true)] [TestCase(1, 2048, false)]
    [TestCase(2, 2048, true)] [TestCase(2, 1024, false)]
    [TestCase(3, 4096, true)] [TestCase(3, 8192, false)]
    [TestCase(4, 8192, true)] [TestCase(4, 4096, false)]
    [TestCase(1, 0, false)] [TestCase(0, 15360, false)] [TestCase(5, 15360, false)]
    [TestCase(3, 5120, true)]
    public void Class_whitelist_requires_the_current_jobs_class_bit(int jobClass, int mask, bool allowed)
    {
        var item = Wear(ArmorResource, ItemWearType.Armor) with { JobRestriction = (ItemJobRestriction)mask };
        ItemWearRules.IsWearAllowed(item, 300, 3, jobClass, 1).Should().Be(allowed);
    }

    [TestCase(0, 15, true)] [TestCase(1, 15, true)] [TestCase(2, 15, true)] [TestCase(3, 15, true)]
    [TestCase(3, 8, true)] [TestCase(2, 8, false)] [TestCase(1, 8, false)] [TestCase(0, 8, false)]
    [TestCase(3, 0, false)] [TestCase(-1, 15, false)] [TestCase(4, 15, false)] [TestCase(0, 1, true)]
    public void Depth_whitelist_shifts_the_depth_index_like_the_official_server(int depth, short mask, bool allowed)
    {
        var item = Wear(ArmorResource, ItemWearType.Armor) with { JobDepth = mask };
        ItemWearRules.IsWearAllowed(item, 300, 3, 1, depth).Should().Be(allowed);
    }

    [TestCase(false, "race")] [TestCase(true, "race")]
    [TestCase(false, "class")] [TestCase(true, "class")]
    [TestCase(false, "depth")] [TestCase(true, "depth")]
    [TestCase(false, "empty")] [TestCase(true, "empty")]
    [TestCase(false, "unknown_job")] [TestCase(true, "unknown_job")]
    public async Task Both_equipment_paths_refuse_unmet_whitelists_before_changing_inventory(bool set, string cause)
    {
        var item = Wear(ArmorResource, ItemWearType.Armor);
        item = cause switch
        {
            "race" => item with { RaceRestriction = ItemRaceRestriction.Deva },
            "class" => item with { JobRestriction = ItemJobRestriction.Magician },
            "depth" => item with { JobDepth = 8 },
            "empty" => item with { RaceRestriction = 0, JobRestriction = 0 },
            _ => item
        };
        var h = Build(300, Items((ArmorHandle, ArmorResource)), item);
        if (cause == "unknown_job") StorageTestHarness.Session(h.Client).CharacterJob = 999999;
        if (set) await h.Service.EquipSetAsync(h.Client, new[] { ArmorHandle });
        else await h.Service.EquipAsync(h.Client, new GameActionPackets.PutonItemRequest(2, ArmorHandle, 0));
        var result = SingleResult(h); result.Result.Should().Be((ushort)ResultCode.NotActable);
        result.RequestMsgID.Should().Be(set ? (ushort)281 : (ushort)200);
        A.CallTo(() => h.Characters.EquipItemAsync(Character, A<uint>._, A<ItemWearType>._)).MustNotHaveHappened();
    }

    [TestCase(false, 3, 100, 1, 1)] [TestCase(true, 3, 100, 1, 1)]
    [TestCase(false, 4, 200, 3, 1)] [TestCase(true, 5, 300, 2, 1)]
    [TestCase(false, 3, 123, 4, 8)] [TestCase(true, 3, 123, 4, 8)]
    [TestCase(false, 3, 0, 1, 1)] [TestCase(true, 4, 0, 3, 1)] [TestCase(false, 5, 0, 2, 1)]
    public async Task Both_equipment_paths_resolve_class_and_depth_from_the_job_table(
        bool set, int race, int job, int jobClass, short depth)
    {
        var item = Wear(ArmorResource, ItemWearType.Armor) with
        {
            RaceRestriction = race switch { 3 => ItemRaceRestriction.Gaia, 4 => ItemRaceRestriction.Deva, _ => ItemRaceRestriction.Asura },
            JobRestriction = (ItemJobRestriction)(1 << (jobClass + 9)), JobDepth = depth
        };
        var h = Build(300, Items((ArmorHandle, ArmorResource)), item);
        var info = StorageTestHarness.Session(h.Client); info.CharacterRace = race; info.CharacterJob = job;
        if (set) await h.Service.EquipSetAsync(h.Client, new[] { ArmorHandle });
        else await h.Service.EquipAsync(h.Client, new GameActionPackets.PutonItemRequest(2, ArmorHandle, 0));
        A.CallTo(() => h.Characters.EquipItemAsync(Character, ArmorHandle, ItemWearType.Armor)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task The_current_job_is_checked_again_after_a_class_change()
    {
        var item = Wear(ArmorResource, ItemWearType.Armor) with { JobRestriction = ItemJobRestriction.Fighter };
        var h = Build(300, Items((ArmorHandle, ArmorResource)), item);
        await h.Service.EquipAsync(h.Client, new GameActionPackets.PutonItemRequest(2, ArmorHandle, 0));
        StorageTestHarness.Session(h.Client).CharacterJob = 123;
        await h.Service.EquipAsync(h.Client, new GameActionPackets.PutonItemRequest(2, ArmorHandle, 0));
        A.CallTo(() => h.Characters.EquipItemAsync(Character, ArmorHandle, ItemWearType.Armor)).MustHaveHappenedOnceExactly();
        new Packet<TS_SC_RESULT>(h.Connection.Sent[^1]).GetDataStruct<TS_SC_RESULT>().Result.Should().Be((ushort)ResultCode.NotActable);
    }

    [Test]
    public async Task A_set_continues_with_an_allowed_piece_after_a_race_refusal()
    {
        var h = Build(300, Items((ArmorHandle, ArmorResource), (HeadyHandle, HeadyResource)),
            Wear(ArmorResource, ItemWearType.Armor) with { RaceRestriction = ItemRaceRestriction.Deva },
            Wear(HeadyResource, ItemWearType.Armor));
        await h.Service.EquipSetAsync(h.Client, new[] { ArmorHandle, HeadyHandle });
        A.CallTo(() => h.Characters.EquipItemAsync(Character, ArmorHandle, A<ItemWearType>._)).MustNotHaveHappened();
        A.CallTo(() => h.Characters.EquipItemAsync(Character, HeadyHandle, A<ItemWearType>._)).MustHaveHappenedOnceExactly();
    }

    private static ItemWearFields Wear(long resourceId, ItemWearType wearType, int rank = 0,
        int useMinLevel = 0, int useMaxLevel = 0)
    {
        return new ItemWearFields((int)resourceId, wearType, rank, useMinLevel, useMaxLevel,
            ItemRaceRestriction.Deva | ItemRaceRestriction.Asura | ItemRaceRestriction.Gaia,
            ItemJobRestriction.Fighter | ItemJobRestriction.Hunter | ItemJobRestriction.Magician | ItemJobRestriction.Summoner, 15);
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

    [TestCase(new short[] { 0, 1, 2, 3 }, false)] [TestCase(new short[] { 1, 2, 4, 8 }, true)]
    [TestCase(new short[] { 1, 2 }, true)] [TestCase(new short[] { 0, 1 }, false)]
    public void The_job_depth_encoding_is_read_from_the_whole_table(short[] depths, bool flags)
    {
        JobDepths.AreFlags(depths).Should().Be(flags);
        JobDepths.ToIndex(flags ? (short)1 : (short)0, flags).Should().Be(0);
        JobDepths.ToIndex(flags ? (short)8 : (short)3, flags).Should().Be(3);
    }

    [TestCase(100, (short)1, true)] [TestCase(100, (short)2, false)]
    [TestCase(101, (short)2, true)] [TestCase(120, (short)8, true)] [TestCase(120, (short)4, false)]
    public async Task A_job_table_holding_the_9_4_depth_index_is_shifted_like_the_official(int job, short mask,
        bool allowed)
    {
        // The 9.4 export: job 100 → depth 0, 101 → 1, 110 → 2, 120 → 3.
        var item = Wear(ArmorResource, ItemWearType.Armor) with { JobDepth = mask };
        var h = Build(300, Items((ArmorHandle, ArmorResource)), new[] { new JobWearFields(100, 1, 0),
            new JobWearFields(101, 1, 1), new JobWearFields(110, 1, 2), new JobWearFields(120, 1, 3) }, item);
        StorageTestHarness.Session(h.Client).CharacterJob = job;
        await h.Service.EquipAsync(h.Client, new GameActionPackets.PutonItemRequest(2, ArmorHandle, 0));
        A.CallTo(() => h.Characters.EquipItemAsync(Character, ArmorHandle, ItemWearType.Armor))
            .MustHaveHappened(allowed ? 1 : 0, Times.Exactly);
    }

    private static Harness Build(int characterLevel, IReadOnlyDictionary<uint, ItemEntity> items,
        params ItemWearFields[] fields) => Build(characterLevel, items, null, fields);

    private static Harness Build(int characterLevel, IReadOnlyDictionary<uint, ItemEntity> items,
        JobWearFields[] jobTable, params ItemWearFields[] fields)
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
        session.CharacterRace = (int)Race.Gaia;

        var jobs = A.Fake<IJobResourceRepository>();
        A.CallTo(() => jobs.GetWearFields()).Returns(jobTable ?? new[] { new JobWearFields(100, 1, 1),
            new JobWearFields(200, 3, 1), new JobWearFields(300, 2, 1), new JobWearFields(123, 4, 8) });

        var service = new EquipmentService(characters, A.Fake<IStatService>(), new ItemWearCatalog(repository),
            A.Fake<IPlayerVisibilityService>(), jobs);

        return new Harness(service, connection, characters, client);
    }

    private sealed record Harness(EquipmentService Service, StorageTestHarness.FrameConnection Connection,
        ICharacterService Characters, GameClient Client);
}
