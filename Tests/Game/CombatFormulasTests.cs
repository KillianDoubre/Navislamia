using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Rates;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

/// <summary>
/// The official damage rules (docs/packet-specs/socle-combat-reel.md): <see cref="CombatFormulas"/>, the
/// skill curve, the monster stats and the two flags on the wire.
/// </summary>
[TestFixture]
public class CombatFormulasTests
{
    /// <summary>Plays back the dice in order and fails on a roll the test did not expect.</summary>
    private sealed class ScriptedRandom : ICombatRandom
    {
        private readonly Queue<int> _rolls;

        public ScriptedRandom(params int[] rolls)
        {
            _rolls = new Queue<int>(rolls);
        }

        public int Remaining => _rolls.Count;

        public int Next(int maxExclusive)
        {
            _rolls.Should().NotBeEmpty("the hit rolled more dice than scripted");
            var roll = _rolls.Dequeue();
            roll.Should().BeInRange(0, maxExclusive - 1);
            return roll;
        }
    }

    // Spread roll 5000 is a factor of exactly 1; crit roll 99 never crits under 99 % critical.
    private const int NoSpread = 5000;
    private const int NoCrit = 99;

    private static Combatant Fighter(int level = 1, float attack = 45f, float defence = 0f, float accuracy = 10f,
        float avoid = 0f, float critical = 0f, float criticalPower = 80f, float blockChance = 0f,
        float blockDefence = 0f, float magicPoint = 0f, float magicDefence = 0f) =>
        new(level, attack, magicPoint, defence, magicDefence, accuracy, avoid, accuracy, avoid, critical,
            criticalPower, blockChance, blockDefence, 20f);

    [TestCase(100f, 115u)]
    [TestCase(40f, 287u)]
    [TestCase(200f, 57u)]
    [TestCase(0f, 1150u)]
    public void The_attack_interval_follows_the_attack_speed(float attackSpeed, uint ticks)
    {
        CombatFormulas.AttackIntervalTicks(attackSpeed).Should().Be(ticks);
    }

    [Test]
    public void The_hit_chance_is_the_official_curve()
    {
        CombatFormulas.HitChance(10, 10, 50f, 50f, 0).Should().Be(95);
        CombatFormulas.HitChance(10, 10, 50f, 50f, 3).Should().Be(98);
        CombatFormulas.HitChance(11, 10, 50f, 100f, 0).Should().Be(52);
        // Forty levels under the target, the level factor floors at 10.
        CombatFormulas.HitChance(1, 41, 50f, 50f, 0).Should().Be(17);
        CombatFormulas.HitChance(1, 1, 50f, 0f, 0).Should().Be(100);
    }

    [Test]
    public void The_defence_formula_is_the_official_one()
    {
        // 1 x 1.7 x (1 - 0.4 x 8.6/45) + 45 x (1 - 0.5 x 8.6/45) = 1.570 + 40.7 = 42.27
        CombatFormulas.DefendedDamage(1, 45f, 8.6f).Should().Be(42);
        // Both terms hit their floors: 1.7 x 0.3 + 10 x 0.05 = 1.01.
        CombatFormulas.DefendedDamage(1, 10f, 100f).Should().Be(1);
        CombatFormulas.DefendedDamage(1, 0f, 0f).Should().Be(2);
    }

    [Test]
    public void A_target_without_avoid_is_never_rolled_against()
    {
        var random = new ScriptedRandom(NoCrit, NoSpread);

        var hit = CombatFormulas.Resolve(Fighter(), Fighter(defence: 8.6f), 45f, DamageKind.Physical, 0, 0, random);

        hit.Should().Be(new HitResult(42, HitFlags.None));
        random.Remaining.Should().Be(0);
    }

    [Test]
    public void A_roll_above_the_hit_chance_misses()
    {
        // Equal levels, accuracy = avoid: 95 %. A roll of 96 misses, 95 still hits.
        var attacker = Fighter(level: 10, accuracy: 50f);
        var target = Fighter(level: 10, avoid: 50f);

        CombatFormulas.Resolve(attacker, target, 45f, DamageKind.Physical, 0, 0, new ScriptedRandom(96))
            .Should().Be(new HitResult(0, HitFlags.Miss));
        CombatFormulas.Resolve(attacker, target, 45f, DamageKind.Physical, 0, 0,
                new ScriptedRandom(95, NoCrit, NoSpread))
            .Flags.Should().Be(HitFlags.None);
    }

    [Test]
    public void A_perfect_block_cancels_the_hit()
    {
        var target = Fighter(blockChance: 30f);

        CombatFormulas.Resolve(Fighter(), target, 45f, DamageKind.Physical, 0, 0, new ScriptedRandom(10, 19))
            .Should().Be(new HitResult(0, HitFlags.PerfectBlock));
    }

    [Test]
    public void A_block_adds_the_block_defence()
    {
        var target = Fighter(defence: 0f, blockChance: 30f, blockDefence: 8.6f);

        CombatFormulas.Resolve(Fighter(), target, 45f, DamageKind.Physical, 0, 0,
                new ScriptedRandom(10, 20, NoCrit, NoSpread))
            .Should().Be(new HitResult(42, HitFlags.Block));
    }

    [Test]
    public void A_critical_adds_the_critical_power()
    {
        var attacker = Fighter(critical: 5f);

        // A roll equal to the critical chance still crits. 50 attack against no defence is 51.7, floored
        // to 51; the critical power of 80 makes it 51 x 1.8 = 91.8.
        CombatFormulas.Resolve(attacker, Fighter(), 50f, DamageKind.Physical, 0, 0, new ScriptedRandom(5, NoSpread))
            .Should().Be(new HitResult(91, HitFlags.Critical));
        CombatFormulas.Resolve(attacker, Fighter(), 50f, DamageKind.Physical, 0, 3, new ScriptedRandom(8, NoSpread))
            .Flags.Should().Be(HitFlags.Critical);
        CombatFormulas.Resolve(attacker, Fighter(), 50f, DamageKind.Physical, 0, 0, new ScriptedRandom(6, NoSpread))
            .Should().Be(new HitResult(51, HitFlags.None));
    }

    [Test]
    public void The_spread_is_five_percent_either_way()
    {
        // 100 attack, no defence, level 1: 1.7 + 100 = 101.
        CombatFormulas.Resolve(Fighter(), Fighter(), 100f, DamageKind.Physical, 0, 0, new ScriptedRandom(NoCrit, 0))
            .Damage.Should().Be(95);
        CombatFormulas.Resolve(Fighter(), Fighter(), 100f, DamageKind.Physical, 0, 0,
                new ScriptedRandom(NoCrit, 10000))
            .Damage.Should().Be(106);
    }

    [Test]
    public void Magic_uses_magic_defence_and_cannot_be_blocked()
    {
        var target = Fighter(defence: 1000f, blockChance: 100f, magicDefence: 8.6f);
        var random = new ScriptedRandom(NoCrit, NoSpread);

        CombatFormulas.Resolve(Fighter(), target, 45f, DamageKind.Magical, 0, 0, random)
            .Should().Be(new HitResult(42, HitFlags.None));
        random.Remaining.Should().Be(0);
    }

    [Test]
    public void Skill_base_damage_follows_the_reference_curves()
    {
        var vars = new decimal[20];
        vars[0] = 1.2m;
        vars[1] = 0.1m;
        vars[2] = 30m;
        vars[3] = 5m;
        vars[4] = 7m;

        // Physical: 100 x (1.2 + 0.1 x 3) + 30 + 5 x 3 = 195.
        SkillDamageCurve.BaseDamage(SkillCastKind.PhysicalAttack, vars, 3, 100f, 999f).Should().Be(195f);
        // Magical: 100 x (1.2 + 0.1 x 3) + 5 + 7 x 3 = 176 (var2 is an enhance term there).
        SkillDamageCurve.BaseDamage(SkillCastKind.MagicAttack, vars, 3, 999f, 100f).Should().Be(176f);
    }

    [Test]
    public void Skill_bonuses_follow_level_gap_and_skill_level()
    {
        var fields = new CastableBuffFields(30001, SkillCastKind.PhysicalAttack, 0, 0, new decimal[20], 0m, 0m, 0,
            0m, 0, 0, 0m, 0m, 0m, 0m, 0m, 1, HitBonus: 10, Percentage: 2, CriticalBonus: 5, CriticalBonusPerSkl: 1);

        SkillDamageCurve.HitBonus(fields, 20, 15).Should().Be(20);
        SkillDamageCurve.HitBonus(fields, 10, 15).Should().Be(0);
        SkillDamageCurve.CriticalBonus(fields, 4).Should().Be(9);
    }

    [Test]
    public void Monster_stats_add_the_resource_columns_to_the_creature_base()
    {
        // Monster 36001 (Epic 7): level 36, stat_id 13500 (str 10 vit 14 dex 7 agi 7 int 7 men 7 luk 15).
        var resource = new MonsterResourceEntity
        {
            Id = 36001, Level = 36, StatId = 13500, Hp = 3054, Mp = 1527, AttackPoint = 147, MagicPoint = 147,
            Defence = 113, MagicDefence = 122, AttackSpeed = -53
        };
        var stats = MonsterCombatStats.From(resource, new StatBaseStats(13500, 10, 14, 7, 7, 7, 7, 15));

        stats.MaxHp.Should().Be(3054 + 20 * 36 + 33 * 14);
        stats.Plain.AttackPointRight.Should().BeApproximately(36 + 2.8f * 10 + 147, 0.01f);
        stats.Plain.Defence.Should().BeApproximately(36 + 1.6f * 14 + 113, 0.01f);
        stats.Plain.AttackSpeed.Should().BeApproximately(100 + 0.1f * 7 - 53, 0.01f);
        stats.Plain.Critical.Should().BeApproximately(3 + 15 / 5f, 0.01f);
    }

    [Test]
    public void A_debuff_now_lowers_the_monster_stat_it_names()
    {
        var resource = new MonsterResourceEntity { Id = 1, Level = 10, Defence = 90 };
        var stats = MonsterCombatStats.From(resource, null);

        var lowered = stats.Compute(new[] { new StatEffect(StatTarget.Defence, -0.5f, true) });

        stats.Plain.Defence.Should().Be(100f);
        lowered.Defence.Should().Be(50f);
    }

    [Test]
    public void The_attack_event_carries_the_hit_flag_after_damage_and_mp_damage()
    {
        var packet = GameAttackPackets.BuildAttackEvent(1, 2, 1150, 1150, GameAttackPackets.ActionAttack, 0, 10, 0, 20, 0,
            (byte)HitFlags.Miss);

        packet.Should().HaveCount(83);
        packet[7 + 15 + 8].Should().Be((byte)HitFlags.Miss);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(15, 2)).Should().Be(1150);
    }

    [Test]
    public void The_skill_damage_hit_carries_the_flag_as_an_int32_at_fourteen()
    {
        var hit = new SkillHit(SkillHitType.Damage, 7, 50, 42, (byte)HitFlags.Critical);
        var packet = GameSkillPackets.BuildSkill(30001, 1, 1, 7, 0, 0, 0, 0, SkillPacketType.Fire, 0, 0, 100, 100, 0,
            0, hit);

        var record = packet.AsSpan(7 + 41 + 9);
        BinaryPrimitives.ReadInt32LittleEndian(record.Slice(10, 4)).Should().Be(42);
        BinaryPrimitives.ReadInt32LittleEndian(record.Slice(14, 4)).Should().Be((int)HitFlags.Critical);
    }

    [Test]
    public void An_immortal_player_takes_no_damage_from_a_monster_swing()
    {
        var resource = new MonsterResourceEntity { Id = 2101, Level = 5, Hp = 100, AttackPoint = 500 };
        var repository = A.Fake<IMonsterResourceRepository>();
        A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._)).Returns(new[] { resource });
        var world = new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions
        {
            Spawns = { new MonsterSpawnPoint { MonsterId = 2101, X = 0, Y = 0, Count = 1, Radius = 0 } }
        }));

        var statService = A.Fake<IStatService>();
        A.CallTo(() => statService.Compute(A<ConnectionInfo>._))
            .Returns(new CharacterStatResult(new StatBlock(), new StatBlock()));
        var combat = new CombatService(world, A.Fake<IMonsterSpawnService>(), A.Fake<ILevelingService>(),
            A.Fake<IGroundItemService>(), A.Fake<IRateService>(), statService, A.Fake<IStateCatalog>(),
            A.Fake<Navislamia.Game.Services.Party.IPartyService>(), random: new ScriptedRandom(NoCrit, NoSpread, NoCrit, NoSpread));

        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client);

        var normal = combat.RollMonsterHit(0, client, out var interval);
        info.IsImmortal = true;
        var immortal = combat.RollMonsterHit(0, client, out _);

        normal.Damage.Should().BeGreaterThan(0);
        immortal.Damage.Should().Be(0);
        // No StatResource row: attack speed 100, so one swing every 1.15 s.
        interval.Should().Be(115u);
    }
}
