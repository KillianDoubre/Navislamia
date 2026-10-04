using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Creatures;
using Navislamia.Game.Services.Stats;
using NUnit.Framework;

namespace Tests.Game;

[TestFixture]
public class EtherealWearTests
{
    private sealed class Catalog : IItemMatchCatalog
    {
        public bool TryGetFields(long code, out ItemMatchFields fields)
        {
            // 1 a graded sword, 2 a graded armour, 3 a belt equipment card without a grade, 4 an armour without one.
            fields = new ItemMatchFields((int)code, code == 3 ? ItemGroup.EquipmentOnBelt : default,
                code == 1 ? ItemType.OnehandSword : ItemType.Shield, 3, code == 1 ? ItemWearType.Weapon : ItemWearType.Armor)
                { Mix = ItemMixFields.Empty with { Grade = code is 3 or 4 ? 0 : 1, MaxEtherealDurability = 100_000 } };
            return true;
        }
    }
    private sealed class Harness
    {
        public readonly DbContextOptions<TelecasterContext> Options = new DbContextOptionsBuilder<TelecasterContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        public readonly Navislamia.Game.Network.Clients.GameClient Client;
        public readonly Navislamia.Game.Network.Clients.ConnectionInfo Info;
        public readonly EtherealWear Wear;
        public readonly ICreatureEvents Creatures = A.Fake<ICreatureEvents>();
        public readonly StatService Stats;
        public Harness(params ItemEntity[] items)
        {
            Client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
            Info = StorageTestHarness.Session(Client); Info.CharacterName = "Ana"; Info.CharacterHandle = 7;
            Info.CharacterLevel = 100; Info.CharacterJob = 100;
            var itemStats = A.Fake<IItemStatCatalog>();
            A.CallTo(() => itemStats.GetEffects(1)).Returns(new[] { new StatEffect(StatTarget.AttackPointRight, 40, false) });
            Stats = new StatService(StatCatalogTestFactory.Create(), itemStats, A.Fake<ISkillPassiveCatalog>(), A.Fake<IStateCatalog>(), itemTemplates: new Catalog());
            var characters = new CharacterService(A.Fake<IStarterItemsRepository>(), new CharacterRepositoryFactory(Options),
                new CharacterGate(), NullLogger<CharacterService>.Instance, itemTemplates: new Catalog());
            var jobs = A.Fake<IJobResourceRepository>();
            A.CallTo(() => jobs.GetWearFields()).Returns(new[] { new JobWearFields(100, 1, 1) });
            Wear = new EtherealWear(characters, new Catalog(), jobs, Stats, Creatures);
            using var db = new TelecasterContext(Options);
            db.Characters.Add(new CharacterEntity { CharacterName = "Ana", CurrentJob = (Job)100,
                Lv = 100, Items = items }); db.SaveChanges();
        }
        /// <summary>What world entry does: the stats seed the worn items the wear can reach.</summary>
        public async Task Seed() => Stats.RefreshEquipment(Info, await Reload());

        public async Task<ItemEntity[]> Reload()
        {
            await using var db = new TelecasterContext(Options); return await db.Items.OrderBy(i => i.Id).ToArrayAsync();
        }
    }
    private static ItemEntity Item(long id, int code, ItemWearType slot, int durability = 100_000, int? summon = null) =>
        new() { Id = id, ItemResourceId = code, Amount = 1, WearInfo = slot, EtherealDurability = durability, EquippedBySummonId = summon };

    [TestCase(true, EtherealHit.Normal, 1, 50)]
    [TestCase(true, EtherealHit.Skill, 1, 83)]
    [TestCase(false, EtherealHit.Normal, 1, 306)]
    [TestCase(true, EtherealHit.Normal, 2, 101)]
    [TestCase(true, EtherealHit.Normal, 0.05, 2)]
    public void Retail_fixed_point_costs_follow_job_damage_rank_and_environment(bool attack, EtherealHit hit, decimal environment, int expected) =>
        EtherealWearRules.Consumption(100, 1, attack, 1000, 3, 1, hit, environment).Should().Be(expected);

    [Test]
    public async Task Attacks_wear_only_the_correct_player_hand_and_flush_all_concurrent_hits()
    {
        var h = new Harness(Item(1, 1, ItemWearType.Weapon), Item(2, 1, ItemWearType.Shield),
            Item(3, 2, ItemWearType.Armor), Item(4, 1, ItemWearType.None), Item(5, 1, ItemWearType.Weapon, summon: 9));
        await h.Seed();
        for (var i = 0; i < 20; i++) h.Wear.Hit(h.Client, true, 1000);
        h.Wear.Hit(h.Client, true, 1000, EtherealHit.LeftHand);
        await h.Wear.FlushAsync(h.Client);
        (await h.Reload()).Select(i => i.EtherealDurability).Should().Equal(99_000, 99_950, 100_000, 100_000, 100_000);
    }

    [Test]
    public async Task Received_damage_wears_armour_and_belt_items_and_keeps_weapons_and_spares()
    {
        var h = new Harness(Item(1, 1, ItemWearType.Weapon), Item(2, 2, ItemWearType.Armor),
            Item(3, 3, ItemWearType.None), Item(4, 2, ItemWearType.SpareWeapon), Item(5, 2, ItemWearType.Armor, summon: 9));
        h.Info.BeltItemIds = new long[] { 3 };
        await h.Seed();
        h.Wear.Hit(h.Client, false, 1000);
        await h.Wear.FlushAsync(h.Client);
        // The belt card has no grade: the item term is its rank's alone.
        (await h.Reload()).Select(i => i.EtherealDurability).Should().Equal(100_000, 99_694,
            100_000 - EtherealWearRules.Consumption(100, 1, false, 1000, 3, 0, EtherealHit.Normal), 100_000, 100_000);
    }

    [Test]
    public async Task A_summon_uses_its_own_equipment_and_level()
    {
        var h = new Harness(Item(1, 1, ItemWearType.Weapon), Item(2, 1, ItemWearType.Weapon, summon: 9),
            Item(3, 1, ItemWearType.Weapon, summon: 10));
        var card = new CreatureCard { SummonId = 9, Level = 50 };
        card.Equipment.Add(new SummonWornItem(2, 1, 0, 0));
        h.Wear.Hit(h.Client, true, 1000, summon: card);
        await h.Wear.FlushAsync(h.Client);
        var items = await h.Reload();
        items[0].EtherealDurability.Should().Be(100_000); items[2].EtherealDurability.Should().Be(100_000);
        items[1].EtherealDurability.Should().Be(100_000 - EtherealWearRules.Consumption(50, 0, true, 1000, 3, 1, EtherealHit.Normal));
    }

    [Test]
    public async Task Exhaustion_is_clamped_persisted_and_removes_the_equipment_bonus()
    {
        var h = new Harness(Item(1, 1, ItemWearType.Weapon, durability: 1));
        await h.Seed();
        h.Stats.Compute(h.Info).ByItem.AttackPointRight.Should().Be(40);
        h.Wear.Hit(h.Client, true, 1000); await h.Wear.FlushAsync(h.Client);
        (await h.Reload()).Single().EtherealDurability.Should().Be(0);
        h.Stats.Compute(h.Info).ByItem.AttackPointRight.Should().Be(0);
        A.CallTo(() => h.Creatures.EquipmentDurabilityChanged(h.Client, A<IReadOnlyList<ItemEntity>>._)).MustHaveHappenedOnceExactly();
        h.Info.EtherealGear.Should().BeEmpty("an exhausted item wears no further");
    }

    [Test]
    public void Gear_without_a_grade_or_unworn_is_never_a_candidate()
    {
        var items = new[] { Item(1, 4, ItemWearType.Armor), Item(2, 1, ItemWearType.None), Item(3, 1, ItemWearType.SpareWeapon),
            Item(4, 3, ItemWearType.None), Item(5, 1, ItemWearType.Weapon, durability: 0), Item(6, 1, ItemWearType.Weapon, summon: 9) };
        EtherealWearRules.PlayerCandidates(items, new long[] { 4 }, new Catalog()).Select(c => (c.ItemId, c.Belt))
            .Should().Equal((4L, true));
    }

    [Test]
    public void A_hit_on_a_player_without_ethereal_gear_never_reaches_the_database()
    {
        var characters = A.Fake<ICharacterService>();
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client); info.CharacterName = "Ana"; info.CharacterHandle = 7;
        var wear = new EtherealWear(characters, new Catalog(), A.Fake<IJobResourceRepository>());

        wear.Hit(client, true, 1000);
        wear.Hit(client, false, 1000);

        A.CallTo(() => characters.ConsumeEtherealAsync(A<string>._, A<Func<ItemEntity, int>>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task Hits_landing_during_a_write_are_summed_into_the_next_one()
    {
        var characters = A.Fake<ICharacterService>();
        var release = new TaskCompletionSource<IReadOnlyList<ItemEntity>>();
        var batches = new List<int>();
        var calls = 0;
        A.CallTo(() => characters.ConsumeEtherealAsync("Ana", A<Func<ItemEntity, int>>._)).ReturnsLazily(call =>
        {
            batches.Add(call.GetArgument<Func<ItemEntity, int>>(1)(Item(1, 1, ItemWearType.Weapon)));
            return ++calls == 1 ? release.Task : Task.FromResult<IReadOnlyList<ItemEntity>>(Array.Empty<ItemEntity>());
        });
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client); info.CharacterName = "Ana"; info.CharacterHandle = 7; info.CharacterLevel = 100;
        info.EtherealGear = EtherealWearRules.PlayerCandidates(new[] { Item(1, 1, ItemWearType.Weapon) }, Array.Empty<long>(), new Catalog());
        var wear = new EtherealWear(characters, new Catalog(), A.Fake<IJobResourceRepository>());
        var one = EtherealWearRules.Consumption(100, 0, true, 1000, 3, 1, EtherealHit.Normal);

        wear.Hit(client, true, 1000);
        for (var i = 0; i < 200 && calls == 0; i++) await Task.Delay(5);
        for (var i = 0; i < 5; i++) wear.Hit(client, true, 1000);
        release.SetResult(Array.Empty<ItemEntity>());
        await wear.FlushAsync(client);

        batches.Should().Equal(one, 5 * one);
    }
}
