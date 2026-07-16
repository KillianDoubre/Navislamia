using System;
using System.Collections.Generic;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

[TestFixture]
public class StatServiceTests
{
    private const int Job = StatCatalogTestFactory.KnownJob;
    private const int WornResourceId = 4001;
    private const int BaggedResourceId = 4002;

    private static readonly ItemStatEffect WornEffect = new(StatTarget.Defence, 40f, false);
    private static readonly ItemStatEffect BaggedEffect = new(StatTarget.Defence, 999f, false);

    private static StatService Create()
    {
        var itemStats = A.Fake<IItemStatCatalog>();
        A.CallTo(() => itemStats.GetEffects(A<int>._)).Returns(Array.Empty<ItemStatEffect>());
        A.CallTo(() => itemStats.GetEffects(WornResourceId)).Returns(new[] { WornEffect });
        A.CallTo(() => itemStats.GetEffects(BaggedResourceId)).Returns(new[] { BaggedEffect });

        return new StatService(StatCatalogTestFactory.Create(), itemStats);
    }

    private static CharacterEntity Character(int jobLevel = 0, List<ItemEntity> items = null,
        Job[] previousJobs = null, int[] jobLvs = null)
    {
        return new CharacterEntity
        {
            Race = 4,
            Lv = 5,
            CurrentJob = (Job)Job,
            Jlv = jobLevel,
            PreviousJobs = previousJobs ?? new Job[3],
            JobLvs = jobLvs ?? new int[3],
            Items = items
        };
    }

    [Test]
    public void Compute_ResolvesBaseStatsFromTheCharacterJob()
    {
        var stats = Create().Compute(Character()).Total;

        stats.StatId.Should().Be(StatCatalogTestFactory.KnownStatId);
        stats.Strength.Should().Be(13);
    }

    [Test]
    public void Compute_CountsOnlyWornItems()
    {
        var items = new List<ItemEntity>
        {
            new() { ItemResourceId = WornResourceId, WearInfo = ItemWearType.Armor },
            new() { ItemResourceId = BaggedResourceId, WearInfo = ItemWearType.None }
        };

        Create().Compute(Character(items: items)).ByItem.Defence.Should().Be(40);
    }

    [Test]
    public void Compute_ToleratesACharacterWithoutItems()
    {
        Create().Compute(Character()).ByItem.Defence.Should().Be(0);
    }

    [Test]
    public void Compute_AppendsTheCurrentJobToThePreviousOnes()
    {
        var withHistory = Create().Compute(Character(jobLevel: 10,
            previousJobs: new[] { (Job)Job, default, default }, jobLvs: new[] { 10, 0, 0 })).Total.Strength;
        var withoutHistory = Create().Compute(Character(jobLevel: 10)).Total.Strength;

        withHistory.Should().BeApproximately(withoutHistory + 10 * 0.5f, 0.01f);
    }

    [Test]
    public void Compute_StopsTheJobHistoryAtTheFirstEmptySlot()
    {
        var stats = Create().Compute(Character(jobLevel: 0,
            previousJobs: new[] { default, (Job)Job, default }, jobLvs: new[] { 0, 10, 0 })).Total;
        var naked = Create().Compute(Character()).Total;

        stats.Strength.Should().Be(naked.Strength);
    }

    [Test]
    public void Seed_CachesThePreviousJobsAndTheWornEffectsOnTheConnection()
    {
        var items = new List<ItemEntity>
        {
            new() { ItemResourceId = WornResourceId, WearInfo = ItemWearType.Armor },
            new() { ItemResourceId = BaggedResourceId, WearInfo = ItemWearType.None }
        };
        var info = new ConnectionInfo();

        Create().Seed(info, Character(items: items, previousJobs: new[] { (Job)Job, default, default },
            jobLvs: new[] { 10, 0, 0 }));

        info.PreviousJobs.Should().Equal((Job, 10));
        info.ItemEffects.Should().Equal(WornEffect);
    }

    [Test]
    public void Compute_FromConnectionInfoMatchesTheEntity()
    {
        var items = new List<ItemEntity>
        {
            new() { ItemResourceId = WornResourceId, WearInfo = ItemWearType.Armor }
        };
        var character = Character(jobLevel: 10, items: items);
        var service = Create();

        var info = new ConnectionInfo
        {
            CharacterJob = Job,
            CharacterJobLevel = 10,
            CharacterLevel = 5,
            CharacterRace = 4
        };
        service.Seed(info, character);

        service.Compute(info).Total.Defence.Should().Be(service.Compute(character).Total.Defence);
    }

    [Test]
    public void ComputeForNewCharacter_UsesTheStarterJobAndLevelOne()
    {
        var stats = Create().ComputeForNewCharacter(4).Total;

        stats.MaxHp.Should().BeGreaterThan(0);
        stats.MoveSpeed.Should().Be(120);
    }
}
