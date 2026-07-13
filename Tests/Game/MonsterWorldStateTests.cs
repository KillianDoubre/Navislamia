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
}
