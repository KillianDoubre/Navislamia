using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Progression;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

/// <summary>
/// Secondary titles (<c>StructPlayer::SetSubTitle</c>, <c>applyStatByTitle</c>), the title events of the summons
/// and of crafting (<c>StructTitleManager</c>), and the item creation mix that feeds the latter
/// (<c>MixManager::CreateItem</c>). docs/packet-specs/socle-titres-secondaires-evenements.md.
/// </summary>
[TestFixture]
public class SubTitleAndCraftEventTests
{
    private static TitleResource Resource(int id, int rate = 3, decimal defence = 40) => new(id, id, new short[] { 96 },
        new decimal[] { 512 }, new decimal[] { defence }, false, "", "", rate);

    [Test]
    public void A_secondary_title_gives_a_tenth_of_its_options()
    {
        var catalog = new TitleCatalog(new ProgressionResources { Titles = new[] { Resource(1), Resource(2, defence: 50) } });

        catalog.GetEffects(1, new[] { 2, 0, 0, 0, 0 }).Should().Equal(
            new StatEffect(StatTarget.Defence, 40, false), new StatEffect(StatTarget.Defence, 5, false));
        catalog.GetEffects(0, new[] { 0, 0, 0, 0, 0 }).Should().BeEmpty();
    }

    [Test]
    public void The_events_match_the_official_condition_values()
    {
        var tameByCode = new TitleConditionType(1, TitleEvents.SummonTameByCode, new[] { 2101, 1, 0 }, false);
        var failByCode = new TitleConditionType(2, TitleEvents.SummonTameByCode, new[] { 2101, 0, 0 }, false);
        var tameByRate = new TitleConditionType(3, TitleEvents.SummonTameByRate, new[] { 5, 0, 0 }, false);
        TitleEvents.SummonTame(2101, 5, true)(tameByCode).Should().Be(1);
        TitleEvents.SummonTame(2101, 5, true)(failByCode).Should().BeNull();
        TitleEvents.SummonTame(2101, 5, false)(tameByRate).Should().Be(1, "rate 5, failed draw");

        var mix = new TitleConditionType(4, TitleEvents.ItemMixByCode, new[] { 1100102, 0, 0 }, false);
        TitleEvents.ItemsMixed(new[] { (1100102, 3L), (7, 1L) })(mix).Should().Be(3);
        TitleEvents.ItemsMixed(new[] { (7, 1L) })(mix).Should().BeNull();
        TitleEvents.ItemUsed(602301)(new TitleConditionType(5, TitleEvents.ItemUseByCode, new[] { 602301, 0, 0 }, false))
            .Should().Be(1);
    }

    [Test]
    public void The_summon_conditions_follow_the_formation_the_mount_and_the_cards()
    {
        var cards = new[]
        {
            new TitleEvents.Card(2101, 5, 1, true, true, false),
            new TitleEvents.Card(2101, 5, 0, true, true, false),
            new TitleEvents.Card(2703, 3, 0, true, true, true),
            new TitleEvents.Card(4602, 6, 0, false, false, false)
        };

        TitleEvents.FromCards(new TitleConditionType(1, TitleEvents.SummonEquipByCode, new[] { 2101, 1, 1 }, false), cards)
            .Should().Be(1, "one formed 2101 at +1 or more");
        TitleEvents.FromCards(new TitleConditionType(2, TitleEvents.SummonEquipByCode, new[] { 2101, 0, 1 }, false), cards)
            .Should().Be(2);
        TitleEvents.FromCards(new TitleConditionType(3, TitleEvents.SummonMountByCode, new[] { 2703, 1, 0 }, true), cards)
            .Should().Be(1);
        TitleEvents.FromCards(new TitleConditionType(4, TitleEvents.SummonMountByCode, new[] { 4003, 1, 0 }, true), cards)
            .Should().Be(0);
        TitleEvents.FromCards(new TitleConditionType(5, TitleEvents.SummonCardGetByRate, new[] { 6, 0, 0 }, true), cards)
            .Should().Be(1, "an untamed card of rate 6 is held");
        TitleEvents.FromCards(new TitleConditionType(6, 4002, new[] { 1, 0, 0 }, true), cards).Should().BeNull();
    }

    private static async Task<(TitleService Service, Navislamia.Game.Network.Clients.GameClient Client,
        DbContextOptions<TelecasterContext> Options)> Titles(params int[] owned)
    {
        var options = new DbContextOptionsBuilder<TelecasterContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), o => o.EnableNullChecks(false)).Options;
        var catalog = new TitleCatalog(new ProgressionResources
        {
            Titles = new[] { Resource(1), Resource(2, defence: 50), Resource(3, rate: 6) }
        });
        var stats = new StatService(StatCatalogTestFactory.Create(), A.Fake<IItemStatCatalog>(),
            A.Fake<ISkillPassiveCatalog>(), A.Fake<IStateCatalog>(), catalog);
        await using (var db = new TelecasterContext(options))
        {
            db.Characters.Add(new CharacterEntity { Id = 1, CharacterName = "Titles", Lv = 5,
                CurrentJob = (Job)StatCatalogTestFactory.KnownJob });
            db.CharacterTitleStates.Add(new CharacterTitleStateEntity { CharacterId = 1, OpenedTitleIds = owned,
                OwnedTitleIds = owned });
            await db.SaveChangesAsync();
        }

        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 1;
        info.CharacterName = "Titles";
        info.CharacterJob = StatCatalogTestFactory.KnownJob;
        info.CharacterLevel = 5;
        return (new TitleService(options, new CharacterGate(), catalog, stats), client, options);
    }

    [Test]
    public async Task A_secondary_title_is_owned_unworn_and_of_rate_five_at_most()
    {
        var (service, client, options) = await Titles(1, 2, 3);
        var info = StorageTestHarness.Session(client);

        (await service.ChooseSubAsync(client, 5, 1)).Should().Be(TitleChoice.InvalidSlot);
        (await service.ChooseSubAsync(client, 0, 3)).Should().Be(TitleChoice.RateTooHigh);
        (await service.ChooseSubAsync(client, 0, 9)).Should().Be(TitleChoice.NotOwned);
        (await service.ChooseMainAsync(client, 1)).Should().Be(TitleChoice.Done);
        (await service.ChooseSubAsync(client, 0, 1)).Should().Be(TitleChoice.InUse, "it is the main title");
        (await service.ChooseSubAsync(client, 2, 2)).Should().Be(TitleChoice.Done);
        (await service.ChooseSubAsync(client, 3, 2)).Should().Be(TitleChoice.InUse);

        info.SubTitleIds.Should().Equal(0, 0, 2, 0, 0);
        info.TitleEffects.Should().Contain(new StatEffect(StatTarget.Defence, 5, false));
        await using var db = new TelecasterContext(options);
        (await db.Characters.SingleAsync()).SubTitleIds.Should().Equal(0, 0, 2, 0, 0);
    }

    [Test]
    public async Task The_main_title_waits_five_minutes_before_it_changes_again()
    {
        var (service, client, _) = await Titles(1, 2);

        (await service.ChooseMainAsync(client, 1)).Should().Be(TitleChoice.Done);
        (await service.ChooseMainAsync(client, 2)).Should().Be(TitleChoice.CoolingDown);
        (await service.ChooseMainAsync(client, 0)).Should().Be(TitleChoice.CoolingDown, "taking it off waits too");

        StorageTestHarness.Session(client).MainTitleLockedUntil = unchecked(ServerClock.Now - 1);
        (await service.ChooseMainAsync(client, 2)).Should().Be(TitleChoice.Done);
    }

    [Test]
    public async Task A_tame_event_counts_and_unlocks_its_title()
    {
        var options = new DbContextOptionsBuilder<TelecasterContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), o => o.EnableNullChecks(false)).Options;
        var catalog = new TitleCatalog(new ProgressionResources
        {
            Titles = new[] { Resource(1) },
            ConditionTypes = new[] { new TitleConditionType(30, TitleEvents.SummonTameByCode, new[] { 2101, 1, 0 }, false) },
            Conditions = new[] { new TitleCondition(1, 0, 30, 2, true) }
        });
        var stats = new StatService(StatCatalogTestFactory.Create(), A.Fake<IItemStatCatalog>(),
            A.Fake<ISkillPassiveCatalog>(), A.Fake<IStateCatalog>(), catalog);
        await using (var db = new TelecasterContext(options))
        {
            db.Characters.Add(new CharacterEntity { Id = 1, CharacterName = "Titles", Lv = 5,
                CurrentJob = (Job)StatCatalogTestFactory.KnownJob });
            await db.SaveChangesAsync();
        }

        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 1;
        info.CharacterName = "Titles";
        var service = new TitleService(options, new CharacterGate(), catalog, stats);

        await service.RecordAsync(client, TitleEvents.SummonTame(2101, 3, true));
        info.MainTitleId.Should().Be(0, "one taming of two");
        await service.RecordAsync(client, TitleEvents.SummonTame(2101, 3, false));
        info.MainTitleId.Should().Be(0, "a failed draw is another condition");
        await service.RecordAsync(client, TitleEvents.SummonTame(2101, 3, true));
        info.MainTitleId.Should().Be(1);
    }

    private static MixResolution CreateRule(int item, int percent, int min, int max, params MixMaterial[] subs) =>
        new(new MixResourceEntity
        {
            Id = 7, MixType = CraftingEngine.MixCreateItem, MixValue01 = item, MixValue02 = 1, MixValue03 = percent,
            MixValue04 = min, MixValue05 = max
        }, subs.Select(sub => sub.Count).ToArray(), subs);

    private static MixMaterial Material(uint handle, int code, long count) =>
        new(code, 0, 0, 0, 0, 1, 0, 0, count, handle, 0, 50);

    [Test]
    public void Create_item_makes_the_item_and_consumes_the_materials()
    {
        var plan = CraftingEngine.Plan(CreateRule(1100102, 100, 2, 4, Material(5, 700, 3), Material(6, 701, 1)), null,
            null, (min, max) => max == 99 ? 50 : 3);

        plan.Refusal.Should().Be(ResultCode.Success);
        plan.Consumed.Select(c => (c.ItemHandle, c.Count)).Should().Equal((5u, 3L), (6u, 1L));
        plan.Created.Should().Equal(new CraftCreation(1100102, 3, 1));
        plan.Change.Should().BeNull();
    }

    [Test]
    public void Create_item_draws_a_group_once_per_count_and_a_failed_draw_still_consumes()
    {
        var picks = new Queue<(int, long)?>(new (int, long)?[] { (-20, 1), (900, 2), (901, 1), (900, 1) });
        var plan = CraftingEngine.Plan(CreateRule(-10, 100, 3, 3, Material(5, 700, 1)), null, null,
            (min, max) => max == 99 ? 0 : 3, _ => picks.Dequeue());

        plan.Created.Should().Equal(new CraftCreation(900, 3, 1), new CraftCreation(901, 1, 1));

        var failed = CraftingEngine.Plan(CreateRule(1100102, 30, 1, 1, Material(5, 700, 1)), null, null,
            (min, max) => max == 99 ? 30 : 1);
        failed.Created.Should().BeEmpty("30 is not above a draw of 30");
        failed.Consumed.Should().ContainSingle();
    }

    [Test]
    public async Task The_mix_rows_are_added_with_the_consumption_in_one_save()
    {
        var material = new ItemEntity { Id = 5, ItemResourceId = 700, Amount = 3, WearInfo = ItemWearType.None, Level = 1 };
        var character = new CharacterEntity { CharacterName = "Crafter", Items = new List<ItemEntity> { material } };
        var repository = A.Fake<ICharacterRepository>();
        A.CallTo(() => repository.GetCharacterByNameWithItemsAsync("Crafter")).Returns(character);
        var factory = A.Fake<ICharacterRepositoryFactory>();
        A.CallTo(() => factory.Create()).Returns(repository);
        var service = new CharacterService(A.Fake<IStarterItemsRepository>(), factory, new CharacterGate(),
            A.Fake<ILogger<CharacterService>>());

        var commit = await service.ApplyCraftWithCreationAsync("Crafter", new[] { new CraftConsumption(5, 3) },
            new[] { new CraftCreation(1100102, 2, 1) });

        commit.Outcome.Should().Be(CraftCommitOutcome.Success);
        commit.Consumed.Should().Equal((5u, 0L));
        commit.Created.Should().ContainSingle(item => item.ItemResourceId == 1100102 && item.Amount == 2
            && item.GenerateBySource == ItemGenerateSource.Mix);
        A.CallTo(() => repository.SaveChangesAsync()).MustHaveHappenedOnceExactly();
    }
}
