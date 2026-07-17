using FluentAssertions;
using Navislamia.Game.Services.Buffs;

namespace Tests.Game;

[TestFixture]
public class HealCurveTests
{
    /// <summary>The emulator's var[i] is our Values[i + 1], so this takes 0-based var indices.</summary>
    private static decimal[] Vars(params decimal[] values)
    {
        var full = new decimal[20];
        values.CopyTo(full, 0);
        return full;
    }

    [Test]
    public void Amount_MatchesSkill3202TheRealHealingLight()
    {
        // 3202: var0 = 0.3 (per magic point), var2 = 80 (flat), var3 = 140 (flat per level).
        var vars = Vars(0.3m, 0, 80, 140);

        HealCurve.Amount(vars, 1, magicPoint: 100, targetMaxHp: 500).Should()
            .Be(250, "30 from magic attack + 80 flat + 140 for level 1");
        HealCurve.Amount(vars, 3, magicPoint: 200, targetMaxHp: 500).Should()
            .Be(560, "60 + 80 + 420");
    }

    [Test]
    public void Amount_ReadsTheMaxHpTermFromVarSeven()
    {
        // 3201: var2 = 45, var3 = 85, var7 = 0.005 of the target's max HP per level.
        var vars = Vars(0, 0, 45, 85, 0, 0, 0, 0.005m);

        HealCurve.Amount(vars, 1, magicPoint: 0, targetMaxHp: 1000).Should()
            .Be(135, "45 + 85 + 0.5% of 1000");
        HealCurve.Amount(vars, 2, magicPoint: 0, targetMaxHp: 1000).Should()
            .Be(225, "45 + 170 + 1% of 1000");
    }

    [Test]
    public void Amount_ScalesThePerMagicPointTermWithTheSkillLevel()
    {
        var vars = Vars(0.1m, 0.01m);

        HealCurve.Amount(vars, 5, magicPoint: 100, targetMaxHp: 0).Should()
            .Be(15, "0.1 + 0.01 * 5 = 0.15 of 100 magic attack");
    }

    [Test]
    public void Amount_IsZeroWhenThereIsNothingToRead()
    {
        HealCurve.Amount(null, 1, 100, 500).Should().Be(0);
        HealCurve.Amount(new decimal[4], 1, 100, 500).Should().Be(0, "too few vars to hold the formula");
        HealCurve.Amount(Vars(), 1, 100, 500).Should().Be(0);
    }

    [Test]
    public void Amount_NeverReturnsANegativeHeal()
    {
        HealCurve.Amount(Vars(0, 0, -50), 1, 0, 0).Should().Be(0);
    }
}
