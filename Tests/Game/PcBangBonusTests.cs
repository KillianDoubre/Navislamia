using FluentAssertions;
using Navislamia.Game.Services.Progression;

namespace Tests.Game;

/// <summary>The PC bang bonus (StructPlayer::getPCBangBonus) in the monster reward.</summary>
[TestFixture]
public class PcBangBonusTests
{
    [Test]
    public void The_mode_picks_the_official_rate()
    {
        MonsterRewardBonuses.PcBangRate(0, 0.1m, 1.2m).Should().Be(0);
        MonsterRewardBonuses.PcBangRate(1, 0.1m, 1.2m).Should().Be(0.1m);
        MonsterRewardBonuses.PcBangRate(2, 0.1m, 1.2m).Should().Be(1.2m);
    }

    [Test]
    public void The_bonus_joins_the_gain_before_the_stamina_bonus_whose_cost_stays_the_plain_gain()
    {
        // Premium (+120 %) without stamina: 100 → 220.
        MonsterRewardBonuses.Apply(100, 10, 0, 5, 1, false, 0, pcBangRate: 1.2m).Should().Be(new BonusReward(220, 22, 0));

        // With stamina: (100 × 2.2) × 2 = 440, and the stamina spent is the plain gain's cost.
        var plain = MonsterRewardBonuses.Apply(100, 10, 100000, 5, 1, false, 0);
        var premium = MonsterRewardBonuses.Apply(100, 10, 100000, 5, 1, false, 0, pcBangRate: 1.2m);
        premium.Exp.Should().Be(440);
        premium.Stamina.Should().Be(plain.Stamina);
    }
}
