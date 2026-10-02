using FluentAssertions;
using System;
using FakeItEasy;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Rates;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

[TestFixture]
public class CombatRewardsTests
{
    [Test]
    public void Resource_values_and_alternate_set_are_selected_without_a_level_formula()
    {
        var row = new MonsterResourceEntity { Exp = 731, Jp = 119, Exp2 = 9001, Jp2 = 301,
            GoldDropPercentage = 30, GoldMin = 3, GoldMax = 8, GoldMin2 = 15, GoldMax2 = 29,
            ChaosDropPercentage = 60, ChaosMin = 1, ChaosMax = 9, ChaosMin2 = 7, ChaosMax2 = 50 };
        MonsterRewardProfile.From(row).Should().Be(new MonsterRewardProfile(731, 119, 30, 3, 8, 60, 1, 9));
        MonsterRewardProfile.From(row, true).Should().Be(new MonsterRewardProfile(9001, 301, 30, 15, 29, 60, 7, 50));
    }

    private static ICombatRandom Draw(int result)
    {
        var random = A.Fake<ICombatRandom>(); A.CallTo(() => random.Next(A<int>._)).Returns(result); return random;
    }

    [TestCase(0, 1, 0, 0)] [TestCase(40, 1, 39, 9)] [TestCase(40, 1, 40, 0)]
    [TestCase(40, 2, 79, 9)] [TestCase(40, 2, 80, 0)] [TestCase(100, 0, 0, 0)]
    [TestCase(100, double.NaN, 0, 0)] [TestCase(100, double.PositiveInfinity, 0, 0)]
    [TestCase(100, -1, 0, 0)] [TestCase(80, 2, 99, 9)]
    public void Probability_is_in_percent_and_rates_change_chance_instead_of_the_amount(int chance, double rate, int roll, int expected)
    {
        CombatRewards.RollAmount(chance, 9, 9, rate, Draw(roll)).Should().Be(expected);
    }

    [TestCase(0, 100)] [TestCase(3, 103)]
    public void Both_amount_bounds_are_inclusive(int value, int expected)
    {
        var random = A.Fake<ICombatRandom>();
        A.CallTo(() => random.Next(100)).Returns(0); A.CallTo(() => random.Next(4)).Returns(value);
        CombatRewards.RollAmount(100, 100, 103, 1, random).Should().Be(expected);
    }

    [Test]
    public void Wide_or_bad_amount_bounds_do_not_overflow_or_produce_negative_rewards()
    {
        var random = A.Fake<ICombatRandom>();
        A.CallTo(() => random.Next(100)).Returns(0);
        A.CallTo(() => random.Next(65536)).Returns(65535);
        A.CallTo(() => random.Next(32768)).Returns(32767);
        CombatRewards.RollAmount(100, 0, int.MaxValue, 1, random).Should().Be(int.MaxValue);
        CombatRewards.RollAmount(100, -100, -1, 1, random).Should().Be(0);
        CombatRewards.RollAmount(100, 9, 3, 1, Draw(0)).Should().Be(3);
    }

    [Test]
    public void Gold_uses_the_official_drop_ceiling_and_zero_rows_do_not_invent_rewards()
    {
        var rates = A.Fake<IRateService>();
        A.CallTo(() => rates.Get(A<RateType>._)).Returns(1);
        A.CallTo(() => rates.Scale(A<long>._, A<RateType>._)).ReturnsLazily((long value, RateType _) => value);
        CombatRewards.Roll(new(901, 211, 100, int.MaxValue, int.MaxValue, 100, 5, 5), rates, Draw(0))
            .Should().Be(new MonsterKillReward(901, 211, 1_000_000, 5));
        CombatRewards.Roll(default, rates, Draw(0)).Should().Be(default(MonsterKillReward));
    }

    [Test]
    public void Progress_saturates_and_party_shares_preserve_every_unit()
    {
        CombatRewards.AddProgress(long.MaxValue - 3, 20).Should().Be(long.MaxValue);
        (CombatRewards.Share(17, 3, 0) + CombatRewards.Share(17, 3, 1) + CombatRewards.Share(17, 3, 2)).Should().Be(17);
        CombatRewards.ChaosCapacity(float.NaN).Should().Be(0);
        CombatRewards.ChaosCapacity(500.9f).Should().Be(500);
    }
}
