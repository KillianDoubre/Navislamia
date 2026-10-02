using FakeItEasy;
using FluentAssertions;
using Navislamia.Configuration.Options;
using Navislamia.Game.Services;
using Navislamia.Game.Services.MonsterSkills;

namespace Tests.Game;

[TestFixture]
public class MonsterTriggerTests
{
    [Test]
    public void Hp_once_includes_the_threshold_and_only_fires_once()
    {
        var t = new MonsterTriggerOptions { Type = 1, Value1 = 50 };
        var random = A.Fake<ICombatRandom>(); uint flag = 0;
        MonsterTriggerRules.Check(t, 51, 1000, ref flag, random).Should().BeFalse();
        MonsterTriggerRules.Check(t, 50, 1000, ref flag, random).Should().BeTrue();
        MonsterTriggerRules.Check(t, 10, 1001, ref flag, random).Should().BeFalse();
    }
    [TestCase(0, 0, false)] [TestCase(100, 9999, true)]
    [TestCase(5, 499, true)] [TestCase(5, 500, false)]
    public void Hp_always_uses_percent_probability_on_a_one_to_ten_thousand_roll(int percent, int roll, bool expected)
    {
        var random = A.Fake<ICombatRandom>(); A.CallTo(() => random.Next(10000)).Returns(roll);
        var t = new MonsterTriggerOptions { Type = 2, Value1 = 50, Value2 = percent }; uint flag = 0;
        MonsterTriggerRules.Check(t, 50, 1000, ref flag, random).Should().Be(expected);
        MonsterTriggerRules.Check(t, 51, 1000, ref flag, random).Should().BeFalse();
    }
    [Test]
    public void Timed_once_consumes_its_flag_and_always_rearms_after_the_strict_boundary()
    {
        var random = A.Fake<ICombatRandom>(); uint flag = 1000;
        var t = new MonsterTriggerOptions { Type = 3, Value1 = 1 };
        MonsterTriggerRules.Check(t, 100, 1100, ref flag, random).Should().BeFalse();
        MonsterTriggerRules.Check(t, 100, 1101, ref flag, random).Should().BeTrue(); flag.Should().Be(0);
        MonsterTriggerRules.Check(t, 100, 2000, ref flag, random).Should().BeFalse();
        t.Type = 4; flag = 1000;
        MonsterTriggerRules.Check(t, 100, 1101, ref flag, random).Should().BeTrue(); flag.Should().Be(1101);
        MonsterTriggerRules.Check(t, 100, 1201, ref flag, random).Should().BeFalse();
        MonsterTriggerRules.Check(t, 100, 1202, ref flag, random).Should().BeTrue();
    }
    [Test]
    public void Include_start_fires_immediately_and_then_at_its_interval_across_clock_wrap()
    {
        var random = A.Fake<ICombatRandom>(); uint flag = 0;
        var t = new MonsterTriggerOptions { Type = 5, Value1 = 1 };
        MonsterTriggerRules.Check(t, 100, uint.MaxValue - 50, ref flag, random).Should().BeTrue();
        MonsterTriggerRules.Check(t, 100, 49, ref flag, random).Should().BeFalse();
        MonsterTriggerRules.Check(t, 100, 50, ref flag, random).Should().BeTrue();
    }
}
