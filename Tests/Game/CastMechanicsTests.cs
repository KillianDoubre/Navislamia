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
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Casting;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Props;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

/// <summary>
/// The official cast mechanics (docs/packet-specs/socle-lancer-competences.md): cast range, landing roll of a
/// harmful state, state stacking, delayed fire, cancel and casting pushback.
/// </summary>
[TestFixture]
public class CastMechanicsTests
{
    // ---- cast range ----

    [Test]
    public void The_range_is_measured_body_to_body_with_the_tolerance()
    {
        // 16 m = 192 units x 1.2 = 230.4; bodies 12 and 24 take 18 off the centre distance.
        CastRules.InRange(16, 0f, 0f, 0f, 12f, 248f, 0f, 24f, false).Should().BeTrue();
        CastRules.InRange(16, 0f, 0f, 0f, 12f, 249f, 0f, 24f, false).Should().BeFalse();
        CastRules.InRange(16, 0f, 0f, 0f, 12f, 300f, 0f, 24f, true).Should().BeTrue("x1.5 on a walking target");
    }

    [Test]
    public void A_range_of_minus_one_is_the_weapon_reach()
    {
        // 12 x 50 / 100 = 6 units x 1.2 = 7.2, plus 6 + 6 of bodies.
        CastRules.InRange(-1, 50f, 0f, 0f, 12f, 19f, 0f, 12f, false).Should().BeTrue();
        CastRules.InRange(-1, 50f, 0f, 0f, 12f, 20f, 0f, 12f, false).Should().BeFalse();
    }

    // ---- landing roll ----

    [TestCase(301, 60f, 30f, 5, 0, 85)]
    [TestCase(313, 0f, 0f, 0, 0, 50)]
    [TestCase(304, 999f, 0f, 0, 40, 40)]
    public void A_harmful_state_rolls_on_accuracy_or_on_its_probability(int effectType, float accuracy,
        float avoid, int hitBonus, int probability, int expected)
    {
        CastRules.StateLandingChance(effectType, accuracy, avoid, hitBonus, probability, 0, 1).Should().Be(expected);
    }

    [Test]
    public void The_probability_grows_with_the_skill_level_and_the_roll_lands_up_to_the_chance()
    {
        CastRules.StateLandingChance(304, 0f, 0f, 0, 30, 5, 4).Should().Be(50);
        CastRules.StateLands(50, 50).Should().BeTrue();
        CastRules.StateLands(50, 51).Should().BeFalse();
    }

    // ---- pushback ----

    [TestCase((byte)1, (byte)0, 20)]
    [TestCase((byte)1, (byte)1, 50)]
    [TestCase((byte)1, (byte)2, 100)]
    [TestCase((byte)2, (byte)1, 50)]
    [TestCase((byte)0, (byte)2, 0)]
    public void A_hit_pushes_back_or_threatens_a_cast_by_its_casting_level(byte type, byte level, int expected)
    {
        CastRules.DamageDisturbance(type, level, 100, 1000, 100).Should().Be(expected);
    }

    [Test]
    public void A_small_hit_disturbs_nothing_and_a_fast_caster_shrugs_it_off()
    {
        CastRules.DamageDisturbance(1, 2, 5, 1000, 100).Should().Be(0, "not above 0.5 % of the maximum");
        CastRules.DamageDisturbance(1, 2, 100, 1000, 150).Should().Be(0, "100 / 150 is 0 in integers");
        CastRules.DamageDisturbance(1, 0, 2000, 1000, 100).Should().Be(180, "(1 + 0.4 x 10 x (2000 / 1000)) x 20");
    }

    [Test]
    public void Stuns_and_their_effect_types_break_a_cast()
    {
        CastRules.InterruptsCasting(6005, 0, ReadOnlySpan<decimal>.Empty).Should().BeTrue();
        CastRules.InterruptsCasting(42, 104, ReadOnlySpan<decimal>.Empty).Should().BeTrue();
        CastRules.InterruptsCasting(42, 82, new decimal[] { 0, 0, 1, 0 }).Should().BeTrue();
        CastRules.InterruptsCasting(42, 82, new decimal[] { 0, 0, 0, 0, 7 }).Should().BeFalse();
        CastRules.InterruptsCasting(42, 1, ReadOnlySpan<decimal>.Empty).Should().BeFalse();
    }

    // ---- stacking ----

    private static StateRule Rule(int id, int group = 0, int reiteration = 0, StateTimeType flags = 0) =>
        new(id, new[] { group, 0, 0 }, reiteration, flags, 0, Array.Empty<decimal>());

    private static readonly Dictionary<int, StateRule> Rules = new()
    {
        [1] = Rule(1, group: 7),
        [2] = Rule(2, group: 7),
        [3] = Rule(3, group: 8),
        [4] = Rule(4, group: 7, flags: StateTimeType.NotErasable),
        [5] = Rule(5, reiteration: 5)
    };

    private static StackDecision Decide(IReadOnlyList<ActiveBuff> existing, int id, int level, uint end,
        bool force = false) => StateStacking.Decide(existing, id, Rules[id], level, end, i => Rules[i], force);

    private static ActiveBuff State(int id, int level, uint end) => new(1, id, 0, level, 0, end);

    [Test]
    public void A_stronger_or_longer_state_of_the_same_group_refuses_the_new_one()
    {
        Decide(new[] { State(1, 5, 100) }, 2, 4, 900).Refused.Should().BeTrue("a higher level is in place");
        Decide(new[] { State(1, 5, 900) }, 2, 5, 100).Refused.Should().BeTrue("the same level lasts longer");
        Decide(new[] { State(3, 9, 900) }, 2, 1, 100).Refused.Should().BeFalse("another group");
    }

    [Test]
    public void A_weaker_state_of_the_same_group_is_replaced()
    {
        var decision = Decide(new[] { State(3, 1, 1), State(1, 2, 100) }, 2, 3, 100);

        decision.Refused.Should().BeFalse();
        decision.Removed.Should().Equal(1);
        decision.RefreshIndex.Should().Be(-1);
    }

    [Test]
    public void The_same_state_is_refreshed_and_stacks_its_levels_up_to_the_reiteration_count()
    {
        var decision = Decide(new[] { State(5, 3, 100) }, 5, 4, 200);

        decision.RefreshIndex.Should().Be(0);
        decision.Level.Should().Be(5, "3 + 4 capped at 5");
        decision.Removed.Should().BeEmpty();
    }

    [Test]
    public void A_non_erasable_state_wins_against_an_erasable_one()
    {
        Decide(new[] { State(4, 1, 100) }, 2, 9, 900).Refused.Should().BeTrue();
        Decide(new[] { State(1, 9, 900) }, 4, 1, 100).Removed.Should().Equal(0);
    }

    [Test]
    public void An_aura_in_place_refuses_and_an_aura_switched_on_clears()
    {
        Decide(new[] { State(1, 1, StateStacking.NeverExpires) }, 2, 9, 900).Refused.Should().BeTrue();
        Decide(new[] { State(1, 9, 900) }, 2, 1, StateStacking.NeverExpires, force: true).Removed.Should().Equal(0);
    }

    // ---- the cast service ----

    private const ushort SkillId = 7001;
    private const long InstanceId = 0;
    private const uint MonsterHandle = 0x40000001;

    private sealed class Rolls : ICombatRandom
    {
        private readonly Queue<int> _rolls;

        public Rolls(params int[] rolls) => _rolls = new Queue<int>(rolls);

        public int Next(int maxExclusive) => _rolls.Count > 0 ? _rolls.Dequeue() : 0;
    }

    private sealed record Harness(SkillCastService Service, GameClient Client, ConnectionInfo Info,
        StorageTestHarness.FrameConnection Connection, ICombatService Combat, MonsterWorldState World);

    private static CastableBuffFields Attack(decimal delayCast = 1m, int castRange = 16, byte castingType = 0,
        byte castingLevel = 0, bool cancelable = true) =>
        new(SkillId, SkillCastKind.PhysicalAttack, 0, 0, new decimal[20], 0m, 0m, 0, 0m, 0, 0, delayCast, 0m, 0m,
            0m, 0m, 0, CastRange: castRange, CastingType: castingType, CastingLevel: castingLevel,
            Cancelable: cancelable, EffectType: 30001, IsHarmful: true);

    private static Harness Build(CastableBuffFields fields, float monsterX = 100f, ICombatRandom random = null)
    {
        var catalog = A.Fake<IBuffCatalog>();
        A.CallTo(() => catalog.Count).Returns(1);
        CastableBuffFields ignored;
        A.CallTo(() => catalog.TryGet(SkillId, out ignored)).Returns(true).AssignsOutAndRefParameters(fields);

        var stats = A.Fake<IStatService>();
        A.CallTo(() => stats.Compute(A<ConnectionInfo>._)).Returns(new CharacterStatResult(
            new StatBlock { AttackRange = 50f, CastingSpeed = 100f, MaxHp = 1000f }, new StatBlock()));

        var repository = A.Fake<IMonsterResourceRepository>();
        A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._)).Returns(new[]
        {
            new MonsterResourceEntity { Id = 2101, Level = 1, Hp = 80, Size = 1m, Scale = 1m }
        });
        var world = new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions
        {
            Spawns = { new MonsterSpawnPoint { MonsterId = 2101, X = (int)monsterX, Y = 0, Count = 1, Radius = 0 } }
        }));

        var combat = A.Fake<ICombatService>();
        var service = new SkillCastService(catalog, stats, A.Fake<IStateCatalog>(), world, combat,
            A.Fake<IFieldPropCatalog>(), A.Fake<IWarpService>(), random: random ?? new Rolls(), runTicks: false);

        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 1;
        info.CharacterHp = 500;
        info.CharacterMaxHp = 1000;
        info.CharacterMp = 1000;
        info.LearnedSkills[SkillId] = 1;
        lock (info.MonsterVisibilityLock)
        {
            info.SpawnedMonsters[InstanceId] = MonsterHandle;
        }

        service.Register(client);
        return new Harness(service, client, info, connection, combat, world);
    }

    private static GameActionPackets.SkillRequest Request() => new(SkillId, 1, MonsterHandle, 0f, 0f, 0f, 0, 1);

    private static List<SkillPacketType> Steps(Harness harness) => harness.Connection.Sent
        .Where(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2)) == (ushort)GamePackets.TM_SC_SKILL)
        .Select(p => (SkillPacketType)p[31]).ToList();

    private static ushort ErrorOf(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(52, 2));

    [Test]
    public void A_cast_with_a_delay_fires_when_its_time_comes()
    {
        var harness = Build(Attack(delayCast: 1m));
        var now = ServerClock.Now;

        harness.Service.Cast(harness.Client, Request());

        Steps(harness).Should().Equal(SkillPacketType.Casting);
        harness.Info.PendingCast.Should().NotBeNull();
        A.CallTo(() => harness.Combat.ApplyDamage(A<GameClient>._, A<long>._, A<uint>._, A<int>._, A<int>._)).MustNotHaveHappened();

        harness.Service.ProcessCasts(unchecked(now + 200));

        Steps(harness).Should().Equal(SkillPacketType.Casting, SkillPacketType.Fire, SkillPacketType.Complete);
        harness.Info.PendingCast.Should().BeNull();
        A.CallTo(() => harness.Combat.ApplyDamage(harness.Client, InstanceId, MonsterHandle, A<int>._, A<int>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public void A_second_cast_during_a_cast_is_refused()
    {
        var harness = Build(Attack(delayCast: 1m));

        harness.Service.Cast(harness.Client, Request());
        harness.Service.Cast(harness.Client, Request());

        var refusal = harness.Connection.Sent.Last();
        ((SkillPacketType)refusal[31]).Should().Be(SkillPacketType.Casting);
        ErrorOf(refusal).Should().Be((ushort)ResultCode.NotActable);
    }

    [Test]
    public void A_cancelled_cast_never_fires()
    {
        var harness = Build(Attack(delayCast: 1m));
        harness.Service.Cast(harness.Client, Request());

        harness.Service.CancelCast(harness.Client).Should().BeTrue();
        harness.Service.ProcessCasts(unchecked(ServerClock.Now + 500));

        Steps(harness).Should().Equal(SkillPacketType.Casting, SkillPacketType.Cancel);
        A.CallTo(() => harness.Combat.ApplyDamage(A<GameClient>._, A<long>._, A<uint>._, A<int>._, A<int>._)).MustNotHaveHappened();
    }

    [Test]
    public void A_skill_not_flagged_cancellable_cannot_be_cancelled()
    {
        var harness = Build(Attack(delayCast: 1m, cancelable: false));
        harness.Service.Cast(harness.Client, Request());

        harness.Service.CancelCast(harness.Client).Should().BeFalse();
        harness.Info.PendingCast.Should().NotBeNull();
    }

    [Test]
    public void A_target_out_of_range_is_refused_too_far()
    {
        var harness = Build(Attack(castRange: 2), monsterX: 500f);

        harness.Service.Cast(harness.Client, Request());

        ErrorOf(harness.Connection.Sent.Single()).Should().Be((ushort)ResultCode.TooFar);
        harness.Info.CharacterMp.Should().Be(1000, "nothing is spent on a refusal");
    }

    [Test]
    public void A_hit_pushes_the_fire_back_and_tells_the_caster()
    {
        var harness = Build(Attack(delayCast: 1m, castingType: CastRules.PushedBack, castingLevel: 1));
        harness.Service.Cast(harness.Client, Request());
        var fireTick = harness.Info.PendingCast.FireTick;

        harness.Service.OnCasterDamaged(harness.Client, 100);

        harness.Info.PendingCast.FireTick.Should().Be(unchecked(fireTick + 50));
        var update = harness.Connection.Sent.Last();
        ((SkillPacketType)update[31]).Should().Be(SkillPacketType.CastingUpdate);
        BinaryPrimitives.ReadUInt32LittleEndian(update.AsSpan(48, 4)).Should().Be(150u, "100 ticks of cast + 50");
    }

    [Test]
    public void A_hit_may_break_a_breakable_cast()
    {
        var harness = Build(Attack(delayCast: 1m, castingType: CastRules.Breakable, castingLevel: 1),
            random: new Rolls(49));
        harness.Service.Cast(harness.Client, Request());

        harness.Service.OnCasterDamaged(harness.Client, 100);

        harness.Info.PendingCast.Should().BeNull("49 < 50 % breaks it");
        Steps(harness).Last().Should().Be(SkillPacketType.Cancel);
    }

    [Test]
    public void The_relay_reaches_the_cast_service()
    {
        var relay = new CastInterrupts();
        var harness = Build(Attack(delayCast: 1m));
        relay.Attach(harness.Service);
        harness.Service.Cast(harness.Client, Request());

        relay.Interrupt(harness.Client);

        harness.Info.PendingCast.Should().BeNull();
    }
}
