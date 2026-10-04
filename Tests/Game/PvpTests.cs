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
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Maps;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Casting;
using Navislamia.Game.Services.Compete;
using Navislamia.Game.Services.Death;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Party;
using Navislamia.Game.Services.Rates;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

[TestFixture]
public class PvpTests
{
    private static ConnectionInfo Info(GameClient client) => StorageTestHarness.Session(client);
    private sealed class Harness : IDisposable
    {
        public readonly GameRuleOptions Rules = new();
        public readonly MonsterWorldState World;
        public readonly PlayerRegistry Registry = new();
        public readonly IPlayerVisibilityService Players = A.Fake<IPlayerVisibilityService>();
        public readonly IStateCatalog States = A.Fake<IStateCatalog>();
        public readonly IStatService Stats = A.Fake<IStatService>();
        public readonly ICompeteService Compete = A.Fake<ICompeteService>();
        public readonly ILevelingService Leveling = A.Fake<ILevelingService>();
        public readonly IDeathDropService DeathDrops = A.Fake<IDeathDropService>();
        public readonly IPartyService Parties = A.Fake<IPartyService>();
        public readonly Navislamia.Game.Services.Creatures.ICreatureEvents Creatures = A.Fake<Navislamia.Game.Services.Creatures.ICreatureEvents>();
        public readonly CastInterrupts Interrupts = new();
        public readonly ISkillCastService CastListener = A.Fake<ISkillCastService>();
        public readonly SkillEffectScheduler Effects = new(false);
        public readonly CombatService Combat;
        public readonly Navislamia.Game.Services.Progression.ITitleService Titles = A.Fake<Navislamia.Game.Services.Progression.ITitleService>();
        public readonly Dictionary<GameClient, StorageTestHarness.FrameConnection> Connections = new();
        public readonly IMapService Map = A.Fake<IMapService>();

        public Harness()
        {
            var resources = A.Fake<IMonsterResourceRepository>();
            A.CallTo(() => resources.GetByIds(A<IReadOnlyCollection<int>>._)).Returns(new[]
                { new MonsterResourceEntity { Id = 2101, Level = 10, Hp = 10000, Exp = 1000, Jp = 10 } });
            World = new MonsterWorldState(resources, Options.Create(new MonsterSpawnOptions
                { Spawns = { new MonsterSpawnPoint { MonsterId = 2101, X = 100, Y = 100, Count = 2, Radius = 0 } } }));
            var options = A.Fake<IOptionsMonitor<GameRuleOptions>>();
            A.CallTo(() => options.CurrentValue).Returns(Rules);
            A.CallTo(() => Map.GetLocationId(A<float>._, A<float>._)).ReturnsLazily((float x, float y) => x >= 500 ? 2 : 1);
            var locations = A.Fake<IWorldLocationService>();
            var field = new WorldLocation(1, 2, 0, 0, 0); var town = new WorldLocation(2, 1, 0, 0, 0);
            A.CallTo(() => locations.TryGet(1, out field)).Returns(true);
            A.CallTo(() => locations.TryGet(2, out town)).Returns(true);
            A.CallTo(() => Players.Registry).Returns(Registry);
            A.CallTo(() => Players.Observers(A<GameClient>._)).ReturnsLazily((GameClient c) => Registry.Clients
                .Where(p => !ReferenceEquals(c, p) && Info(p).Layer == Info(c).Layer
                    && Info(p).SpawnedPlayers.ContainsKey(Info(c).CharacterHandle)).ToArray());
            A.CallTo(() => Players.SendToObservers(A<GameClient>._, A<byte[]>._, A<bool>._)).Invokes((GameClient c, byte[] f, bool self) =>
            {
                if (self) c.Connection.Send(f);
                foreach (var observer in Players.Observers(c)) observer.Connection.Send(f);
            });
            A.CallTo(() => Stats.Compute(A<ConnectionInfo>._)).ReturnsLazily((ConnectionInfo i) =>
                new CharacterStatResult(new StatBlock { AttackPointRight = 4000, MagicPoint = 4000, MaxHp = 5000, MaxMp = 1000,
                    AttackRange = 50, AttackSpeed = 100, Critical = -1, AccuracyRight = 1000, MagicAccuracy = 1000,
                    FireResistance = i.CharacterHandle == 101 ? 150 : 0 }, new StatBlock()));
            var random = A.Fake<ICombatRandom>();
            A.CallTo(() => random.Next(A<int>._)).ReturnsLazily((int max) => max == 10001 ? 5000 : 0);
            A.CallTo(() => Parties.MemberCount(A<GameClient>._)).Returns(2);
            A.CallTo(() => Creatures.LimitPlayerExperience(A<GameClient>._, A<long>._))
                .ReturnsLazily((GameClient player, long exp) => exp);
            A.CallTo(() => Parties.RewardMembers(A<GameClient>._, A<float>._, A<float>._, A<byte>._))
                .ReturnsLazily((GameClient c, float x, float y, byte l) => new[] { c });
            var rates = A.Fake<IRateService>();
            A.CallTo(() => rates.Scale(A<long>._, A<RateType>._)).ReturnsLazily((long value, RateType type) => value);
            Interrupts.Attach(CastListener);
            Combat = new CombatService(World, A.Fake<IMonsterSpawnService>(), Leveling, A.Fake<IGroundItemService>(),
                rates, Stats, States, Parties, random: random, players: Players, casts: Interrupts,
                deathDrops: DeathDrops, compete: Compete, rules: options, runTicks: false,
                pkFields: new PkFieldService(Map, locations, options), creatures: Creatures, titles: Titles);
        }
        public GameClient Client(uint handle, float x = 100, byte layer = 0)
        {
            var frames = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
            var client = StorageTestHarness.NewGameClient(frames, playerVisibilityService: Players, combatService: Combat); Connections[client] = frames;
            var info = Info(client);
            info.CharacterHandle = handle; info.CharacterName = "Player" + handle;
            info.CharacterLevel = 10; info.CharacterHp = 5000; info.CharacterMaxHp = 5000; info.CharacterMp = 1000;
            info.X = x; info.Y = 100; info.Layer = layer;
            foreach (var other in Registry.Clients)
            { info.SpawnedPlayers[Info(other).CharacterHandle] = default; Info(other).SpawnedPlayers[handle] = default; }
            info.SpawnedMonsters[0] = 0x40000001; info.SpawnedMonsters[1] = 0x40000002;
            Registry.Register(handle, client);
            return client;
        }
        public SkillCastService Skills(CastableSkillRow row)
        {
            var repository = A.Fake<ISkillResourceRepository>();
            A.CallTo(() => repository.GetCastableSkills()).Returns(new[] { row });
            return new SkillCastService(new BuffCatalog(repository), Stats, States, World, Combat,
                A.Fake<Navislamia.Game.Services.Props.IFieldPropCatalog>(), A.Fake<IWarpService>(), Players,
                Effects, Interrupts, runTicks: false);
        }
        public void Cast(SkillCastService service, GameClient caster, GameClient target, uint targetHandle = 0)
        {
            Info(caster).LearnedSkills[9000] = 1; service.Register(caster);
            service.Cast(caster, new GameActionPackets.SkillRequest(9000, Info(caster).CharacterHandle,
                targetHandle == 0 ? Info(target).CharacterHandle : targetHandle, Info(target).X, 100, 0,
                (sbyte)Info(caster).Layer, 1));
        }
        public List<byte[]> SkillFrames(GameClient client, SkillPacketType type) => Connections[client].Sent
            .Where(f => BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(4)) == 401 && f[31] == (byte)type).ToList();
        public void Dispose() => Effects.Dispose();
    }

    private static CastableSkillRow Row(int effect, decimal delay = 0m, bool character = true)
    {
        var vars = new decimal[20]; vars[0] = 1; vars[4] = 5; vars[6] = 3; vars[8] = 1; vars[9] = 5;
        return new CastableSkillRow(9000, effect, true, effect is 261 or 271 or 30011 ? 2 : 1,
            effect == 301 ? 1101 : null, 0, vars, 10, 0, 1, 0, 10, 0, delay, 0, 0, 1, 0, 0,
            CastRange: 20, UseOnCharacter: character, ProbabilityOnHit: 100, ElementalType: 1);
    }

    [TestCase(false, 5)] [TestCase(true, 0)]
    public void Repeated_pk_activation_frames_charge_morality_only_once(bool pkServer, int expected)
    {
        using var h = new Harness(); h.Rules.PkServer = pkServer;
        var frame = new byte[7]; BinaryPrimitives.WriteUInt32LittleEndian(frame, 7);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), 800);
        frame[6] = StorageTestHarness.Checksum(frame);
        var frames = new StorageTestHarness.FrameConnection(frame.Concat(frame).ToArray());
        var client = StorageTestHarness.NewGameClient(frames, combatService: h.Combat);
        Info(client).CharacterHandle = 100;
        client.OnDataReceived(frames.BytesAvailable);
        Info(client).PkMode.Should().BeTrue(); Info(client).ImmoralPoint.Should().Be(expected);
    }

    [Test]
    public async Task Returning_to_character_selection_saves_the_pvp_snapshot()
    {
        var characters = A.Fake<ICharacterService>();
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()),
            characterService: characters);
        var info = Info(client); info.CharacterName = "Player"; info.CharacterLevel = 10;
        info.PkMode = true; info.ImmoralPoint = 1000.3333m; info.PkCount = 23; info.DkCount = 7;
        var method = typeof(GameClient).GetMethod("SaveProgressSafelyAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        await (Task)method.Invoke(client, new object[] { "test" });
        A.CallTo(() => characters.SaveProgressAsync("Player", 10, A<int>._, A<long>._, A<long>._,
            A<long>._, A<int>._, A<float>._, A<float>._, true, new PvpProgress(1000.3333m, 23, 7), 0,
            A<Navislamia.Game.Services.Huntaholic.HuntaholicProgress?>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public void Persistent_ground_area_reselects_players_and_respects_pk_fields_each_tick()
    {
        using var h = new Harness(); var caster = h.Client(100); var victim = h.Client(101); var entrant = h.Client(102, 300);
        Info(caster).PkMode = true;
        var row = Row(271); row.Vars[6] = 2; row.Vars[8] = 1; row.Vars[10] = 1;
        var service = h.Skills(row with { Target = 4, RequiredTarget = 2 });
        Info(caster).LearnedSkills[9000] = 1; service.Register(caster);
        service.Cast(caster, new GameActionPackets.SkillRequest(9000, 100, 0, 100, 100, 0, 0, 1));
        var firstHp = Info(victim).CharacterHp; firstHp.Should().BeLessThan(5000);
        var now = unchecked(Info(caster).SkillCooldowns[9000] - 100);
        Info(victim).X = 600; Info(entrant).X = 100;
        h.Effects.Tick(now + 100);
        Info(victim).CharacterHp.Should().Be(firstHp); Info(entrant).CharacterHp.Should().BeLessThan(5000);
        Info(caster).X = 600; var entrantHp = Info(entrant).CharacterHp;
        h.Effects.Tick(now + 200);
        Info(entrant).CharacterHp.Should().Be(entrantHp);
    }

    [TestCase(10, 5000, 100.3333, 910)]
    [TestCase(10, 0, 101, 910)]
    [TestCase(11, 5000, 101, 864)]
    public void Monster_rewards_reduce_morality_only_for_alive_players_at_an_eligible_level(
        int level, int hp, decimal immoral, long exp)
    {
        using var h = new Harness(); var killer = h.Client(100); Info(killer).CharacterLevel = level;
        Info(killer).CharacterHp = hp; Info(killer).ImmoralPoint = 101; Info(killer).PartyId = 1;
        h.Combat.ApplyDamage(killer, 0, 0x40000001, int.MaxValue);
        Info(killer).ImmoralPoint.Should().Be(immoral); Info(killer).CharacterExp.Should().Be(exp);
        Info(killer).CharacterJp.Should().Be(level == 10 ? 10 : 9);
    }

    [TestCase("town")] [TestCase("replaced")]
    public void Normal_attack_stops_when_the_player_is_protected_or_the_session_changes(string change)
    {
        using var h = new Harness(); var attacker = h.Client(100); var victim = h.Client(101);
        Info(attacker).PkMode = true; h.Combat.StartAttack(attacker, 101);
        if (change == "town") Info(victim).X = 600; else h.Client(101);
        typeof(CombatService).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(h.Combat, new object[] { DateTime.UtcNow.AddSeconds(1) });
        Info(victim).CharacterHp.Should().Be(5000);
    }

    [Test]
    public void Percentage_additional_damage_receives_the_pvp_factor_once()
    {
        using var h = new Harness(); var attacker = h.Client(100); var victim = h.Client(101);
        Info(attacker).PkMode = true;
        var baseline = h.Combat.RollPlayerHit(attacker, victim, 4000, DamageKind.Physical, 0, 0).Damage;
        var values = new decimal[20]; values[0] = 1; values[6] = 100; values[8] = 1; values[11] = 99;
        A.CallTo(() => h.States.GetRule(77)).Returns(new StateRule(77, Array.Empty<int>(), 0, 0, 23, values));
        Info(attacker).ActiveBuffs.Add(new ActiveBuff(1, 77, 0, 1, 0, uint.MaxValue));
        h.Combat.StartAttack(attacker, 101);
        typeof(CombatService).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(h.Combat, new object[] { DateTime.UtcNow.AddSeconds(1) });
        (5000 - Info(victim).CharacterHp).Should().Be(baseline + baseline / 2);
    }

    [Test]
    public void Reflection_reduces_elemental_damage_without_reflecting_it_back_again()
    {
        using var h = new Harness(); var attacker = h.Client(101); var victim = h.Client(100);
        Info(attacker).PkMode = true;
        var values = new decimal[20]; values[0] = 1000; values[6] = 100; values[8] = 1;
        A.CallTo(() => h.States.GetRule(77)).Returns(new StateRule(77, Array.Empty<int>(), 0, 0, 44, values));
        foreach (var client in new[] { attacker, victim })
            Info(client).ActiveBuffs.Add(new ActiveBuff(1, 77, 0, 1, 0, uint.MaxValue));
        h.Combat.DamagePlayerByPlayer(attacker, victim, 200);
        Info(victim).CharacterHp.Should().Be(4800); Info(attacker).CharacterHp.Should().Be(4975);
    }

    [TestCase(30001)] [TestCase(231)] [TestCase(232)]
    public void Offensive_skills_target_players_and_broadcast_reduced_damage_and_hp(int effect)
    {
        using var h = new Harness(); var caster = h.Client(100); var victim = h.Client(101); var observer = h.Client(102);
        Info(caster).PkMode = true;
        h.Cast(h.Skills(Row(effect)), caster, victim);
        var frame = h.SkillFrames(caster, SkillPacketType.Fire).Single();
        var damage = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(67));
        damage.Should().BeGreaterThan(0).And.BeLessThan(200);
        Info(victim).CharacterHp.Should().Be(5000 - damage);
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(58)).Should().Be(101);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(62)).Should().Be(Info(victim).CharacterHp);
        frame[66].Should().Be(1);
        h.SkillFrames(victim, SkillPacketType.Fire).Single().Should().Equal(frame);
        h.SkillFrames(observer, SkillPacketType.Fire).Single().Should().Equal(frame);
        Info(caster).CharacterMp.Should().Be(990);
    }

    [TestCase("town")] [TestCase("neutral")] [TestCase("party")] [TestCase("guild")]
    [TestCase("layer")] [TestCase("unseen")] [TestCase("dead")] [TestCase("flags")] [TestCase("range")]
    public void Invalid_player_targets_are_refused_before_spending_mp(string reason)
    {
        using var h = new Harness(); var caster = h.Client(100); var victim = h.Client(101);
        Info(caster).PkMode = true;
        if (reason == "town") Info(victim).X = 600;
        if (reason == "neutral") Info(caster).PkMode = false;
        if (reason == "party") { Info(caster).PartyId = 1; Info(victim).PartyId = 1; }
        if (reason == "guild") { Info(caster).GuildId = 1; Info(victim).GuildId = 1; }
        if (reason == "layer") Info(victim).Layer = 1;
        if (reason == "unseen") Info(caster).SpawnedPlayers.Remove(101);
        if (reason == "dead") Info(victim).CharacterHp = 0;
        if (reason == "range") Info(victim).X = 450;
        h.Cast(h.Skills(Row(231, character: reason != "flags")), caster, victim);
        h.SkillFrames(caster, SkillPacketType.Fire).Should().BeEmpty();
        Info(caster).CharacterMp.Should().Be(1000);
    }

    [TestCase("town")] [TestCase("pkoff")] [TestCase("leave")] [TestCase("replace")]
    public void Delayed_casts_recheck_permission_and_the_target_session_before_firing(string change)
    {
        using var h = new Harness(); var caster = h.Client(100); var victim = h.Client(101);
        Info(caster).PkMode = true;
        var service = h.Skills(Row(231, delay: 1)); h.Cast(service, caster, victim);
        var now = Info(caster).PendingCast.FireTick;
        if (change == "town") Info(victim).X = 600;
        if (change == "pkoff") Info(caster).PkMode = false;
        if (change == "leave") h.Registry.Unregister(101);
        if (change == "replace") { h.Registry.Unregister(101); h.Client(101); }
        service.ProcessCasts(now + 1);
        Info(victim).CharacterHp.Should().Be(5000);
        h.SkillFrames(caster, SkillPacketType.Fire).Should().BeEmpty();
        h.SkillFrames(caster, SkillPacketType.Cancel).Should().ContainSingle();
    }

    [TestCase(30011)] [TestCase(261)]
    public void Area_skills_share_one_selection_between_monsters_and_hostile_players(int effect)
    {
        using var h = new Harness(); var caster = h.Client(100); var victim = h.Client(101);
        var ally = h.Client(102); var protectedPlayer = h.Client(103, 520); var otherLayer = h.Client(104, layer: 1);
        Info(caster).PkMode = true; Info(caster).PartyId = Info(ally).PartyId = 1;
        h.Cast(h.Skills(Row(effect)), caster, victim);
        var frame = h.SkillFrames(caster, SkillPacketType.Fire).Single();
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(55)).Should().Be(3, "two monsters and one enemy");
        Info(victim).CharacterHp.Should().BeLessThan(5000);
        Info(ally).CharacterHp.Should().Be(5000); Info(protectedPlayer).CharacterHp.Should().Be(5000);
        Info(otherLayer).CharacterHp.Should().Be(5000);
        h.World.GetHp(0).Should().BeLessThan(10200);
        Info(caster).CharacterMp.Should().Be(990);
    }

    [Test]
    public void Multi_hit_skills_stop_when_the_victim_becomes_protected()
    {
        using var h = new Harness(); var caster = h.Client(100); var victim = h.Client(101);
        Info(caster).PkMode = true; h.Cast(h.Skills(Row(232)), caster, victim);
        var hp = Info(victim).CharacterHp; hp.Should().BeLessThan(5000);
        Info(victim).X = 600; h.Effects.Tick(ServerClock.Now + 110);
        Info(victim).CharacterHp.Should().Be(hp);
        h.SkillFrames(caster, SkillPacketType.Fire).Should().ContainSingle();
    }

    [Test]
    public void A_player_debuff_uses_the_players_state_and_stat_refresh_path()
    {
        using var h = new Harness(); var caster = h.Client(100); var victim = h.Client(101);
        Info(caster).PkMode = true;
        h.Cast(h.Skills(Row(301)), caster, victim);
        Info(victim).ActiveBuffs.Should().ContainSingle().Which.StateId.Should().Be(1101);
        Info(victim).ActiveBuffs.Single().SourceHandle.Should().Be(100);
        Info(caster).ActiveBuffs.Should().BeEmpty();
        A.CallTo(() => h.Stats.RefreshBuffs(Info(victim))).MustHaveHappenedOnceExactly();
        h.Connections[victim].Sent.Any(f => BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(4)) == 505).Should().BeTrue();
    }

    [Test]
    public void An_innocent_kill_updates_morality_and_counts_once_and_applies_nemesis()
    {
        using var h = new Harness(); var killer = h.Client(100); var victim = h.Client(101);
        Info(killer).PkMode = true; Info(killer).CharacterLevel = 20;
        h.Combat.DamagePlayerByPlayer(killer, victim, 5000);
        h.Combat.DamagePlayerByPlayer(killer, victim, 5000);
        Info(killer).ImmoralPoint.Should().Be(100); Info(killer).PkCount.Should().Be(1);
        Info(killer).DkCount.Should().Be(1);
        A.CallTo(() => h.Titles.RecordAsync(killer,
            A<Func<Navislamia.Game.Services.Progression.TitleConditionType,long?>>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => h.CastListener.ApplyState(killer, 5999, 1, 720000)).MustHaveHappenedOnceExactly();
        A.CallTo(() => h.Leveling.ApplyDeathPenalty(A<GameClient>._)).MustNotHaveHappened();
        var immoral = h.Connections[killer].Sent.Single(f => f.Length == 37 && System.Text.Encoding.ASCII.GetString(f, 12, 7) == "immoral");
        BinaryPrimitives.ReadInt64LittleEndian(immoral.AsSpan(28)).Should().Be(1000000);
        (ActorStatus.ForPlayer(Info(killer)) & Navislamia.Game.Network.Packets.Enums.CreatureStatus.PlayerBloody).Should().NotBe(0);
    }

    [TestCase(true, 0)] [TestCase(false, 100)]
    public void Killing_a_pk_player_or_a_criminal_does_not_increase_morality(bool pk, int immoral)
    {
        using var h = new Harness(); var killer = h.Client(100); var victim = h.Client(101);
        Info(victim).PkMode = pk; Info(victim).ImmoralPoint = immoral;
        h.Combat.DamagePlayerByPlayer(killer, victim, 5000);
        Info(victim).CharacterHp.Should().Be(0);
        Info(killer).GetPvpProgress().Should().Be(new PvpProgress(0, 0, 0));
    }

    [Test]
    public void Duels_work_in_town_and_do_not_change_morality_or_death_rewards()
    {
        using var h = new Harness(); var killer = h.Client(100, 600); var victim = h.Client(101, 600);
        h.Rules.PkServer = true;
        A.CallTo(() => h.Compete.AreCompeting(killer, victim)).Returns(true);
        h.Cast(h.Skills(Row(231)), killer, victim);
        Info(victim).CharacterHp.Should().BeLessThan(5000);
        h.Combat.DamagePlayerByPlayer(killer, victim, 5000);
        A.CallTo(() => h.Compete.OnKilledBy(victim, killer)).MustHaveHappenedOnceExactly();
        Info(killer).GetPvpProgress().Should().Be(new PvpProgress(0, 0, 0));
        A.CallTo(() => h.Leveling.ApplyDeathPenalty(A<GameClient>._)).MustNotHaveHappened();
        A.CallTo(() => h.DeathDrops.DropOnDeathAsync(A<GameClient>._)).MustNotHaveHappened();
        A.CallTo(() => h.Titles.RecordAsync(A<GameClient>._,
            A<Func<Navislamia.Game.Services.Progression.TitleConditionType,long?>>._)).MustNotHaveHappened();
    }

    [Test]
    public void Pk_server_death_uses_the_death_path_and_reduces_victim_morality()
    {
        using var h = new Harness(); var killer = h.Client(100); var victim = h.Client(101);
        h.Rules.PkServer = true; Info(victim).ImmoralPoint = 101; Info(victim).PkCount = 10;
        h.Combat.DamagePlayerByPlayer(killer, victim, 5000);
        A.CallTo(() => h.Leveling.ApplyDeathPenalty(victim)).MustHaveHappenedOnceExactly();
        A.CallTo(() => h.DeathDrops.DropOnDeathAsync(victim)).MustHaveHappenedOnceExactly();
        Info(victim).ImmoralPoint.Should().Be(92);
        var condition = new Navislamia.Game.Services.Progression.TitleConditionType(1, 6002, new[] { 100,0,0 }, false);
        A.CallTo(() => h.Titles.RecordAsync(killer,
            A<Func<Navislamia.Game.Services.Progression.TitleConditionType,long?>>.That.Matches(f => f(condition) == 1)))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public void Player_skill_damage_uses_resistance_and_mana_shield_and_pushes_back_casts()
    {
        using var h = new Harness(); var killer = h.Client(100); var victim = h.Client(101);
        Info(killer).PkMode = true;
        var values = new decimal[20]; values[0] = .5m; values[4] = 99;
        A.CallTo(() => h.States.GetRule(77)).Returns(new StateRule(77, Array.Empty<int>(), 0, 0, 49, values));
        Info(victim).ActiveBuffs.Add(new ActiveBuff(1, 77, 0, 1, 0, uint.MaxValue));
        var damage = h.Combat.RollPlayerHit(killer, victim, 4000, DamageKind.Magical, 0, 0, 1).Damage;
        h.Combat.DamagePlayerByPlayer(killer, victim, damage, true);
        Info(victim).CharacterMp.Should().Be(1000 - damage / 2);
        Info(victim).CharacterHp.Should().Be(5000 - damage + damage / 2);
        A.CallTo(() => h.CastListener.OnCasterDamaged(victim, damage - damage / 2)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public void Summon_skill_damage_uses_its_level_and_stats_resistance_and_pvp_factor_once()
    {
        using var h = new Harness(); var master = h.Client(100); var victim = h.Client(101);
        Info(master).PkMode = true;
        var stats = new StatBlock { MagicPoint = 120, Critical = -1 };
        var random = A.Fake<ICombatRandom>();
        A.CallTo(() => random.Next(A<int>._)).ReturnsLazily((int max) => max == 10001 ? 5000 : 0);
        var defender = h.Stats.Compute(Info(victim)).Total;
        var expected = CombatFormulas.Resolve(Combatant.From(stats, 8), Combatant.From(defender, 10),
            120, DamageKind.Magical, 0, 0, random, 1).Damage;
        h.Combat.RollSummonHitOnPlayer(master, victim, stats, 8, 120, DamageKind.Magical, 0, 0, 1)
            .Damage.Should().Be((int)(expected * h.Rules.PvpDamageRate));
        Info(master).PkMode = false;
        h.Combat.RollSummonHitOnPlayer(master, victim, stats, 8, 120, DamageKind.Magical, 0, 0, 1)
            .Flags.Should().Be(HitFlags.Miss);
    }

    [Test]
    public void Player_reflection_hits_the_attacking_summon_and_keeps_the_masters_vitals()
    {
        using var h = new Harness(); var master = h.Client(100); var victim = h.Client(101);
        Info(master).PkMode = true;
        Info(master).Summons = new[] { new SummonPresence(300, new SummonWorldEntry { Hp = 500, MaxHp = 500,
            BaseStats = new StatBlock { FireResistance = 150 } }, 100, 100, 0) };
        var values = new decimal[20]; values[0] = 1000; values[6] = 100; values[8] = 1;
        A.CallTo(() => h.States.GetRule(77)).Returns(new StateRule(77, Array.Empty<int>(), 0, 0, 44, values));
        Info(victim).ActiveBuffs.Add(new ActiveBuff(1, 77, 0, 1, 0, uint.MaxValue));
        h.Combat.DamagePlayerBySummon(master, victim, 300, 100, true);
        A.CallTo(() => h.Creatures.SummonReflected(master, 300, 25)).MustHaveHappenedOnceExactly();
        Info(master).CharacterHp.Should().Be(5000); Info(victim).CharacterHp.Should().Be(4900);
    }
}
