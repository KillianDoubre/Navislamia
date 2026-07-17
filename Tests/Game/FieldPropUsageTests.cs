using FluentAssertions;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services.Props;
using NUnit.Framework;

namespace Tests.Game;

[TestFixture]
public class FieldPropUsageTests
{
    /// <summary>The value every one of the 454 spawned props carries: all races, all classes.</summary>
    private const int LimitEveryRaceAndClass = 15388;

    private const int RaceGaia = 3;
    private const int RaceDeva = 4;
    private const int RaceAsura = 5;

    private static FieldPropTemplate Template(int limit = 0, int minLevel = 0, int maxLevel = 0,
        int limitJobId = 0, params PropActivation[] activations)
    {
        return new FieldPropTemplate(123002, 6904, 0, minLevel, maxLevel, limit, limitJobId,
            new PropAction(PropActionKind.ExitDungeon, 0, 0, 123000), activations);
    }

    private static ConnectionInfo Player(int race = RaceGaia, int level = 12, int job = 100)
    {
        return new ConnectionInfo { CharacterRace = (byte)race, CharacterLevel = level, CharacterJob = job };
    }

    [TestCase(RaceGaia)]
    [TestCase(RaceDeva)]
    [TestCase(RaceAsura)]
    public void every_race_may_use_a_prop_whose_limit_allows_every_race(int race)
    {
        FieldPropUsage.IsUsable(Template(LimitEveryRaceAndClass, 1, 300), Player(race))
            .Should().BeTrue("all 454 spawned props carry limit 15388, so reading it as an exclusion " +
                             "makes every prop in the world unusable");
    }

    [Test]
    public void a_prop_limited_to_one_race_refuses_the_others()
    {
        const int devaOnly = 0x4;

        FieldPropUsage.IsUsable(Template(devaOnly), Player(RaceDeva)).Should().BeTrue();
        FieldPropUsage.IsUsable(Template(devaOnly), Player(RaceGaia)).Should().BeFalse();
        FieldPropUsage.IsUsable(Template(devaOnly), Player(RaceAsura)).Should().BeFalse();
    }

    [Test]
    public void a_prop_with_no_race_bit_allows_everyone()
    {
        FieldPropUsage.IsUsable(Template(), Player(RaceAsura)).Should().BeTrue();
    }

    [Test]
    public void the_level_range_is_enforced()
    {
        FieldPropUsage.IsUsable(Template(minLevel: 20), Player(level: 12)).Should().BeFalse();
        FieldPropUsage.IsUsable(Template(maxLevel: 10), Player(level: 12)).Should().BeFalse();
        FieldPropUsage.IsUsable(Template(minLevel: 1, maxLevel: 300), Player(level: 12)).Should().BeTrue();
    }

    [Test]
    public void a_job_limited_prop_refuses_another_job()
    {
        FieldPropUsage.IsUsable(Template(limitJobId: 200), Player(job: 100)).Should().BeFalse();
        FieldPropUsage.IsUsable(Template(limitJobId: 100), Player(job: 100)).Should().BeTrue();
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(4)]
    public void an_activation_condition_we_cannot_evaluate_refuses(int condition)
    {
        var template = Template(activations: new PropActivation(condition, 1000077, 1));

        FieldPropUsage.IsUsable(template, Player())
            .Should().BeFalse("a gated prop must refuse rather than let anyone through");
    }
}
