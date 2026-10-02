using System;
using System.Collections.Generic;
using System.Linq;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// The official hate list (<c>StructMonster::addHate</c>, <c>findNextEnemy</c>; docs/packet-specs/socle-haine.md).
/// </summary>
[TestFixture]
public class HateTests
{
    private static (MonsterWorldState World, long Id) World()
    {
        var repository = A.Fake<IMonsterResourceRepository>();
        A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._)).Returns(new[]
        {
            new MonsterResourceEntity { Id = 2101, Level = 1, Hp = 80 }
        });
        var world = new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions
        {
            Spawns = { new MonsterSpawnPoint { MonsterId = 2101, X = 0, Y = 0, Count = 1, Radius = 0 } }
        }));
        return (world, world.WithinRange(0, 0, 100).Single().InstanceId);
    }

    private static GameClient Player() =>
        StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));

    private static GameClient TargetOf(MonsterWorldState world, long id) =>
        world.SnapshotAggro().Single(a => a.InstanceId == id).Enemy;

    [Test]
    public void The_first_attacker_is_the_target_and_hate_accumulates()
    {
        var (world, id) = World();
        var a = Player();

        world.AddHate(id, a, 10);
        world.AddHate(id, a, 5);

        TargetOf(world, id).Should().BeSameAs(a);
        world.GetHate(id, a).Should().Be(15);
    }

    [Test]
    public void The_target_changes_only_when_another_enemy_hates_strictly_more()
    {
        var (world, id) = World();
        var a = Player();
        var b = Player();
        world.AddHate(id, a, 10);

        world.AddHate(id, b, 10);
        TargetOf(world, id).Should().BeSameAs(a, "a tie keeps the current target");

        world.AddHate(id, b, 1);
        TargetOf(world, id).Should().BeSameAs(b);
    }

    [Test]
    public void Hate_never_goes_below_zero()
    {
        var (world, id) = World();
        var a = Player();

        world.AddHate(id, a, 5);
        world.AddHate(id, a, -50);

        world.GetHate(id, a).Should().Be(0);
    }

    [Test]
    public void A_lost_target_hands_over_to_the_next_most_hated_enemy()
    {
        var (world, id) = World();
        var a = Player();
        var b = Player();
        var c = Player();
        world.AddHate(id, a, 30);
        world.AddHate(id, b, 10);
        world.AddHate(id, c, 20);

        world.DropTarget(id).Should().BeTrue();
        TargetOf(world, id).Should().BeSameAs(c);

        world.DropTarget(id).Should().BeTrue();
        world.DropTarget(id).Should().BeFalse("nobody is left");
        world.SnapshotAggro().Should().BeEmpty();
    }

    [Test]
    public void A_leaving_player_is_struck_off_every_list_and_a_death_clears_it()
    {
        var (world, id) = World();
        var a = Player();
        var b = Player();
        world.AddHate(id, a, 30);
        world.AddHate(id, b, 10);

        world.ClearAggroFor(b);
        world.GetHate(id, b).Should().Be(0);
        TargetOf(world, id).Should().BeSameAs(a);

        world.Kill(id, DateTime.UtcNow.AddMinutes(1));
        world.GetHate(id, a).Should().Be(0);
        world.AddHate(id, a, 10);
        world.SnapshotAggro().Should().BeEmpty("a corpse hates nobody");
    }

    [Test]
    public void A_heal_draws_the_hate_of_the_healed_players_enemies()
    {
        var (world, id) = World();
        var tank = Player();
        var healer = Player();
        world.AddHate(id, tank, 10);

        world.AddHateFromHelp(tank, healer, 25);

        world.GetHate(id, healer).Should().Be(25);
        TargetOf(world, id).Should().BeSameAs(healer);
    }

    [TestCase(0, 100, 10, 2, 0)]
    [TestCase(-1, 100, 10, 2, 120)]
    [TestCase(2, 30, 5, 2, 230)]
    public void A_skill_is_worth_its_hate_points(double hateMod, int basic, double perLevel, int level, int expected)
    {
        HateRules.SkillHate((decimal)hateMod, basic, (decimal)perLevel, level, 100).Should().Be(expected);
    }
}
