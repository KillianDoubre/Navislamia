using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.GmCommands;

namespace Tests.Game;

[TestFixture]
public class GmCommandParserTests
{
    [Test]
    public void IsCommand_NeedsTheSlashAndAnyTypeButWhisper()
    {
        GmCommandParser.IsCommand((byte)ChatType.Normal, "/position").Should().BeTrue();
        GmCommandParser.IsCommand((byte)ChatType.Yell, "/position").Should().BeTrue();
        GmCommandParser.IsCommand((byte)ChatType.Global, "/position").Should().BeTrue();

        GmCommandParser.IsCommand((byte)ChatType.Whisper, "/position").Should()
            .BeFalse("NGemity relays a whispered slash line as a whisper");
        GmCommandParser.IsCommand((byte)ChatType.Normal, "position").Should().BeFalse();
        GmCommandParser.IsCommand((byte)ChatType.Normal, "hello /position").Should().BeFalse();
        GmCommandParser.IsCommand((byte)ChatType.Normal, "").Should().BeFalse();
        GmCommandParser.IsCommand((byte)ChatType.Normal, null).Should().BeFalse();
    }

    [Test]
    public void IsCommand_IgnoresTheNulTerminatorTheChatBufferCarries()
    {
        GmCommandParser.IsCommand((byte)ChatType.Normal, "/position\0").Should().BeTrue();
        GmCommandParser.IsCommand((byte)ChatType.Normal, "  /position").Should().BeTrue();
    }

    [Test]
    public void TryParse_SplitsNameArgumentsAndRest()
    {
        GmCommandParser.TryParse("/Item 101221  5\0", out var line).Should().BeTrue();

        line.Name.Should().Be("item", "the name is case-insensitive");
        line.Args.Should().Equal("101221", "5");
        line.Rest.Should().Be("101221  5");
    }

    [Test]
    public void TryParse_KeepsTheFreeTextForNotice()
    {
        GmCommandParser.TryParse("/notice  Server restart in 5 minutes ", out var line).Should().BeTrue();

        line.Name.Should().Be("notice");
        line.Rest.Should().Be("Server restart in 5 minutes");
    }

    [Test]
    public void TryParse_AcceptsANameWithNoArgument()
    {
        GmCommandParser.TryParse("/doit", out var line).Should().BeTrue();

        line.Name.Should().Be("doit");
        line.Args.Should().BeEmpty();
        line.Rest.Should().BeEmpty();
    }

    [TestCase("/")]
    [TestCase("/ position")]
    [TestCase("position")]
    [TestCase("")]
    public void TryParse_RefusesALineWithoutAName(string message)
    {
        GmCommandParser.TryParse(message, out _).Should().BeFalse();
    }
}

[TestFixture]
public class GmCommandCatalogTests
{
    [Test]
    public void Names_AreUniqueAndLowerCase()
    {
        GmCommandCatalog.All.Select(definition => definition.Name).Should().OnlyHaveUniqueItems();
        GmCommandCatalog.All.Should().OnlyContain(definition => definition.Name == definition.Name.ToLowerInvariant());
    }

    [Test]
    public void EveryCommandHasADefinition()
    {
        GmCommandCatalog.All.Select(definition => definition.Command).Should()
            .BeEquivalentTo(Enum.GetValues<GmCommand>());
    }

    [Test]
    public void UnprivilegedCommands_AreTheNgemityPlayerOnes()
    {
        GmCommandCatalog.All.Where(definition => !definition.Privileged).Select(definition => definition.Name)
            .Should().BeEquivalentTo("help", "position", "sitdown", "standup", "battle", "walk");
    }

    [Test]
    public void PrivilegedCommands_AreHiddenBelowTheThreshold()
    {
        GmCommandCatalog.TryResolve("warp", 0, out _).Should().BeFalse();
        GmCommandCatalog.TryResolve("warp", GmCommandRules.GmPermission - 1, out _).Should().BeFalse();
        GmCommandCatalog.TryResolve("warp", GmCommandRules.GmPermission, out var warp).Should().BeTrue();
        warp.Command.Should().Be(GmCommand.Warp);

        GmCommandCatalog.TryResolve("position", 0, out var position).Should().BeTrue();
        position.Command.Should().Be(GmCommand.Position);
    }

    [Test]
    public void AnUnknownNameResolvesToNothingButIsNotConfusedWithAHiddenOne()
    {
        GmCommandCatalog.TryResolve("run", GmCommandRules.GmPermission, out _).Should()
            .BeFalse("/run executes Lua, which this repository never does");
        GmCommandCatalog.Exists("run").Should().BeFalse();
        GmCommandCatalog.Exists("warp").Should().BeTrue();
    }

    [Test]
    public void AvailableTo_ListsOnlyWhatTheCallerMayRun()
    {
        GmCommandCatalog.AvailableTo(0).Should().OnlyContain(definition => !definition.Privileged);
        GmCommandCatalog.AvailableTo(GmCommandRules.GmPermission).Should().HaveCount(GmCommandCatalog.All.Count);
    }
}

[TestFixture]
public class GmCommandRulesTests
{
    [Test]
    public void TryParseSwitch_ReadsOnOffAndFallsBackWhenMissing()
    {
        GmCommandRules.TryParseSwitch(Array.Empty<string>(), true, out var missing).Should().BeTrue();
        missing.Should().BeTrue();

        GmCommandRules.TryParseSwitch(new[] { "OFF" }, true, out var off).Should().BeTrue();
        off.Should().BeFalse();

        GmCommandRules.TryParseSwitch(new[] { "on" }, false, out var on).Should().BeTrue();
        on.Should().BeTrue();

        GmCommandRules.TryParseSwitch(new[] { "maybe" }, false, out _).Should().BeFalse();
        GmCommandRules.TryParseSwitch(new[] { "on", "off" }, false, out _).Should().BeFalse();
    }

    [Test]
    public void TryParseWarp_ReadsInvariantCoordinates()
    {
        GmCommandRules.TryParseWarp(new[] { "94454.5", "126040" }, out var x, out var y).Should().BeTrue();
        x.Should().Be(94454.5f);
        y.Should().Be(126040f);
    }

    [TestCase("-1", "10")]
    [TestCase("10", "NaN")]
    [TestCase("10", "Infinity")]
    [TestCase("10", "abc")]
    [TestCase("94454,5", "10")]
    public void TryParseWarp_RefusesACoordinateOutsideTheWorld(string x, string y)
    {
        GmCommandRules.TryParseWarp(new[] { x, y }, out _, out _).Should().BeFalse();
    }

    [Test]
    public void TryParseWarp_NeedsExactlyTwoArguments()
    {
        GmCommandRules.TryParseWarp(new[] { "10" }, out _, out _).Should().BeFalse();
        GmCommandRules.TryParseWarp(new[] { "10", "20", "30" }, out _, out _).Should().BeFalse();
        GmCommandRules.TryParseWarp(null, out _, out _).Should().BeFalse();
    }

    [Test]
    public void TryParseItem_DefaultsTheCountToOne()
    {
        GmCommandRules.TryParseItem(new[] { "101221" }, out var code, out var count).Should().BeTrue();
        code.Should().Be(101221);
        count.Should().Be(1);
    }

    [Test]
    public void TryParseItem_BoundsTheCount()
    {
        GmCommandRules.TryParseItem(new[] { "1", GmCommandRules.MaxItemCount.ToString() }, out _, out var max)
            .Should().BeTrue();
        max.Should().Be(GmCommandRules.MaxItemCount);

        GmCommandRules.TryParseItem(new[] { "1", (GmCommandRules.MaxItemCount + 1).ToString() }, out _, out _)
            .Should().BeFalse();
        GmCommandRules.TryParseItem(new[] { "1", "0" }, out _, out _).Should().BeFalse();
    }

    [TestCase("0")]
    [TestCase("-5")]
    [TestCase("abc")]
    public void TryParseItem_RefusesANonPositiveCode(string code)
    {
        GmCommandRules.TryParseItem(new[] { code }, out _, out _).Should().BeFalse();
    }

    [Test]
    public void TryParseGold_TakesASignedNonZeroDelta()
    {
        GmCommandRules.TryParseGold(new[] { "-500" }, out var delta).Should().BeTrue();
        delta.Should().Be(-500);

        GmCommandRules.TryParseGold(new[] { "0" }, out _).Should().BeFalse();
        GmCommandRules.TryParseGold(Array.Empty<string>(), out _).Should().BeFalse();
    }

    [Test]
    public void ApplyGold_NeverGoesNegativeNorWraps()
    {
        GmCommandRules.ApplyGold(100, -500).Should().Be(0);
        GmCommandRules.ApplyGold(100, 50).Should().Be(150);
        GmCommandRules.ApplyGold(long.MaxValue - 1, 10).Should().Be(long.MaxValue);
    }

    [Test]
    public void TryParseLevel_OnlyGoesUpAndStopsAtTheCurve()
    {
        GmCommandRules.TryParseLevel(new[] { "50" }, 10, 300, out var target).Should().BeTrue();
        target.Should().Be(50);

        GmCommandRules.TryParseLevel(new[] { "10" }, 10, 300, out _).Should().BeFalse("not above the current level");
        GmCommandRules.TryParseLevel(new[] { "5" }, 10, 300, out _).Should().BeFalse("no level loss is established");
        GmCommandRules.TryParseLevel(new[] { "301" }, 10, 300, out _).Should().BeFalse();
    }

    [Test]
    public void FormatPosition_IsInvariant()
    {
        GmCommandRules.FormatPosition(94454.5f, 126040f, 12.25f, 0).Should()
            .Be("X: 94454.5 Y: 126040 Z: 12.25 Layer: 0");
    }
}

[TestFixture]
public class GmActorStatusTests
{
    [Test]
    public void ForPlayer_ComposesEveryFlagIntoOneSnapshot()
    {
        ActorStatus.ForPlayer(false).Should().Be(0u);
        ActorStatus.ForPlayer(true, sitting: true, battleMode: true, walking: true).Should()
            .Be(CreatureStatus.PlayerPkOn | CreatureStatus.PlayerSitdown | CreatureStatus.BattleMode |
                CreatureStatus.PlayerWalking);
        ActorStatus.ForPlayer(false, sitting: true).Should().Be(CreatureStatus.PlayerSitdown);
    }

    [Test]
    public void ForPlayer_KeepsThePkBitWhenAnotherFlagIsSet()
    {
        (ActorStatus.ForPlayer(true, walking: true) & CreatureStatus.PlayerPkOn).Should()
            .Be(CreatureStatus.PlayerPkOn, "the mask is a snapshot: one flag must not clear another");
    }
}

[TestFixture]
public class LevelCurveExperienceTests
{
    // cumulativeExp[L] is the threshold to go from L to L + 1.
    private static readonly long[] Curve = { long.MaxValue, 100, 250, 600, long.MaxValue };

    [Test]
    public void TryGetExperienceFor_ReturnsThePreviousLevelsThreshold()
    {
        LevelCurve.TryGetExperienceFor(Curve, 4, 1, out var one).Should().BeTrue();
        one.Should().Be(0);

        LevelCurve.TryGetExperienceFor(Curve, 4, 2, out var two).Should().BeTrue();
        two.Should().Be(100);

        LevelCurve.TryGetExperienceFor(Curve, 4, 4, out var four).Should().BeTrue();
        four.Should().Be(600);

        LevelCurve.Resolve(Curve, 4, four, 1).Should().Be(4, "that experience must resolve to that level");
    }

    [Test]
    public void TryGetExperienceFor_RefusesAnOutOfRangeLevel()
    {
        LevelCurve.TryGetExperienceFor(Curve, 4, 0, out _).Should().BeFalse();
        LevelCurve.TryGetExperienceFor(Curve, 4, 5, out _).Should().BeFalse();
        LevelCurve.TryGetExperienceFor(null, 4, 2, out _).Should().BeFalse();
    }
}
