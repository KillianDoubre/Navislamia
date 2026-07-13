using FluentAssertions;
using Navislamia.Game.Services;

namespace Tests.Game;

[TestFixture]
public class LevelCurveTests
{
    private static readonly long[] Cumulative = { long.MaxValue, 6, 21, 53, 125, 285 };
    private const int MaxLevel = 5;

    [Test]
    public void Resolve_StaysAtLevel_WhenExpBelowThreshold()
    {
        LevelCurve.Resolve(Cumulative, MaxLevel, 5, 1).Should().Be(1);
    }

    [Test]
    public void Resolve_AdvancesOneLevel_AtThreshold()
    {
        LevelCurve.Resolve(Cumulative, MaxLevel, 6, 1).Should().Be(2);
        LevelCurve.Resolve(Cumulative, MaxLevel, 20, 2).Should().Be(2);
    }

    [Test]
    public void Resolve_AdvancesMultipleLevels_InOneStep()
    {
        LevelCurve.Resolve(Cumulative, MaxLevel, 100, 1).Should().Be(4);
    }

    [Test]
    public void Resolve_CapsAtMaxLevel()
    {
        LevelCurve.Resolve(Cumulative, MaxLevel, long.MaxValue, 1).Should().Be(MaxLevel);
    }
}
