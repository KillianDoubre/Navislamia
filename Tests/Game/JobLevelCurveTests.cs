using FluentAssertions;
using Navislamia.Game.Services;

namespace Tests.Game;

[TestFixture]
public class JobLevelCurveTests
{
    private static readonly int[] Cost = { 0, 3, 5, 8, 12, 20, 32, 67, 88, 148, 0 };

    [Test]
    public void NextCost_ReturnsJpCostForCurrentJobLevel()
    {
        JobLevelCurve.NextCost(Cost, 1).Should().Be(3);
        JobLevelCurve.NextCost(Cost, 9).Should().Be(148);
    }

    [Test]
    public void NextCost_ReturnsZero_WhenTierIsCapped()
    {
        JobLevelCurve.NextCost(Cost, 10).Should().Be(0);
    }

    [Test]
    public void NextCost_ClampsBelowOne_ToFirstJobLevel()
    {
        JobLevelCurve.NextCost(Cost, 0).Should().Be(3);
    }

    [Test]
    public void NextCost_ReturnsZero_WhenOutOfRange()
    {
        JobLevelCurve.NextCost(Cost, 100).Should().Be(0);
    }
}
