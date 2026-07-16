using FluentAssertions;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

[TestFixture]
public class JobLevelBonusCurveTests
{
    private static int[] Parts(int jobLevel, int count = 3, int length = 20)
    {
        var parts = new int[count];
        JobLevelBonusCurve.FillLevelParts(jobLevel, parts, length);
        return parts;
    }

    [Test]
    public void FillLevelParts_SplitsBelowTheFirstChunk()
    {
        Parts(15).Should().Equal(15, 0, 0);
    }

    [Test]
    public void FillLevelParts_SplitsAcrossChunks()
    {
        Parts(35).Should().Equal(20, 15, 0);
    }

    [Test]
    public void FillLevelParts_LastChunkAbsorbsTheRemainder()
    {
        Parts(62).Should().Equal(20, 20, 22);
    }

    [Test]
    public void FillLevelParts_HandlesZeroAndExactBoundaries()
    {
        Parts(0).Should().Equal(0, 0, 0);
        Parts(20).Should().Equal(20, 0, 0);
        Parts(60).Should().Equal(20, 20, 20);
    }

    [Test]
    public void SumProduct_MultipliesPerLevelBonusByAchievedLevels()
    {
        JobLevelBonusCurve.SumProduct(new[] { 0.5f, 0.4f, 0.3f }, new[] { 20, 15, 0 })
            .Should().BeApproximately(16f, 0.001f);
    }

    [Test]
    public void SumProduct_KeepsFractionalRatesInsteadOfTruncating()
    {
        JobLevelBonusCurve.SumProduct(new[] { 0.5f, 0f, 0f }, new[] { 1, 0, 0 })
            .Should().BeApproximately(0.5f, 0.001f);
    }
}
