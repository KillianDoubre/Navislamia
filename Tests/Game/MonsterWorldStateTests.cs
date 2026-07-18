using System;
using System.Collections.Generic;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services;

namespace Tests.Game;

[TestFixture]
public class MonsterWorldStateTests
{
    private const long InstanceId = 0;

    private static MonsterWorldState BuildState(int hp)
    {
        var options = new MonsterSpawnOptions
        {
            Spawns = { new MonsterSpawnPoint { MonsterId = 2101, X = 1000, Y = 2000, Count = 1, Radius = 0 } }
        };
        var repository = A.Fake<IMonsterResourceRepository>();
        A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._))
            .Returns(new[] { new MonsterResourceEntity { Id = 2101, Level = 5, Hp = hp, Race = 1 } });

        return new MonsterWorldState(repository, Options.Create(options));
    }

    [Test]
    public void ApplyDamage_ReducesHp_AndClampsAtZero()
    {
        var state = BuildState(100);

        state.ApplyDamage(InstanceId, 30).Should().Be(70);
        state.ApplyDamage(InstanceId, 100).Should().Be(0);
        state.GetHp(InstanceId).Should().Be(0);
    }

    [Test]
    public void NewInstance_IsAlive_WithFullHp()
    {
        var state = BuildState(250);

        state.IsAlive(InstanceId).Should().BeTrue();
        state.GetHp(InstanceId).Should().Be(250);
    }

    [Test]
    public void Kill_ThenCollectRespawns_RestoresFullHp_AfterDeadline()
    {
        var state = BuildState(100);
        var now = DateTime.UtcNow;
        state.ApplyDamage(InstanceId, 100);
        state.Kill(InstanceId, now.AddSeconds(10));

        state.IsAlive(InstanceId).Should().BeFalse();
        state.CollectRespawns(now).Should().BeEmpty();
        state.IsAlive(InstanceId).Should().BeFalse();

        state.CollectRespawns(now.AddSeconds(11)).Should().Contain(InstanceId);
        state.IsAlive(InstanceId).Should().BeTrue();
        state.GetHp(InstanceId).Should().Be(100);
    }

    [Test]
    public void GetPosition_DefaultsToSpawnOrigin()
    {
        var state = BuildState(100);

        state.GetPosition(InstanceId).Should().Be((1000f, 2000f));
    }

    private const byte WanderSpeed = 25;

    [Test]
    public void TryBeginWander_SchedulesOnFirstSight_ThenPicksADestinationWithinRadius()
    {
        var state = BuildState(100);
        var now = DateTime.UtcNow;

        state.TryBeginWander(InstanceId, now, WanderSpeed, out _).Should().BeFalse();

        state.TryBeginWander(InstanceId, now.AddSeconds(15), WanderSpeed, out var order).Should().BeTrue();
        Distance((order.DestX, order.DestY), (1000f, 2000f)).Should().BeLessThanOrEqualTo(150f);
        // The monster starts at its origin and interpolates toward the destination over time, so it is
        // still near the origin the instant the move begins — it does not teleport to the destination.
        Distance(state.GetPosition(InstanceId), (1000f, 2000f)).Should().BeLessThanOrEqualTo(1f);
    }

    [Test]
    public void TryBeginWander_ReturnsFalse_ForDeadInstance()
    {
        var state = BuildState(100);
        var now = DateTime.UtcNow;
        state.Kill(InstanceId, now.AddSeconds(10));

        state.TryBeginWander(InstanceId, now.AddSeconds(20), WanderSpeed, out _).Should().BeFalse();
    }

    [Test]
    public void Respawn_ReturnsMonsterToOrigin()
    {
        var state = BuildState(100);
        var now = DateTime.UtcNow;
        state.TryBeginWander(InstanceId, now, WanderSpeed, out _);
        state.TryBeginWander(InstanceId, now.AddSeconds(15), WanderSpeed, out _).Should().BeTrue();

        state.Kill(InstanceId, now.AddSeconds(16));
        state.CollectRespawns(now.AddSeconds(17));

        state.GetPosition(InstanceId).Should().Be((1000f, 2000f));
    }

    [Test]
    public void BeginMove_RecordsTheDestinationAndIsMoving()
    {
        var state = BuildState(100);

        var order = state.BeginMove(InstanceId, 1500f, 2000f, WanderSpeed);

        order.DestX.Should().Be(1500f);
        state.TryGetMoveDestination(InstanceId, out var dx, out var dy).Should().BeTrue();
        (dx, dy).Should().Be((1500f, 2000f));
        state.IsMoving(InstanceId).Should().BeTrue("a 500-unit move at speed 25 takes ~6 s");
    }

    [Test]
    public void Aggro_RemembersThePreAggroPosition_AndClearAggroHome()
    {
        var state = BuildState(100);
        var client = Client();

        // Origin is (1000, 2000); acquire from there.
        state.SetAggro(InstanceId, client);

        state.TryGetAggroHome(InstanceId, out var hx, out var hy).Should().BeTrue();
        (hx, hy).Should().Be((1000f, 2000f), "the monster returns to where it was when it aggroed");
    }

    // The aggro state machine only uses a client as an identity reference, so an uninitialised
    // instance is enough and avoids GameClient's socket/network constructor.
    private static GameClient Client() =>
        (GameClient)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GameClient));

    [Test]
    public void AnAggroedMonster_DoesNotWander()
    {
        var state = BuildState(100);
        var now = DateTime.UtcNow;

        state.SetAggro(InstanceId, Client());

        state.TryBeginWander(InstanceId, now.AddSeconds(15), WanderSpeed, out _).Should().BeFalse();
    }

    [Test]
    public void Kill_ClearsAggro()
    {
        var state = BuildState(100);

        state.SetAggro(InstanceId, Client());
        state.Kill(InstanceId, DateTime.UtcNow.AddSeconds(10));

        state.TryGetAggro(InstanceId, out _, out _).Should().BeFalse();
    }

    [Test]
    public void SetAggro_KeepsTheAttackCooldown_WhenTheSameClientReaggros()
    {
        var state = BuildState(100);
        var client = Client();

        state.SetAggro(InstanceId, client);
        state.SetNextAttack(InstanceId, 5000);
        state.SetAggro(InstanceId, client);

        state.TryGetAggro(InstanceId, out _, out var nextAttack).Should().BeTrue();
        nextAttack.Should().Be(5000, "retaliation must not reset an in-progress swing timer");
    }

    [Test]
    public void ClearAggroFor_DropsOnlyTheLeavingClientsTargets()
    {
        var state = BuildState(100);
        var leaving = Client();

        state.SetAggro(InstanceId, leaving);

        state.ClearAggroFor(leaving).Should().Contain(InstanceId);
        state.TryGetAggro(InstanceId, out _, out _).Should().BeFalse();
    }

    [Test]
    public void StopMove_FreezesTheMonsterAndEndsMovement()
    {
        var state = BuildState(100);
        state.BeginMove(InstanceId, 5000f, 6000f, 25);
        state.IsMoving(InstanceId).Should().BeTrue();

        var (x, y) = state.StopMove(InstanceId);

        state.IsMoving(InstanceId).Should().BeFalse("a stopped monster stands still to attack");
        state.GetPosition(InstanceId).Should().Be((x, y));
    }

    [Test]
    public void ReturnHome_SuppressesWanderUntilTheMonsterArrives()
    {
        var state = BuildState(100);
        var now = DateTime.UtcNow;
        // Make wander eligible: schedule and pass its first deadline.
        state.TryBeginWander(InstanceId, now, 25, out _);

        state.ReturnHome(InstanceId, 9000f, 9000f, 80);

        // While still walking home (a long move), wander is refused so the return is not hijacked.
        state.TryBeginWander(InstanceId, now.AddSeconds(30), 25, out _)
            .Should().BeFalse("a monster walking home must not be pulled into a wander");
    }

    private static float Distance((float X, float Y) a, (float X, float Y) b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return (float)System.Math.Sqrt(dx * dx + dy * dy);
    }
}
