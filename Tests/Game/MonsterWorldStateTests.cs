using System;
using System.Collections.Generic;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
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

    [Test]
    public void TryBeginWander_SchedulesOnFirstSight_ThenMovesWithinRadius()
    {
        var state = BuildState(100);
        var now = DateTime.UtcNow;

        state.TryBeginWander(InstanceId, now, out _).Should().BeFalse();

        state.TryBeginWander(InstanceId, now.AddSeconds(15), out var destination).Should().BeTrue();
        Distance(destination, (1000f, 2000f)).Should().BeLessThanOrEqualTo(150f);
        state.GetPosition(InstanceId).Should().Be(destination);
    }

    [Test]
    public void TryBeginWander_ReturnsFalse_ForDeadInstance()
    {
        var state = BuildState(100);
        var now = DateTime.UtcNow;
        state.Kill(InstanceId, now.AddSeconds(10));

        state.TryBeginWander(InstanceId, now.AddSeconds(20), out _).Should().BeFalse();
    }

    [Test]
    public void Respawn_ReturnsMonsterToOrigin()
    {
        var state = BuildState(100);
        var now = DateTime.UtcNow;
        state.TryBeginWander(InstanceId, now, out _);
        state.TryBeginWander(InstanceId, now.AddSeconds(15), out _).Should().BeTrue();

        state.Kill(InstanceId, now.AddSeconds(16));
        state.CollectRespawns(now.AddSeconds(17));

        state.GetPosition(InstanceId).Should().Be((1000f, 2000f));
    }

    private static float Distance((float X, float Y) a, (float X, float Y) b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return (float)System.Math.Sqrt(dx * dx + dy * dy);
    }
}
