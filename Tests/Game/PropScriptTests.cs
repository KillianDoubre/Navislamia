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

    [Test]
    public void open_market_carries_the_market_name()
    {
        var action = PropScript.Parse("open_market(deva_weapon)");

        action.Kind.Should().Be(PropActionKind.OpenMarket);
        action.Name.Should().Be("deva_weapon");
        action.Should().Be(PropAction.Market("deva_weapon"));
    }

    [Test]
    public void open_market_is_read_without_its_closing_parenthesis()
    {
        // Every merchant entry of the Epic 7.3 dialog catalogue is the truncated "open_market(": the
        // name was concatenated in the client's Lua and was not captured at generation.
        var action = PropScript.Parse("open_market(");

        action.Kind.Should().Be(PropActionKind.OpenMarket);
        action.Name.Should().BeEmpty();
    }

    [TestCase("open_market()")]
    [TestCase("open_market( )")]
    public void open_market_is_read_with_an_empty_argument(string script)
    {
        var action = PropScript.Parse(script);

        action.Kind.Should().Be(PropActionKind.OpenMarket);
        action.Name.Should().BeEmpty("the market service refuses an unnamed market instead of guessing");
    }

    [Test]
    public void open_market_tolerates_the_spacing_used_in_the_data()
    {
        PropScript.Parse(" open_market( deva_weapon ) ").Should().Be(PropAction.Market("deva_weapon"));
    }

    [Test]
    public void another_kind_carries_no_market_name()
    {
        PropScript.Parse("common_warp_gate(105093, 137583)").Name.Should().BeNull();
    }
}
