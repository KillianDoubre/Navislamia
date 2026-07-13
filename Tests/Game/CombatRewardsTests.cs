using FluentAssertions;
using Navislamia.Game.Services;

namespace Tests.Game;

[TestFixture]
public class CombatRewardsTests
{
    [Test]
    public void Compute_ScalesExpJpAndGoldWithLevel()
    {
        var (exp, jp, gold) = CombatRewards.Compute(5);

        exp.Should().Be(10 + 5 * 5);
        jp.Should().Be(5 + 5 * 2);
        gold.Should().Be(5 + 5 * 3);
    }

    [Test]
    public void Compute_TreatsNonPositiveLevelAsOne()
    {
        CombatRewards.Compute(0).Should().Be(CombatRewards.Compute(1));
    }
}
