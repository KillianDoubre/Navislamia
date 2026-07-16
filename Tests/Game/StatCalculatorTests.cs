using System;
using System.Collections.Generic;
using FluentAssertions;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

[TestFixture]
public class StatCalculatorTests
{
    private const int Job = StatCatalogTestFactory.KnownJob;

    private static StatCalculator Calculator()
    {
        return new StatCalculator(StatCatalogTestFactory.Create());
    }

    private static StatCalculatorInput Input(int level = 1, int jobLevel = 0,
        IReadOnlyList<ItemStatEffect> effects = null)
    {
        return new StatCalculatorInput(
            Job: Job,
            JobHistory: new[] { (Job: Job, JobLevel: jobLevel) },
            Level: level,
            ItemEffects: effects ?? Array.Empty<ItemStatEffect>());
    }

    [Test]
    public void Compute_ReadsTheBaseStatsFromTheJobStatRow()
    {
        var stats = Calculator().Compute(Input()).Total;

        stats.StatId.Should().Be(StatCatalogTestFactory.KnownStatId);
        stats.Strength.Should().Be(13);
        stats.Vitality.Should().Be(12);
    }

    [Test]
    public void Compute_SeedsAdvancedStatsFromLevelAndConstants()
    {
        var stats = Calculator().Compute(Input(level: 10)).Total;

        stats.MoveSpeed.Should().Be(120);
        stats.CriticalPower.Should().Be(80);
        stats.PerfectBlock.Should().Be(20);
        stats.AttackRange.Should().Be(50);
        stats.CoolTimeSpeed.Should().Be(100);
        stats.HpRegenPoint.Should().Be(48 + 2 * 10);
        stats.MaxStamina.Should().Be(500000);
        stats.MaxChaos.Should().Be(500);
    }

    [Test]
    public void Compute_AppliesTheStatDerivedBonuses()
    {
        var stats = Calculator().Compute(Input(level: 5)).Total;

        stats.AttackPointRight.Should().BeApproximately(5 + 2.8f * stats.Strength, 0.01f);
        stats.Defence.Should().BeApproximately(5 + 1.6f * stats.Vitality, 0.01f);
        stats.MagicDefence.Should().BeApproximately(5 + 2f * stats.Wisdom, 0.01f);
        stats.MagicPoint.Should().BeApproximately(5 + 2f * stats.Intelligence, 0.01f);
        stats.Critical.Should().BeApproximately(3 + stats.Luck / 5f, 0.01f);
        stats.MaxHp.Should().BeApproximately(20 * 5 + 33f * stats.Vitality, 0.01f);
        stats.MaxMp.Should().BeApproximately(20 * 5 + 30f * stats.Intelligence, 0.01f);
    }

    [Test]
    public void Compute_MirrorsAccuracyLeftOntoAccuracyRight()
    {
        var stats = Calculator().Compute(Input(level: 5)).Total;

        stats.AccuracyLeft.Should().Be(stats.AccuracyRight);
    }

    [Test]
    public void Compute_AddsJobLevelBonusPerTwentyLevelChunk()
    {
        var atZero = Calculator().Compute(Input(jobLevel: 0)).Total.Strength;
        var atTen = Calculator().Compute(Input(jobLevel: 10)).Total.Strength;
        var atTwenty = Calculator().Compute(Input(jobLevel: 20)).Total.Strength;

        atTen.Should().BeApproximately(atZero + 10 * 0.5f, 0.01f);
        atTwenty.Should().BeApproximately(atZero + 20 * 0.5f, 0.01f);
    }

    [Test]
    public void Compute_UsesTheSecondChunkRateBeyondTwentyJobLevels()
    {
        var atZero = Calculator().Compute(Input(jobLevel: 0)).Total.Strength;
        var atThirty = Calculator().Compute(Input(jobLevel: 30)).Total.Strength;

        atThirty.Should().BeApproximately(atZero + 20 * 0.5f + 10 * 0.4f, 0.01f);
    }

    [Test]
    public void Compute_AccumulatesEveryJobInTheHistory()
    {
        var single = Calculator().Compute(Input(jobLevel: 10)).Total.Strength;
        var twice = Calculator().Compute(new StatCalculatorInput(Job,
            new[] { (Job, 10), (Job, 10) }, 1, Array.Empty<ItemStatEffect>())).Total.Strength;

        twice.Should().BeApproximately(single + 10 * 0.5f, 0.01f);
    }

    [Test]
    public void Compute_ByItemHoldsOnlyTheItemContribution()
    {
        var effects = new[] { new ItemStatEffect(StatTarget.Defence, 40f, false) };
        var result = Calculator().Compute(Input(level: 5, effects: effects));
        var naked = Calculator().Compute(Input(level: 5));

        result.ByItem.Defence.Should().Be(40);
        result.ByItem.Strength.Should().Be(0);
        result.ByItem.MoveSpeed.Should().Be(0);
        result.Total.Defence.Should().BeApproximately(naked.Total.Defence + 40, 0.01f);
    }

    [Test]
    public void Compute_ItemStatBonusFeedsTheDerivedFormulas()
    {
        var effects = new[] { new ItemStatEffect(StatTarget.Vitality, 10f, false) };
        var withItem = Calculator().Compute(Input(level: 5, effects: effects)).Total;
        var naked = Calculator().Compute(Input(level: 5)).Total;

        withItem.MaxHp.Should().BeApproximately(naked.MaxHp + 33f * 10, 0.01f);
        withItem.Defence.Should().BeApproximately(naked.Defence + 1.6f * 10, 0.01f);
    }

    [Test]
    public void Compute_AppliesPercentItemEffectsAfterEveryFlatOne()
    {
        var effects = new[]
        {
            new ItemStatEffect(StatTarget.MaxHp, 0.10f, true),
            new ItemStatEffect(StatTarget.MaxHp, 1000f, false)
        };

        Calculator().Compute(Input(level: 5, effects: effects)).ByItem.MaxHp
            .Should().BeApproximately(1100f, 0.01f);
    }

    [Test]
    public void Compute_ReturnsAnEmptyBaseForAnUnknownJob()
    {
        var input = Input() with { Job = 999, JobHistory = new[] { (Job: 999, JobLevel: 5) } };
        var stats = Calculator().Compute(input).Total;

        stats.StatId.Should().Be(0);
        stats.Strength.Should().Be(0);
        stats.MoveSpeed.Should().Be(120);
    }

    [Test]
    public void Compute_ClampsLevelToOne()
    {
        Calculator().Compute(Input(level: 0)).Total.HpRegenPoint.Should().Be(48 + 2);
    }
}
