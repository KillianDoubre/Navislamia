using System;
using System.Linq;
using FluentAssertions;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

[TestFixture]
public class MonsterInstanceFactoryTests
{
    [Test]
    public void Build_ExpandsSpawnIntoInstances_WithinRadius_AndResourceStats()
    {
        var spawns = new[]
        {
            new MonsterSpawnPoint { MonsterId = 2101, X = 1000, Y = 2000, Count = 3, Radius = 200 }
        };
        var resources = new[]
        {
            new MonsterResourceEntity { Id = 2101, Level = 5, Hp = 900, Race = 1 }
        };

        var instances = MonsterInstanceFactory.Build(spawns, resources);

        instances.Should().HaveCount(3);
        instances.Select(i => i.InstanceId).Should().BeEquivalentTo(new long[] { 0, 1, 2 });
        instances.Should().OnlyContain(i =>
            i.MonsterId == 2101 && i.Level == 5 && i.Hp == 900 + 20 * 5 && i.Race == 1 &&
            Math.Abs(i.X - 1000) <= 200 && Math.Abs(i.Y - 2000) <= 200);
        instances.Should().OnlyContain(i => i.FaceDirection >= 0f && i.FaceDirection < (float)(Math.PI * 2));
    }

    [Test]
    public void Build_AllowsAlternateStatsResource_ForDevelopmentSpawn()
    {
        var spawns = new[]
        {
            new MonsterSpawnPoint
            {
                MonsterId = 31002, ResourceId = 651,
                X = 1000, Y = 2000, Count = 1, Radius = 0
            }
        };
        var resources = new[]
        {
            new MonsterResourceEntity { Id = 651, Level = 7, Hp = 1234, Race = 2 }
        };

        var monster = MonsterInstanceFactory.Build(spawns, resources).Single();

        monster.MonsterId.Should().Be(31002);
        monster.Level.Should().Be(7);
        monster.Hp.Should().Be(1234 + 20 * 7);
        monster.Race.Should().Be(2);
    }

    [Test]
    public void Build_ExpandsOfficialArea_InsideExactRectangle()
    {
        var options = new MonsterSpawnOptions
        {
            Areas =
            {
                new MonsterSpawnArea
                {
                    SpawnGroupId = 2000050,
                    Left = 94656, Top = 126096, Right = 95760, Bottom = 126960,
                    Monsters =
                    {
                        new MonsterSpawnPopulation
                        {
                            ResourceId = 150009, Count = 7
                        }
                    }
                }
            }
        };
        var resources = new[]
        {
            new MonsterResourceEntity { Id = 150009, Level = 150, Hp = 423873 }
        };

        var monsters = MonsterInstanceFactory.Build(options, resources);

        monsters.Should().HaveCount(7);
        monsters.Should().OnlyContain(monster =>
            monster.MonsterId == 150009 && monster.Level == 150 && monster.Hp == 423873 + 20 * 150 &&
            monster.X >= 94656 && monster.X <= 95760 &&
            monster.Y >= 126096 && monster.Y <= 126960);
    }

    [TestCase(true, 5)]
    [TestCase(false, 3)]
    public void Build_LeavesTheAutoTrapsOut_WhenGameUseAutoTrapIsOff(bool useAutoTrap, int expected)
    {
        // monster_respawn.lua: the rare 5049 (id % 100 = 49, below 310000) is an Auto Trap, respawned only when
        // game.use_auto_trap is 1; 1003 is an ordinary monster of the same box.
        var options = new MonsterSpawnOptions
        {
            UseAutoTrap = useAutoTrap,
            Areas =
            {
                new MonsterSpawnArea
                {
                    Left = 18942, Top = 5040, Right = 19446, Bottom = 5292,
                    Monsters =
                    {
                        new MonsterSpawnPopulation { ResourceId = 1003, Count = 3 },
                        new MonsterSpawnPopulation { ResourceId = 5049, Count = 2 }
                    }
                }
            }
        };
        var resources = new[]
        {
            new MonsterResourceEntity { Id = 1003, Level = 1, Hp = 50 },
            new MonsterResourceEntity { Id = 5049, Level = 1, Hp = 50 }
        };

        MonsterInstanceFactory.Build(options, resources).Should().HaveCount(expected);
        MonsterInstanceFactory.GetRequiredResourceIds(options).Contains(5049).Should().Be(useAutoTrap);
    }

    [TestCase(5041, true)]
    [TestCase(10049, true)]
    [TestCase(150044, true)]
    [TestCase(5042, false)]
    [TestCase(310041, false)]
    [TestCase(1003, false)]
    public void IsAutoTrap_FollowsTheLuaTagAndCeiling(int monsterId, bool trap) =>
        MonsterInstanceFactory.IsAutoTrap(monsterId).Should().Be(trap);

    [Test]
    public void Build_GivesTheMonsterItsRealMaxHp_FromStatResourceLevelAndColumn()
    {
        // Monster 710 (Epic 7 data): level 1, stat_id 10100 (vit 1), hp column 105. The official
        // server's maximum is 105 + 20 x 1 + 33 x 1 = 158, not the column.
        var spawns = new[] { new MonsterSpawnPoint { MonsterId = 710, X = 0, Y = 0, Count = 1, Radius = 0 } };
        var resources = new[] { new MonsterResourceEntity { Id = 710, Level = 1, Hp = 105, StatId = 10100 } };

        var monster = MonsterInstanceFactory.Build(spawns, resources,
            statId => statId == 10100 ? new StatBaseStats(10100, 1, 1, 0, 0, 0, 0, 10) : null).Single();

        monster.Hp.Should().Be(158);
        monster.Combat.Should().NotBeNull();
        monster.Combat.MaxHp.Should().Be(158);
    }

    [Test]
    public void Build_SkipsSpawns_WithUnknownMonsterId()
    {
        var spawns = new[]
        {
            new MonsterSpawnPoint { MonsterId = 9999, X = 0, Y = 0, Count = 5, Radius = 100 }
        };
        var resources = new[]
        {
            new MonsterResourceEntity { Id = 2101, Level = 5, Hp = 900, Race = 1 }
        };

        MonsterInstanceFactory.Build(spawns, resources).Should().BeEmpty();
    }
}
