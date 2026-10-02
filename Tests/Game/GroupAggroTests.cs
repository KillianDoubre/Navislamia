using System;
using System.Collections.Generic;
using System.Linq;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// The official group aggro (<c>StructMonster::processFirstAttack</c>): a monster with
/// <c>f_group_first_attack</c> that takes a player on sight gives it to the monsters of its
/// <c>monster_group</c> within its range. See docs/packet-specs/socle-aggro-groupe.md.
/// </summary>
[TestFixture]
public class GroupAggroTests
{
    private static MonsterWorldState World()
    {
        var repository = A.Fake<IMonsterResourceRepository>();
        A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._)).Returns(new[]
        {
            new MonsterResourceEntity { Id = 2101, Level = 1, Hp = 80, FirstAttack = 1, GroupFirstAttack = 1, MonsterGroup = 7, VisibleRange = 10 },
            new MonsterResourceEntity { Id = 2102, Level = 1, Hp = 80, MonsterGroup = 7, VisibleRange = 10 },
            new MonsterResourceEntity { Id = 2103, Level = 1, Hp = 80, MonsterGroup = 8, VisibleRange = 10 }
        });
        return new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions
        {
            Spawns =
            {
                new MonsterSpawnPoint { MonsterId = 2101, X = 0, Y = 0, Count = 1, Radius = 0 },
                new MonsterSpawnPoint { MonsterId = 2102, X = 50, Y = 0, Count = 1, Radius = 0 },
                new MonsterSpawnPoint { MonsterId = 2103, X = 60, Y = 0, Count = 1, Radius = 0 },
                new MonsterSpawnPoint { MonsterId = 2102, X = 500, Y = 0, Count = 1, Radius = 0 }
            }
        }));
    }

    private static MonsterInstance Find(MonsterWorldState world, int monsterId, float x)
        => world.WithinRange(0, 0, 2000).Single(i => i.MonsterId == monsterId && Math.Abs(i.X - x) < 1);

    [Test]
    public void TheLeaderRalliesOnlyItsGroupWithinItsRange()
    {
        var world = World();
        var leader = Find(world, 2101, 0);
        var near = Find(world, 2102, 50);
        var otherGroup = Find(world, 2103, 60);
        var far = Find(world, 2102, 500);
        var enemy = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var visible = new[] { leader.InstanceId, near.InstanceId, otherGroup.InstanceId, far.InstanceId };

        world.SetAggro(leader.InstanceId, enemy);
        var rallied = world.RallyGroup(leader, visible, enemy);

        rallied.Should().Be(1);
        world.SnapshotAggro().Select(a => a.InstanceId).Should()
            .BeEquivalentTo(new[] { leader.InstanceId, near.InstanceId });
    }

    [Test]
    public void AMonsterWithoutTheFlagRalliesNobody()
    {
        var world = World();
        var follower = Find(world, 2102, 50);
        var enemy = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));

        world.RallyGroup(follower, new[] { Find(world, 2101, 0).InstanceId }, enemy).Should().Be(0);
        world.SnapshotAggro().Should().BeEmpty();
    }

    [Test]
    public void TheRuleNeedsTheSameNonZeroGroupAndTheRange()
    {
        var leader = new MonsterInstance(1, 1, 0, 0, 0, 1, 80, 0, 0, true, 10, 10, 0, 1, 1, 0, 0,
            MonsterGroup: 7, GroupFirstAttack: true);
        var member = leader with { InstanceId = 2, GroupFirstAttack = false };

        MonsterAiRules.JoinsGroupAttack(leader, 0, 0, member, 120, 0).Should().BeTrue("12 × visible_range");
        MonsterAiRules.JoinsGroupAttack(leader, 0, 0, member, 121, 0).Should().BeFalse();
        MonsterAiRules.JoinsGroupAttack(leader, 0, 0, member with { MonsterGroup = 8 }, 10, 0).Should().BeFalse();
        MonsterAiRules.JoinsGroupAttack(leader with { MonsterGroup = 0 }, 0, 0, member with { MonsterGroup = 0 }, 10, 0)
            .Should().BeFalse("group 0 is no group");
        MonsterAiRules.JoinsGroupAttack(leader, 0, 0, leader, 0, 0).Should().BeFalse();
    }
}
