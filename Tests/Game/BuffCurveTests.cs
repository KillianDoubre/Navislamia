using FluentAssertions;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;

namespace Tests.Game;

[TestFixture]
public class BuffCurveTests
{
    private static CastableBuffFields Fields(decimal stateSecond = 0, decimal stateSecondPerLevel = 0,
        int stateLevelBase = 0, decimal stateLevelPerSkill = 0, int costMp = 0, int costMpPerSkl = 0,
        decimal delayCast = 0, decimal delayCastPerSkl = 0, decimal delayCommon = 0,
        decimal delayCooltime = 0, decimal delayCooltimePerSkl = 0)
    {
        return new CastableBuffFields(1, SkillCastKind.Buff, 100, 0, new decimal[20], stateSecond,
            stateSecondPerLevel, stateLevelBase, stateLevelPerSkill, costMp, costMpPerSkl, delayCast,
            delayCastPerSkl, delayCommon, delayCooltime, delayCooltimePerSkl, 0);
    }

    [Test]
    public void DurationTicks_ConvertsStateSecondFromSecondsToArTimeTicks()
    {
        // Every duration in this table is in seconds and an ar_time tick is 10 ms, so 20 s is 2000 ticks.
        BuffCurve.DurationTicks(Fields(stateSecond: 20), 1).Should().Be(2000);
        BuffCurve.DurationTicks(Fields(stateSecond: 600), 1).Should().Be(60000);
        BuffCurve.DurationTicks(Fields(stateSecond: 1800), 1).Should().Be(180000);
    }

    [Test]
    public void DurationTicks_AddsThePerLevelSecondsBeforeConverting()
    {
        BuffCurve.DurationTicks(Fields(stateSecond: 10, stateSecondPerLevel: 5), 4).Should().Be(3000);
    }

    [Test]
    public void DurationTicks_IsZeroForANonPositiveDuration()
    {
        BuffCurve.DurationTicks(Fields(), 3).Should().Be(0);
    }

    [Test]
    public void StateLevel_IsBasePlusPerSkillLevel()
    {
        BuffCurve.StateLevel(Fields(stateLevelBase: 15, stateLevelPerSkill: 3), 4).Should().Be(27);
        BuffCurve.StateLevel(Fields(stateLevelBase: 0, stateLevelPerSkill: 2), 5).Should().Be(10);
        BuffCurve.StateLevel(Fields(), 5).Should().Be(0);
    }

    [Test]
    public void MpCost_IsBasePlusPerSkillLevel()
    {
        BuffCurve.MpCost(Fields(costMp: 30, costMpPerSkl: 5), 3).Should().Be(45);
        BuffCurve.MpCost(Fields(), 3).Should().Be(0);
    }

    [Test]
    public void CooldownAndCastDelay_AreSecondsAndAreConvertedToTicks()
    {
        // The reference loader reads every delay_* column as GetFloat() * 100, so they are seconds just
        // like state_second. Deep Evasion's delay_cooltime of 120 is 120 seconds, measured in-client.
        BuffCurve.CooldownTicks(Fields(delayCooltime: 120), 1).Should().Be(12000);
        BuffCurve.CastDelayTicks(Fields(delayCast: 1.5m), 1).Should().Be(150);
        BuffCurve.CommonDelayTicks(Fields(delayCommon: 1)).Should().Be(100);
    }

    [Test]
    public void CooldownAndCastDelay_AddTheirPerLevelTerms()
    {
        BuffCurve.CooldownTicks(Fields(delayCooltime: 100, delayCooltimePerSkl: 10), 3).Should().Be(13000);
        BuffCurve.CastDelayTicks(Fields(delayCast: 20, delayCastPerSkl: 2), 5).Should().Be(3000);
    }

    [Test]
    public void ServerClock_TicksAtOneHundredPerSecond()
    {
        ServerClock.TicksPerSecond.Should().Be(100);
        ServerClock.FromSeconds(20).Should().Be(2000);
        ServerClock.FromSeconds(0.5m).Should().Be(50);
        ServerClock.FromSeconds(-5).Should().Be(0);
    }
}
