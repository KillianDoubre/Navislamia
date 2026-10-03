using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

/// <summary>
/// The spare weapon set (wear positions 24..27) and <c>TM_CS_SWAP_EQUIP</c> (223), <c>onSwapEquip</c> of the
/// official server: a spare item gives nothing, and the swap judges it when it comes to the main slot
/// (docs/packet-specs/223-swap-equip.md).
/// </summary>
[TestFixture]
public class SpareEquipmentTests
{
    private const string Name = "Swapper";
    private const ushort Handle = 42;
    private const long Sword = 101100;
    private const long Bow = 101200;
    private const long Armor = 100201;
    private const long HighArmor = 100299;

    private static (CharacterService Service, ICharacterRepository Repository) Characters(params ItemEntity[] items)
    {
        var character = new CharacterEntity { CharacterName = Name, Items = items.ToList() };
        var repository = A.Fake<ICharacterRepository>();
        A.CallTo(() => repository.GetCharacterByNameWithItemsAsync(Name)).Returns(character);
        var factory = A.Fake<ICharacterRepositoryFactory>();
        A.CallTo(() => factory.Create()).Returns(repository);
        var service = new CharacterService(A.Fake<IStarterItemsRepository>(), factory, new CharacterGate(),
            A.Fake<ILogger<CharacterService>>());
        return (service, repository);
    }

    [Test]
    public async Task The_swap_exchanges_the_main_and_the_spare_sets()
    {
        var sword = new ItemEntity { Id = 1, ItemResourceId = Sword, WearInfo = ItemWearType.Weapon };
        var bow = new ItemEntity { Id = 2, ItemResourceId = Bow, WearInfo = ItemWearType.SpareWeapon };
        var shield = new ItemEntity { Id = 3, ItemResourceId = Armor, WearInfo = ItemWearType.SpareShield };
        var (service, repository) = Characters(sword, bow, shield);

        var result = await service.SwapEquipAsync(Name, _ => true);

        result.Should().NotBeNull();
        result!.Value.Moved.Should().BeEquivalentTo(new[] { sword, bow, shield });
        sword.WearInfo.Should().Be(ItemWearType.SpareWeapon);
        bow.WearInfo.Should().Be(ItemWearType.Weapon);
        shield.WearInfo.Should().Be(ItemWearType.Shield, "an empty main slot takes the spare item alone");
        A.CallTo(() => repository.SaveChangesAsync()).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task One_refused_spare_item_refuses_the_whole_swap()
    {
        var sword = new ItemEntity { Id = 1, ItemResourceId = Sword, WearInfo = ItemWearType.Weapon };
        var bow = new ItemEntity { Id = 2, ItemResourceId = Bow, WearInfo = ItemWearType.SpareWeapon };
        var (service, repository) = Characters(sword, bow);

        var result = await service.SwapEquipAsync(Name, item => item.ItemResourceId != Bow);

        result.Should().BeNull();
        sword.WearInfo.Should().Be(ItemWearType.Weapon);
        bow.WearInfo.Should().Be(ItemWearType.SpareWeapon);
        A.CallTo(() => repository.SaveChangesAsync()).MustNotHaveHappened();
    }

    [Test]
    public async Task Nothing_to_swap_saves_nothing()
    {
        var (service, repository) = Characters(new ItemEntity { Id = 1, ItemResourceId = Armor, WearInfo = ItemWearType.Armor });

        var result = await service.SwapEquipAsync(Name, _ => true);

        result!.Value.Moved.Should().BeEmpty();
        A.CallTo(() => repository.SaveChangesAsync()).MustNotHaveHappened();
    }

    [Test]
    public void A_spare_item_gives_no_stats()
    {
        var itemStats = A.Fake<IItemStatCatalog>();
        A.CallTo(() => itemStats.GetEffects(A<int>._)).Returns(new[] { new StatEffect(StatTarget.Defence, 40f, false) });
        var passives = A.Fake<ISkillPassiveCatalog>();
        A.CallTo(() => passives.Resolve(A<int>._, A<int>._, A<ItemType?>._)).Returns(Array.Empty<StatEffect>());
        var stats = new StatService(StatCatalogTestFactory.Create(), itemStats, passives, A.Fake<IStateCatalog>());
        var character = new CharacterEntity
        {
            Race = 4, Lv = 5, CurrentJob = (Job)StatCatalogTestFactory.KnownJob, PreviousJobs = new Job[3],
            JobLvs = new int[3],
            Items = new List<ItemEntity>
            {
                new() { ItemResourceId = Sword, WearInfo = ItemWearType.Weapon },
                new() { ItemResourceId = Bow, WearInfo = ItemWearType.SpareWeapon },
                new() { ItemResourceId = Armor, WearInfo = ItemWearType.SpareDecoShield }
            }
        };

        stats.Compute(character).ByItem.Defence.Should().Be(40, "only the main weapon counts");
    }

    [TestCase(ItemWearType.SpareWeapon, ItemWearType.Weapon)]
    [TestCase(ItemWearType.SpareShield, ItemWearType.Shield)]
    [TestCase(ItemWearType.SpareDecoWeapon, ItemWearType.DecoWeapon)]
    [TestCase(ItemWearType.SpareDecoShield, ItemWearType.DecoShield)]
    [TestCase(ItemWearType.Armor, ItemWearType.None)]
    public void A_spare_slot_doubles_its_main_slot(ItemWearType spare, ItemWearType main) =>
        EquipmentService.MainOf(spare).Should().Be(main);

    private static (EquipmentService Service, ICharacterService Characters, GameClient Client,
        StorageTestHarness.FrameConnection Connection) Equipment()
    {
        var items = new Dictionary<uint, ItemEntity>
        {
            [1] = new() { Id = 1, ItemResourceId = Sword, WearInfo = ItemWearType.None },
            [2] = new() { Id = 2, ItemResourceId = Armor, WearInfo = ItemWearType.None }
        };
        var characters = A.Fake<ICharacterService>();
        A.CallTo(() => characters.GetItemByHandleAsync(Name, A<uint>._))
            .ReturnsLazily((string _, uint handle) => Task.FromResult(items.TryGetValue(handle, out var item) ? item : null));
        A.CallTo(() => characters.EquipItemAsync(Name, A<uint>._, A<ItemWearType>._))
            .Returns(new EquipItemResult(EquipItemOutcome.NotFound, null, null, null));
        var resources = A.Fake<IItemResourceRepository>();
        var everyone = ItemRaceRestriction.Deva | ItemRaceRestriction.Asura | ItemRaceRestriction.Gaia;
        var classes = ItemJobRestriction.Fighter | ItemJobRestriction.Hunter | ItemJobRestriction.Magician |
                      ItemJobRestriction.Summoner;
        A.CallTo(() => resources.GetWearFields()).Returns(new[]
        {
            new ItemWearFields((int)Sword, ItemWearType.Weapon, 0, 0, 0, everyone, classes, 15),
            new ItemWearFields((int)Armor, ItemWearType.Armor, 0, 0, 0, everyone, classes, 15),
            new ItemWearFields((int)HighArmor, ItemWearType.Armor, 0, 50, 0, everyone, classes, 15)
        });
        var jobs = A.Fake<IJobResourceRepository>();
        A.CallTo(() => jobs.GetWearFields()).Returns(new[] { new JobWearFields(100, 1, 1), new JobWearFields(200, 3, 1),
            new JobWearFields(300, 2, 1) });
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection, characterService: characters);
        var session = StorageTestHarness.Session(client);
        session.CharacterName = Name;
        session.CharacterHandle = Handle;
        session.CharacterLevel = 10;
        session.CharacterHp = 100;
        session.CharacterRace = (int)Race.Gaia;
        var stats = A.Fake<IStatService>();
        A.CallTo(() => stats.Compute(A<ConnectionInfo>._)).Returns(new CharacterStatResult(new StatBlock(), new StatBlock()));
        var service = new EquipmentService(characters, stats, new ItemWearCatalog(resources),
            A.Fake<IPlayerVisibilityService>(), jobs);
        return (service, characters, client, connection);
    }

    private static ushort Id(byte[] frame) => BitConverter.ToUInt16(frame, 4);

    [Test]
    public async Task A_spare_slot_takes_an_item_of_its_main_slot_only()
    {
        var (service, characters, client, _) = Equipment();

        await service.EquipAsync(client, new GameActionPackets.PutonItemRequest((sbyte)ItemWearType.SpareWeapon, 1, 0));
        A.CallTo(() => characters.EquipItemAsync(Name, 1u, ItemWearType.SpareWeapon)).MustHaveHappenedOnceExactly();

        await service.EquipAsync(client, new GameActionPackets.PutonItemRequest((sbyte)ItemWearType.SpareWeapon, 2, 0));
        A.CallTo(() => characters.EquipItemAsync(Name, 2u, A<ItemWearType>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task A_dead_character_cannot_swap()
    {
        var (service, characters, client, connection) = Equipment();
        StorageTestHarness.Session(client).CharacterHp = 0;

        await service.SwapAsync(client);

        A.CallTo(() => characters.SwapEquipAsync(A<string>._, A<Func<ItemEntity, bool>>._)).MustNotHaveHappened();
        var result = connection.Sent.Single(frame => Id(frame) == (ushort)GamePackets.TM_SC_RESULT);
        BitConverter.ToUInt16(result, 7).Should().Be((ushort)GamePackets.TM_CS_SWAP_EQUIP);
        BitConverter.ToUInt16(result, 9).Should().Be((ushort)ResultCode.NotActable);
    }

    [Test]
    public async Task A_refused_swap_answers_and_a_swap_sends_the_wear_of_each_moved_item()
    {
        var (service, characters, client, connection) = Equipment();
        A.CallTo(() => characters.SwapEquipAsync(Name, A<Func<ItemEntity, bool>>._))
            .Returns(Task.FromResult<(CharacterEntity, IReadOnlyList<ItemEntity>)?>(null));
        await service.SwapAsync(client);
        connection.Sent.Should().ContainSingle(frame => Id(frame) == (ushort)GamePackets.TM_SC_RESULT);

        connection.Sent.Clear();
        var moved = new[]
        {
            new ItemEntity { Id = 1, ItemResourceId = Sword, WearInfo = ItemWearType.SpareWeapon },
            new ItemEntity { Id = 5, ItemResourceId = Sword, WearInfo = ItemWearType.Weapon }
        };
        var character = new CharacterEntity
        {
            CharacterName = Name, Items = moved.ToList(), PreviousJobs = new Job[3], JobLvs = new int[3]
        };
        A.CallTo(() => characters.SwapEquipAsync(Name, A<Func<ItemEntity, bool>>._))
            .Returns(Task.FromResult<(CharacterEntity, IReadOnlyList<ItemEntity>)?>((character, moved)));
        await service.SwapAsync(client);

        connection.Sent.Count(frame => Id(frame) == (ushort)GamePackets.TM_SC_ITEM_WEAR_INFO).Should().Be(2);
        connection.Sent.Should().NotContain(frame => Id(frame) == (ushort)GamePackets.TM_SC_RESULT);
    }

    [Test]
    public async Task At_world_entry_an_item_that_no_longer_qualifies_goes_back_to_the_bag()
    {
        var (service, characters, _, _) = Equipment();
        var sword = new ItemEntity { Id = 1, ItemResourceId = Sword, WearInfo = ItemWearType.Weapon };
        var high = new ItemEntity { Id = 2, ItemResourceId = HighArmor, WearInfo = ItemWearType.Armor };
        var twice = new ItemEntity { Id = 3, ItemResourceId = Sword, WearInfo = ItemWearType.Weapon };
        var spare = new ItemEntity { Id = 4, ItemResourceId = HighArmor, WearInfo = ItemWearType.SpareWeapon };
        var summons = new ItemEntity { Id = 5, ItemResourceId = HighArmor, WearInfo = ItemWearType.Armor, EquippedBySummonId = 9 };
        var bag = new ItemEntity { Id = 6, ItemResourceId = HighArmor, WearInfo = ItemWearType.None };
        var character = new CharacterEntity
        {
            CharacterName = Name, Lv = 10, Race = (int)Race.Gaia, CurrentJob = 0,
            Items = new List<ItemEntity> { twice, spare, summons, bag, high, sword }
        };

        service.FindUnwearableItems(character).Should().BeEquivalentTo(new[] { high, twice },
            "level 50 armour at level 10, and a second item in the weapon slot");

        await service.RevalidateWornItemsAsync(character);

        sword.WearInfo.Should().Be(ItemWearType.Weapon);
        high.WearInfo.Should().Be(ItemWearType.None);
        twice.WearInfo.Should().Be(ItemWearType.None);
        spare.WearInfo.Should().Be(ItemWearType.SpareWeapon, "the swap judges a spare item, not the login");
        summons.WearInfo.Should().Be(ItemWearType.Armor, "the summon's item is the summon's");
        A.CallTo(() => characters.UnwearItemsAsync(Name, A<IReadOnlyCollection<long>>.That.Matches(ids =>
            ids.Count == 2 && ids.Contains(2) && ids.Contains(3)))).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task At_world_entry_nothing_is_saved_when_everything_qualifies()
    {
        var (service, characters, _, _) = Equipment();
        var character = new CharacterEntity
        {
            CharacterName = Name, Lv = 60, Race = (int)Race.Gaia,
            Items = new List<ItemEntity> { new() { Id = 2, ItemResourceId = HighArmor, WearInfo = ItemWearType.Armor } }
        };

        await service.RevalidateWornItemsAsync(character);

        A.CallTo(() => characters.UnwearItemsAsync(A<string>._, A<IReadOnlyCollection<long>>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task UnwearItems_takes_the_items_off_in_one_save()
    {
        var worn = new ItemEntity { Id = 2, ItemResourceId = HighArmor, WearInfo = ItemWearType.Armor };
        var kept = new ItemEntity { Id = 3, ItemResourceId = Sword, WearInfo = ItemWearType.Weapon };
        var (service, repository) = Characters(worn, kept);

        await service.UnwearItemsAsync(Name, new long[] { 2 });

        worn.WearInfo.Should().Be(ItemWearType.None);
        kept.WearInfo.Should().Be(ItemWearType.Weapon);
        A.CallTo(() => repository.SaveChangesAsync()).MustHaveHappenedOnceExactly();
    }
}
