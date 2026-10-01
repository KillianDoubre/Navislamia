using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Maps.Collision;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Rates;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

/// <summary>
/// Monster movement around obstacles, real monster speeds, the death penalty and the state icons
/// (docs/packet-specs/socle-deplacement-monstres.md, socle-perte-experience.md).
/// </summary>
[TestFixture]
public class MovementAndDeathTests
{
    private static BlockPolygon Box(float minX, float minY, float maxX, float maxY) =>
        new(new[] { minX, maxX, maxX, minX }, new[] { minY, minY, maxY, maxY });

    /// <summary>A wall from (90, -100) to (110, 100): what lies between x = 0 and x = 200 on y = 0.</summary>
    private static CollisionMap Wall() => new(new[] { Box(90, -100, 110, 100) });

    [Test]
    public void A_point_and_a_walk_are_tested_against_the_obstacles()
    {
        var map = Wall();

        map.IsBlocked(100, 0).Should().BeTrue();
        map.IsBlocked(50, 0).Should().BeFalse();
        map.IsWalkBlocked(0, 0, 200, 0).Should().BeTrue();
        map.IsWalkBlocked(0, 0, 50, 50).Should().BeFalse();
        map.IsWalkBlocked(0, 150, 200, 150).Should().BeFalse("the line passes above the wall");
        // A unit standing inside an obstacle may always walk out of it.
        map.IsWalkBlocked(100, 0, 100, 300).Should().BeFalse();
    }

    [Test]
    public void The_attribute_file_is_read_in_world_coordinates()
    {
        var directory = Path.Combine(Path.GetTempPath(), "navislamia-maps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllLines(Path.Combine(directory, "terrainseamlessworld.cfg"), new[]
            {
                "; comment", "TILE_LENGTH=42", "TILECOUNT_PER_SEGMENT=6", "SEGMENTCOUNT_PER_MAP=64",
                "MAPSIZE=14,16", "MAPFILE=1,2,0,M001_002,10200"
            });
            using (var writer = new BinaryWriter(File.Create(Path.Combine(directory, "m001_002.nfa"))))
            {
                writer.Write(1);
                writer.Write(4);
                foreach (var (x, y) in new[] { (0, 0), (8, 0), (8, 8), (0, 8) })
                {
                    writer.Write(x);
                    writer.Write(y);
                }
            }

            var map = CollisionMap.Load(directory);

            map.Polygons.Should().ContainSingle();
            var polygon = map.Polygons[0];
            // world = map index x 16128 + value x (42 / 8)
            polygon.MinX.Should().Be(16128f);
            polygon.MaxX.Should().Be(16128f + 8 * 5.25f);
            polygon.MinY.Should().Be(2 * 16128f);
            map.IsBlocked(16128f + 20f, 2 * 16128f + 20f).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void A_free_line_is_walked_straight()
    {
        PathFinder.Find(Wall(), 0, 150, 200, 150).Should().Equal((200f, 150f));
    }

    [Test]
    public void A_path_goes_around_the_wall_and_never_enters_it()
    {
        var map = Wall();

        var path = PathFinder.Find(map, 0, 0, 200, 0);

        path.Should().NotBeNull();
        path!.Count.Should().BeGreaterThan(1);
        path[^1].Should().Be((200f, 0f));
        var (x, y) = (0f, 0f);
        foreach (var point in path)
        {
            map.IsWalkBlocked(x, y, point.X, point.Y).Should().BeFalse();
            (x, y) = point;
        }
    }

    [Test]
    public void An_enclosed_goal_has_no_path()
    {
        // Four walls around (500, 500): the goal cannot be reached from outside.
        var map = new CollisionMap(new[]
        {
            Box(400, 400, 600, 420), Box(400, 580, 600, 600), Box(400, 400, 420, 600), Box(580, 400, 600, 600)
        });

        PathFinder.Find(map, 0, 0, 500, 500).Should().BeNull();
    }

    [Test]
    public void A_path_is_timed_and_interpolated_leg_by_leg()
    {
        var path = new List<(float X, float Y)> { (100, 0), (100, 100) };
        var ends = MonsterMovement.PathEndTicks(0, 0, path, 1000, 30);

        // length x 30 / speed: 100 units at 30 take 100 ticks per leg.
        ends.Should().Equal(1100u, 1200u);
        MonsterMovement.PositionAlong(0, 0, path, ends, 1000, 1050).Should().Be((50f, 0f));
        MonsterMovement.PositionAlong(0, 0, path, ends, 1000, 1150).Should().Be((100f, 50f));
        MonsterMovement.PositionAlong(0, 0, path, ends, 1000, 5000).Should().Be((100f, 100f));
    }

    [Test]
    public void A_monster_moves_at_its_run_speed_over_seven()
    {
        MonsterMovement.SpeedByte(120f).Should().Be(17);
        MonsterMovement.SpeedByte(2f * 140f).Should().Be(40);
        MonsterMovement.SpeedByte(1f).Should().Be(1);

        MonsterCombatStats.From(new MonsterResourceEntity { Level = 1, RunSpeed = 140 }, null)
            .Plain.MoveSpeed.Should().Be(140f);
        MonsterCombatStats.From(new MonsterResourceEntity { Level = 1, RunSpeed = 0 }, null)
            .Plain.MoveSpeed.Should().Be(10f, "the reference's floor");
    }

    [Test]
    public void A_monster_spawns_outside_the_obstacles_when_it_can()
    {
        var spawns = new[] { new MonsterSpawnPoint { MonsterId = 1, X = 0, Y = 0, Count = 30, Radius = 100 } };
        var resources = new[] { new MonsterResourceEntity { Id = 1, Level = 1, Hp = 10 } };

        var monsters = MonsterInstanceFactory.Build(spawns, resources, isBlocked: (x, _) => x < 0);

        monsters.Should().OnlyContain(monster => monster.X >= 0);
    }

    [Test]
    public void A_monster_walks_around_a_wall_and_does_not_wander_into_one()
    {
        var world = World(WorldCollision.From(Wall()), x: 0, y: 0);

        var order = world.BeginWalk(0, 200, 0, 30);

        order.Should().NotBeNull();
        order!.Value.Path.Should().NotBeNull();
        order.Value.Path!.Count.Should().BeGreaterThan(1);
        order.Value.DestX.Should().Be(200f);

        // Everything around is an obstacle: the wander is refused every time.
        var walled = World(WorldCollision.From(new CollisionMap(new[] { Box(-1000, -1000, 1000, 1000) })), 0, 0);
        var now = DateTime.UtcNow;
        walled.TryBeginWander(0, now, 17, out _).Should().BeFalse();
        for (var i = 1; i <= 20; i++)
        {
            walled.TryBeginWander(0, now.AddMinutes(i), 17, out _).Should().BeFalse();
        }
    }

    [Test]
    public void The_death_penalty_is_the_official_curve()
    {
        var cumulative = new long[] { long.MaxValue, 21, 61, 150, 285, 500, 800, 1300, 2000, 3200, 5087 };

        LevelCurve.DeathPenalty(cumulative, 10, 1).Should().Be(0, "level 1 loses nothing");
        // 5087 x (0.15 / 9 + 0.0005) = 87.3
        LevelCurve.DeathPenalty(cumulative, 10, 10).Should().Be(87);
        LevelCurve.DeathPenalty(cumulative, 10, 10, pkServer: true).Should().Be(174);
    }

    [Test]
    public void A_death_takes_the_penalty_and_can_take_a_level_with_it()
    {
        var levels = A.Fake<ILevelResourceRepository>();
        A.CallTo(() => levels.GetAll()).Returns(Enumerable.Range(1, 10)
            .Select(level => new LevelResourceEntity { Level = level, NormalExp = level * 100L, JLvs = new int[4] })
            .ToList());
        var stats = A.Fake<IStatService>();
        A.CallTo(() => stats.Compute(A<ConnectionInfo>._))
            .Returns(new CharacterStatResult(new StatBlock { MaxHp = 500, MaxMp = 50 }, new StatBlock()));
        var leveling = new LevelingService(levels, stats, A.Fake<IRateService>());

        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 7;
        info.CharacterLevel = 5;
        info.CharacterExp = 405;
        info.CharacterMp = 80;

        // need(5) = 500: 500 x (0.15 / 4 + 0.0005) = 19, so 405 -> 386, below level 5's start (400).
        leveling.ApplyDeathPenalty(client).Should().Be(19);

        info.CharacterExp.Should().Be(386);
        info.CharacterLevel.Should().Be(4);
        info.CharacterMp.Should().Be(50, "the maxima shrink with the level");
        connection.Sent.Select(Id).Should().Contain((ushort)GamePackets.TM_SC_LEVEL_UPDATE);
    }

    [Test]
    public void Only_the_killing_hit_costs_experience()
    {
        var leveling = A.Fake<ILevelingService>();
        var combat = new CombatService(World(null, 0, 0), A.Fake<IMonsterSpawnService>(), leveling,
            A.Fake<IGroundItemService>(), A.Fake<IRateService>(), A.Fake<IStatService>(), A.Fake<IStateCatalog>());
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client);
        info.CharacterHp = 30;

        combat.DamagePlayer(client, 10).Should().Be(20);
        combat.DamagePlayer(client, 50).Should().Be(0);
        combat.DamagePlayer(client, 50).Should().Be(0);

        A.CallTo(() => leveling.ApplyDeathPenalty(client)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public void A_player_arrives_in_view_with_its_states_and_auras()
    {
        var info = new ConnectionInfo { CharacterHandle = 9 };
        info.ActiveBuffs.Add(new ActiveBuff(3, 164401, 0, 2, 100, 900));
        info.ActiveAuras[0] = 2001;

        var frames = CompanionFrames.States(info);

        frames.Select(Id).Should().Equal((ushort)GamePackets.TM_SC_AURA, (ushort)GamePackets.TM_SC_STATE);
        BinaryPrimitives.ReadUInt32LittleEndian(frames[1].AsSpan(7, 4)).Should().Be(9u);
        BinaryPrimitives.ReadUInt32LittleEndian(frames[1].AsSpan(13, 4)).Should().Be(164401u);
    }

    private static MonsterWorldState World(IWorldCollision collision, int x, int y)
    {
        var repository = A.Fake<IMonsterResourceRepository>();
        A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._))
            .Returns(new[] { new MonsterResourceEntity { Id = 2101, Level = 1, Hp = 80, RunSpeed = 120 } });
        return new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions
        {
            Spawns = { new MonsterSpawnPoint { MonsterId = 2101, X = x, Y = y, Count = 1, Radius = 0 } }
        }), collision: collision);
    }

    private static ushort Id(byte[] frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2));
}
