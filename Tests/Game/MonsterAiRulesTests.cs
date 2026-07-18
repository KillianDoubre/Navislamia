using FluentAssertions;
using Navislamia.Game.Services;
using NUnit.Framework;

namespace Tests.Game;

[TestFixture]
public class MonsterAiRulesTests
{
    // A spawn-area monster: visible 18 (scaled 216), chase 100 (scaled 1200).
    private const int VisibleRange = 18;
    private const int ChaseRange = 100;
    private const float Reach = 150f;

    private static MonsterAiAction Decide(bool hasTarget, bool aggressive, bool streamed,
        bool cooldownElapsed, float monsterX, float playerX, float homeX = 0f)
    {
        return MonsterAiRules.Decide(hasTarget, aggressive, streamed, cooldownElapsed,
            monsterX, 0f, homeX, 0f, playerX, 0f, VisibleRange, ChaseRange, Reach);
    }

    [Test]
    public void an_aggressive_monster_acquires_a_player_inside_its_visible_range()
    {
        Decide(hasTarget: false, aggressive: true, streamed: true, cooldownElapsed: false,
            monsterX: 0f, playerX: 200f).Should().Be(MonsterAiAction.Acquire);
    }

    [Test]
    public void a_player_just_outside_the_visible_range_is_not_acquired()
    {
        Decide(hasTarget: false, aggressive: true, streamed: true, cooldownElapsed: false,
            monsterX: 0f, playerX: 217f).Should().Be(MonsterAiAction.Idle);
    }

    [Test]
    public void a_passive_monster_does_not_acquire_on_sight()
    {
        Decide(hasTarget: false, aggressive: false, streamed: true, cooldownElapsed: false,
            monsterX: 0f, playerX: 50f).Should().Be(MonsterAiAction.Idle);
    }

    [Test]
    public void a_monster_never_acquires_a_player_it_is_not_streamed_to()
    {
        Decide(hasTarget: false, aggressive: true, streamed: false, cooldownElapsed: false,
            monsterX: 0f, playerX: 50f).Should().Be(MonsterAiAction.Idle);
    }

    [Test]
    public void a_target_beyond_attack_reach_is_chased()
    {
        Decide(hasTarget: true, aggressive: true, streamed: true, cooldownElapsed: true,
            monsterX: 0f, playerX: 400f).Should().Be(MonsterAiAction.Chase);
    }

    [Test]
    public void a_target_within_attack_reach_is_attacked_when_the_cooldown_elapsed()
    {
        Decide(hasTarget: true, aggressive: true, streamed: true, cooldownElapsed: true,
            monsterX: 0f, playerX: 100f).Should().Be(MonsterAiAction.Attack);
    }

    [Test]
    public void a_target_within_reach_but_on_cooldown_waits()
    {
        Decide(hasTarget: true, aggressive: true, streamed: true, cooldownElapsed: false,
            monsterX: 0f, playerX: 100f).Should().Be(MonsterAiAction.Idle);
    }

    [Test]
    public void a_monster_pulled_beyond_its_chase_range_from_home_drops_the_target()
    {
        // Home at 0, monster dragged to 1300 > scaled chase range 1200.
        Decide(hasTarget: true, aggressive: true, streamed: true, cooldownElapsed: true,
            monsterX: 1300f, playerX: 1350f, homeX: 0f).Should().Be(MonsterAiAction.Drop);
    }

    [Test]
    public void losing_sight_of_the_target_drops_it()
    {
        Decide(hasTarget: true, aggressive: true, streamed: false, cooldownElapsed: true,
            monsterX: 0f, playerX: 100f).Should().Be(MonsterAiAction.Drop);
    }

    [Test]
    public void the_damage_is_one_hundredth_of_max_hp()
    {
        MonsterAiRules.PlayerDamage(5000).Should().Be(50);
    }

    [Test]
    public void the_damage_never_falls_below_one()
    {
        MonsterAiRules.PlayerDamage(50).Should().Be(1);
        MonsterAiRules.PlayerDamage(0).Should().Be(1);
    }

    [Test]
    public void the_chase_step_stops_short_of_the_player()
    {
        var (x, _) = MonsterAiRules.ChaseStep(0f, 0f, 1000f, 0f, Reach);

        x.Should().BeApproximately(1000f - Reach * 0.8f, 0.1f);
    }

    [Test]
    public void the_chase_step_does_not_overshoot_a_close_player()
    {
        var step = MonsterAiRules.ChaseStep(0f, 0f, 50f, 0f, Reach);

        step.X.Should().Be(0f, "a player already inside the stop distance is not walked away from");
    }
}
