using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Creatures;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

/// <summary>A summon in a fight: hate, damage, death and its hold (docs/packet-specs/socle-invocations-progression.md).</summary>
[TestFixture]
public class SummonCombatTests
{
    private static readonly long[] Exp = new long[] { 5, 17, 43, 100, 228, 480, 950, 1700, 2900, 4700 }
        .Concat(Enumerable.Range(11, 160).Select(level => 4700L * level * level)).ToArray();

    private sealed class Harness
    {
        public readonly ICharacterService Characters = A.Fake<ICharacterService>();
        public readonly ICombatService Combat = A.Fake<ICombatService>();
        public readonly IPlayerVisibilityService Players = A.Fake<IPlayerVisibilityService>();
        public readonly PlayerRegistry Registry = new();
        public readonly MonsterWorldState World;
        public readonly CreatureService Service;
        public readonly GameClient Client;
        public readonly ConnectionInfo Info;
        public readonly CreatureCard Card;
        public const uint MonsterHandle = 0x40000001;

        public Harness(int summonLevel = 8, long exp = 960)
        {
            var repository = A.Fake<IMonsterResourceRepository>();
            A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._)).Returns(new[]
            {
                new MonsterResourceEntity { Id = 3001, Level = 5, Hp = 100, Size = 10, Scale = 1 }
            });
            World = new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions
                { Spawns = { new MonsterSpawnPoint { MonsterId = 3001, X = 100, Y = 100, Count = 1, Radius = 0 } } }));
            World.WithinRange(100, 100, 10);
            A.CallTo(() => Players.Registry).Returns(Registry);
            A.CallTo(() => Players.Observers(A<GameClient>._)).Returns(Array.Empty<GameClient>());
            var catalog = new CreatureCatalog(Options.Create(new CreatureCatalogOptions
            {
                Summons =
                {
                    new SummonResourceOptions { Id = 2101, Form = 1, CardId = 540014, StatId = 2101, RunSpeed = 100,
                        AttackRange = 0.2f, Size = 2.4f, Scale = 1f, Stats = new float[] { 14, 7, 16, 5, 10, 8, 10 } }
                },
                SummonExp = Exp.ToList()
            }));
            Service = new CreatureService(catalog, Characters, World, Combat, new SummonWorldService(Players), Players,
                runTicks: false);
            Client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
            Info = StorageTestHarness.Session(Client);
            Info.CharacterHandle = 7;
            Info.CharacterName = "Ana";
            Info.CharacterLevel = 20;
            Info.X = Info.DestinationX = 100;
            Info.Y = Info.DestinationY = 100;
            Info.SpawnedMonsters[0] = MonsterHandle;
            Registry.Register(7, Client);
            Card = new CreatureCard
            {
                ItemId = 60, Code = 540014, Amount = 1, Flag = CreatureRules.WithSummonFlag(ItemFlag.None),
                SummonId = 9, SummonCode = 2101, SummonName = "RossParr", Level = summonLevel, Exp = exp,
                MaxReachedLevel = summonLevel
            };
            Info.CreatureCards[60] = Card;
            Info.SummonSlots = new long[] { 60, 0, 0, 0, 0, 0 };
        }

        public uint Summon()
        {
            Service.Summon(Client, 60).Should().BeTrue();
            return Info.Summons[0].Handle;
        }

        public List<byte[]> Sent => ((StorageTestHarness.FrameConnection)Client.Connection).Sent;

        public IEnumerable<ushort> Ids => Sent.Select(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2)));
    }

    [Test]
    public void A_monster_turns_on_the_summon_that_earned_most_of_the_hate_and_forgets_it_after()
    {
        var h = new Harness();
        var summon = h.Summon();

        h.World.AddSummonHate(0, h.Client, summon, 50);
        h.World.TryGetSummonFocus(0, h.Client, out var focus).Should().BeTrue();
        focus.Should().Be(summon);
        h.World.TryGetAggro(0, out var enemy, out _).Should().BeTrue();
        enemy.Should().BeSameAs(h.Client, "the hate list stays keyed by the player");

        h.World.AddHate(0, h.Client, 80);
        h.World.TryGetSummonFocus(0, h.Client, out _).Should().BeFalse("the master now earned more of it");

        h.World.ForgetSummon(h.Client, summon);
        h.World.GetHate(0, h.Client).Should().Be(80, "the summon's share is struck off");
    }

    [Test]
    public void A_monster_that_only_hated_the_summon_gives_up_when_the_summon_leaves()
    {
        var h = new Harness();
        var summon = h.Summon();
        h.World.AddSummonHate(0, h.Client, summon, 50);

        h.World.ForgetSummon(h.Client, summon).Should().Contain(0);
        h.World.TryGetAggro(0, out _, out _).Should().BeFalse();
    }

    [Test]
    public void A_summon_swing_puts_the_hate_on_the_summon()
    {
        var h = new Harness();
        var summon = h.Summon();
        A.CallTo(() => h.Combat.GetMonsterStats(0)).Returns(new StatBlock());
        A.CallTo(() => h.Combat.ApplyDamage(h.Client, 0, Harness.MonsterHandle, A<int>._, 0)).Returns(60);

        h.Service.SummonAttack(h.Client, summon, Harness.MonsterHandle);
        h.Service.ProcessSwings(DateTime.UtcNow.AddSeconds(1));

        A.CallTo(() => h.Combat.ApplyDamage(h.Client, 0, Harness.MonsterHandle, A<int>._, 0)).MustHaveHappenedOnceExactly();
        h.World.TryGetSummonFocus(0, h.Client, out var focus).Should().BeTrue();
        focus.Should().Be(summon);
    }

    [Test]
    public void A_hit_takes_hp_and_the_killing_one_costs_exp_and_stops_the_summon()
    {
        var h = new Harness(summonLevel: 8, exp: 960);
        var summon = h.Summon();
        var maxHp = h.Info.Summons[0].Hp;

        h.Service.DamageSummon(h.Client, summon, 10).Should().Be(maxHp - 10);
        h.Ids.Should().Contain((ushort)GamePackets.TM_SC_HPMP);

        h.World.AddSummonHate(0, h.Client, summon, 50);
        h.Service.DamageSummon(h.Client, summon, 100_000).Should().Be(0);

        h.Card.IsDead.Should().BeTrue();
        var penalty = SummonProgression.DeathPenalty(8, level => level <= Exp.Length ? Exp[level - 1] : 0);
        h.Card.Exp.Should().Be(960 - penalty);
        h.Card.Level.Should().Be(7, "the penalty took the summon under its level's threshold");
        h.World.TryGetAggro(0, out _, out _).Should().BeFalse("a dead summon is no one's target");

        h.Service.SummonAttack(h.Client, summon, Harness.MonsterHandle);
        h.Service.ProcessSwings(DateTime.UtcNow.AddSeconds(1));
        A.CallTo(() => h.Combat.ApplyDamage(A<GameClient>._, A<long>._, A<uint>._, A<int>._, A<int>._))
            .MustNotHaveHappened();
    }

    [Test]
    public void A_dead_summon_is_sent_back_after_its_hold_and_stays_dead()
    {
        var h = new Harness();
        var summon = h.Summon();
        h.Service.DamageSummon(h.Client, summon, 100_000);

        h.Service.ProcessDeadSummons(unchecked(ServerClock.Now + CreatureService.DeadHoldTicks - 100));
        h.Info.Summons.Should().ContainSingle("the hold is not over");

        h.Service.ProcessDeadSummons(unchecked(ServerClock.Now + CreatureService.DeadHoldTicks + 100));
        h.Info.Summons.Should().BeEmpty();
        h.Ids.Should().Contain((ushort)GamePackets.TM_SC_UNSUMMON);

        // Summoned again, it enters dead (StructPlayer::Summon keeps the death time).
        h.Service.Summon(h.Client, 60).Should().BeTrue();
        h.Info.Summons[0].Hp.Should().Be(0);
    }

    [Test]
    public void A_revived_summon_leaves_the_dead_hold()
    {
        var h = new Harness();
        var summon = h.Summon();
        h.Service.DamageSummon(h.Client, summon, 100_000);

        h.Service.SetSummonVitals(h.Client, h.Card, 50, 10);
        h.Card.IsDead.Should().BeFalse();
        h.Service.ProcessDeadSummons(unchecked(ServerClock.Now + CreatureService.DeadHoldTicks + 100));
        h.Info.Summons.Should().ContainSingle();
        h.Info.Summons[0].Hp.Should().Be(50);
    }

    [Test]
    public void A_summon_out_regenerates_and_a_dead_one_does_not()
    {
        var h = new Harness();
        var summon = h.Summon();
        h.Service.DamageSummon(h.Client, summon, 30);
        var hurt = h.Info.Summons[0].Hp;

        h.Service.ProcessRegeneration();
        h.Info.Summons[0].Hp.Should().BeGreaterThan(hurt);
        h.Ids.Should().Contain((ushort)GamePackets.TM_SC_REGEN_HPMP);

        h.Service.DamageSummon(h.Client, summon, 100_000);
        h.Service.ProcessRegeneration();
        h.Info.Summons[0].Hp.Should().Be(0);
    }

    [Test]
    public void A_monster_swing_on_a_summon_is_rolled_on_the_summon_stats()
    {
        var h = new Harness();
        h.Summon();
        h.Service.TryGetSummonTarget(h.Client, h.Info.Summons[0].Handle, out var target).Should().BeTrue();
        target.Level.Should().Be(8);
        target.Stats.MaxHp.Should().Be(h.Info.Summons[0].Stats.MaxHp);
        CombatRange.InterUnitReach(0.2f, 1, 1, target.Size, target.Scale)
            .Should().BeGreaterThan(CombatRange.MeleeReach(0.2f, 1, 1), "a summon's body is larger than a player's");
    }
}
