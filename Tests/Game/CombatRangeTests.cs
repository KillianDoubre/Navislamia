using FluentAssertions;
using Navislamia.Game.Services;
using NUnit.Framework;

namespace Tests.Game;

[TestFixture]
public class CombatRangeTests
{
    [Test]
    public void a_small_monster_reaches_about_two_player_body_radii()
    {
        // attack_range 0.6, size 1, scale 1: weapon 0.072 + (12 + 12)/2 = ~12.07.
        CombatRange.MeleeReach(0.6f, 1f, 1f).Should().BeApproximately(12.07f, 0.1f);
    }

    [Test]
    public void a_bigger_monster_reaches_farther()
    {
        // size 6, scale 2: unit size 6*12*2 = 144, reach ~ 0.072 + (144 + 12)/2 = ~78.
        CombatRange.MeleeReach(0.6f, 6f, 2f).Should().BeApproximately(78.07f, 0.2f);
    }

    [Test]
    public void a_sizeless_monster_never_resolves_below_the_player_body()
    {
        CombatRange.MeleeReach(0f, 0f, 0f).Should().Be(CombatRange.PlayerUnitSize);
    }

    [Test]
    public void a_target_inside_the_reach_is_in_range()
    {
        CombatRange.InReach(0f, 0f, 40f, 0f, 50f).Should().BeTrue();
    }

    [Test]
    public void a_target_beyond_the_reach_is_out_of_range()
    {
        CombatRange.InReach(0f, 0f, 60f, 0f, 50f).Should().BeFalse();
    }

    [Test]
    public void the_reach_is_measured_in_two_dimensions()
    {
        CombatRange.InReach(0f, 0f, 40f, 40f, 50f).Should().BeFalse();
        CombatRange.InReach(0f, 0f, 35f, 35f, 50f).Should().BeTrue();
    }
}
