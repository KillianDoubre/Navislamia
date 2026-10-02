using System;
using FluentAssertions;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services.Buffs;

namespace Tests.Game;

[TestFixture]
public class SkillCastRangeTests
{
    [TestCase(1, 100, 26.39f, 12, 12, false, true)]
    [TestCase(1, 100, 26.41f, 12, 12, false, false)]
    [TestCase(1, 100, 30, 12, 12, true, true)]
    [TestCase(1, 100, 30.01f, 12, 12, true, false)]
    [TestCase(-1, 50, 19.19f, 12, 12, false, true)]
    [TestCase(-1, 50, 19.21f, 12, 12, false, false)]
    [TestCase(0, 50, 12, 12, 12, false, true)]
    [TestCase(0, 50, 12.01f, 12, 12, false, false)]
    [TestCase(1, 50, 80, 120, 24, false, true)]
    [TestCase(-2, 50, 1, 12, 12, false, false)]
    [TestCase(1, 50, float.NaN, 12, 12, false, false)]
    [TestCase(1, 50, float.PositiveInfinity, 12, 12, false, false)]
    public void Target_gate_follows_original_range_units_body_sizes_and_tolerances(int range, float weapon,
        float distance, float casterSize, float targetSize, bool moving, bool expected)
    {
        SkillCastRangeRules.InRange(range, weapon, distance, casterSize, targetSize, moving).Should().Be(expected);
    }

    [Test]
    public void Player_movement_tolerance_expires_and_handles_clock_wrap()
    {
        var info = new ConnectionInfo { X = 100, Y = 100, DestinationX = 150, DestinationY = 100,
            MoveSpeed = 30, MoveStartTick = uint.MaxValue - 20 };
        SkillCastRangeRules.IsPlayerMoving(info, uint.MaxValue - 21).Should().BeFalse();
        SkillCastRangeRules.IsPlayerMoving(info, uint.MaxValue - 20).Should().BeTrue();
        SkillCastRangeRules.IsPlayerMoving(info, 0).Should().BeTrue();
        SkillCastRangeRules.IsPlayerMoving(info, 29).Should().BeFalse();
        SkillCastRangeRules.PlayerPosition(info, 29).Should().Be((150f, 100f));
    }
}
