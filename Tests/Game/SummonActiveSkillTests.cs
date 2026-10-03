using System.Buffers.Binary;
using System.Reflection;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Casting;
using Navislamia.Game.Services.Creatures;
using Navislamia.Game.Services.GmCommands;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Props;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

[TestFixture]
public class SummonActiveSkillTests
{
    private const ushort Skill = 7001;
    private const uint Monster = 0x40000001;
    private static ConnectionInfo Info(GameClient c) => StorageTestHarness.Session(c);
    private static ushort Packet(byte[] f) => BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(4));
    private static uint U32(byte[] f, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(offset));
    private static int I32(byte[] f, int offset) => BinaryPrimitives.ReadInt32LittleEndian(f.AsSpan(offset));
    private static CastableBuffFields Fields(int effect = 30001, decimal delay = 0, decimal cooldown = 0) => new(
        Skill, SkillAreaRules.IsSupportedDamage(effect) ? SkillAreaRules.IsMagical(effect) ? SkillCastKind.MagicAttack : SkillCastKind.PhysicalAttack
            : effect == 701 ? SkillCastKind.Aura : effect is 501 or 508 or 521 ? SkillCastKind.Heal : SkillCastKind.Buff,
        42, 3, new decimal[20], 60, 0, 2, 0, 10, 0, delay, 0, 0, cooldown, 0, 1,
        EffectType: effect, CastRange: 20, ValidRange: 10, Cancelable: true, HateMod: 1);

    private sealed class Harness : IDisposable
    {
        public readonly ICombatService Combat = A.Fake<ICombatService>();
        public readonly IStatService Stats = A.Fake<IStatService>();
        public readonly IStateCatalog States = A.Fake<IStateCatalog>();
        public readonly IBuffCatalog Catalog = A.Fake<IBuffCatalog>();
        public readonly ICombatRandom Random = A.Fake<ICombatRandom>();
        public readonly IPlayerVisibilityService Players = A.Fake<IPlayerVisibilityService>();
        public readonly PlayerRegistry Registry = new();
        public readonly SkillEffectScheduler Effects = new(false);
        public readonly CastInterrupts Interrupts = new();
        public readonly MonsterWorldState World;
        public readonly CreatureService Creatures;
        public readonly SkillCastService Service;
        public readonly GameClient Master;
        public readonly CreatureCard Card;
        public readonly List<(long Id, int Damage)> Damage = new();
        public readonly Dictionary<GameClient, StorageTestHarness.FrameConnection> Wires = new();
        public SummonPresence Actor => Info(Master).Summons[0];
        public Harness(CastableBuffFields fields)
        {
            A.CallTo(() => Catalog.TryGet(Skill, out fields)).Returns(true).AssignsOutAndRefParameters(fields);
            A.CallTo(() => Random.Next(A<int>._)).ReturnsLazily((int max) => max == 10001 ? 5000 : 50);
            A.CallTo(() => Stats.Compute(A<ConnectionInfo>._)).Returns(new CharacterStatResult(new StatBlock
            { MaxHp = 1000, MaxMp = 500, AttackPointRight = 9000, MagicPoint = 9000, AttackRange = 50 }, new StatBlock()));
            A.CallTo(() => Players.Registry).Returns(Registry);
            A.CallTo(() => Players.Observers(A<GameClient>._)).ReturnsLazily((GameClient subject) => Registry.Clients.Where(c => c != subject).ToArray());
            A.CallTo(() => Players.SendToObservers(A<GameClient>._, A<byte[]>._, A<bool>._)).Invokes((GameClient c, byte[] f, bool self) =>
            { if (self) c.Connection.Send(f); foreach (var peer in Players.Observers(c)) peer.Connection.Send(f); });
            var repository = A.Fake<IMonsterResourceRepository>();
            A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._)).Returns(new[]
                { new MonsterResourceEntity { Id = 2101, Level = 5, Hp = 10000, Size = 1, Scale = 1 } });
            World = new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions
            {
                Spawns =
                { new MonsterSpawnPoint { MonsterId = 2101, X = 100, Y = 100, Radius = 0, Count = 1 },
                  new MonsterSpawnPoint { MonsterId = 2101, X = 125, Y = 100, Radius = 0, Count = 1 },
                  new MonsterSpawnPoint { MonsterId = 2101, X = 400, Y = 100, Radius = 0, Count = 1 } }
            }));
            World.WithinRange(100, 100, 500);
            A.CallTo(() => Combat.GetMonsterStats(A<long>._)).Returns(new StatBlock());
            A.CallTo(() => Combat.ApplyDamage(A<GameClient>._, A<long>._, A<uint>._, A<int>._, 0))
                .ReturnsLazily((GameClient c, long id, uint handle, int damage, int hate) =>
                { Damage.Add((id, damage)); return World.GetHp(id); });
            var creatureCatalog = new CreatureCatalog(Options.Create(new CreatureCatalogOptions
            {
                Summons = { new SummonResourceOptions { Id = 2101, Form = 1, CardId = 540014, StatId = 2101,
                RunSpeed = 100, AttackRange = .2f, Size = 2.4f, Scale = 1,
                Stats = new float[] { 14, 7, 16, 5, 10, 8, 10 } } }
            }));
            Creatures = new CreatureService(creatureCatalog, A.Fake<ICharacterService>(), World, Combat,
                new SummonWorldService(Players), Players, runTicks: false, castInterrupts: Interrupts);
            Service = new SkillCastService(Catalog, Stats, States, World, Combat, A.Fake<IFieldPropCatalog>(),
                A.Fake<IWarpService>(), Players, Effects, Interrupts, Random, runTicks: false, creatures: Creatures);
            Master = Player(7);
            Card = AddCard(Master, 60);
            Creatures.Summon(Master, 60).Should().BeTrue();
            Actor.Stats = new StatBlock
            {
                MaxHp = 1000,
                MaxMp = 500,
                AttackPointRight = 80,
                MagicPoint = 120,
                MagicAccuracy = 1000,
                AccuracyRight = 1000,
                Critical = -1,
                AttackRange = 50,
                CastingSpeed = 100
            };
            Actor.Hp = 500; Actor.Mp = 200;
            Actor.PositionProvider = _ => (100, 100);
            Wires[Master].Sent.Clear();
        }
        public GameClient Player(uint handle, float x = 100, long? party = 9)
        {
            var wire = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
            var c = StorageTestHarness.NewGameClient(wire, playerVisibilityService: Players, combatService: Combat,
                creatureService: Creatures, skillCastService: Service);
            Wires[c] = wire; var i = Info(c);
            i.CharacterHandle = handle; i.CharacterHp = 500; i.CharacterMp = 200; i.CharacterLevel = 200;
            i.X = i.DestinationX = x; i.Y = i.DestinationY = 100; i.PartyId = party;
            i.SummonSlots = new long[6];
            i.SpawnedMonsters[0] = Monster; i.SpawnedMonsters[1] = Monster + 1; i.SpawnedMonsters[2] = Monster + 2;
            foreach (var peer in Registry.Clients) { i.SpawnedPlayers[Info(peer).CharacterHandle] = default; Info(peer).SpawnedPlayers[handle] = default; }
            Registry.Register(handle, c); Service.Register(c); return c;
        }
        public CreatureCard AddCard(GameClient master, long item)
        {
            var card = new CreatureCard
            {
                ItemId = item,
                Code = 540014,
                Amount = 1,
                SummonId = item + 100,
                SummonCode = 2101,
                Level = 8,
                Flag = CreatureRules.WithSummonFlag(ItemFlag.None)
            };
            card.Skills[Skill] = 2; Info(master).CreatureCards[item] = card;
            Info(master).SummonSlots[Array.FindIndex(Info(master).SummonSlots, x => x == 0)] = item;
            return card;
        }
        public void Cast(uint target = Monster, uint? caster = null, byte level = 1, float x = 100, sbyte layer = 0) => Service.Cast(Master,
            new GameActionPackets.SkillRequest(Skill, caster ?? Actor.Handle, target, x, 100, 0, layer, level));
        public byte[][] Frames(SkillPacketType type, GameClient? recipient = null) => Wires[recipient ?? Master].Sent
            .Where(f => Packet(f) == 401 && f[31] == (byte)type).ToArray();
        public ushort Error => BinaryPrimitives.ReadUInt16LittleEndian(Frames(SkillPacketType.Casting).Last().AsSpan(52));
        public void Dispose() => Effects.Dispose();
    }

    [Test]
    public void Damage_uses_the_summons_stats_mp_and_handle_and_credits_its_master()
    {
        var fields = Fields(); fields.Vars[0] = 1;
        using var h = new Harness(fields); var peer = h.Player(8);
        Info(peer).SpawnedMonsters[0] = 0x40000777;
        h.Cast();
        h.Actor.Mp.Should().Be(190); Info(h.Master).CharacterMp.Should().Be(200); h.Card.Mp.Should().Be(190);
        var expected = CombatFormulas.Resolve(Combatant.From(h.Actor.Stats, 8), Combatant.From(new StatBlock(), 5),
            80, DamageKind.Physical, 0, 0, h.Random).Damage;
        h.Damage.Should().Equal((0L, expected));
        var casting = h.Frames(SkillPacketType.Casting).Single();
        U32(casting, 10).Should().Be(h.Actor.Handle); I32(casting, 32).Should().Be(0); I32(casting, 36).Should().Be(10);
        I32(casting, 40).Should().Be(500); I32(casting, 44).Should().Be(190);
        U32(h.Frames(SkillPacketType.Fire, peer).Single(), 14).Should().Be(0x40000777);
        h.World.TryGetSummonFocus(0, h.Master, out var focus).Should().BeTrue(); focus.Should().Be(h.Actor.Handle);
        h.Frames(SkillPacketType.Complete).Should().ContainSingle();
    }

    [TestCase("foreign")]
    [TestCase("unlearned")]
    [TestCase("level")]
    [TestCase("dead")]
    [TestCase("masterdead")]
    [TestCase("ridden")]
    [TestCase("mp")]
    [TestCase("layer")]
    [TestCase("range")]
    [TestCase("nan")]
    [TestCase("stun")]
    public void Invalid_casts_do_not_consume_mp_or_deal_damage(string reason)
    {
        using var h = new Harness(Fields()); uint? caster = null; byte level = 1; sbyte layer = 0; float x = 100;
        switch (reason)
        {
            case "foreign": caster = 9999; break;
            case "unlearned": h.Card.Skills.Clear(); break;
            case "level": level = 3; break;
            case "dead": h.Actor.Hp = 0; break;
            case "masterdead": Info(h.Master).CharacterHp = 0; break;
            case "ridden": Info(h.Master).RideHandle = h.Actor.Handle; break;
            case "mp": h.Actor.Mp = 9; break;
            case "layer": layer = 1; break;
            case "range": h.Actor.PositionProvider = _ => (1000, 100); break;
            case "nan": x = float.NaN; break;
            case "stun": h.Actor.ActiveBuffs.Add(new ActiveBuff(1, 6005, 1, 1, 0, uint.MaxValue)); break;
        }
        var mp = h.Actor.Mp; h.Cast(caster: caster, level: level, layer: layer, x: x);
        h.Actor.Mp.Should().Be(mp); h.Damage.Should().BeEmpty(); h.Error.Should().NotBe(0);
    }

    [Test]
    public void Delay_busy_and_cooldown_are_per_summon_and_survive_reentry()
    {
        using var h = new Harness(Fields(delay: 2, cooldown: 60));
        h.Cast(); var now = ServerClock.Now;
        U32(h.Frames(SkillPacketType.Casting).Single(), 48).Should().Be(200);
        h.Damage.Should().BeEmpty(); h.Cast(); h.Actor.Mp.Should().Be(190); h.Error.Should().Be((ushort)ResultCode.NotActable);
        h.Service.ProcessCasts(now + 201); h.Damage.Should().ContainSingle();
        var skillList = h.Wires[h.Master].Sent.Last(f => Packet(f) == 403);
        skillList[18].Should().Be(2, "casting a lower level does not lower the learned level shown by the client");
        U32(skillList, 20).Should().Be(6000); U32(skillList, 24).Should().BeGreaterThan(0);
        h.Cast(); h.Error.Should().Be((ushort)ResultCode.CoolTime); h.Actor.Mp.Should().Be(190);
        h.Creatures.Unsummon(h.Master, 60); h.Creatures.Summon(h.Master, 60);
        U32(h.Wires[h.Master].Sent.Last(f => Packet(f) == 403), 24).Should().BeGreaterThan(0);
        h.Cast(); h.Error.Should().Be((ushort)ResultCode.CoolTime);
    }

    [Test]
    public void Master_and_second_summon_can_cast_while_first_is_casting()
    {
        using var h = new Harness(Fields(delay: 2));
        var first = h.Actor; h.Cast();
        var secondCard = h.AddCard(h.Master, 61); secondCard.SummonHandle = first.Handle + 100;
        var second = new SummonPresence(secondCard.SummonHandle, first.Entry, 100, 100, 0);
        Info(h.Master).Summons = Info(h.Master).Summons.Append(second).ToArray();
        second.Mp = 200; second.PositionProvider = _ => (100, 100);
        h.Cast(caster: second.Handle);
        Info(h.Master).LearnedSkills[Skill] = 1; h.Cast(caster: Info(h.Master).CharacterHandle);
        Info(h.Master).PendingCast.Should().NotBeNull(); second.Mp.Should().Be(190); first.Mp.Should().Be(190);
        h.Service.ProcessCasts(ServerClock.Now + 201);
        h.Frames(SkillPacketType.Complete).Should().HaveCount(3);
    }

    [TestCase("unsummon")]
    [TestCase("death")]
    [TestCase("warp")]
    [TestCase("exit")]
    [TestCase("hold")]
    public void Lifecycle_and_hold_cancel_pending_casts(string action)
    {
        using var h = new Harness(Fields(delay: 2)); h.Cast(); var handle = h.Actor.Handle;
        switch (action)
        {
            case "unsummon": h.Creatures.Unsummon(h.Master, 60); h.Creatures.Summon(h.Master, 60); break;
            case "death": h.Creatures.DamageSummon(h.Master, handle, 10000); break;
            case "warp": h.Creatures.FollowWarp(h.Master); break;
            case "exit": h.Creatures.OnWorldExit(h.Master); break;
            case "hold": h.Creatures.HoldSummon(h.Master, handle, true); break;
        }
        h.Service.ProcessCasts(ServerClock.Now + 500); h.Effects.Tick(ServerClock.Now + 500);
        h.Damage.Should().BeEmpty(); h.Frames(SkillPacketType.Cancel).Should().ContainSingle();
    }

    [Test]
    public void Cancellation_packet_addresses_only_its_owned_actor()
    {
        using var h = new Harness(Fields(delay: 2)); h.Cast();
        Info(h.Master).LearnedSkills[Skill] = 1; h.Cast(caster: 7);
        var method = typeof(GameClient).GetMethod("HandleCancelAction", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var packet = new byte[11]; BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7), 9999);
        method.Invoke(h.Master, new object[] { packet }); h.Frames(SkillPacketType.Cancel).Should().BeEmpty();
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7), h.Actor.Handle);
        method.Invoke(h.Master, new object[] { packet });
        Info(h.Master).PendingCast.Should().NotBeNull(); h.Frames(SkillPacketType.Cancel).Should().ContainSingle();
        U32(h.Frames(SkillPacketType.Cancel).Single(), 10).Should().Be(h.Actor.Handle);
    }

    [Test]
    public void Damage_pushes_back_a_summons_cast_and_reports_its_vitals()
    {
        using var h = new Harness(Fields(delay: 2) with { CastingType = CastRules.PushedBack, CastingLevel = 1 });
        h.Cast(); var tick = ServerClock.Now;
        h.Creatures.DamageSummon(h.Master, h.Actor.Handle, 10);
        var update = h.Frames(SkillPacketType.CastingUpdate).Single();
        U32(update, 10).Should().Be(h.Actor.Handle); U32(update, 48).Should().Be(250); I32(update, 40).Should().Be(490);
        h.Service.ProcessCasts(tick + 201); h.Damage.Should().BeEmpty();
        h.Service.ProcessCasts(tick + 251); h.Damage.Should().ContainSingle();
    }

    [TestCase(30011, 2)]
    [TestCase(30012, 6)]
    [TestCase(232, 3)]
    [TestCase(261, 2)]
    public void Area_and_multiple_hits_use_the_shared_geometry_and_timing(int effect, int expected)
    {
        var fields = Fields(effect); fields.Vars[0] = 1;
        fields.Vars[4] = 3; fields.Vars[6] = 3; fields.Vars[7] = 0; fields.Vars[8] = .5m; fields.Vars[9] = 3;
        if (effect == 30012) fields.Vars[7] = 3;
        using var h = new Harness(fields); h.Cast();
        var tick = ServerClock.Now;
        if (effect == 232) { h.Damage.Should().ContainSingle(); h.Effects.Tick(tick + 51); h.Effects.Tick(tick + 102); }
        h.Damage.Should().HaveCount(expected); h.Damage.Should().NotContain(d => d.Id == 2);
    }

    [Test]
    public void A_cancelled_sequence_cannot_resume_on_the_next_summon()
    {
        var f = Fields(232); f.Vars[0] = 1; f.Vars[6] = 3; f.Vars[8] = 1;
        using var h = new Harness(f); h.Cast(); h.Damage.Should().ContainSingle();
        h.Creatures.Unsummon(h.Master, 60); h.Creatures.Summon(h.Master, 60);
        h.Effects.Tick(ServerClock.Now + 1000); h.Damage.Should().ContainSingle();
    }

    [Test]
    public void Elemental_resistance_is_applied_to_the_summons_magic_damage()
    {
        var f = Fields(231) with { ElementalType = 1 }; f.Vars[0] = 1;
        using var h = new Harness(f);
        A.CallTo(() => h.Combat.GetMonsterStats(0)).Returns(new StatBlock { FireResistance = 300 });
        h.Cast(); h.Damage.Single().Damage.Should().Be(0);
    }

    [TestCase(521)]
    [TestCase(508)]
    public void Region_heals_use_summon_magic_and_heal_its_master_and_party(int effect)
    {
        var f = Fields(effect) with { Target = 51, UseOnNeutral = false }; f.Vars[0] = 1; f.Vars[10] = 10;
        f.Vars[11] = 2; f.Vars[12] = 3; if (effect == 508) f.Vars[5] = 1;
        using var h = new Harness(f); var peer = h.Player(8, 150); var outsider = h.Player(9, party: 10);
        h.Cast(target: h.Actor.Handle);
        h.Actor.Hp.Should().Be(620); Info(h.Master).CharacterHp.Should().Be(620); Info(peer).CharacterHp.Should().Be(620);
        Info(outsider).CharacterHp.Should().Be(500);
        if (effect == 508) h.Actor.Mp.Should().Be(310);
        BinaryPrimitives.ReadUInt16LittleEndian(h.Frames(SkillPacketType.Fire).Single().AsSpan(55)).Should().Be(3);
    }

    [Test]
    public void Buffs_and_aura_projection_track_the_summon_source_and_leaving_party()
    {
        using var h = new Harness(Fields(701) with { Target = 51, UseOnNeutral = false }); var peer = h.Player(8, 150);
        h.Cast(target: h.Actor.Handle);
        Info(peer).ActiveBuffs.Should().ContainSingle(s => s.SourceHandle == h.Actor.Handle && s.AuraProjection);
        h.Actor.ActiveAuras.Should().ContainValue(Skill);
        Info(peer).PartyId = 10; h.Service.ProcessBuffs(ServerClock.Now);
        Info(peer).ActiveBuffs.Should().BeEmpty();
        h.Cast(target: h.Actor.Handle); h.Actor.ActiveAuras.Should().BeEmpty(); Info(h.Master).ActiveBuffs.Should().BeEmpty();
    }

    [Test]
    public void Unsummon_removes_aura_from_its_master_immediately()
    {
        using var h = new Harness(Fields(701) with { Target = 51 }); h.Cast(target: h.Actor.Handle);
        Info(h.Master).ActiveBuffs.Should().ContainSingle(); h.Creatures.Unsummon(h.Master, 60);
        Info(h.Master).ActiveBuffs.Should().BeEmpty();
    }

    [TestCase(101, false)]
    [TestCase(102, true)]
    public void Master_and_self_with_master_targets_are_imported_and_apply_to_the_declared_pair(int target, bool self)
    {
        var row = new CastableSkillRow(Skill, 301, false, target, 42, 0, new decimal[20], 60, 0, 1, 0,
            10, 0, 0, 0, 0, 0, 0, 1, UseOnCharacter: true);
        BuffCatalog.TryClassify(row, out var fields).Should().BeTrue();
        using var h = new Harness(fields with { CastRange = 20 }); var peer = h.Player(8);
        h.Cast(target: 0);
        Info(h.Master).ActiveBuffs.Should().ContainSingle(b => b.SourceHandle == h.Actor.Handle);
        h.Actor.ActiveBuffs.Any().Should().Be(self); Info(peer).ActiveBuffs.Should().BeEmpty();
    }

    [Test]
    public void An_incoming_stun_interrupts_a_summon_cast()
    {
        using var h = new Harness(Fields(delay: 2)); h.Cast();
        var stun = Fields(301) with { SkillId = 7002, StateId = 6005 };
        A.CallTo(() => h.Catalog.TryGet(7002, out stun)).Returns(true).AssignsOutAndRefParameters(stun);
        h.Service.ApplyItemSkill(h.Master, 7002, 1, h.Actor.Handle).Should().BeTrue();
        h.Service.ProcessCasts(ServerClock.Now + 300);
        h.Damage.Should().BeEmpty(); h.Frames(SkillPacketType.Cancel).Should().ContainSingle();
    }

    [Test]
    public void The_client_skill_packet_routes_the_summons_caster_handle()
    {
        var f = Fields(); f.Vars[0] = 1;
        using var h = new Harness(f);
        var packet = new byte[31];
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(7), Skill);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(9), h.Actor.Handle);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(13), Monster);
        packet[30] = 1;
        typeof(GameClient).GetMethod("HandleSkill", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(h.Master, new object[] { packet });
        h.Damage.Should().ContainSingle(); h.Actor.Mp.Should().Be(190);
    }

    [Test]
    public void Hostile_player_skills_roll_the_summons_stats_and_credit_its_master()
    {
        var f = Fields(231) with { ElementalType = 1 }; f.Vars[0] = 1;
        using var h = new Harness(f); var victim = h.Player(8);
        A.CallTo(() => h.Combat.ArePlayerEnemies(h.Master, victim)).Returns(true);
        A.CallTo(() => h.Combat.RollSummonHitOnPlayer(h.Master, victim, h.Actor.Stats, 8, 120,
            DamageKind.Magical, A<int>._, A<int>._, 1)).Returns(new HitResult(30, HitFlags.None));
        h.Cast(target: 8);
        A.CallTo(() => h.Combat.DamagePlayerBySummon(h.Master, victim, h.Actor.Handle, 30, true)).MustHaveHappenedOnceExactly();
        Info(h.Master).CharacterMp.Should().Be(200);
    }

    [TestCase("unsummon")] [TestCase("unregister")]
    public void Persistent_ground_skill_keeps_its_origin_and_magic_and_releases_the_cast(string leave)
    {
        var f = Fields(271) with { RequiredTarget = 0, Target = 4 };
        f.Vars[0] = 1; f.Vars[6] = 2; f.Vars[8] = 1; f.Vars[9] = 3; f.Vars[10] = 1;
        using var h = new Harness(f); h.Cast(target: 0);
        var tick = ServerClock.Now; h.Damage.Should().HaveCount(2); var firstDamage = h.Damage[0].Damage;
        h.Frames(SkillPacketType.Complete).Should().ContainSingle();
        h.Service.CancelSummonCast(h.Master, h.Actor.Handle).Should().BeFalse();
        h.Actor.PositionProvider = _ => (400, 100); h.Actor.Stats.MagicPoint = 9000;
        h.Effects.Tick(tick + 101);
        h.Damage.Should().HaveCount(4); h.Damage.Should().NotContain(d => d.Id == 2);
        h.Damage.Skip(2).Should().OnlyContain(d => d.Damage == firstDamage);
        if (leave == "unsummon") h.Creatures.Unsummon(h.Master, 60);
        else h.Service.Unregister(h.Master);
        h.Effects.Tick(tick + 202); h.Damage.Should().HaveCount(4);
    }

    [Test]
    public void Aura_status_is_sent_to_late_observers_and_removed_on_death()
    {
        using var h = new Harness(Fields(701) with { Target = 51 }); h.Cast(target: h.Actor.Handle);
        CompanionFrames.SummonStates(h.Actor).Should().Contain(f => Packet(f) == 407 && U32(f, 7) == h.Actor.Handle);
        h.Creatures.DamageSummon(h.Master, h.Actor.Handle, 10000);
        Info(h.Master).ActiveBuffs.Should().BeEmpty(); h.Actor.ActiveAuras.Should().BeEmpty();
    }

    [Test]
    public void Healing_hate_is_assigned_to_the_summon()
    {
        var f = Fields(521) with { Target = 101 }; f.Vars[0] = 1;
        using var h = new Harness(f); h.World.AddHate(0, h.Master, 10);
        h.Cast(target: 7);
        h.World.TryGetSummonFocus(0, h.Master, out var focus).Should().BeTrue(); focus.Should().Be(h.Actor.Handle);
    }

    [Test]
    public void Common_delay_blocks_a_different_skill_without_spending_more_mp()
    {
        using var h = new Harness(Fields() with { DelayCommon = 60 }); h.Cast();
        var second = Fields() with { SkillId = 7002 };
        A.CallTo(() => h.Catalog.TryGet(7002, out second)).Returns(true).AssignsOutAndRefParameters(second);
        h.Card.Skills[7002] = 1;
        h.Service.Cast(h.Master, new GameActionPackets.SkillRequest(7002, h.Actor.Handle, Monster, 100, 100, 0, 0, 1));
        h.Error.Should().Be((ushort)ResultCode.CoolTime); h.Actor.Mp.Should().Be(190);
    }

    [Test]
    public void Hold_stops_following_and_attacking_but_allows_explicit_movement_and_resume()
    {
        using var h = new Harness(Fields()); var handle = h.Actor.Handle;
        h.Creatures.SummonAttack(h.Master, handle, Monster);
        h.Creatures.HoldSummon(h.Master, handle, true).Should().BeTrue();
        h.Creatures.ProcessSwings(DateTime.UtcNow.AddSeconds(5)); h.Damage.Should().BeEmpty();
        var count = h.Wires[h.Master].Sent.Count;
        var waypoints = new byte[8]; BinaryPrimitives.WriteSingleLittleEndian(waypoints.AsSpan(0), 120);
        BinaryPrimitives.WriteSingleLittleEndian(waypoints.AsSpan(4), 100);
        h.Creatures.MoveSummon(h.Master, handle, 100, 100, 0, 1, waypoints);
        h.Wires[h.Master].Sent.Should().HaveCount(count);
        h.Creatures.MoveSummon(h.Master, handle, 100, 100, 0, 0, waypoints);
        h.Wires[h.Master].Sent.Count.Should().BeGreaterThan(count);
        h.Creatures.HoldSummon(h.Master, handle, false); h.Actor.Held.Should().BeFalse();
        count = h.Wires[h.Master].Sent.Count;
        h.Creatures.MoveSummon(h.Master, handle, 100, 100, 0, 1, waypoints);
        h.Wires[h.Master].Sent.Count.Should().BeGreaterThan(count);
        h.Creatures.HoldSummon(h.Master, 9999, true).Should().BeFalse();
        h.Actor.Hp = 0; h.Creatures.HoldSummon(h.Master, handle, true).Should().BeFalse();
    }

    [TestCase("/hold", true)]
    [TestCase("/hold off", false)]
    [TestCase("/hold {handle} on", true)]
    [TestCase("/hold 9999 on", false)]
    [TestCase("/hold invalid", false)]
    public async Task Hold_chat_command_is_available_to_normal_players(string command, bool expected)
    {
        using var h = new Harness(Fields());
        var gm = new GmCommandService(A.Fake<IWarpService>(), h.Combat, A.Fake<ILevelingService>(), h.Stats,
            A.Fake<ICharacterService>(), A.Fake<IItemSortCatalog>(), h.World, null, h.Service, h.States,
            A.Fake<Navislamia.Game.Services.Rates.IRateService>(), creatures: h.Creatures);
        await gm.HandleAsync(h.Master, command.Replace("{handle}", h.Actor.Handle.ToString()), new[] { h.Master });
        h.Actor.Held.Should().Be(expected);
    }
}
