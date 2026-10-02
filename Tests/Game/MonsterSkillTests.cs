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
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.MonsterSkills;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

/// <summary>Monster skills (docs/packet-specs/socle-competences-monstres.md).</summary>
[TestFixture]
public class MonsterSkillTests
{
    private const int LinkId = 77;
    private const long InstanceId = 0;
    private const uint MonsterHandle = 0x40000001;
    private const uint PlayerHandle = 1234;

    private sealed class FixedRandom : ICombatRandom
    {
        private readonly Queue<int> _rolls;

        public FixedRandom(params int[] rolls) => _rolls = new Queue<int>(rolls);

        public int Next(int maxExclusive) => _rolls.Count > 0 ? _rolls.Dequeue() : maxExclusive - 1;
    }

    private static CastableSkillRow Row(int id, int effectType, bool harmful, int? stateId = null,
        decimal stateSecond = 0m, decimal cooltime = 0m, decimal[] vars = null) =>
        new(id, effectType, harmful, 1, stateId, 0, vars ?? new decimal[20], stateSecond, 0m, 1, 0m, 0, 0, 0m, 0m,
            0m, cooltime, 0m, 0);

    private static MonsterSkill Skill(CastableSkillRow row, int level = 1, double probability = 1.0)
    {
        MonsterSkillCatalog.TryClassify(row, level, probability, out var skill).Should().BeTrue();
        return skill;
    }

    [Test]
    public void The_families_are_classified_and_the_rest_left_out()
    {
        Skill(Row(1, 101, true)).Effect.Should().Be(MonsterSkillEffect.PhysicalFlat);
        Skill(Row(2, 201, true)).Effect.Should().Be(MonsterSkillEffect.MagicFlat);
        Skill(Row(3, 30001, true)).Effect.Should().Be(MonsterSkillEffect.PhysicalScaled);
        Skill(Row(4, 231, true)).Effect.Should().Be(MonsterSkillEffect.MagicScaled);
        Skill(Row(5, 501, false)).Should().Match<MonsterSkill>(s => s.Effect == MonsterSkillEffect.Heal && s.OnSelf);
        Skill(Row(6, 301, true, 100)).Should().Match<MonsterSkill>(s => s.Effect == MonsterSkillEffect.State && !s.OnSelf);
        Skill(Row(7, 301, false, 100)).OnSelf.Should().BeTrue();

        MonsterSkillCatalog.TryClassify(Row(8, 113, true), 1, 1.0, out _).Should().BeTrue("regions are implemented");
        MonsterSkillCatalog.TryClassify(Row(9, 301, true), 1, 1.0, out _).Should().BeFalse("a state skill without a state");
        MonsterSkillCatalog.TryClassify(Row(1, 101, true), 1, 0.0, out _).Should().BeTrue("Lua can cast a zero-probability slot");
    }

    [Test]
    public void The_pick_rolls_each_entry_in_order_and_skips_a_cooling_skill()
    {
        var first = Skill(Row(1, 101, true), probability: 0.05);
        var second = Skill(Row(2, 201, true), probability: 0.06);
        var skills = new[] { first, second };

        // 0.05 x 10000 = 500 > 499 hits; 500 does not.
        MonsterSkillRules.Pick(skills, _ => true, new FixedRandom(499)).Should().BeSameAs(first);
        MonsterSkillRules.Pick(skills, _ => true, new FixedRandom(500, 599)).Should().BeSameAs(second);
        MonsterSkillRules.Pick(skills, _ => true, new FixedRandom(500, 600)).Should().BeNull();
        MonsterSkillRules.Pick(skills, s => s != first, new FixedRandom(0, 0)).Should().BeSameAs(second);
    }

    [Test]
    public void The_damage_curves_follow_the_reference()
    {
        var vars = new decimal[20];
        vars[0] = 30m;
        vars[1] = 10m;

        // T1: attack + var0 + var1 x lvl.
        MonsterSkillRules.BaseDamage(Skill(Row(1, 101, true, vars: vars), level: 12), 100f, 7f).Should().Be(250f);
        MonsterSkillRules.BaseDamage(Skill(Row(2, 201, true, vars: vars), level: 2), 7f, 100f).Should().Be(150f);
    }

    [Test]
    public void A_monster_cooldown_and_heal_live_in_the_world_state()
    {
        var world = World(hp: 1000);
        world.IsSkillReady(InstanceId, 9, 100).Should().BeTrue();
        world.SetSkillCooldown(InstanceId, 9, 200);
        world.IsSkillReady(InstanceId, 9, 199).Should().BeFalse();
        world.IsSkillReady(InstanceId, 9, 200).Should().BeTrue();

        world.ApplyDamage(InstanceId, 300);
        world.Heal(InstanceId, 500).Should().Be(300, "capped at the maximum");
        world.GetHp(InstanceId).Should().Be(1000);

        world.SetSkillCooldown(InstanceId, 9, 5000);
        world.Kill(InstanceId, DateTime.UtcNow.AddMinutes(1));
        world.IsSkillReady(InstanceId, 9, 0).Should().BeTrue("a corpse keeps no cooldown");
        world.Heal(InstanceId, 10).Should().Be(0, "a corpse cannot heal");
    }

    [Test]
    public void A_damage_skill_replaces_the_swing_and_lands_through_the_combat_rule()
    {
        var vars = new decimal[20];
        vars[0] = 30m;
        var (service, combat, _, connection, info) = Build(Row(9520, 101, true, cooltime: 5m, vars: vars));
        A.CallTo(() => combat.RollMonsterHit(InstanceId, A<GameClient>._, 130f, DamageKind.Physical, A<int>._,
                A<int>._))
            .Returns(new HitResult(40, HitFlags.Critical));

        service.TryCast(Client(connection), InstanceId, MonsterHandle, 1000, out _).Should().BeTrue();

        info.CharacterHp.Should().Be(60);
        var skills = connection.Sent.Where(p => Id(p) == (ushort)GamePackets.TM_SC_SKILL).ToList();
        skills.Should().HaveCount(3, "casting, fire, complete");
        var fire = skills[1];
        BinaryPrimitives.ReadUInt32LittleEndian(fire.AsSpan(10, 4)).Should().Be(MonsterHandle);
        var hit = fire.AsSpan(7 + 41 + 9);
        BinaryPrimitives.ReadUInt32LittleEndian(hit.Slice(1, 4)).Should().Be(PlayerHandle);
        BinaryPrimitives.ReadInt32LittleEndian(hit.Slice(10, 4)).Should().Be(40);
        BinaryPrimitives.ReadInt32LittleEndian(hit.Slice(14, 4)).Should().Be((int)HitFlags.Critical);

        // Five seconds of cooldown: the same skill cannot come up again a tick later.
        service.TryCast(Client(connection), InstanceId, MonsterHandle, 1010, out _).Should().BeFalse();
        service.TryCast(Client(connection), InstanceId, MonsterHandle, 1500, out _).Should().BeTrue();
    }

    [Test]
    public void A_harmful_state_lands_on_the_player_through_the_buff_path()
    {
        var (service, combat, skillCast, connection, _) = Build(Row(7005, 301, true, 4001, stateSecond: 8m));
        // STATE_SKILL_FUNCTOR: 301 lands on magic accuracy - magic avoid + 50 >= the roll (99 here).
        A.CallTo(() => combat.GetMonsterStats(A<long>._)).Returns(new StatBlock { MagicAccuracy = 60f });

        service.TryCast(Client(connection), InstanceId, MonsterHandle, 1000, out _).Should().BeTrue();

        A.CallTo(() => skillCast.ApplyState(A<GameClient>._, 4001, 1, 800u)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public void A_harmful_state_the_player_resists_does_not_land()
    {
        var (service, combat, skillCast, connection, _) = Build(Row(7005, 301, true, 4001, stateSecond: 8m));
        A.CallTo(() => combat.GetMonsterStats(A<long>._)).Returns(new StatBlock { MagicAccuracy = 20f });
        A.CallTo(() => combat.GetPlayerStats(A<GameClient>._)).Returns(new StatBlock { MagicAvoid = 30f });

        // 20 - 30 + 0 + 50 = 40 < 99: resisted, the skill still went off.
        service.TryCast(Client(connection), InstanceId, MonsterHandle, 1000, out _).Should().BeTrue();

        A.CallTo(() => skillCast.ApplyState(A<GameClient>._, A<int>._, A<int>._, A<uint>._)).MustNotHaveHappened();
    }

    [Test]
    public void A_self_state_and_a_heal_land_on_the_monster()
    {
        var (buffService, _, skillCast, connection, _) = Build(Row(7007, 301, false, 4002, stateSecond: 10m));
        buffService.TryCast(Client(connection), InstanceId, MonsterHandle, 1000, out _).Should().BeTrue();
        A.CallTo(() => skillCast.ApplyState(A<GameClient>._, A<int>._, A<int>._, A<uint>._)).MustNotHaveHappened();
        connection.Sent.Where(p => Id(p) == (ushort)GamePackets.TM_SC_STATE)
            .Select(p => BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(7, 4)))
            .Should().ContainSingle().Which.Should().Be(MonsterHandle);

        var vars = new decimal[20];
        vars[2] = 200m;
        var (healService, _, _, healConnection, _) = Build(Row(8002, 501, false, vars: vars), damageFirst: 300);
        healService.TryCast(Client(healConnection), InstanceId, MonsterHandle, 1000, out _).Should().BeTrue();
        var fire = healConnection.Sent.Where(p => Id(p) == (ushort)GamePackets.TM_SC_SKILL).ElementAt(1);
        var hit = fire.AsSpan(7 + 41 + 9);
        hit[0].Should().Be((byte)SkillHitType.AddHp);
        BinaryPrimitives.ReadInt32LittleEndian(hit.Slice(9, 4)).Should().Be(200);
    }

    private static readonly Dictionary<StorageTestHarness.FrameConnection, GameClient> Clients = new();

    private static GameClient Client(StorageTestHarness.FrameConnection connection) => Clients[connection];

    private static (MonsterSkillService Service, ICombatService Combat, ISkillCastService SkillCast,
        StorageTestHarness.FrameConnection Connection, ConnectionInfo Info) Build(CastableSkillRow row,
            int damageFirst = 0)
    {
        var skills = A.Fake<ISkillResourceRepository>();
        A.CallTo(() => skills.GetSkillRows(A<IReadOnlyCollection<int>>._)).Returns(new[] { row });
        var catalog = new MonsterSkillCatalog(Options.Create(new MonsterSkillOptions
        {
            Links = { [LinkId] = new List<MonsterSkillEntryOptions> { new() { SkillId = row.SkillId, Level = 1, Probability = 1 } } }
        }), skills);

        var world = World(hp: 1000);
        if (damageFirst > 0)
        {
            world.ApplyDamage(InstanceId, damageFirst);
        }

        var combat = A.Fake<ICombatService>();
        A.CallTo(() => combat.GetMonsterStats(A<long>._)).Returns(new StatBlock { AttackPointRight = 100f });
        A.CallTo(() => combat.DamagePlayer(A<GameClient>._, A<int>._)).ReturnsLazily((GameClient target, int damage) =>
        {
            var session = StorageTestHarness.Session(target);
            session.CharacterHp = Math.Max(0, session.CharacterHp - damage);
            return session.CharacterHp;
        });
        A.CallTo(() => combat.DamagePlayer(A<GameClient>._, A<int>._, A<long>._, A<bool>._))
            .ReturnsLazily((GameClient target, int damage, long _, bool _) =>
            {
                var session = StorageTestHarness.Session(target);
                session.CharacterHp = Math.Max(0, session.CharacterHp - damage);
                return session.CharacterHp;
            });
        var skillCast = A.Fake<ISkillCastService>();
        var service = new MonsterSkillService(catalog, world, combat, skillCast, new FixedRandom());

        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = PlayerHandle;
        info.CharacterHp = 100;
        info.CharacterMaxHp = 100;
        Clients[connection] = client;
        return (service, combat, skillCast, connection, info);
    }

    private static MonsterWorldState World(int hp)
    {
        var repository = A.Fake<IMonsterResourceRepository>();
        A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._)).Returns(new[]
        {
            new MonsterResourceEntity { Id = 2101, Level = 1, Hp = hp - 20, MonsterSkillLinkId = LinkId }
        });

        return new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions
        {
            Spawns = { new MonsterSpawnPoint { MonsterId = 2101, X = 0, Y = 0, Count = 1, Radius = 0 } }
        }));
    }

    private static ushort Id(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));
}
