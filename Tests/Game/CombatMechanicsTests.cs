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
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Casting;
using Navislamia.Game.Services.Combat;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Rates;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

/// <summary>
/// Double attack, dual wield, bow, additional damage, mana shield and reflection
/// (docs/packet-specs/socle-mecaniques-combat.md).
/// </summary>
[TestFixture]
public class CombatMechanicsTests
{
    private static ActiveStateRule State(int effect, int level, params (int Index, decimal Value)[] values)
    {
        var all = new decimal[20];
        foreach (var (index, value) in values)
        {
            all[index] = value;
        }

        return new ActiveStateRule(new StateRule(1, Array.Empty<int>(), 0, 0, effect, all), level);
    }

    [Test]
    public void Double_attack_sums_its_states_for_the_weapon_they_name()
    {
        var swords = State(AttackMechanics.DoubleAttackEffect, 2, (0, 5m), (1, 2.5m), (8, 101m));
        var any = State(AttackMechanics.DoubleAttackEffect, 1, (0, 10m), (8, 99m));

        AttackMechanics.DoubleAttackRatio(new[] { swords, any }, ItemType.OnehandSword).Should().Be(20f);
        AttackMechanics.DoubleAttackRatio(new[] { swords, any }, ItemType.Dagger).Should().Be(10f);
        AttackMechanics.DoubleAttackRatio(new[] { swords, any }, null).Should().Be(0f, "bare hands");
    }

    [Test]
    public void Two_weapons_and_a_double_attack_make_up_to_four_hits()
    {
        AttackMechanics.HitCount(false, false).Should().Be(1);
        AttackMechanics.HitCount(true, false).Should().Be(2);
        AttackMechanics.HitCount(true, true).Should().Be(4);
        AttackMechanics.IsDualWield(ItemType.OnehandSword, ItemType.Dagger).Should().BeTrue();
        AttackMechanics.IsDualWield(ItemType.LightBow, null).Should().BeFalse("an archer's shield slot holds arrows");
    }

    [Test]
    public void The_attack_flag_is_the_last_that_applies()
    {
        AttackMechanics.AttackFlag(true, false, ItemType.OnehandSword).Should().Be(AttackMechanics.FlagDoubleAttack);
        AttackMechanics.AttackFlag(true, true, ItemType.OnehandSword).Should().Be(AttackMechanics.FlagDoubleWeapon);
        AttackMechanics.AttackFlag(false, false, ItemType.HeavyBow).Should().Be(AttackMechanics.FlagUsingBow);
        AttackMechanics.AttackFlag(false, false, ItemType.Crossbow).Should().Be(AttackMechanics.FlagUsingCrossBow);
    }

    [Test]
    public void The_left_hand_swaps_its_own_weapon_for_the_right_one()
    {
        var total = new StatBlock { AttackPointRight = 300f, AccuracyRight = 80f };
        var right = new[] { new StatEffect(StatTarget.AttackPointRight, 100f, false), new StatEffect(StatTarget.AccuracyRight, 10f, false) };
        var left = new[] { new StatEffect(StatTarget.AttackPointRight, 60f, false) };

        AttackMechanics.LeftHand(total, right, left).Should().Be((260f, 70f));
    }

    [Test]
    public void Additional_damage_picks_melee_or_ranged_and_adds_flat_or_a_share()
    {
        var melee = State(AttackMechanics.AdditionalDamageOnAttack, 2, (0, 10m), (1, 5m), (6, 30m), (8, 1m), (11, 0m));
        var both = State(AttackMechanics.AmpAdditionalDamageOnAttack, 1, (0, 0.1m), (6, 100m), (11, 99m));

        var forMelee = AttackMechanics.AdditionalDamages(new[] { melee, both }, false);
        forMelee.Should().HaveCount(2);
        forMelee[0].Should().Be(new AdditionalDamage(30, 1, 20f, 0f));
        AttackMechanics.AdditionalAmount(forMelee[1], 250).Should().Be(25);
        AttackMechanics.AdditionalDamages(new[] { melee, both }, true).Should().ContainSingle("only the 99 one is ranged");
    }

    [Test]
    public void A_mana_shield_takes_its_share_within_the_mp_left()
    {
        var shield = State(AttackMechanics.ManaShield, 1, (0, 0.3m), (1, 0.1m), (4, 1m));

        AttackMechanics.ManaShieldRatio(new[] { shield }, false).Should().BeApproximately(0.4f, 0.0001f);
        AttackMechanics.ManaShieldRatio(new[] { shield }, true).Should().Be(0f, "a physical shield");
        AttackMechanics.ManaShieldAbsorb(100, 0.4f, 1000).Should().Be(40);
        AttackMechanics.ManaShieldAbsorb(100, 0.4f, 15).Should().Be(15);
    }

    [Test]
    public void A_reflection_sends_a_flat_amount_or_a_share_back()
    {
        var share = State(AttackMechanics.DamageReflectPercent, 1, (0, 0.2m), (2, 0.5m), (6, 100m));
        var flat = State(AttackMechanics.DamageReflect, 3, (0, 10m), (1, 5m), (6, 50m));

        var reflects = AttackMechanics.Reflects(new[] { share, flat });
        AttackMechanics.ReflectAmount(reflects[0], 200, false).Should().Be(40);
        AttackMechanics.ReflectAmount(reflects[0], 200, true).Should().Be(100);
        AttackMechanics.ReflectAmount(reflects[1], 200, false).Should().Be(25);
        reflects[1].Ratio.Should().Be(50);
    }

    [Test]
    public void A_swing_of_several_hits_lays_out_one_attack_info_each()
    {
        var hits = new[]
        {
            new AttackHit(120, 8, 880, new[] { 0, 20, 0, 0, 0, 0, 0 }),
            new AttackHit(60, 0, 820)
        };

        var packet = GameAttackPackets.BuildAttackEvent(1, 2, 1150, 1150, GameAttackPackets.ActionAttack,
            AttackMechanics.FlagDoubleWeapon, hits, 500);

        packet.Length.Should().Be(7 + 15 + 61 * 2);
        packet[20].Should().Be(AttackMechanics.FlagDoubleWeapon);
        packet[21].Should().Be(2);
        var first = packet.AsSpan(22);
        BinaryPrimitives.ReadInt32LittleEndian(first).Should().Be(120);
        first[8].Should().Be(8);
        BinaryPrimitives.ReadInt32LittleEndian(first.Slice(9 + 4)).Should().Be(20, "fire's share");
        BinaryPrimitives.ReadInt32LittleEndian(first.Slice(37)).Should().Be(880);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(22 + 61 + 37)).Should().Be(820);
    }

    [Test]
    public void An_aiming_bow_sends_no_hit()
    {
        var packet = GameAttackPackets.BuildAttackEvent(1, 2, 920, 920, AttackMechanics.ActionAiming,
            AttackMechanics.FlagUsingBow, Array.Empty<AttackHit>(), 500);

        packet.Length.Should().Be(22);
        packet[19].Should().Be(AttackMechanics.ActionAiming);
        packet[21].Should().Be(0);
    }

    // ---- the damage a player takes ----

    private static (CombatService Combat, GameClient Client, ConnectionInfo Info, long MonsterId) Defender(
        StateRule rule, int stateLevel)
    {
        var repository = A.Fake<IMonsterResourceRepository>();
        A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._))
            .Returns(new[] { new MonsterResourceEntity { Id = 2101, Level = 1, Hp = 5000 } });
        var world = new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions
        {
            Spawns = { new MonsterSpawnPoint { MonsterId = 2101, X = 0, Y = 0, Count = 1, Radius = 0 } }
        }));
        var monsterId = world.WithinRange(0, 0, 100).Single().InstanceId;

        var states = A.Fake<IStateCatalog>();
        A.CallTo(() => states.GetRule(77)).Returns(rule);
        var random = A.Fake<ICombatRandom>();
        A.CallTo(() => random.Next(A<int>._)).Returns(0);
        var combat = new CombatService(world, A.Fake<IMonsterSpawnService>(), A.Fake<ILevelingService>(),
            A.Fake<IGroundItemService>(), A.Fake<IRateService>(), A.Fake<IStatService>(), states,
            A.Fake<Navislamia.Game.Services.Party.IPartyService>(), random: random);

        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client);
        info.CharacterHp = 1000;
        info.CharacterMp = 100;
        lock (info.BuffLock)
        {
            info.ActiveBuffs.Add(new ActiveBuff(1, 77, 0, stateLevel, 0, uint.MaxValue - 1));
        }

        lock (info.MonsterVisibilityLock)
        {
            info.SpawnedMonsters[monsterId] = 0x40000001;
        }

        return (combat, client, info, monsterId);
    }

    [Test]
    public void The_mana_shield_takes_part_of_a_monster_hit_from_the_mp()
    {
        var values = new decimal[20];
        values[0] = 0.5m;
        values[4] = 99m;
        var (combat, client, info, monsterId) = Defender(new StateRule(77, Array.Empty<int>(), 0, 0,
            AttackMechanics.ManaShield, values), 1);

        combat.DamagePlayer(client, 300, monsterId, false).Should().Be(800, "the 100 MP absorb 100 of the 300");

        info.CharacterMp.Should().Be(0, "150 to absorb, 100 held");
    }

    [Test]
    public void A_reflection_hurts_the_monster_that_struck()
    {
        var values = new decimal[20];
        values[0] = 0.5m;
        values[6] = 100m;
        var (combat, client, _, monsterId) = Defender(new StateRule(77, Array.Empty<int>(), 0, 0,
            AttackMechanics.DamageReflectPercent, values), 1);
        var world = (MonsterWorldState)typeof(CombatService).GetField("_worldState",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(combat);
        var before = world!.GetHp(monsterId);

        combat.DamagePlayer(client, 200, monsterId, false);

        world.GetHp(monsterId).Should().Be(before - 100);
    }
}
