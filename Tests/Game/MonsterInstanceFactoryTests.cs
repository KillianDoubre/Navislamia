using System;
using System.Linq;
using FluentAssertions;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.Services;

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
            i.MonsterId == 2101 && i.Level == 5 && i.Hp == 900 && i.Race == 1 &&
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
        monster.Hp.Should().Be(1234);
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
            monster.MonsterId == 150009 && monster.Level == 150 && monster.Hp == 423873 &&
            monster.X >= 94656 && monster.X <= 95760 &&
            monster.Y >= 126096 && monster.Y <= 126960);
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
