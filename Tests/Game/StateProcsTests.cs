using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Casting;
using Navislamia.Game.Services.Combat;
using Navislamia.Game.Services.Creatures;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Party;
using Navislamia.Game.Services.Props;
using Navislamia.Game.Services.Rates;
using Navislamia.Game.Services.Stats;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using NUnit.Framework;

namespace Tests.Game;

[TestFixture]
public partial class StateProcsTests
{
    private static ConnectionInfo Info(GameClient c) => StorageTestHarness.Session(c);
    private static decimal[] Vars(int state = 42)
    {
        var v = new decimal[20];
        v[0] = state; v[2] = 1; v[3] = 2; v[4] = 3; v[5] = .25m;
        v[6] = 100; v[8] = 99; v[12] = 31; v[18] = 99;
        return v;
    }
    private static ICombatRandom Draw(int roll = 0)
    {
        var random = A.Fake<ICombatRandom>(); A.CallTo(() => random.Next(A<int>._)).Returns(roll); return random;
    }

    [TestCase(0, 1000, 0)] [TestCase(1, 1000, 1)] [TestCase(599, 1000, 59)]
    [TestCase(600, 1000, 60)] [TestCase(1000, 1000, 100)] [TestCase(0, 0, 0)]
    public void Vital_percentages_distinguish_zero_from_a_nonzero_value_below_one_percent(int value, float max, int expected) =>
        AttackProcConditions.Percent(value, max).Should().Be(expected);

    [TestCase(10048, StateProcEvent.Attack, false)] [TestCase(10049, StateProcEvent.Attack, true)]
    [TestCase(10050, StateProcEvent.BeingAttacked, false)] [TestCase(10051, StateProcEvent.BeingAttacked, true)]
    [TestCase(10052, StateProcEvent.Kill, true)]
    [TestCase(10053, StateProcEvent.Critical, false)] [TestCase(10054, StateProcEvent.Critical, true)]
    [TestCase(10055, StateProcEvent.BeingCritical, false)] [TestCase(10056, StateProcEvent.BeingCritical, true)]
    [TestCase(10057, StateProcEvent.Avoid, false)] [TestCase(10058, StateProcEvent.Avoid, true)]
    [TestCase(10059, StateProcEvent.Block, false)] [TestCase(10060, StateProcEvent.Block, true)]
    [TestCase(10061, StateProcEvent.PerfectBlock, false)] [TestCase(10062, StateProcEvent.PerfectBlock, true)]
    public void Every_official_skill_family_has_the_right_event_and_target(int effect, StateProcEvent trigger, bool self)
    {
        StateProcs.Describe(effect, false, out var actual, out var own).Should().BeTrue();
        actual.Should().Be(trigger); own.Should().Be(self);
        var v = Vars();
        var proc = StateProcs.Resolve(new[] { new StateProcTag(700, effect, 3, v) }, trigger,
            101, EnergyProcs.NormalAttack, 0, 100, 80, 0, 100, Draw()).Single();
        proc.Should().Be(new StateProc(700, 42, 7, 375, 0, self));
    }

    [TestCase(26, StateProcEvent.Attack, false)] [TestCase(36, StateProcEvent.Attack, false)]
    [TestCase(37, StateProcEvent.Attack, true)] [TestCase(38, StateProcEvent.BeingAttacked, false)]
    [TestCase(39, StateProcEvent.BeingAttacked, true)] [TestCase(3201, StateProcEvent.Kill, true)]
    [TestCase(3202, StateProcEvent.Critical, false)] [TestCase(3203, StateProcEvent.Critical, true)]
    [TestCase(3204, StateProcEvent.BeingCritical, false)] [TestCase(3205, StateProcEvent.BeingCritical, true)]
    [TestCase(3206, StateProcEvent.Avoid, false)] [TestCase(3207, StateProcEvent.Avoid, true)]
    [TestCase(3208, StateProcEvent.Block, false)] [TestCase(3209, StateProcEvent.Block, true)]
    [TestCase(3210, StateProcEvent.PerfectBlock, false)] [TestCase(3211, StateProcEvent.PerfectBlock, true)]
    [TestCase(3311, StateProcEvent.Dead, true)]
    public void State_trigger_ids_are_a_separate_space(int effect, StateProcEvent trigger, bool self)
    {
        StateProcs.Describe(effect, true, out var actual, out var own).Should().BeTrue();
        actual.Should().Be(trigger); own.Should().Be(self);
    }

    [Test]
    public void Ratio_is_truncated_and_the_success_boundary_is_strict()
    {
        var v = Vars(); v[6] = .8m; v[7] = .2m;
        AttackProcConditions.Attack(v, 1, 101, 1, 0, 100, 100, 0).Should().BeTrue();
        AttackProcConditions.Attack(v, 1, 101, 1, 0, 100, 100, 1).Should().BeFalse();
        v[7] = 0;
        EnergyProcs.Applies(v, 1, 101, 1, 0, 100, 100, 0).Should().BeFalse("an int ratio of zero");
        v[6] = 100;
        EnergyProcs.Applies(v, 1, 101, 1, 0, 100, 100, 99).Should().BeTrue();
    }

    [TestCase(19, 30, false)] [TestCase(20, 30, true)] [TestCase(80, 69, true)]
    [TestCase(81, 40, false)] [TestCase(50, 29, false)] [TestCase(50, 70, false)] [TestCase(50, -1, false)]
    public void Owner_hp_bounds_are_inclusive_and_target_max_is_exclusive(int hp, int target, bool expected)
    {
        var v = Vars(); v[14] = 20; v[15] = 80; v[16] = 30; v[17] = 70;
        EnergyProcs.Applies(v, 1, 101, 1, 0, hp, target, 0).Should().Be(expected);
    }

    [Test]
    public void Weapon_type_and_element_must_all_match_before_a_proc_can_fire()
    {
        var v = Vars(); v[8] = 101; v[9] = 102; v[12] = 20; v[18] = 3;
        EnergyProcs.Applies(v, 1, 102, 20, 3, 100, 100, 0).Should().BeTrue();
        EnergyProcs.Applies(v, 1, 201, 20, 3, 100, 100, 0).Should().BeFalse();
        EnergyProcs.Applies(v, 1, 102, 18, 3, 100, 100, 0).Should().BeFalse();
        EnergyProcs.Applies(v, 1, 102, 20, 2, 100, 100, 0).Should().BeFalse();
        var random = Draw();
        StateProcs.Resolve(new[] { new StateProcTag(700, 10048, 1, v) }, StateProcEvent.Attack,
            201, 20, 3, 100, 100, 0, 100, random).Should().BeEmpty();
        A.CallTo(() => random.Next(A<int>._)).MustNotHaveHappened();
    }

    [Test]
    public void Kill_tags_use_level_difference_and_mp_and_do_not_use_attack_or_element_fields()
    {
        var v = Vars(); v[12] = 0; v[18] = 5; v[19] = 60; v[16] = 99; v[17] = 1;
        AttackProcConditions.Death(v, 1, 101, 100, 0, 5, 60, 0, false).Should().BeTrue();
        AttackProcConditions.Death(v, 1, 101, 100, 0, 6, 60, 0, false).Should().BeFalse();
        AttackProcConditions.Death(v, 1, 101, 100, 0, 5, 59, 0, false).Should().BeFalse();
        v[18] = -1; v[14] = 10;
        AttackProcConditions.Death(v, 1, 101, 0, 0, 500, 60, 0, true).Should().BeTrue("death tags have no owner HP bounds");
        AttackProcConditions.Death(v, 1, 101, 0, 0, 500, 60, 0, false).Should().BeFalse();
    }

    [Test]
    public void Legacy_state_26_only_procs_on_normal_attacks_and_ignores_new_fields()
    {
        var v = Vars(); v[8] = 99; v[14] = 200; v[18] = 3;
        var tag = new StateProcTag(0, 26, 1, v, true);
        StateProcs.Resolve(new[] { tag }, StateProcEvent.Attack, 0, 1, 0, 1, 1, 0, 100, Draw()).Should().ContainSingle();
        StateProcs.Resolve(new[] { tag }, StateProcEvent.Attack, 0, 18, 0, 1, 1, 0, 100, Draw()).Should().BeEmpty();
    }

    [Test]
    public void Learned_zero_and_unrelated_effects_cannot_produce_states()
    {
        var catalog = new StateProcs(new[] { (700, 10048, Vars()), (701, 32001, Vars()) });
        var tags = catalog.Learned(new Dictionary<int, byte> { [700] = 0, [701] = 1, [702] = 3 }).ToArray();
        StateProcs.Resolve(tags, StateProcEvent.Attack, 0, 1, 0, 100, 100, 0, 100, Draw()).Should().BeEmpty();
        StateProcs.Describe(32001, false, out _, out _).Should().BeFalse("32001 amplifies summon experience");
    }

    private sealed class Harness
    {
        public readonly IBuffCatalog Catalog = A.Fake<IBuffCatalog>();
        public readonly IStateCatalog States = A.Fake<IStateCatalog>();
        public readonly IStatService Stats = A.Fake<IStatService>();
        public readonly ILevelingService Leveling = A.Fake<ILevelingService>();
        public readonly PlayerRegistry Registry = new();
        public readonly IPlayerVisibilityService Players = A.Fake<IPlayerVisibilityService>();
        public readonly Dictionary<GameClient, StorageTestHarness.FrameConnection> Wires = new();
        public readonly Dictionary<int, StateRule> Rules = new();
        public readonly MonsterWorldState World;
        public readonly CombatService Combat;
        public readonly SkillCastService Casts;
        public readonly long Monster;
        public Harness(params (int Skill, int Effect, decimal[] Values)[] procs)
        {
            A.CallTo(() => States.Exists(A<int>._)).Returns(true);
            A.CallTo(() => States.GetRule(A<int>._)).ReturnsLazily((int id) => Rules.GetValueOrDefault(id, StateRule.None with { StateId = id }));
            A.CallTo(() => Stats.Compute(A<ConnectionInfo>._)).Returns(new CharacterStatResult(new StatBlock
                { MaxHp = 1000, MaxMp = 1000, AttackPointRight = 100, AccuracyRight = 1000, MagicPoint = 80,
                    Critical = -1, AttackSpeed = 100, AttackRange = 50, MoveSpeed = 100 }, new StatBlock()));
            A.CallTo(() => Players.Registry).Returns(Registry);
            A.CallTo(() => Players.Observers(A<GameClient>._)).ReturnsLazily((GameClient c) => Registry.Clients.Where(x => x != c).ToArray());
            A.CallTo(() => Players.SendToObservers(A<GameClient>._, A<byte[]>._, A<bool>._))
                .Invokes((GameClient c, byte[] frame, bool self) =>
                { if (self) c.Connection.Send(frame); foreach (var viewer in Players.Observers(c)) viewer.Connection.Send(frame); });
            var repo = A.Fake<IMonsterResourceRepository>();
            A.CallTo(() => repo.GetByIds(A<IReadOnlyCollection<int>>._)).Returns(new[]
                { new MonsterResourceEntity { Id = 2101, Level = 10, Hp = 5000, Size = 1, Scale = 1, Exp = 1 } });
            World = new MonsterWorldState(repo, Options.Create(new MonsterSpawnOptions
                { Spawns = { new MonsterSpawnPoint { MonsterId = 2101, Count = 1 } } }));
            Monster = World.WithinRange(0, 0, 100).Single().InstanceId;
            var parties = A.Fake<IPartyService>();
            A.CallTo(() => parties.RewardMembers(A<GameClient>._, A<float>._, A<float>._, A<byte>._))
                .ReturnsLazily((GameClient c, float x, float y, byte layer) => new[] { c });
            var rates = A.Fake<IRateService>(); A.CallTo(() => rates.Get(A<RateType>._)).Returns(1);
            var options = A.Fake<IOptionsMonitor<GameRuleOptions>>();
            A.CallTo(() => options.CurrentValue).Returns(new GameRuleOptions { PkFieldsEverywhere = true, PvpDamageRate = 1 });
            var relay = new CastInterrupts();
            Combat = new CombatService(World, A.Fake<IMonsterSpawnService>(), Leveling, A.Fake<IGroundItemService>(),
                rates, Stats, States, parties, random: Draw(), players: Players, casts: relay, runTicks: false, rules: options,
                stateProcs: new StateProcs(procs), cooldownProcs: new CooldownProcs(procs));
            var creatures = A.Fake<ICreatureService>();
            A.CallTo(() => creatures.SetSummonVitals(A<GameClient>._, A<CreatureCard>._, A<int>._, A<int>._))
                .Invokes((GameClient c, CreatureCard card, int hp, int mp) =>
                { card.Hp = hp; card.Mp = mp; var summon = Info(c).Summons.Single(s => s.Handle == card.SummonHandle); summon.Hp = hp; summon.Mp = mp; });
            Casts = new SkillCastService(Catalog, Stats, States, World, Combat,
                A.Fake<IFieldPropCatalog>(), A.Fake<IWarpService>(), Players, interrupts: relay, runTicks: false, creatures: creatures,
                leveling: Leveling);
        }
        public GameClient Client(uint handle)
        {
            var wire = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
            var c = StorageTestHarness.NewGameClient(wire); Wires[c] = wire;
            var i = Info(c); i.CharacterHandle = handle; i.CharacterName = "P" + handle;
            i.CharacterLevel = 10; i.CharacterHp = i.CharacterMaxHp = 1000; i.CharacterMp = 1000;
            i.SpawnedMonsters[Monster] = 0x40000000 + handle;
            Registry.Register(handle, c); Casts.Register(c); return c;
        }
        public SummonPresence Summon(GameClient c, uint handle)
        {
            var card = new CreatureCard { ItemId = handle, Amount = 1, SummonId = handle, SummonCode = 1,
                SummonHandle = handle, Hp = 1000, Mp = 1000, Level = 10 };
            Info(c).CreatureCards[handle] = card;
            var summon = new SummonPresence(handle, new SummonWorldEntry { Hp = 1000, MaxHp = 1000, Mp = 1000,
                MaxMp = 1000, Level = 10 }, 0, 0, 0);
            Info(c).Summons = Info(c).Summons.Append(summon).ToArray(); return summon;
        }
    }

    [TestCase(HitFlags.None, false, 0, 0)] [TestCase(HitFlags.Miss, false, 1, 1)]
    [TestCase(HitFlags.Block, false, 1, 1)] [TestCase(HitFlags.PerfectBlock, false, 1, 1)]
    [TestCase(HitFlags.Critical, true, 1, 1)]
    public void Combat_routes_hit_flags_to_the_correct_owner_and_target(HitFlags flags, bool secondSwing, int attackExtra, int defenseExtra)
    {
        var effects = new[] { 10048, 10049, 10050, 10051, 10053, 10054, 10055, 10056,
            10057, 10058, 10059, 10060, 10061, 10062 };
        var h = new Harness(effects.Select((e, n) => (e, e, Vars(100 + n))).ToArray());
        var attacker = h.Client(1); var target = h.Client(2);
        foreach (var e in effects) { Info(attacker).LearnedSkills[e] = 1; Info(target).LearnedSkills[e] = 1; }
        h.Combat.NotifyHit(new CombatActor(attacker), new CombatActor(target), new HitResult(0, flags), attackProcs: !secondSwing);
        var baseCount = flags == HitFlags.Miss || secondSwing ? 0 : 2;
        Info(attacker).ActiveBuffs.Should().HaveCount(baseCount + attackExtra + (flags == HitFlags.Critical ? 1 : 0));
        Info(target).ActiveBuffs.Should().HaveCount(baseCount + defenseExtra + (flags == HitFlags.Critical ? 1 : 0));
    }

    [Test]
    public void A_real_swing_applies_the_passive_and_notifies_each_monster_viewer_with_its_handle()
    {
        var h = new Harness((700, 10048, Vars())); var c = h.Client(1); var viewer = h.Client(2);
        Info(c).LearnedSkills[700] = 3;
        h.Combat.StartAttack(c, Info(c).GetMonsterHandle(h.Monster));
        typeof(CombatService).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(h.Combat, new object[] { DateTime.UtcNow.AddSeconds(1) });
        h.World.GetStates(h.Monster).Should().ContainSingle().Which.SourceHandle.Should().Be(1);
        foreach (var client in new[] { c, viewer })
        {
            var frame = h.Wires[client].Sent.Single(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4)) == 505);
            BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(7)).Should().Be(Info(client).GetMonsterHandle(h.Monster));
        }
    }

    [Test]
    public void A_summons_own_passive_uses_its_level_and_mp_without_using_the_masters_skills()
    {
        var v = Vars(); v[13] = 2000;
        var h = new Harness((700, 10049, v), (701, 10049, Vars(43))); var c = h.Client(1); var s = h.Summon(c, 10);
        Info(c).LearnedSkills[701] = 1;
        Info(c).CreatureCards[10].Skills[700] = 2;
        h.Combat.NotifyHit(new CombatActor(c, 10), new CombatActor(c, MonsterId: h.Monster), new HitResult(10, HitFlags.None));
        s.ActiveBuffs.Should().ContainSingle().Which.StateLevel.Should().Be(5);
        s.ActiveBuffs[0].SourceHandle.Should().Be(10); s.Mp.Should().Be(0); Info(c).CharacterMp.Should().Be(1000);
        Info(c).ActiveBuffs.Should().BeEmpty();
    }

    [Test]
    public void A_received_monster_critical_procs_player_states_and_snapshots_the_true_proc_caster()
    {
        var h = new Harness((700, 10055, Vars())); var c = h.Client(1); Info(c).LearnedSkills[700] = 1;
        A.CallTo(() => h.States.Periodic(42)).Returns(new PeriodicStateRule(42, 3, 1, 1, 0, 0, 0, 0, new decimal[20]));
        h.Combat.NotifyHit(new CombatActor(c, MonsterId: h.Monster), new CombatActor(c), new HitResult(10, HitFlags.Critical));
        var state = h.World.GetStates(h.Monster).Single();
        state.SourceHandle.Should().Be(1); state.Pulse.Should().NotBeNull();
        state.StateId.Should().Be(42); Info(c).ActiveBuffs.Should().BeEmpty();
    }

    [Test]
    public void An_active_monster_state_can_proc_against_a_player_with_monster_attribution()
    {
        var h = new Harness(); var c = h.Client(1);
        h.Rules[77] = new StateRule(77, Array.Empty<int>(), 0, 0, 36, Vars());
        h.World.AddState(h.Monster, 77, 0, 2, ServerClock.Now, uint.MaxValue);
        h.Combat.NotifyHit(new CombatActor(c, MonsterId: h.Monster), new CombatActor(c), new HitResult(10, HitFlags.None));
        var state = Info(c).ActiveBuffs.Single();
        state.SourceHandle.Should().Be(Info(c).GetMonsterHandle(h.Monster));
        state.Pulse.MonsterId.Should().Be(h.Monster); state.Pulse.MonsterLife.Should().Be(h.World.LifeVersion(h.Monster));
    }

    [Test]
    public void Expired_proc_states_do_not_fire_and_replacing_a_proc_source_does_not_skip_the_next_tag()
    {
        var h = new Harness(); var c = h.Client(1);
        h.Rules[77] = new StateRule(77, new[] { 9 }, 0, 0, 37, Vars());
        h.Rules[42] = new StateRule(42, new[] { 9 }, 0, 0, 0, Array.Empty<decimal>());
        h.Rules[78] = new StateRule(78, new[] { 9 }, 0, 0, 37, Vars(43));
        var now = ServerClock.Now;
        Info(c).ActiveBuffs.Add(new ActiveBuff(1, 77, 0, 1, now, now + 100));
        Info(c).ActiveBuffs.Add(new ActiveBuff(2, 78, 0, 1, now, now + 100));
        Info(c).ActiveBuffs.Add(new ActiveBuff(3, 79, 0, 1, now - 10, now - 1));
        h.Rules[79] = new StateRule(79, Array.Empty<int>(), 0, 0, 37, Vars(44));
        h.Combat.NotifyHit(new CombatActor(c), new CombatActor(c, MonsterId: h.Monster), new HitResult(10, HitFlags.None));
        Info(c).ActiveBuffs.Select(s => s.StateId).Should().Contain(new[] { 42, 43 }).And.NotContain(44);
    }

    [Test]
    public void A_refused_state_still_spends_mp_without_requiring_enough_mp_to_proc()
    {
        var v = Vars(); v[13] = 5000;
        var h = new Harness((700, 10049, v)); var c = h.Client(1); Info(c).LearnedSkills[700] = 1;
        h.Rules[42] = new StateRule(42, new[] { 9 }, 0, 0, 0, Array.Empty<decimal>());
        Info(c).ActiveBuffs.Add(new ActiveBuff(1, 42, 0, 99, ServerClock.Now, uint.MaxValue));
        h.Combat.NotifyHit(new CombatActor(c), new CombatActor(c, MonsterId: h.Monster), new HitResult(10, HitFlags.None));
        Info(c).ActiveBuffs.Should().ContainSingle().Which.StateLevel.Should().Be(99);
        Info(c).CharacterMp.Should().Be(0);
    }

    [Test]
    public void Monster_death_rewards_proc_once_for_the_player_and_nearby_summons()
    {
        var v = Vars(); v[18] = -1;
        var h = new Harness((700, 10052, v)); var c = h.Client(1); var summon = h.Summon(c, 10);
        Info(c).LearnedSkills[700] = 1; Info(c).CreatureCards[10].Skills[700] = 2;
        h.Combat.ApplyDamage(c, h.Monster, Info(c).GetMonsterHandle(h.Monster), int.MaxValue);
        Info(c).ActiveBuffs.Should().ContainSingle("player kill credit"); summon.ActiveBuffs.Should().ContainSingle("summon kill credit");
        var end = Info(c).ActiveBuffs[0].EndTick;
        h.Combat.ApplyDamage(c, h.Monster, Info(c).GetMonsterHandle(h.Monster), int.MaxValue);
        Info(c).ActiveBuffs.Single().EndTick.Should().Be(end);
    }

    [Test]
    public void Player_death_procs_3311_once_with_the_official_mp_threshold()
    {
        var v = Vars(); v[18] = -1; v[19] = 60;
        var h = new Harness(); var c = h.Client(1);
        h.Rules[77] = new StateRule(77, Array.Empty<int>(), 0, 0, 3311, v);
        Info(c).ActiveBuffs.Add(new ActiveBuff(1, 77, 0, 1, ServerClock.Now, uint.MaxValue));
        h.Combat.DamagePlayer(c, 1000, h.Monster, false);
        Info(c).ActiveBuffs.Should().Contain(s => s.StateId == 42);
        var end = Info(c).ActiveBuffs.Single(s => s.StateId == 42).EndTick;
        h.Combat.DamagePlayer(c, 1000, h.Monster, false);
        Info(c).ActiveBuffs.Single(s => s.StateId == 42).EndTick.Should().Be(end);
        A.CallTo(() => h.Leveling.ApplyDeathPenalty(c)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public void A_death_proc_cannot_apply_an_erase_on_death_state()
    {
        var h = new Harness(); var c = h.Client(1); Info(c).CharacterHp = 0;
        h.Rules[42] = new StateRule(42, Array.Empty<int>(), 0, StateTimeType.EraseOnDead, 0, Array.Empty<decimal>());
        h.Casts.ApplyCombatState(new CombatActor(c), new CombatActor(c), new StateProc(700, 42, 1, 100, 50, true));
        Info(c).ActiveBuffs.Should().BeEmpty(); Info(c).CharacterMp.Should().Be(950);
    }

    [Test]
    public void The_catalog_loads_proc_rows_even_when_they_are_not_castable_skills()
    {
        var repo = A.Fake<ISkillResourceRepository>();
        A.CallTo(() => repo.GetSkillRowsByEffectType(A<IReadOnlyCollection<int>>.That.Matches(ids =>
                ids.OrderBy(i => i).SequenceEqual(Enumerable.Range(10048, 15)))))
            .Returns(new[] { new CastableSkillRow() with { SkillId = 700, EffectType = 10048, Vars = Vars() } });
        var procs = new StateProcs(repo);
        var tags = procs.Learned(new Dictionary<int, byte> { [700] = 2 });
        StateProcs.Resolve(tags, StateProcEvent.Attack, 101, 1, 0, 100, 100, 0, 100, Draw())
            .Should().ContainSingle().Which.Level.Should().Be(5);
        A.CallTo(() => repo.GetCastableSkills()).MustNotHaveHappened();
    }

    [Test]
    public void A_real_damage_skill_dispatches_its_type_and_element_to_combat()
    {
        var v = Vars(); v[12] = 18; v[18] = 3;
        var h = new Harness((700, 10048, v)); var c = h.Client(1);
        var damage = new decimal[20]; damage[0] = 1;
        var fields = new CastableBuffFields(9000, SkillCastKind.PhysicalAttack, 0, 0, damage, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, EffectType: 30001, IsHarmful: true, ElementalType: 3, CastRange: 20);
        A.CallTo(() => h.Catalog.TryGet(9000, out fields)).Returns(true).AssignsOutAndRefParameters(fields);
        Info(c).LearnedSkills[9000] = 1; Info(c).LearnedSkills[700] = 2;
        h.Casts.Cast(c, new GameActionPackets.SkillRequest(9000, 1, Info(c).GetMonsterHandle(h.Monster), 0, 0, 0, 0, 1));
        h.World.GetStates(h.Monster).Should().ContainSingle().Which.StateLevel.Should().Be(5);
    }

    [Test]
    public void Helpful_heals_also_fire_the_official_helpful_magic_mask()
    {
        var v = Vars(); v[12] = 12;
        var h = new Harness((700, 10049, v)); var c = h.Client(1);
        var values = new decimal[20]; values[2] = 30;
        var fields = new CastableBuffFields(9000, SkillCastKind.Heal, 0, 0, values, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, EffectType: 501, IsHarmful: false, RequiredTarget: 0, Target: 51);
        A.CallTo(() => h.Catalog.TryGet(9000, out fields)).Returns(true).AssignsOutAndRefParameters(fields);
        Info(c).LearnedSkills[9000] = 1; Info(c).LearnedSkills[700] = 1; Info(c).CharacterHp = 500;
        h.Casts.Cast(c, new GameActionPackets.SkillRequest(9000, 1, 1, 0, 0, 0, 0, 1));
        Info(c).CharacterHp.Should().Be(530); Info(c).ActiveBuffs.Should().ContainSingle().Which.StateId.Should().Be(42);
    }

    [Test]
    public void Pvp_death_fires_both_the_killers_passive_and_the_victims_death_state_once()
    {
        var v = Vars(); v[18] = -1;
        var h = new Harness((700, 10052, v)); var killer = h.Client(1); var victim = h.Client(2);
        Info(killer).PkMode = true; Info(killer).LearnedSkills[700] = 1;
        h.Rules[77] = new StateRule(77, Array.Empty<int>(), 0, 0, 3311, Vars(43));
        Info(victim).ActiveBuffs.Add(new ActiveBuff(1, 77, 0, 1, ServerClock.Now, uint.MaxValue));
        h.Combat.DamagePlayerByPlayer(killer, victim, 1000);
        Info(killer).ActiveBuffs.Should().ContainSingle().Which.StateId.Should().Be(42);
        Info(victim).ActiveBuffs.Should().Contain(s => s.StateId == 43);
        var end = Info(killer).ActiveBuffs.Single().EndTick;
        h.Combat.DamagePlayerByPlayer(killer, victim, 1000);
        Info(killer).ActiveBuffs.Single().EndTick.Should().Be(end);
    }

    [Test]
    public void A_periodic_killing_blow_fires_death_procs_without_firing_hit_procs()
    {
        var h = new Harness((700, 10049, Vars(44))); var c = h.Client(1);
        Info(c).LearnedSkills[700] = 1; Info(c).CharacterHp = 10;
        var v = Vars(); v[18] = -1;
        h.Rules[77] = new StateRule(77, Array.Empty<int>(), 0, 0, 3311, v);
        Info(c).ActiveBuffs.Add(new ActiveBuff(1, 77, 0, 1, ServerClock.Now, uint.MaxValue));
        A.CallTo(() => h.States.Periodic(99)).Returns(new PeriodicStateRule(99, 2, 1, 0, 0, 0, 20, 0, new decimal[20]));
        h.Casts.ApplyMonsterState(c, 99, 1, 500, h.Monster);
        var start = Info(c).ActiveBuffs.Single(s => s.StateId == 99).StartTick;
        h.Casts.ProcessPeriodicStates(start + 101);
        Info(c).CharacterHp.Should().Be(0);
        Info(c).ActiveBuffs.Should().Contain(s => s.StateId == 42).And.NotContain(s => s.StateId == 44);
    }
}
