using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Dungeons;
using Navislamia.Game.Services.Props;

namespace Tests.Game;

/// <summary>The secret dungeons' key monsters and portals (docs/packet-specs/socle-donjons-instances-secrets.md).</summary>
[TestFixture]
public class SecretPortalTests
{
    private static readonly int[] KeyMonsters = { 10070016, 10090023, 10125009, 10146009, 10158006, 10165006 };

    private static DungeonCatalog Catalog() => new(Options.Create(new DungeonOptions { TimeZone = "UTC" }));

    [Test]
    public void Each_key_monster_opens_its_secret_dungeons_portal_for_ten_minutes()
    {
        var catalog = Catalog();

        catalog.DeathProps.Keys.Should().BeEquivalentTo(KeyMonsters);
        catalog.DeathProps[10090023].Should().Be(new DeathProp(10090023, 120291, 600, 16));
        foreach (var death in catalog.DeathProps.Values)
        {
            var action = PropScript.Parse(catalog.PropTemplates[death.PropId].Script);
            action.Kind.Should().Be(PropActionKind.EnterSecretDungeon);
            catalog.Secrets.Should().Contain(action.DungeonId);
        }
    }

    [Test]
    public void The_key_monsters_come_from_the_eight_random_respawns()
    {
        var respawns = Catalog().KeyMonsterRespawns;

        respawns.Select(r => r.Id).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
        respawns.Should().OnlyContain(r => r.IntervalTicks == 60000 && r.Count == 1 && r.Boxes.Length > 0);
        respawns.Select(r => r.MonsterId).Distinct().Should().BeEquivalentTo(KeyMonsters);
    }

    private sealed class Scene
    {
        public readonly MonsterWorldState World;
        public readonly DynamicFieldProps Props = new();
        public readonly SecretPortals Portals;

        public Scene()
        {
            var repository = A.Fake<IMonsterResourceRepository>();
            A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._)).ReturnsLazily(call =>
                call.GetArgument<IReadOnlyCollection<int>>(0)
                    .Select(id => new MonsterResourceEntity { Id = id, Level = 90, Hp = 100, Race = 1 }).ToArray());
            World = new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions()));
            Portals = new SecretPortals(Catalog(), World, Props, null, null, null, new Random(5));
        }

        public MonsterInstance[] KeyMonstersAlive() => KeyMonsters
            .SelectMany(id => World.WithinRange(200000, 120000, 200000).Where(m => m.MonsterId == id && World.IsAlive(m.InstanceId)))
            .ToArray();
    }

    [Test]
    public void The_first_tick_spawns_one_key_monster_per_respawn_in_one_of_its_boxes()
    {
        var scene = new Scene();
        scene.Portals.Tick(1000);

        var alive = scene.KeyMonstersAlive();
        alive.Should().HaveCount(8);
        var crystal = Catalog().KeyMonsterRespawns[0];
        alive.Where(m => m.MonsterId == 10070016).Should().Contain(m => crystal.Boxes.Any(b =>
            m.X >= b.Left && m.X <= b.Right && m.Y >= b.Top && m.Y <= b.Bottom));

        scene.Portals.Tick(2000);
        scene.KeyMonstersAlive().Should().HaveCount(8, "a living key monster is not doubled");
    }

    [Test]
    public void A_key_monster_death_opens_the_portal_where_it_fell_then_it_closes_and_the_monster_returns()
    {
        var scene = new Scene();
        scene.Portals.Tick(1000);
        var palmir = scene.KeyMonstersAlive().First(m => m.MonsterId == 10090023);
        var (x, y) = scene.World.GetPosition(palmir.InstanceId);

        scene.World.TryKill(palmir.InstanceId, DateTime.UtcNow.AddHours(1));
        scene.Portals.OnMonsterKilled(palmir.InstanceId, 5000);

        var portal = scene.Props.Within(x, y, 0, 1).Should().ContainSingle().Which;
        portal.PropId.Should().Be(120291);
        portal.ZOffset.Should().Be(16);
        scene.Props.TryGet(portal.InstanceId, out var prop).Should().BeTrue();
        prop.Template.Action.Should().Be(new PropAction(PropActionKind.EnterSecretDungeon, 0, 0, 120201));

        scene.Portals.Tick(5000 + 59_999);
        scene.Props.Within(x, y, 0, 1).Should().ContainSingle("600 s is 60 000 ticks");
        scene.KeyMonstersAlive().Count(m => m.MonsterId == 10090023).Should().Be(1, "the other Palmir respawn");

        scene.Portals.Tick(5000 + 60_000);
        scene.Props.Within(x, y, 0, 1).Should().BeEmpty("the portal closed");
        scene.KeyMonstersAlive().Count(m => m.MonsterId == 10090023).Should().Be(2, "the key monster came back");
        scene.World.TryGetInstance(palmir.InstanceId, out _).Should().BeFalse("its corpse left with the respawn");
    }

    [Test]
    public void An_ordinary_monster_opens_nothing()
    {
        var scene = new Scene();
        var ids = scene.World.SpawnDungeonMonsters(new[] { new MonsterSpawnPoint { MonsterId = 2101, Count = 1, X = 100, Y = 100 } });

        scene.Portals.OnMonsterKilled(ids[0], 1000);

        scene.Props.Within(100, 100, 0, 1000).Should().BeEmpty();
    }
}
