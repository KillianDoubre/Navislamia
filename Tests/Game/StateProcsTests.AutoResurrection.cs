using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Casting;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// EF_AUTO_RESURRECTION_AFTER_REMOVE_STATE (3321), StructPlayer::onAfterRemoveState: the state the death trigger
/// 314084 puts (314085) brings its dead owner back when it ends. socle-passifs-etats-combat.md.
/// </summary>
public partial class StateProcsTests
{
    // Epic 7 StateResource 314085: value_0..3 = 1, 0, 0.1, 0.25.
    private static readonly decimal[] AutoResurrection = { 1, 0, 0.1m, 0.25m };

    [Test]
    public void The_resurrection_spends_the_MP_and_turns_a_share_of_it_into_HP()
    {
        ResurrectionRules.VitalsByAutoResurrection(AutoResurrection, 2, 800, 1000).Should().Be((480, 0),
            "cost = 1 × 800, HP = (0.1 + 0.25 × 2) × 800");
        ResurrectionRules.VitalsByAutoResurrection(AutoResurrection, 2, 0, 1000).Should().Be((1, 0), "at least 1 HP");
        ResurrectionRules.VitalsByAutoResurrection(AutoResurrection, 8, 2000, 1000).Should().Be((1000, 0),
            "AddHP stops at the maximum");
    }

    [Test]
    public void A_dead_player_comes_back_when_its_auto_resurrection_state_ends()
    {
        var h = new Harness();
        h.Rules[314085] = StateRule.None with { StateId = 314085, EffectType = 3321, Values = AutoResurrection };
        var c = h.Client(1);
        var now = ServerClock.Now;
        Info(c).CharacterHp = 0;
        Info(c).CharacterMp = 800;
        Info(c).ActiveBuffs.Add(new ActiveBuff(1, 314085, 0, 2, now - 400, now - 1));

        h.Casts.ProcessBuffs(now);

        Info(c).CharacterHp.Should().Be(480);
        Info(c).CharacterMp.Should().Be(0);
        A.CallTo(() => h.Leveling.RestoreDeathExperience(c, 1m)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public void A_living_player_is_not_touched_when_the_state_ends()
    {
        var h = new Harness();
        h.Rules[314085] = StateRule.None with { StateId = 314085, EffectType = 3321, Values = AutoResurrection };
        var c = h.Client(1);
        var now = ServerClock.Now;
        Info(c).CharacterHp = 300;
        Info(c).CharacterMp = 800;
        Info(c).ActiveBuffs.Add(new ActiveBuff(1, 314085, 0, 2, now - 400, now - 1));

        h.Casts.ProcessBuffs(now);

        Info(c).CharacterHp.Should().Be(300);
        Info(c).CharacterMp.Should().Be(800);
        A.CallTo(() => h.Leveling.RestoreDeathExperience(A<GameClient>._, A<decimal>._)).MustNotHaveHappened();
    }
}
