using System;
using System.Collections.Generic;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

internal static class StatCatalogTestFactory
{
    public const int KnownJob = 100;
    public const int KnownStatId = 7;

    public static JobLevelBonusFields Bonus(int job, decimal[] strength = null)
    {
        var zero = new decimal[] { 0, 0, 0 };
        return new JobLevelBonusFields(job,
            strength ?? new decimal[] { 0.5m, 0.4m, 0.3m }, zero, zero, zero, zero, zero, zero,
            0, 0, 0, 0, 0, 0, 0);
    }

    public static StatCatalog Create(IReadOnlyList<JobStatFields> jobs = null,
        StatResourceEntity statRow = null, IReadOnlyList<JobLevelBonusFields> bonuses = null)
    {
        jobs ??= new[] { new JobStatFields(KnownJob, KnownStatId) };
        statRow ??= new StatResourceEntity
        {
            Id = KnownStatId, Strength = 13, Vitality = 12, Dexterity = 11,
            Agility = 11, Intelligence = 10, Wisdom = 11, Luck = 10
        };
        bonuses ??= new[] { Bonus(KnownJob) };

        var jobRepository = A.Fake<IJobResourceRepository>();
        A.CallTo(() => jobRepository.GetJobStatIds()).Returns(jobs);

        var statRepository = A.Fake<IStatResourceRepository>();
        A.CallTo(() => statRepository.GetById(A<int>._)).Returns(null);
        A.CallTo(() => statRepository.GetById(statRow.Id > 0 ? (int)statRow.Id : KnownStatId)).Returns(statRow);

        var bonusRepository = A.Fake<IJobLevelBonusRepository>();
        A.CallTo(() => bonusRepository.GetAll()).Returns(bonuses);

        return new StatCatalog(jobRepository, statRepository, bonusRepository);
    }
}

[TestFixture]
public class StatCatalogTests
{
    [Test]
    public void TryGetBaseStats_ResolvesJobThroughStatIdToTheStatRow()
    {
        var catalog = StatCatalogTestFactory.Create();

        catalog.TryGetBaseStats(StatCatalogTestFactory.KnownJob, out var stats).Should().BeTrue();
        stats.StatId.Should().Be(StatCatalogTestFactory.KnownStatId);
        stats.Strength.Should().Be(13);
        stats.Vitality.Should().Be(12);
        stats.Wisdom.Should().Be(11);
    }

    [Test]
    public void TryGetBaseStats_ReturnsFalseForAnUnknownJob()
    {
        StatCatalogTestFactory.Create().TryGetBaseStats(999, out _).Should().BeFalse();
    }

    [Test]
    public void TryGetBaseStats_ReturnsFalseWhenTheStatRowIsMissing()
    {
        var catalog = StatCatalogTestFactory.Create(
            jobs: new[] { new JobStatFields(StatCatalogTestFactory.KnownJob, 4242) });

        catalog.TryGetBaseStats(StatCatalogTestFactory.KnownJob, out _).Should().BeFalse();
    }

    [Test]
    public void TryGetJobBonus_ExposesOneEntryPerBaseStat()
    {
        StatCatalogTestFactory.Create()
            .TryGetJobBonus(StatCatalogTestFactory.KnownJob, out var bonus).Should().BeTrue();

        bonus.Entries.Should().HaveCount(7);
        bonus.Entries.Should().Contain(entry => entry.Target == StatTarget.Strength);
        bonus.Entries.Should().Contain(entry => entry.Target == StatTarget.Luck);
    }

    [Test]
    public void TryGetJobBonus_KeepsTheFractionalPerLevelRates()
    {
        StatCatalogTestFactory.Create().TryGetJobBonus(StatCatalogTestFactory.KnownJob, out var bonus);

        var strength = Array.Find(bonus.Entries, entry => entry.Target == StatTarget.Strength);
        strength.PerLevel.Should().Equal(0.5f, 0.4f, 0.3f);
    }

    [Test]
    public void TryGetJobBonus_ReturnsFalseForAnUnknownJob()
    {
        StatCatalogTestFactory.Create().TryGetJobBonus(999, out _).Should().BeFalse();
    }
}
