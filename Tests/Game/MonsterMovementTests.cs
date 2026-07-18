using FluentAssertions;
using Navislamia.Game.Services;
using NUnit.Framework;

namespace Tests.Game;

[TestFixture]
public class MonsterMovementTests
{
    [Test]
    public void a_move_takes_length_times_thirty_over_speed_ticks()
    {
        // 300 units at speed 30 → 300 * 30 / 30 = 300 ticks (3 s at 10 ms/tick).
        MonsterMovement.EndTick(1000, 300f, 30).Should().Be(1000 + 300);
    }

    [Test]
    public void a_zero_speed_or_zero_length_move_is_instant()
    {
        MonsterMovement.EndTick(1000, 300f, 0).Should().Be(1000);
        MonsterMovement.EndTick(1000, 0f, 30).Should().Be(1000);
    }

    [Test]
    public void before_the_move_starts_the_position_is_the_start()
    {
        MonsterMovement.PositionAt(0f, 0f, 300f, 0f, 1000, 1300, 1000)
            .Should().Be((0f, 0f));
    }

    [Test]
    public void halfway_through_the_position_is_the_midpoint()
    {
        MonsterMovement.PositionAt(0f, 0f, 300f, 0f, 1000, 1300, 1150)
            .Should().Be((150f, 0f));
    }

    [Test]
    public void after_the_move_ends_the_position_is_the_destination()
    {
        MonsterMovement.PositionAt(0f, 0f, 300f, 0f, 1000, 1300, 5000)
            .Should().Be((300f, 0f));
    }

    [Test]
    public void an_instant_move_resolves_to_the_destination()
    {
        MonsterMovement.PositionAt(0f, 0f, 300f, 0f, 1000, 1000, 1000)
            .Should().Be((300f, 0f));
    }

    [Test]
    public void the_interpolation_is_two_dimensional()
    {
        var (x, y) = MonsterMovement.PositionAt(0f, 0f, 300f, 600f, 1000, 1300, 1150);
        x.Should().Be(150f);
        y.Should().Be(300f);
    }
}
