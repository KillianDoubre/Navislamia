using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Creatures;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

/// <summary>
/// A summon's equipment (<c>StructSummon::TranslateWearPosition</c>, <c>onPutonItem</c>/<c>onPutoffItem</c> with a
/// summon target): card-form items, the summon's level, its slots, and the master's items kept apart.
/// docs/packet-specs/socle-equipement-invocation.md.
/// </summary>
[TestFixture]
public class SummonEquipmentTests
{
    private const long SummonArmor = 690001;
    private const long Necklace = 690002;
    private const long HighArmor = 690003;
    private const uint CardFlag = 1;

    [Test]
    public void The_server_places_an_item_in_its_range_and_refuses_a_second_of_its_group()
    {
        var none = Array.Empty<SummonWearRules.Worn>();
        SummonWearRules.Resolve(24, ItemGroup.Armor, none, 2).Should().Be(0);
        SummonWearRules.Resolve(24, ItemGroup.Armor, new[] { new SummonWearRules.Worn(0, (int)ItemGroup.Belt) }, 2)
            .Should().Be(1);
        SummonWearRules.Resolve(24, ItemGroup.Armor, new[] { new SummonWearRules.Worn(0, (int)ItemGroup.Armor) }, 2)
            .Should().BeNull("an armour is already worn");
        SummonWearRules.Resolve(24, ItemGroup.Accessory, none, 2).Should().BeNull("slot 2 needs an enhanced card");
        SummonWearRules.Resolve(24, ItemGroup.Accessory, none, 3).Should().Be(2);
        SummonWearRules.Resolve(24, ItemGroup.Artifact, none, 5).Should().Be(3);
        SummonWearRules.Resolve(1, ItemGroup.Armor, none, 2).Should().Be(1, "an asked slot is taken as it is");
        SummonWearRules.Resolve(2, ItemGroup.Armor, none, 2).Should().BeNull();
        SummonWearRules.Resolve(25, ItemGroup.Armor, none, 2).Should().BeNull();
    }

    [Test]
    public void Only_a_card_form_item_goes_on_a_summon()
    {
        SummonWearRules.IsCardForm((ItemFlag)1).Should().BeTrue();
        SummonWearRules.IsCardForm((ItemFlag)0).Should().BeFalse();
        SummonWearRules.IsCardForm(ItemFlag.None).Should().BeFalse("-1 is no flag at all");
    }

    private static (CharacterService Service, ICharacterRepository Repository) Characters(params ItemEntity[] items)
    {
        var character = new CharacterEntity { CharacterName = "Ana", Items = items.ToList() };
        var repository = A.Fake<ICharacterRepository>();
        A.CallTo(() => repository.GetCharacterByNameWithItemsAsync("Ana")).Returns(character);
        var factory = A.Fake<ICharacterRepositoryFactory>();
        A.CallTo(() => factory.Create()).Returns(repository);
        return (new CharacterService(A.Fake<IStarterItemsRepository>(), factory, new CharacterGate(),
            A.Fake<ILogger<CharacterService>>()), repository);
    }

    [Test]
    public async Task A_put_on_takes_the_slot_and_sends_its_former_item_back_to_the_bag()
    {
        var old = new ItemEntity { Id = 1, ItemResourceId = SummonArmor, WearInfo = 0, EquippedBySummonId = 9 };
        var fresh = new ItemEntity { Id = 2, ItemResourceId = SummonArmor, WearInfo = ItemWearType.None };
        var playerWeapon = new ItemEntity { Id = 3, ItemResourceId = 101100, WearInfo = ItemWearType.Weapon };
        var (service, repository) = Characters(old, fresh, playerWeapon);

        var result = await service.EquipSummonItemAsync("Ana", 2, 9, (_, worn) =>
        {
            worn.Should().ContainSingle().Which.Should().BeSameAs(old, "the player's weapon is not the summon's slot 0");
            return 0;
        });

        result.Code.Should().Be(ResultCode.Success);
        fresh.WearInfo.Should().Be((ItemWearType)0);
        fresh.EquippedBySummonId.Should().Be(9);
        old.WearInfo.Should().Be(ItemWearType.None);
        old.EquippedBySummonId.Should().BeNull();
        playerWeapon.WearInfo.Should().Be(ItemWearType.Weapon);
        A.CallTo(() => repository.SaveChangesAsync()).MustHaveHappenedOnceExactly();

        (await service.UnequipSummonItemAsync("Ana", 9, 0)).Should().BeSameAs(fresh);
        fresh.WearInfo.Should().Be(ItemWearType.None);
        (await service.UnequipSummonItemAsync("Ana", 9, 0)).Should().BeNull();
    }

    [Test]
    public void The_master_ignores_the_items_his_summons_wear()
    {
        var summons = new ItemEntity { Id = 1, ItemResourceId = SummonArmor, WearInfo = 0, EquippedBySummonId = 9 };
        ItemWearRules.IsWornByPlayer(summons).Should().BeFalse();
        ItemWearRules.IsWornByPlayerAt(summons, ItemWearType.Weapon).Should().BeFalse();

        var itemStats = A.Fake<IItemStatCatalog>();
        A.CallTo(() => itemStats.GetEffects(A<int>._)).Returns(new[] { new StatEffect(StatTarget.Defence, 40f, false) });
        var passives = A.Fake<ISkillPassiveCatalog>();
        A.CallTo(() => passives.Resolve(A<int>._, A<int>._, A<ItemType?>._)).Returns(Array.Empty<StatEffect>());
        var stats = new StatService(StatCatalogTestFactory.Create(), itemStats, passives, A.Fake<IStateCatalog>());
        var character = new CharacterEntity
        {
            Race = 4, Lv = 5, CurrentJob = (Job)StatCatalogTestFactory.KnownJob, PreviousJobs = new Job[3],
            JobLvs = new int[3], Items = new List<ItemEntity> { summons }
        };
        stats.Compute(character).ByItem.Defence.Should().Be(0);

        var frame = GameCharacterPackets.BuildInventory(new[] { summons }).Single();
        BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(9 + ItemFixedInfoWriter.Size, 2)).Should().Be(-1,
            "the 287 after the summon's 301 puts it on the summon");
    }

    private sealed class Harness
    {
        public readonly ICharacterService Characters = A.Fake<ICharacterService>();
        public readonly IItemStatCatalog ItemStats = A.Fake<IItemStatCatalog>();
        public readonly CreatureService Service;
        public readonly GameClient Client;
        public readonly CreatureCard Card;
        public readonly Dictionary<uint, ItemEntity> Bag = new();

        public Harness()
        {
            var players = A.Fake<IPlayerVisibilityService>();
            A.CallTo(() => players.Registry).Returns(new PlayerRegistry());
            A.CallTo(() => players.Observers(A<GameClient>._)).Returns(Array.Empty<GameClient>());
            var monsters = A.Fake<IMonsterResourceRepository>();
            A.CallTo(() => monsters.GetByIds(A<IReadOnlyCollection<int>>._)).Returns(Array.Empty<MonsterResourceEntity>());
            var catalog = new CreatureCatalog(Options.Create(new CreatureCatalogOptions
            {
                Summons =
                {
                    new SummonResourceOptions { Id = 2101, Form = 1, CardId = 540014, StatId = 2101, RunSpeed = 100,
                        AttackRange = 0.2f, Size = 2.4f, Scale = 1f, Stats = new float[] { 14, 7, 16, 5, 10, 8, 10 } }
                }
            }));
            var everyone = ItemRaceRestriction.Deva | ItemRaceRestriction.Asura | ItemRaceRestriction.Gaia;
            var resources = A.Fake<IItemResourceRepository>();
            A.CallTo(() => resources.GetWearFields()).Returns(new[]
            {
                new ItemWearFields((int)SummonArmor, ItemWearType.Armor, 0, 0, 0, everyone, ItemJobRestriction.None, 0),
                new ItemWearFields((int)HighArmor, ItemWearType.Armor, 0, 80, 0, everyone, ItemJobRestriction.None, 0)
            });
            var match = A.Fake<IItemMatchCatalog>();
            ItemMatchFields ignored;
            A.CallTo(() => match.TryGetFields(A<long>._, out ignored)).Returns(true)
                .AssignsOutAndRefParameters(new ItemMatchFields(0, ItemGroup.Armor, ItemType.Armor, 0, ItemWearType.Armor));
            A.CallTo(() => ItemStats.GetEffects(A<int>._)).Returns(new[] { new StatEffect(StatTarget.Defence, 40f, false) });
            A.CallTo(() => Characters.GetItemByHandleAsync("Ana", A<uint>._))
                .ReturnsLazily((string _, uint handle) => Task.FromResult(Bag.GetValueOrDefault(handle)));

            Service = new CreatureService(catalog, Characters,
                new MonsterWorldState(monsters, Options.Create(new MonsterSpawnOptions())), A.Fake<ICombatService>(),
                new SummonWorldService(players), players, runTicks: false, wearCatalog: new ItemWearCatalog(resources),
                itemMatch: match, itemStats: ItemStats);
            Client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
            var info = StorageTestHarness.Session(Client);
            info.CharacterHandle = 7;
            info.CharacterName = "Ana";
            info.CharacterLevel = 60;
            info.CharacterHp = 1000;
            Card = new CreatureCard
            {
                ItemId = 60, Code = 540014, Amount = 1, Flag = CreatureRules.WithSummonFlag(ItemFlag.None), SummonId = 9,
                SummonCode = 2101, SummonName = "RossParr", Level = 30, MaxReachedLevel = 30, SummonHandle = 0x50000001,
                InfoSent = true
            };
            info.CreatureCards[60] = Card;
            info.SummonSlots = new long[] { 60, 0, 0, 0, 0, 0 };
        }

        public List<byte[]> Sent => ((StorageTestHarness.FrameConnection)Client.Connection).Sent;

        public IEnumerable<byte[]> Frames(GamePackets id) =>
            Sent.Where(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2)) == (ushort)id);

        public ushort LastResult => BinaryPrimitives.ReadUInt16LittleEndian(Frames(GamePackets.TM_SC_RESULT).Last().AsSpan(9, 2));
    }

    [Test]
    public async Task A_card_form_item_goes_on_the_summon_with_its_287_and_moves_its_stats()
    {
        var h = new Harness();
        var item = new ItemEntity { Id = 70, ItemResourceId = SummonArmor, Flag = (ItemFlag)CardFlag, WearInfo = ItemWearType.None };
        h.Bag[70] = item;
        A.CallTo(() => h.Characters.EquipSummonItemAsync("Ana", 70u, 9, A<Func<ItemEntity, IReadOnlyList<ItemEntity>, int?>>._))
            .ReturnsLazily((string _, uint _, long _, Func<ItemEntity, IReadOnlyList<ItemEntity>, int?> choose) =>
            {
                choose(item, Array.Empty<ItemEntity>()).Should().Be(0);
                item.WearInfo = 0;
                item.EquippedBySummonId = 9;
                return Task.FromResult(new SummonEquipResult(ResultCode.Success, item, null));
            });

        await h.Service.EquipItemAsync(h.Client, new GameActionPackets.PutonItemRequest(24, 70, h.Card.SummonHandle));

        h.LastResult.Should().Be((ushort)ResultCode.Success);
        var wear = h.Frames(GamePackets.TM_SC_ITEM_WEAR_INFO).Single();
        BinaryPrimitives.ReadUInt32LittleEndian(wear.AsSpan(7, 4)).Should().Be(70);
        BinaryPrimitives.ReadInt16LittleEndian(wear.AsSpan(11, 2)).Should().Be(0);
        BinaryPrimitives.ReadUInt32LittleEndian(wear.AsSpan(13, 4)).Should().Be(h.Card.SummonHandle);
        h.Card.Equipment.Should().ContainSingle(worn => worn.ItemId == 70 && worn.Slot == 0);
        h.Frames(GamePackets.TM_SC_STAT_INFO).Should().HaveCount(2);
    }

    [Test]
    public async Task An_item_not_in_card_form_or_above_the_summon_level_is_refused()
    {
        var h = new Harness();
        h.Bag[70] = new ItemEntity { Id = 70, ItemResourceId = SummonArmor, Flag = 0, WearInfo = ItemWearType.None };
        h.Bag[71] = new ItemEntity { Id = 71, ItemResourceId = HighArmor, Flag = (ItemFlag)CardFlag, WearInfo = ItemWearType.None };

        await h.Service.EquipItemAsync(h.Client, new GameActionPackets.PutonItemRequest(24, 70, h.Card.SummonHandle));
        h.LastResult.Should().Be((ushort)ResultCode.NotActable);
        await h.Service.EquipItemAsync(h.Client, new GameActionPackets.PutonItemRequest(24, 71, h.Card.SummonHandle));
        h.LastResult.Should().Be((ushort)ResultCode.NotActable, "level 80 on a level 30 summon");
        await h.Service.EquipItemAsync(h.Client, new GameActionPackets.PutonItemRequest(24, 70, 0x12345678));
        h.LastResult.Should().Be((ushort)ResultCode.AccessDenied, "not one of the master's summons");
        A.CallTo(() => h.Characters.EquipSummonItemAsync(A<string>._, A<uint>._, A<long>._,
            A<Func<ItemEntity, IReadOnlyList<ItemEntity>, int?>>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task A_put_off_returns_the_item_and_unworn_on_the_wire()
    {
        var h = new Harness();
        h.Card.Equipment.Add(new SummonWornItem(70, (int)SummonArmor, 0, 0));
        A.CallTo(() => h.Characters.UnequipSummonItemAsync("Ana", 9, 0))
            .Returns(new ItemEntity { Id = 70, ItemResourceId = SummonArmor, WearInfo = ItemWearType.None });

        await h.Service.UnequipItemAsync(h.Client, new GameActionPackets.PutoffItemRequest(0, h.Card.SummonHandle));

        h.LastResult.Should().Be((ushort)ResultCode.Success);
        h.Card.Equipment.Should().BeEmpty();
        BinaryPrimitives.ReadInt16LittleEndian(h.Frames(GamePackets.TM_SC_ITEM_WEAR_INFO).Single().AsSpan(11, 2))
            .Should().Be(-1);

        A.CallTo(() => h.Characters.UnequipSummonItemAsync("Ana", 9, 1)).Returns(Task.FromResult<ItemEntity>(null));
        await h.Service.UnequipItemAsync(h.Client, new GameActionPackets.PutoffItemRequest(1, h.Card.SummonHandle));
        h.LastResult.Should().Be((ushort)ResultCode.NotExist);
    }
}
