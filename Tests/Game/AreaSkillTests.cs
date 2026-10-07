using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Scripting;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.MonsterSkills;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

[TestFixture]
public class AreaSkillTests
{
    private const uint MonsterHandle = 0x40000001;
    private const uint PlayerHandle = 100;
    private static CastableSkillRow Row(int effect, decimal[] vars = null, int required = 1, int id = 9000) =>
        new(id, effect, true, effect == 271 ? 4 : effect is 232 or 241 or 30016 ? 1 : 2, null, 0, vars ?? new decimal[20],
            0, 0, 0, 0, 10, 0, 0, 0, 0, 1, 0, 1, RequiredTarget: required, CastRange: 20);
    private static CastableBuffFields Fields(int effect, decimal[] vars = null)
    {
        var row = Row(effect, vars);
        MonsterSkillCatalog.TryClassify(row, 1, 1, out var skill).Should().BeTrue();
        return skill.Fields;
    }
    private static decimal[] Vars(int radiusIndex, int radius = 5)
    { var v = new decimal[20]; v[0] = 1; v[radiusIndex] = radius; return v; }

    [TestCase(30011)] [TestCase(232)] [TestCase(261)] [TestCase(271)]
    public void Requested_player_families_are_castable_without_a_state(int effect)
    {
        BuffCatalog.TryClassify(Row(effect), out var f).Should().BeTrue();
        f.EffectType.Should().Be(effect);
        f.Kind.Should().Be(effect == 30011 ? SkillCastKind.PhysicalAttack : SkillCastKind.MagicAttack);
    }

    [TestCase(111, 2)] [TestCase(112, 5)] [TestCase(113, 8)] [TestCase(211, 2)]
    [TestCase(261, 9)] [TestCase(262, 9)] [TestCase(263, 9)] [TestCase(271, 9)]
    [TestCase(30011, 4)] [TestCase(30012, 4)] [TestCase(30013, 4)]
    public void Area_radius_uses_the_effects_column_and_twelve_world_units(int effect, int index)
    { SkillAreaRules.Area(Fields(effect, Vars(index))).Radius.Should().Be(60); }

    [Test]
    public void Cone_direction_cross_and_circle_have_distinct_target_sets()
    {
        var area = new SkillArea(100, false, 1, MathF.PI / 4);
        SkillAreaRules.Contains(area, 0, 0, 50, 0, 40, 0).Should().BeTrue();
        SkillAreaRules.Contains(area, 0, 0, 50, 0, -40, 0).Should().BeFalse();
        SkillAreaRules.Contains(area, 0, 0, 50, 0, 0, 40).Should().BeFalse();
        area = area with { Shape = 0, Property = 2 };
        SkillAreaRules.Contains(area, 0, 0, 50, 0, 40, 11).Should().BeTrue();
        SkillAreaRules.Contains(area, 0, 0, 50, 0, 40, 12).Should().BeFalse();
        SkillAreaRules.Contains(area, 0, 0, 50, 0, -40, 0).Should().BeFalse();
        area = area with { Shape = 2 };
        SkillAreaRules.Contains(area, 0, 0, 50, 0, -40, 0).Should().BeTrue();
        SkillAreaRules.Contains(area, 0, 0, 50, 0, 0, 40).Should().BeTrue();
        SkillAreaRules.Contains(area, 0, 0, 50, 0, 40, 40).Should().BeFalse();
        area = area with { Shape = -1, IncludeOrigin = false };
        SkillAreaRules.Contains(area, 0, 0, 50, 0, 0, 0).Should().BeFalse();
        SkillAreaRules.Contains(area, 0, 0, 50, 0, 100, 0).Should().BeTrue();
        SkillAreaRules.Contains(area, 0, 0, 50, 0, 101, 0).Should().BeFalse();
    }

    [Test]
    public void Distribution_divides_damage_or_caps_targets_nearest_the_selected_origin()
    {
        var source = new[] { 10f, 30f, 50f };
        float damage = 120;
        var area = new SkillArea(100, Distribution: 1, TargetMax: 2);
        SkillAreaRules.Select(source, area, 0, 0, 50, 0, x => (x, 0), ref damage).Should().HaveCount(3);
        damage.Should().Be(80);
        area = area with { Distribution = 3, TargetMax = 1 };
        SkillAreaRules.Select(source, area, 0, 0, 50, 0, x => (x, 0), ref damage).Should().Equal(50);
        area = area with { Distribution = 4 };
        SkillAreaRules.Select(source, area, 0, 0, 50, 0, x => (x, 0), ref damage).Should().Equal(10);
    }

    [Test]
    public void Multi_hit_frame_has_independent_fixed_stride_records_and_unique_target_count()
    {
        var hits = new[] { new SkillHit(SkillHitType.Damage, 10, 90, 10),
            new SkillHit(SkillHitType.MagicDamage, 10, 80, 10, 8), new SkillHit(SkillHitType.Damage, 20, 50, 30) };
        var frame = GameSkillPackets.BuildSkill(9000, 1, 100, 10, 0, 0, 0, 0, SkillPacketType.Fire,
            0, 0, 100, 100, hits: hits, multiple: true, range: 60, fireCount: 2);
        frame.Length.Should().Be(57 + 45 * 3);
        frame[48].Should().Be(1); frame[53].Should().Be(2); frame[54].Should().Be(2);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(55)).Should().Be(3);
        BinaryPrimitives.ReadSingleLittleEndian(frame.AsSpan(49)).Should().Be(60);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(57 + 45 + 5)).Should().Be(80);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(57 + 45 + 14)).Should().Be(8);
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(57 + 90 + 1)).Should().Be(20);
    }

    [Test]
    public void Scheduler_spaces_shots_and_handles_the_clock_wrapping_without_bursting()
    {
        using var effects = new SkillEffectScheduler(false);
        var fires = 0; var completed = 0;
        effects.Schedule(uint.MaxValue - 10, 20, 3, _ => { fires++; return true; }, () => completed++);
        effects.Tick(uint.MaxValue - 11); fires.Should().Be(0);
        effects.Tick(uint.MaxValue - 10); fires.Should().Be(1);
        effects.Tick(8); fires.Should().Be(1);
        effects.Tick(9); fires.Should().Be(2);
        effects.Tick(1000); fires.Should().Be(3); completed.Should().Be(1);
        effects.Tick(1000); fires.Should().Be(3);
    }

    private sealed class Harness : IDisposable
    {
        public readonly SkillEffectScheduler Effects = new(false);
        public readonly MonsterWorldState World;
        public ICombatService Combat = A.Fake<ICombatService>();
        public readonly IPlayerVisibilityService Players = A.Fake<IPlayerVisibilityService>();
        public readonly PlayerRegistry Registry = new();
        public readonly IStatService Stats = A.Fake<IStatService>();
        public readonly Dictionary<GameClient, StorageTestHarness.FrameConnection> Connections = new();
        public Harness(int monsterId = 2101)
        {
            var repository = A.Fake<IMonsterResourceRepository>();
            A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._)).Returns(new[]
                { new MonsterResourceEntity { Id = monsterId, Level = 1, Hp = 980, MonsterSkillLinkId = 77 },
                    new MonsterResourceEntity { Id = 2055001, Level = 1, Hp = 980 },
                    new MonsterResourceEntity { Id = 2065001, Level = 1, Hp = 980 } });
            World = new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions
            {
                Spawns = { new MonsterSpawnPoint { MonsterId = monsterId, X = 100, Y = 100, Count = 1 },
                    new MonsterSpawnPoint { MonsterId = monsterId, X = 125, Y = 100, Count = 1 },
                    new MonsterSpawnPoint { MonsterId = monsterId, X = 200, Y = 100, Count = 1 } }
            }));
            A.CallTo(() => Players.Registry).Returns(Registry);
            A.CallTo(() => Players.Observers(A<GameClient>._)).ReturnsLazily((GameClient c) => Registry.Clients.Where(p => p != c).ToArray());
            A.CallTo(() => Stats.Compute(A<ConnectionInfo>._)).Returns(new CharacterStatResult(new StatBlock
                { AttackPointRight = 100, MagicPoint = 200 }, new StatBlock()));
            A.CallTo(() => Combat.GetMonsterStats(A<long>._)).Returns(new StatBlock { AttackPointRight = 100, MagicPoint = 200 });
            A.CallTo(() => Combat.RollHit(A<GameClient>._, A<long>._, A<float>._, A<DamageKind>._, A<int>._, A<int>._))
                .Returns(new HitResult(25, HitFlags.Critical));
            A.CallTo(() => Combat.ApplyDamage(A<GameClient>._, A<long>._, A<uint>._, A<int>._, A<int>._))
                .ReturnsLazily((GameClient c, long id, uint handle, int damage, int hate) =>
                { var hp = World.ApplyDamage(id, damage); if (hp <= 0) World.Kill(id, DateTime.UtcNow.AddSeconds(1)); return hp; });
            A.CallTo(() => Combat.RollMonsterHit(A<long>._, A<GameClient>._, A<float>._, A<DamageKind>._, A<int>._, A<int>._))
                .Returns(new HitResult(25, HitFlags.Critical));
            A.CallTo(() => Combat.DamagePlayer(A<GameClient>._, A<int>._, A<long>._, A<bool>._))
                .ReturnsLazily((GameClient c, int damage, long attacker, bool magical) =>
            { var i = StorageTestHarness.Session(c); return i.CharacterHp = Math.Max(0, i.CharacterHp - damage); });
        }
        public GameClient Client(uint handle = PlayerHandle, float x = 80, float y = 100, sbyte layer = 0)
        {
            var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
            var client = StorageTestHarness.NewGameClient(connection, playerVisibilityService: Players);
            Connections[client] = connection;
            var info = StorageTestHarness.Session(client);
            info.CharacterHandle = handle; info.CharacterHp = 1000; info.CharacterMp = 100;
            info.CharacterLevel = 1; info.X = x; info.Y = y; info.Layer = (byte)layer;
            foreach (var other in Registry.Clients)
            { info.SpawnedPlayers[StorageTestHarness.Session(other).CharacterHandle] = default;
                StorageTestHarness.Session(other).SpawnedPlayers[handle] = default; }
            for (var id = 0; id < 3; id++)
            { var mh = MonsterHandle + (uint)id + (handle - PlayerHandle) * 10;
                info.SpawnedMonsters[id] = mh; }
            Registry.Register(handle, client);
            return client;
        }
        public void UseRealCombat(IStateCatalog states = null)
        {
            var random = A.Fake<ICombatRandom>();
            A.CallTo(() => random.Next(A<int>._)).Returns(0);
            Combat = new CombatService(World, A.Fake<IMonsterSpawnService>(), A.Fake<ILevelingService>(),
                A.Fake<IGroundItemService>(), A.Fake<Navislamia.Game.Services.Rates.IRateService>(), Stats,
                states ?? A.Fake<IStateCatalog>(), A.Fake<Navislamia.Game.Services.Party.IPartyService>(),
                random: random, players: Players, runTicks: false);
        }
        public SkillCastService PlayerService(CastableSkillRow row)
        {
            var repository = A.Fake<ISkillResourceRepository>();
            A.CallTo(() => repository.GetCastableSkills()).Returns(new[] { row });
            return new SkillCastService(new BuffCatalog(repository), Stats, A.Fake<IStateCatalog>(), World,
                Combat, A.Fake<Navislamia.Game.Services.Props.IFieldPropCatalog>(), A.Fake<IWarpService>(), Players, Effects, runTicks: false);
        }
        public MonsterSkillService MonsterService(CastableSkillRow row, MonsterSkillOptions options = null, IScriptService scripts = null,
            ICharacterService characters = null, ISkillCastService skillCast = null)
        {
            var repository = A.Fake<ISkillResourceRepository>();
            A.CallTo(() => repository.GetSkillRows(A<IReadOnlyCollection<int>>._)).Returns(new[] { row });
            options ??= new MonsterSkillOptions { Links = { [77] = new() { new() { SkillId = row.SkillId, Level = 1, Probability = 1 } } } };
            return new MonsterSkillService(new MonsterSkillCatalog(Options.Create(options), repository), World,
                Combat, skillCast ?? A.Fake<ISkillCastService>(), players: Players, scripts: scripts, effects: Effects, characters: characters);
        }
        public void Cast(SkillCastService service, GameClient client, int id = 9000, uint target = MonsterHandle,
            float x = 100, float y = 100)
        {
            var info = StorageTestHarness.Session(client); info.LearnedSkills[id] = 1;
            service.Register(client);
            service.Cast(client, new GameActionPackets.SkillRequest((ushort)id, info.CharacterHandle, target,
                x, y, 0, (sbyte)info.Layer, 1));
        }
        public List<byte[]> Frames(GameClient client, SkillPacketType type) => Connections[client].Sent
            .Where(f => BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(4)) == 401 && f[31] == (byte)type).ToList();
        public void Dispose() => Effects.Dispose();

        /// <summary>
        /// The instant a ground skill's actor started, read from the caster's own ENTER of it (start_time @30, minus the
        /// caster's clock offset). Not the cooldown minus its length: the cooldown is armed on an earlier read of the
        /// clock than the fire, and under load the two reads fall one tick apart.
        /// </summary>
        public uint GroundStart(GameClient caster)
        {
            var enter = Connections[caster].Sent.Single(p => p.Length == 38 && p[25] == 5);
            return unchecked(BinaryPrimitives.ReadUInt32LittleEndian(enter.AsSpan(30))
                             - StorageTestHarness.Session(caster).ClientClockOffset);
        }
    }

    [TestCase(261)] [TestCase(271)] [TestCase(30011)]
    public void Real_player_area_damage_resists_each_target_and_reports_reduced_hp_on_the_wire(int effect)
    {
        using var h = new Harness(); var caster = h.Client();
        var states = A.Fake<IStateCatalog>();
        A.CallTo(() => states.Resolve(77, 1)).Returns(new[] { new StatEffect(StatTarget.FireResistance, 150, false) });
        h.World.AddState(0, 77, 0, 1, 0, uint.MaxValue);
        h.UseRealCombat(states);
        var vars = Vars(effect == 30011 ? 4 : 9);
        if (effect == 271) { vars[6] = 2; vars[8] = 1; vars[10] = 1; }
        var service = h.PlayerService(Row(effect, vars) with { ElementalType = 1 });
        h.Cast(service, caster);
        var frame = h.Frames(caster, effect == 271 ? SkillPacketType.RegionFire : SkillPacketType.Fire).Single();
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(55)).Should().BeGreaterThanOrEqualTo(2);
        var reduced = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(67));
        var full = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(112));
        reduced.Should().Be(full / 2); full.Should().BeGreaterThan(0);
        frame[66].Should().Be(1); frame[111].Should().Be(1);
        h.World.GetHp(0).Should().Be(1000 - reduced);
        h.World.GetHp(1).Should().Be(1000 - full);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(62)).Should().Be(h.World.GetHp(0));
    }

    [Test]
    public void Real_multi_hit_skill_reloads_resistance_for_each_shot()
    {
        using var h = new Harness(); var caster = h.Client();
        var states = A.Fake<IStateCatalog>();
        A.CallTo(() => states.Resolve(77, 1)).Returns(new[] { new StatEffect(StatTarget.FireResistance, 300, false) });
        h.UseRealCombat(states);
        var vars = Vars(6, 3); vars[8] = 1;
        var service = h.PlayerService(Row(232, vars) with { ElementalType = 1 });
        h.Cast(service, caster);
        var hp = h.World.GetHp(0); hp.Should().BeLessThan(1000);
        h.World.AddState(0, 77, 0, 1, 0, uint.MaxValue);
        h.Effects.Tick(ServerClock.Now + 105);
        h.World.GetHp(0).Should().Be(hp);
        var second = h.Frames(caster, SkillPacketType.Fire)[1];
        BinaryPrimitives.ReadInt32LittleEndian(second.AsSpan(67)).Should().Be(0);
        second[66].Should().Be(1);
    }

    [TestCase(111)] [TestCase(113)] [TestCase(261)] [TestCase(262)] [TestCase(30013)]
    public void Real_monster_area_skills_respect_each_players_resistance(int effect)
    {
        using var h = new Harness(); var primary = h.Client(x: 100); var near = h.Client(101, 125);
        A.CallTo(() => h.Stats.Compute(A<ConnectionInfo>._)).ReturnsLazily((ConnectionInfo info) =>
            new CharacterStatResult(new StatBlock { FireResistance = info.CharacterHandle == PlayerHandle ? 150 : 0 }, new StatBlock()));
        h.UseRealCombat();
        var vars = Vars(effect == 111 ? 2 : effect == 113 ? 8 : effect == 30013 ? 4 : 9);
        if (effect is 113 or 262) { vars[5] = -1; vars[6] = 1; vars[7] = 1; }
        if (effect == 30013) { vars[7] = -1; vars[8] = 1; vars[9] = 1; }
        h.MonsterService(Row(effect, vars) with { ElementalType = 1 }).TryCast(primary, 0, MonsterHandle, 1000, out _).Should().BeTrue();
        var fire = h.Frames(primary, SkillPacketType.Fire).Single();
        BinaryPrimitives.ReadUInt16LittleEndian(fire.AsSpan(55)).Should().Be(2);
        var reduced = BinaryPrimitives.ReadInt32LittleEndian(fire.AsSpan(67));
        var full = BinaryPrimitives.ReadInt32LittleEndian(fire.AsSpan(112));
        reduced.Should().Be(full / 2); full.Should().BeGreaterThan(0);
        StorageTestHarness.Session(primary).CharacterHp.Should().Be(1000 - reduced);
        StorageTestHarness.Session(near).CharacterHp.Should().Be(1000 - full);
        fire[66].Should().Be(1); fire[111].Should().Be(1);
    }

    [TestCase(101)] [TestCase(201)]
    public void Real_monster_single_target_skills_reduce_damage_before_the_mana_shield(int effect)
    {
        using var h = new Harness(); var primary = h.Client(x: 100); var control = h.Client(101, 125);
        A.CallTo(() => h.Stats.Compute(A<ConnectionInfo>._)).ReturnsLazily((ConnectionInfo info) =>
            new CharacterStatResult(new StatBlock { FireResistance = info.CharacterHandle == PlayerHandle ? 150 : 0 }, new StatBlock()));
        var states = A.Fake<IStateCatalog>(); var values = new decimal[20]; values[0] = .5m; values[4] = 99;
        A.CallTo(() => states.GetRule(77)).Returns(new Navislamia.Game.Services.Casting.StateRule(77,
            Array.Empty<int>(), 0, 0, Navislamia.Game.Services.Combat.AttackMechanics.ManaShield, values));
        StorageTestHarness.Session(primary).ActiveBuffs.Add(new ActiveBuff(1, 77, 0, 1, 0, uint.MaxValue));
        h.UseRealCombat(states);
        var vars = new decimal[20]; vars[0] = 100;
        var row = Row(effect, vars) with { ElementalType = 1 };
        MonsterSkillCatalog.TryClassify(row, 1, 1, out var skill).Should().BeTrue();
        var stats = h.Combat.GetMonsterStats(0);
        var damage = MonsterSkillRules.BaseDamage(skill, stats.AttackPointRight, stats.MagicPoint);
        var full = h.Combat.RollMonsterHit(0, control, damage, effect == 201 ? DamageKind.Magical : DamageKind.Physical, 0, 0).Damage;
        h.MonsterService(row).TryCast(primary, 0, MonsterHandle, 1000, out _).Should().BeTrue();
        var fire = h.Frames(primary, SkillPacketType.Fire).Single();
        var reduced = BinaryPrimitives.ReadInt32LittleEndian(fire.AsSpan(67));
        reduced.Should().Be(full / 2);
        var info = StorageTestHarness.Session(primary);
        (1000 - info.CharacterHp + 100 - info.CharacterMp).Should().Be(reduced);
        info.CharacterMp.Should().Be(100 - Math.Min(100, reduced / 2));
        fire[66].Should().Be(1);
    }

    [Test]
    public void Player_area_hits_each_live_monster_once_and_remaps_all_observer_handles()
    {
        using var h = new Harness(); var caster = h.Client(); var watcher = h.Client(101);
        h.Cast(h.PlayerService(Row(30011, Vars(4))), caster);
        h.World.GetHp(0).Should().Be(975); h.World.GetHp(1).Should().Be(975); h.World.GetHp(2).Should().Be(1000);
        var own = h.Frames(caster, SkillPacketType.Fire).Single();
        var other = h.Frames(watcher, SkillPacketType.Fire).Single();
        BinaryPrimitives.ReadUInt16LittleEndian(own.AsSpan(55)).Should().Be(2);
        BinaryPrimitives.ReadUInt32LittleEndian(other.AsSpan(58)).Should().Be(MonsterHandle + 10);
        BinaryPrimitives.ReadUInt32LittleEndian(other.AsSpan(103)).Should().Be(MonsterHandle + 11);
        StorageTestHarness.Session(caster).CharacterMp.Should().Be(90);
    }

    [Test]
    public void Player_multiple_magic_hits_are_spaced_and_do_not_hit_a_respawned_target()
    {
        using var h = new Harness(); var caster = h.Client(); var v = Vars(6, 3); v[8] = 1;
        var now = ServerClock.Now;
        h.Cast(h.PlayerService(Row(232, v)), caster);
        h.World.GetHp(0).Should().Be(975);
        h.Frames(caster, SkillPacketType.Complete).Should().BeEmpty();
        h.Effects.Tick(now + 50); h.World.GetHp(0).Should().Be(975);
        h.Effects.Tick(now + 102); h.World.GetHp(0).Should().Be(950);
        h.World.Kill(0, DateTime.UtcNow); h.World.CollectRespawns(DateTime.UtcNow.AddSeconds(1));
        h.Effects.Tick(now + 202); h.World.GetHp(0).Should().Be(1000);
        h.Frames(caster, SkillPacketType.Complete).Should().ContainSingle();
        h.Frames(caster, SkillPacketType.Fire).Should().HaveCount(2);
    }

    [Test]
    public void Ground_area_remains_at_the_cast_point_and_reselects_moving_targets_until_expiry()
    {
        using var h = new Harness(); var caster = h.Client(); var v = Vars(9); v[6] = 2; v[8] = 1; v[10] = 1;
        var service = h.PlayerService(Row(271, v, required: 2));
        h.Cast(service, caster, target: 0);
        var now = h.GroundStart(caster);
        h.World.GetHp(0).Should().Be(975);
        h.Frames(caster, SkillPacketType.Complete).Should().ContainSingle();
        h.World.BeginWalk(0, 300, 100, 255); h.World.StopMove(0);
        StorageTestHarness.Session(caster).X = 180;
        h.Effects.Tick(now + 100);
        // Monster 1 stays in the fixed circle; moving the caster does not move the zone.
        h.World.GetHp(1).Should().Be(950); h.World.GetHp(2).Should().Be(1000);
        h.Effects.Tick(now + 200); h.Effects.Tick(now + 300);
        h.Frames(caster, SkillPacketType.RegionFire).Should().HaveCount(3);
        StorageTestHarness.Session(caster).CharacterMp.Should().Be(90);
    }

    [Test]
    public void Monster_area_hits_nearby_players_and_excludes_other_layers()
    {
        using var h = new Harness(); var primary = h.Client(x: 100); var near = h.Client(101, 125); var far = h.Client(102, 200);
        var otherLayer = h.Client(103, 100, layer: 1);
        h.MonsterService(Row(261, Vars(9))).TryCast(primary, 0, MonsterHandle, 1000, out _).Should().BeTrue();
        StorageTestHarness.Session(primary).CharacterHp.Should().Be(975);
        StorageTestHarness.Session(near).CharacterHp.Should().Be(975);
        StorageTestHarness.Session(far).CharacterHp.Should().Be(1000);
        StorageTestHarness.Session(otherLayer).CharacterHp.Should().Be(1000);
        h.Frames(otherLayer, SkillPacketType.Fire).Should().BeEmpty();
        BinaryPrimitives.ReadUInt32LittleEndian(h.Frames(near, SkillPacketType.Fire).Single().AsSpan(10))
            .Should().Be(MonsterHandle + 10);
    }

    [Test]
    public void Real_Lua_trigger_casts_a_zero_probability_slot_without_shifting_unsupported_indices()
    {
        using var h = new Harness(); var target = h.Client();
        var scripts = new ScriptService(NullLogger<ScriptService>.Instance);
        scripts.RunString("function test_trigger(m,t,i,x,y,l,r) assert(get_monster_id(m)==2101); assert(i==0); monster_skill_cast(1,m,t) end").Should().Be(1);
        var options = new MonsterSkillOptions
        {
            Links = { [77] = new() { new() { SkillId = 1, Level = 1, Probability = 0 }, new() { SkillId = 9000, Level = 1, Probability = 0 } } },
            Triggers = { [77] = new() { new() { Type = 5, Value1 = 10, Function = "test_trigger" } } }
        };
        var service = h.MonsterService(Row(111, Vars(2)), options, scripts);
        service.TryCast(target, 0, MonsterHandle, 1000, out _).Should().BeTrue();
        StorageTestHarness.Session(target).CharacterHp.Should().Be(975);
        service.TryCast(target, 0, MonsterHandle, 1001, out _).Should().BeFalse();
        scripts.RunString("assert(get_monster_id(1073741825)==0)").Should().Be(1, "context is cleared after the trigger");
    }

    [Test]
    public void Bundled_official_trigger_calls_the_expected_skill_for_a_known_monster()
    {
        var scripts = new ScriptService(NullLogger<ScriptService>.Instance);
        var index = -1; uint handle = 0;
        scripts.RunMonsterTrigger("trigger", new MonsterScriptContext
        {
            MonsterHandle = MonsterHandle, TargetHandle = PlayerHandle, MonsterId = 10146002, TriggerIndex = 1,
            CastSkill = (i, t, _, _) => { index = i; handle = t; return true; }
        }).Should().BeTrue();
        index.Should().Be(1); handle.Should().Be(MonsterHandle);
    }

    [TestCase(111, 2)] [TestCase(113, 8)] [TestCase(262, 9)] [TestCase(30013, 4)]
    public void Monster_families_hit_each_player_with_their_own_damage_formula(int effect, int radiusIndex)
    {
        using var h = new Harness(); var target = h.Client(x: 100); var near = h.Client(101, 125);
        var v = Vars(radiusIndex); if (effect is 113 or 262) { v[5] = -1; v[6] = 1; v[7] = 1; }
        if (effect == 30013) { v[7] = -1; v[8] = 1; v[9] = 1; }
        h.MonsterService(Row(effect, v)).TryCast(target, 0, MonsterHandle, 1000, out _).Should().BeTrue();
        StorageTestHarness.Session(near).CharacterHp.Should().Be(975);
        var expected = effect is 111 or 113 ? 101f : effect == 262 ? 200f : 100f;
        A.CallTo(() => h.Combat.RollMonsterHit(0, near, expected,
            effect == 262 ? DamageKind.Magical : DamageKind.Physical, A<int>._, A<int>._)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public void Monster_multi_hits_wait_for_the_interval_and_stop_after_player_death()
    {
        using var h = new Harness(); var target = h.Client(); var v = Vars(6, 3); v[8] = 1;
        var service = h.MonsterService(Row(232, v));
        StorageTestHarness.Session(target).CharacterHp = 50;
        service.TryCast(target, 0, MonsterHandle, 1000, out _).Should().BeTrue();
        StorageTestHarness.Session(target).CharacterHp.Should().Be(25);
        service.TryCast(target, 0, MonsterHandle, 1050, out _).Should().BeTrue("the cast keeps the normal swing waiting");
        h.Effects.Tick(1099); StorageTestHarness.Session(target).CharacterHp.Should().Be(25);
        h.Effects.Tick(1100); StorageTestHarness.Session(target).CharacterHp.Should().Be(0);
        h.Effects.Tick(1200); h.Effects.Tick(1300);
        h.Frames(target, SkillPacketType.Fire).Should().HaveCount(2);
        h.Frames(target, SkillPacketType.Complete).Should().ContainSingle();
    }

    [Test]
    public void At_once_physical_multi_hits_send_each_result_and_stop_on_a_killing_hit()
    {
        using var h = new Harness(); var target = h.Client(); var v = Vars(4, 3); v[0] = 1;
        h.World.ApplyDamage(0, 970);
        h.Cast(h.PlayerService(Row(30016, v) with { Target = 1 }), target);
        h.World.GetHp(0).Should().Be(0);
        var fire = h.Frames(target, SkillPacketType.Fire).Single();
        BinaryPrimitives.ReadUInt16LittleEndian(fire.AsSpan(55)).Should().Be(2);
        BinaryPrimitives.ReadInt32LittleEndian(fire.AsSpan(57 + 45 + 5)).Should().Be(0);
        A.CallTo(() => h.Combat.ApplyDamage(target, 0, MonsterHandle, 25, A<int>._)).MustHaveHappenedTwiceExactly();
    }

    [Test]
    public void Invalid_ground_coordinates_and_out_of_range_casts_charge_nothing()
    {
        using var h = new Harness(); var target = h.Client(); var service = h.PlayerService(Row(271, Vars(9), required: 2));
        h.Cast(service, target, target: 0, x: float.NaN);
        h.Cast(service, target, target: 0, x: 10000);
        StorageTestHarness.Session(target).CharacterMp.Should().Be(100);
        h.Frames(target, SkillPacketType.Fire).Should().BeEmpty();
        h.Frames(target, SkillPacketType.Casting).Should().HaveCount(2);
    }

    [Test]
    public void Area_cast_delay_is_real_and_mana_is_not_charged_a_second_time()
    {
        using var h = new Harness(); var caster = h.Client();
        var service = h.PlayerService(Row(30011, Vars(4)) with { DelayCast = 2 });
        h.Cast(service, caster);
        var now = unchecked(StorageTestHarness.Session(caster).SkillCooldowns[9000] - 100);
        h.World.GetHp(0).Should().Be(1000);
        // The delay is the ordinary pending cast's; the fires start when it is over.
        service.ProcessCasts(now + 199); h.World.GetHp(0).Should().Be(1000);
        service.ProcessCasts(now + 200); h.World.GetHp(0).Should().Be(975);
        StorageTestHarness.Session(caster).CharacterMp.Should().Be(90);
        h.Frames(caster, SkillPacketType.Complete).Should().ContainSingle();
    }

    [Test]
    public void Ground_damage_keeps_the_magic_at_creation_and_cancels_after_disconnect()
    {
        using var h = new Harness(); var caster = h.Client(); var v = Vars(9); v[6] = 5; v[8] = 1; v[10] = 2;
        h.Cast(h.PlayerService(Row(271, v, required: 2)), caster, target: 0);
        var now = h.GroundStart(caster);
        A.CallTo(() => h.Stats.Compute(A<ConnectionInfo>._)).Returns(new CharacterStatResult(new StatBlock { MagicPoint = 999 }, new StatBlock()));
        h.Effects.Tick(now + 100);
        A.CallTo(() => h.Combat.RollHit(caster, 0, 400, DamageKind.Magical, A<int>._, A<int>._)).MustHaveHappenedTwiceExactly();
        h.Registry.Unregister(PlayerHandle, caster);
        h.Effects.Tick(now + 200);
        h.World.GetHp(0).Should().Be(950);
    }

    [Test]
    public void Monster_shots_do_not_damage_a_disconnected_player()
    {
        using var h = new Harness(); var target = h.Client(); var v = Vars(6, 3); v[8] = 1;
        h.MonsterService(Row(232, v)).TryCast(target, 0, MonsterHandle, 1000, out _).Should().BeTrue();
        h.Registry.Unregister(PlayerHandle, target);
        h.Effects.Tick(1100);
        StorageTestHarness.Session(target).CharacterHp.Should().Be(975);
    }

    [Test]
    public void Lua_add_state_uses_ticks_and_respects_the_monster_handle()
    {
        using var h = new Harness(); var target = h.Client();
        var scripts = new ScriptService(NullLogger<ScriptService>.Instance);
        scripts.RunString("function state_trigger(m,t) add_state(4001,3,500,m) end");
        var options = new MonsterSkillOptions { Triggers = { [77] = new() { new() { Type = 5, Value1 = 10, Function = "state_trigger" } } } };
        h.MonsterService(Row(111, Vars(2)), options, scripts).TryCast(target, 0, MonsterHandle, 1000, out _).Should().BeTrue();
        var state = h.World.GetStates(0).Single();
        state.StateId.Should().Be(4001); state.StateLevel.Should().Be(3); state.EndTick.Should().Be(1500);
    }

    [Test]
    public void Skill_opportunities_have_an_independent_cadence_and_reset_at_the_end_of_combat()
    {
        using var h = new Harness(); var target = h.Client(); h.World.SetAggro(0, target);
        h.World.TrySkillOpportunity(0, 1000, 100).Should().BeTrue();
        h.World.TrySkillOpportunity(0, 1099, 100).Should().BeFalse();
        h.World.TrySkillOpportunity(0, 1100, 100).Should().BeTrue();
        h.World.ClearAggroFor(target);
        h.World.TrySkillOpportunity(0, 1101, 100).Should().BeTrue();
    }

    [Test]
    public void A_failed_timed_callback_and_completion_do_not_stop_other_skills()
    {
        using var effects = new SkillEffectScheduler(false); var hit = 0;
        effects.Schedule(1000, 100, 1, _ => throw new InvalidOperationException(), () => throw new InvalidOperationException());
        effects.Schedule(1000, 100, 2, _ => { hit++; return true; }, null);
        effects.Tick(1000); effects.Tick(1100); hit.Should().Be(2);
    }

    [Test]
    public void At_once_magic_multi_hits_are_all_serialized_in_one_fire()
    {
        using var h = new Harness(); var target = h.Client(); var v = Vars(6, 3);
        h.MonsterService(Row(241, v)).TryCast(target, 0, MonsterHandle, 1000, out _).Should().BeTrue();
        StorageTestHarness.Session(target).CharacterHp.Should().Be(925);
        var frame = h.Frames(target, SkillPacketType.Fire).Single();
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(55)).Should().Be(3); frame[54].Should().Be(3);
    }

    [Test]
    public void Skill_prop_enter_uses_the_official_static_prefix_and_skill_info_layout()
    {
        var packet = GameSpawnPackets.BuildEnterSkillProp(123, 10, 20, 30, 2, 456, 1000, 3185);
        packet.Length.Should().Be(38); packet[7].Should().Be(2); packet[25].Should().Be(5);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8)).Should().Be(123);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(12)).Should().Be(10);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(20)).Should().Be(30);
        packet[24].Should().Be(2);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(26)).Should().Be(456);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(30)).Should().Be(1000);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(34)).Should().Be(3185);
        packet[6].Should().Be(StorageTestHarness.Checksum(packet));
    }

    [Test]
    public void Ground_actor_enters_for_late_observers_and_leaves_at_expiry_with_the_same_handle()
    {
        using var h = new Harness(); var caster = h.Client(); var v = Vars(9); v[6] = 2; v[8] = 1; v[10] = 1;
        h.Cast(h.PlayerService(Row(271, v, required: 2)), caster, target: 0);
        var ownEnter = h.Connections[caster].Sent.Single(p => p.Length == 38 && p[25] == 5);
        var propHandle = BinaryPrimitives.ReadUInt32LittleEndian(ownEnter.AsSpan(8));
        var now = h.GroundStart(caster);
        var watcher = h.Client(101); StorageTestHarness.Session(watcher).ClientClockOffset = 30;
        h.Effects.Tick(now + 50);
        var enter = h.Connections[watcher].Sent.Single(p => p.Length == 38 && p[25] == 5);
        BinaryPrimitives.ReadUInt32LittleEndian(enter.AsSpan(8)).Should().Be(propHandle);
        BinaryPrimitives.ReadUInt32LittleEndian(enter.AsSpan(30)).Should().Be(now + 30);
        h.Effects.Tick(now + 100); h.Effects.Tick(now + 200); h.Effects.Tick(now + 201);
        var leave = h.Connections[watcher].Sent.Single(p => p.Length == 11
            && BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4)) == (ushort)GamePackets.TM_SC_LEAVE);
        BinaryPrimitives.ReadUInt32LittleEndian(leave.AsSpan(7)).Should().Be(propHandle);
        h.Effects.Tick(now + 300);
        h.Connections[watcher].Sent.Count(p => p.Length == 11).Should().Be(1);
    }

    [Test]
    public void Failed_or_unknown_Lua_trigger_allows_a_regular_skill_and_clears_context()
    {
        using var h = new Harness(); var target = h.Client();
        var scripts = new ScriptService(NullLogger<ScriptService>.Instance);
        scripts.RunString("function broken(m,t) assert(get_monster_id(m)==2101); error('broken') end");
        var options = new MonsterSkillOptions
        {
            Links = { [77] = new() { new() { SkillId = 9000, Level = 1, Probability = 1 } } },
            Triggers = { [77] = new() { new() { Type = 5, Value1 = 10, Function = "broken" } } }
        };
        h.MonsterService(Row(111, Vars(2)), options, scripts).TryCast(target, 0, MonsterHandle, 1000, out _).Should().BeTrue();
        StorageTestHarness.Session(target).CharacterHp.Should().Be(975);
        scripts.RunString("assert(get_monster_id(1073741825)==0)").Should().Be(1);
        scripts.RunMonsterTrigger("missing", new MonsterScriptContext()).Should().BeFalse();
    }
    [Test]
    public void Official_Lua_reinforcements_enter_for_observers_move_and_inherit_aggro()
    {
        using var h = new Harness(13); var target = h.Client(); var watcher = h.Client(101);
        var otherLayer = h.Client(102, layer: 1);
        h.World.SetAggro(0, target);
        var options = new MonsterSkillOptions { Triggers = { [77] = new()
        {
            new() { Type = 1, Value1 = -1, Function = "trigger" },
            new() { Type = 5, Value1 = 10, Function = "trigger" }
        } } };
        h.MonsterService(Row(111, Vars(2)), options, new ScriptService(NullLogger<ScriptService>.Instance))
            .TryCast(target, 0, MonsterHandle, 1000, out _).Should().BeTrue();
        var reinforcements = h.World.WithinRange(100, 100, 150).Where(m => m.MonsterId == 2055001).ToArray();
        reinforcements.Should().HaveCount(2);
        foreach (var monster in reinforcements)
        {
            h.World.TryGetAggro(monster.InstanceId, out var enemy, out _).Should().BeTrue();
            enemy.Should().BeSameAs(target);
            monster.X.Should().Be(100); monster.Y.Should().Be(100);
            h.World.TryGetMoveDestination(monster.InstanceId, out var x, out var y).Should().BeTrue();
            x.Should().BeInRange(40, 160); y.Should().BeInRange(40, 160);
            var own = StorageTestHarness.Session(target).GetMonsterHandle(monster.InstanceId);
            var peer = StorageTestHarness.Session(watcher).GetMonsterHandle(monster.InstanceId);
            own.Should().NotBe(0); peer.Should().NotBe(0); peer.Should().NotBe(own);
            StorageTestHarness.Session(otherLayer).GetMonsterHandle(monster.InstanceId).Should().Be(0);
            var move = h.Connections[watcher].Sent.Single(p => p.Length >= 15
                && BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4)) == (ushort)GamePackets.TM_SC_MOVE
                && BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(11)) == peer);
            move[16].Should().Be(MonsterMovement.SpeedByte(monster.Combat.Plain.MoveSpeed * 3));
        }
    }

    [Test]
    public void Script_spawn_death_is_claimed_once_and_does_not_respawn_or_reuse_its_id()
    {
        using var h = new Harness();
        var monster = h.World.RespawnNearMonster(0, 2055001, 1).Single().Instance;
        h.World.TryKill(monster.InstanceId, DateTime.UtcNow).Should().BeTrue();
        h.World.TryKill(monster.InstanceId, DateTime.UtcNow).Should().BeFalse();
        h.World.CollectRespawns(DateTime.UtcNow.AddSeconds(1));
        h.World.TryGetInstance(monster.InstanceId, out _).Should().BeFalse();
        h.World.IsAlive(monster.InstanceId).Should().BeFalse();
        h.World.WithinRange(100, 100, 500).Should().NotContain(m => m.InstanceId == monster.InstanceId);
        h.World.RespawnNearMonster(0, 2055001, 1).Single().Instance.InstanceId.Should().BeGreaterThan(monster.InstanceId);
        h.World.Kill(0, DateTime.UtcNow.AddSeconds(10));
        h.World.RespawnNearMonster(0, 2055001, 2).Should().BeEmpty();
    }

    [Test]
    public void Official_anti_bot_branch_persists_the_flag_and_clear_removes_nemesis()
    {
        using var h = new Harness(5041); var target = h.Client();
        var info = StorageTestHarness.Session(target); info.AccountName = "Account"; info.CharacterName = "Target";
        var characters = A.Fake<ICharacterService>(); var stateService = A.Fake<ISkillCastService>();
        A.CallTo(() => characters.SetAutoUsedAsync("Account", "Target", A<bool>._)).Returns(true);
        var scripts = new ScriptService(NullLogger<ScriptService>.Instance);
        var options = new MonsterSkillOptions { Triggers = { [77] = new()
        {
            new() { Type = 1, Value1 = -1, Function = "trigger" },
            new() { Type = 5, Value1 = 10, Function = "trigger" }
        } } };
        h.MonsterService(Row(111, Vars(2)), options, scripts, characters, stateService)
            .TryCast(target, 0, MonsterHandle, 1000, out _).Should().BeTrue();
        info.AutoUsed.Should().BeTrue();
        A.CallTo(() => characters.SetAutoUsedAsync("Account", "Target", true)).MustHaveHappenedOnceExactly();
        A.CallTo(() => stateService.ApplyState(target, 5997, 11, 8640000)).MustHaveHappenedOnceExactly();
        scripts.RunString("function clear_auto(m,t) assert(set_auto_user(0,t)==0) end");
        options.Triggers[77] = new() { new() { Type = 5, Value1 = 10, Function = "clear_auto" } };
        h.World.ClearAggro(0);
        h.MonsterService(Row(111, Vars(2)), options, scripts, characters, stateService)
            .TryCast(target, 0, MonsterHandle, 1001, out _).Should().BeTrue();
        info.AutoUsed.Should().BeFalse();
        A.CallTo(() => characters.SetAutoUsedAsync("Account", "Target", false)).MustHaveHappenedOnceExactly();
        A.CallTo(() => stateService.RemoveState(target, 5997)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public void Player_area_hits_monsters_outside_caster_streaming_and_reaches_their_observers()
    {
        using var h = new Harness(); var caster = h.Client(); var watcher = h.Client(101);
        StorageTestHarness.Session(caster).SpawnedMonsters.Remove(1);
        A.CallTo(() => h.Players.Observers(caster)).Returns(Array.Empty<GameClient>());
        h.Cast(h.PlayerService(Row(30011, Vars(4))), caster);
        h.World.GetHp(1).Should().Be(975);
        var own = h.Frames(caster, SkillPacketType.Fire).Single();
        BinaryPrimitives.ReadUInt16LittleEndian(own.AsSpan(55)).Should().Be(1);
        var peer = h.Frames(watcher, SkillPacketType.Fire).Single();
        BinaryPrimitives.ReadUInt16LittleEndian(peer.AsSpan(55)).Should().Be(2);
        BinaryPrimitives.ReadUInt32LittleEndian(peer.AsSpan(103)).Should().Be(MonsterHandle + 11);
        A.CallTo(() => h.Combat.ApplyDamage(caster, 1, 0, 25, A<int>._)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public void Monster_area_hits_a_player_who_does_not_stream_the_caster()
    {
        using var h = new Harness(); var primary = h.Client(); var victim = h.Client(101, 125);
        StorageTestHarness.Session(victim).SpawnedMonsters.Remove(0);
        h.MonsterService(Row(111, Vars(2))).TryCast(primary, 0, MonsterHandle, 1000, out _).Should().BeTrue();
        StorageTestHarness.Session(victim).CharacterHp.Should().Be(975);
        var fire = h.Frames(victim, SkillPacketType.Fire).Single();
        BinaryPrimitives.ReadUInt32LittleEndian(fire.AsSpan(10)).Should().Be(0);
        BinaryPrimitives.ReadUInt16LittleEndian(fire.AsSpan(55)).Should().Be(2);
    }

    [TestCase(30011)] [TestCase(232)] [TestCase(261)]
    public void Targeted_player_casts_refuse_a_distant_monster_before_mana_and_cooldown(int effect)
    {
        using var h = new Harness(); var caster = h.Client(x: 500);
        h.Cast(h.PlayerService(Row(effect, Vars(4)) with { CastRange = 1 }), caster);
        var info = StorageTestHarness.Session(caster);
        info.CharacterMp.Should().Be(100); info.SkillCooldowns.Should().BeEmpty();
        h.World.GetHp(0).Should().Be(1000);
        var failure = h.Frames(caster, SkillPacketType.Casting).Single();
        BinaryPrimitives.ReadUInt16LittleEndian(failure.AsSpan(52)).Should().Be((ushort)ResultCode.TooFar);
        h.Frames(caster, SkillPacketType.Fire).Should().BeEmpty();
    }

    [TestCase(93f, false)] [TestCase(94f, true)]
    public void Ground_cast_with_weapon_range_is_checked_instead_of_ignoring_minus_one(float x, bool allowed)
    {
        using var h = new Harness(); var caster = h.Client(x: x);
        A.CallTo(() => h.Stats.Compute(A<ConnectionInfo>._)).Returns(new CharacterStatResult(
            new StatBlock { MagicPoint = 200, AttackRange = 50 }, new StatBlock()));
        h.Cast(h.PlayerService(Row(271, Vars(9), required: 2) with { CastRange = -1 }), caster, target: 0);
        StorageTestHarness.Session(caster).CharacterMp.Should().Be(allowed ? 90 : 100);
        h.Frames(caster, SkillPacketType.Fire).Count.Should().Be(allowed ? 1 : 0);
    }

    [TestCase(79f, false)] [TestCase(80f, true)]
    public void Player_weapon_range_uses_the_casters_stat_and_both_body_radii(float x, bool allowed)
    {
        using var h = new Harness(); var caster = h.Client(x: x);
        A.CallTo(() => h.Stats.Compute(A<ConnectionInfo>._)).Returns(new CharacterStatResult(
            new StatBlock { MagicPoint = 200, AttackRange = 100 }, new StatBlock()));
        h.Cast(h.PlayerService(Row(232, Vars(6)) with { CastRange = -1 }), caster);
        h.World.GetHp(0).Should().Be(allowed ? 975 : 1000);
        StorageTestHarness.Session(caster).CharacterMp.Should().Be(allowed ? 90 : 100);
    }

    [Test]
    public void Monster_pick_skips_the_out_of_range_skill_and_casts_the_next_without_charging_its_cooldown()
    {
        using var h = new Harness(); var target = h.Client(x: 500);
        var near = Row(111, Vars(2)) with { CastRange = 1 };
        var far = Row(262, Vars(9), id: 9001) with { CastRange = 50 };
        var repository = A.Fake<ISkillResourceRepository>();
        A.CallTo(() => repository.GetSkillRows(A<IReadOnlyCollection<int>>._)).Returns(new[] { near, far });
        var options = new MonsterSkillOptions { Links = { [77] = new()
            { new() { SkillId = 9000, Level = 1, Probability = 1 }, new() { SkillId = 9001, Level = 1, Probability = 1 } } } };
        var service = new MonsterSkillService(new MonsterSkillCatalog(Options.Create(options), repository),
            h.World, h.Combat, A.Fake<ISkillCastService>(), players: h.Players, effects: h.Effects);
        service.TryCast(target, 0, MonsterHandle, 1000, out _).Should().BeTrue();
        h.World.IsSkillReady(0, 9000, 1000).Should().BeTrue();
        h.World.IsSkillReady(0, 9001, 1000).Should().BeFalse();
        BinaryPrimitives.ReadUInt16LittleEndian(h.Frames(target, SkillPacketType.Casting).Single().AsSpan(7))
            .Should().Be(9001);
    }

    [Test]
    public void Lua_cannot_bypass_cast_range_or_start_a_cooldown_for_a_refused_skill()
    {
        using var h = new Harness(); var target = h.Client(x: 500);
        var scripts = new ScriptService(NullLogger<ScriptService>.Instance);
        scripts.RunString("function range_trigger(m,t) assert(not monster_skill_cast(0,m,t)) end").Should().Be(1);
        var options = new MonsterSkillOptions
        {
            Links = { [77] = new() { new() { SkillId = 9000, Level = 1, Probability = 0 } } },
            Triggers = { [77] = new() { new() { Type = 5, Value1 = 10, Function = "range_trigger" } } }
        };
        h.MonsterService(Row(111, Vars(2)) with { CastRange = 1 }, options, scripts)
            .TryCast(target, 0, MonsterHandle, 1000, out _).Should().BeFalse();
        h.World.IsSkillReady(0, 9000, 1000).Should().BeTrue();
        h.Frames(target, SkillPacketType.Casting).Should().BeEmpty();
        StorageTestHarness.Session(target).CharacterHp.Should().Be(1000);
    }

    [TestCase(false)] [TestCase(true)]
    public void Monster_cast_range_tolerance_depends_on_the_players_current_movement(bool moving)
    {
        using var h = new Harness(); var target = h.Client(x: 123);
        var info = StorageTestHarness.Session(target);
        if (moving) { info.MoveStartTick = 1000; info.DestinationX = 200; info.DestinationY = 100; }
        h.MonsterService(Row(111, Vars(2)) with { CastRange = 1 })
            .TryCast(target, 0, MonsterHandle, 1000, out _).Should().Be(moving);
        info.CharacterHp.Should().Be(moving ? 975 : 1000);
    }

    [Test]
    public void Wrong_request_layer_cannot_start_a_targeted_series()
    {
        using var h = new Harness(); var caster = h.Client();
        StorageTestHarness.Session(caster).LearnedSkills[9000] = 1;
        h.PlayerService(Row(232, Vars(6))).Cast(caster,
            new GameActionPackets.SkillRequest(9000, PlayerHandle, MonsterHandle, 100, 100, 0, 1, 1));
        StorageTestHarness.Session(caster).CharacterMp.Should().Be(100);
        h.Frames(caster, SkillPacketType.Fire).Should().BeEmpty();
        BinaryPrimitives.ReadUInt16LittleEndian(h.Frames(caster, SkillPacketType.Casting).Single().AsSpan(52))
            .Should().Be((ushort)ResultCode.InvalidArgument);
    }

    [TestCase(30011)]
    [TestCase(232)]
    public void Player_area_and_multi_hit_skills_work_in_private_layers_without_hitting_public_monsters(int effect)
    {
        using var h = new Harness();
        var caster = h.Client(layer: 2);
        var info = StorageTestHarness.Session(caster);
        var id = h.World.SpawnDungeonMonsters(new[]
        {
            new MonsterSpawnPoint { MonsterId = 2101, Count = 1, X = 100, Y = 100, Layer = 2 }
        }).Single();
        info.SpawnedMonsters.Clear();
        info.SpawnedMonsters[id] = MonsterHandle;
        h.Cast(h.PlayerService(Row(effect, Vars(effect == 30011 ? 4 : 6))), caster);
        h.Effects.Tick(uint.MaxValue / 2);
        h.World.GetHp(id).Should().BeLessThan(1000);
        h.World.GetHp(0).Should().Be(1000);
        h.Frames(caster, SkillPacketType.Fire).Should().NotBeEmpty();
    }

    [Test]
    public void Self_ground_area_starts_at_the_casters_interpolated_position()
    {
        using var h = new Harness(); var caster = h.Client(x: 80);
        var info = StorageTestHarness.Session(caster);
        info.DestinationX = 120; info.DestinationY = 100;
        info.MoveStartTick = unchecked(ServerClock.Now - 100);
        var v = Vars(9, 1); v[6] = 2; v[8] = 1; v[10] = 1;
        h.Cast(h.PlayerService(Row(271, v, required: 0)), caster, target: 0);
        var enter = h.Connections[caster].Sent.Single(p => p.Length == 38 && p[25] == 5);
        BinaryPrimitives.ReadSingleLittleEndian(enter.AsSpan(12)).Should().Be(120);
        h.World.GetHp(0).Should().Be(1000); h.World.GetHp(1).Should().Be(975);
    }

    [Test]
    public void Monster_area_excludes_a_player_who_walked_out_since_the_last_position_report()
    {
        using var h = new Harness(); var primary = h.Client(x: 100); var moving = h.Client(101, 125);
        var info = StorageTestHarness.Session(moving);
        info.DestinationX = 400; info.DestinationY = 100; info.MoveStartTick = 900;
        h.MonsterService(Row(111, Vars(2))).TryCast(primary, 0, MonsterHandle, 1000, out _).Should().BeTrue();
        info.CharacterHp.Should().Be(1000);
        StorageTestHarness.Session(primary).CharacterHp.Should().Be(975);
    }

}
