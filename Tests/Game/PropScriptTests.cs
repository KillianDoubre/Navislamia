using FluentAssertions;
using Navislamia.Game.Services.Props;
using NUnit.Framework;

namespace Tests.Game;

[TestFixture]
public class PropScriptTests
{
    [Test]
    public void common_warp_gate_carries_its_destination()
    {
        var action = PropScript.Parse("common_warp_gate(105093, 137583)");

        action.Kind.Should().Be(PropActionKind.CommonWarpGate);
        action.X.Should().Be(105093);
        action.Y.Should().Be(137583);
    }

    [Test]
    public void common_warp_gate_tolerates_the_spacing_used_in_the_data()
    {
        PropScript.Parse("common_warp_gate(97636 , 29721)")
            .Should().Be(PropAction.Warp(97636, 29721));
    }

    [Test]
    public void RunTeleport_skips_its_cost_argument()
    {
        var action = PropScript.Parse("RunTeleport( 0 , 219233 , 14804 )");

        action.Kind.Should().Be(PropActionKind.RunTeleport);
        action.X.Should().Be(219233);
        action.Y.Should().Be(14804);
    }

    [Test]
    public void enter_and_exit_dungeon_carry_the_dungeon_id()
    {
        PropScript.Parse("enter_dungeon(123000)").Should()
            .Be(new PropAction(PropActionKind.EnterDungeon, 0, 0, 123000));
        PropScript.Parse("exit_dungeon(123000)").Should()
            .Be(new PropAction(PropActionKind.ExitDungeon, 0, 0, 123000));
    }

    [TestCase("warp_gate(60101)")]
    [TestCase("show_dungeon_stone(123000)")]
    [TestCase("supply_event_item()")]
    [TestCase("enter_vulcanus()")]
    [TestCase("1")]
    [TestCase("")]
    [TestCase(null)]
    public void an_unsupported_script_resolves_to_nothing(string script)
    {
        PropScript.Parse(script).Should().Be(PropAction.None);
    }

    [TestCase("common_warp_gate(105093")]
    [TestCase("common_warp_gate(105093)")]
    [TestCase("common_warp_gate(a, b)")]
    [TestCase("enter_dungeon()")]
    public void a_malformed_script_resolves_to_nothing(string script)
    {
        PropScript.Parse(script).Should().Be(PropAction.None);
    }
}
