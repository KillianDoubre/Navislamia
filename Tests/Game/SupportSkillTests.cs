using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
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

[TestFixture]
public class SupportSkillTests
{
    private sealed class BlockingSave : SaveChangesInterceptor
    {
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult(true);
            await Release.Task;
            return result;
        }
    }
    private const int Skill = 7001;
    private static uint FrameHandle(byte[] frame) => BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(7));
    private static ConnectionInfo Info(GameClient c) => StorageTestHarness.Session(c);
    private static CastableBuffFields Fields(int target = 51, int effect = 301, decimal delay = 0) => new(
        Skill, effect is 501 or 505 or 508 or 521 ? SkillCastKind.Heal : effect == 701 ? SkillCastKind.Aura : SkillCastKind.Buff,
        42, 3, new decimal[20], 60, 0, 2, 0, 10, 0, delay, 0, 0, 0, 0, 0,
        EffectType: effect, Target: target, CastRange: 20, ValidRange: 10, UseOnNeutral: false);

    private sealed class Harness
    {
        public readonly SkillCastService Service;
        public readonly IBuffCatalog Catalog = A.Fake<IBuffCatalog>();
        public readonly IStateCatalog States = A.Fake<IStateCatalog>();
        public readonly IStatService Stats = A.Fake<IStatService>();
        public readonly ICombatService Combat = A.Fake<ICombatService>();
        public readonly IPlayerVisibilityService Players = A.Fake<IPlayerVisibilityService>();
        public readonly PlayerRegistry Registry = new();
        public readonly Dictionary<GameClient, StorageTestHarness.FrameConnection> Wires = new();
        public Harness(CastableBuffFields fields, IBuffPersistence persistence = null)
        {
            A.CallTo(() => Catalog.TryGet(Skill, out fields)).Returns(true).AssignsOutAndRefParameters(fields);
            A.CallTo(() => States.Exists(A<int>._)).Returns(true);
            A.CallTo(() => States.Resolve(42, A<int>._)).Returns(new[] { new StatEffect(StatTarget.MaxHp, 50, false) });
            A.CallTo(() => Stats.Compute(A<ConnectionInfo>._)).ReturnsLazily((ConnectionInfo info) =>
                new CharacterStatResult(new StatBlock { MaxHp = 1000 + info.ActiveBuffs.Count * 50,
                    MaxMp = 500, MagicPoint = info.CharacterHandle == 1 ? 100 : 10, AttackRange = 50 }, new StatBlock()));
            A.CallTo(() => Players.Registry).Returns(Registry);
            A.CallTo(() => Players.Observers(A<GameClient>._)).ReturnsLazily((GameClient subject) =>
                Registry.Clients.Where(c => !ReferenceEquals(c, subject) && Info(c).SpawnedPlayers.ContainsKey(Info(subject).CharacterHandle)).ToArray());
            A.CallTo(() => Players.SendToObservers(A<GameClient>._, A<byte[]>._, A<bool>._))
                .Invokes((GameClient subject, byte[] frame, bool self) =>
                { if (self) subject.Connection.Send(frame); foreach (var peer in Players.Observers(subject)) peer.Connection.Send(frame); });
            var world = new MonsterWorldState(A.Fake<IMonsterResourceRepository>(), Options.Create(new MonsterSpawnOptions()));
            Service = new SkillCastService(Catalog, Stats, States, world, Combat,
                A.Fake<IFieldPropCatalog>(), A.Fake<IWarpService>(), Players, runTicks: false, buffPersistence: persistence);
        }
        public GameClient Player(uint handle, float x = 0, long? party = 9, byte layer = 0)
        {
            var wire = new StorageTestHarness.FrameConnection(Array.Empty<byte>()); var client = StorageTestHarness.NewGameClient(wire);
            Wires[client] = wire; var info = Info(client); info.CharacterHandle = handle; info.CharacterName = "P" + handle;
            info.CharacterHp = 500; info.CharacterMp = 200; info.CharacterMaxHp = 1000;
            info.PartyId = party; info.Layer = layer; info.X = info.DestinationX = x;
            info.LearnedSkills[Skill] = 1;
            foreach (var peer in Registry.Clients) { Info(peer).SpawnedPlayers[handle] = handle; info.SpawnedPlayers[Info(peer).CharacterHandle] = Info(peer).CharacterHandle; }
            Registry.Register(handle, client); Service.Register(client); return client;
        }
        public SummonPresence Summon(GameClient master, uint handle, float x = 0)
        {
            var summon = new SummonPresence(handle, new SummonWorldEntry { CardHandle = handle + 1000,
                Hp = 50, MaxHp = 100, Mp = 20, MaxMp = 100, BaseStats = new StatBlock { MaxHp = 100, MaxMp = 100, Defence = 7 } }, x, 0, Info(master).Layer);
            Info(master).Summons = Info(master).Summons.Append(summon).ToArray(); return summon;
        }
        public void Cast(GameClient caster, uint target = 0) => Service.Cast(caster,
            new GameActionPackets.SkillRequest(Skill, Info(caster).CharacterHandle, target, 0, 0, 0, unchecked((sbyte)Info(caster).Layer), 1));
        public byte[] Fire(GameClient caster) => Wires[caster].Sent.Last(p => Packet(p) == GamePackets.TM_SC_SKILL && p[31] == 0);
        public static GamePackets Packet(byte[] frame) => (GamePackets)BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4));
    }

    [TestCase(21, true, true, false, false)]
    [TestCase(51, true, true, true, true)]
    [TestCase(45, true, false, true, false)]
    [TestCase(31, false, false, true, false)]
    [TestCase(32, false, false, true, true)]
    public void A_buff_selects_the_declared_players_and_summons(int target, bool casterBuff, bool memberBuff, bool ownBuff, bool memberSummonBuff)
    {
        var h = new Harness(Fields(target)); var caster = h.Player(1); var member = h.Player(2, 50);
        var stranger = h.Player(3, 20, party: 10); var far = h.Player(4, 121); var otherLayer = h.Player(5, layer: 1);
        var own = h.Summon(caster, 100, 30); var other = h.Summon(member, 101, 55);
        h.Cast(caster);
        Info(caster).ActiveBuffs.Any().Should().Be(casterBuff); Info(member).ActiveBuffs.Any().Should().Be(memberBuff);
        own.ActiveBuffs.Any().Should().Be(ownBuff); other.ActiveBuffs.Any().Should().Be(memberSummonBuff);
        Info(stranger).ActiveBuffs.Should().BeEmpty(); Info(far).ActiveBuffs.Should().BeEmpty(); Info(otherLayer).ActiveBuffs.Should().BeEmpty();
        if (ownBuff) own.Stats.MaxHp.Should().Be(150);
    }

    [Test]
    public void A_direct_buff_uses_the_ally_target_and_rejects_unseen_or_distant_handles_without_spending_mp()
    {
        var h = new Harness(Fields(1)); var caster = h.Player(1); var member = h.Player(2, 50);
        h.Cast(caster, 2); Info(caster).ActiveBuffs.Should().BeEmpty(); Info(member).ActiveBuffs.Should().ContainSingle();
        var mp = Info(caster).CharacterMp; h.Cast(caster, 9999); Info(caster).CharacterMp.Should().Be(mp);
        Info(member).X = Info(member).DestinationX = 400; h.Cast(caster, 2); Info(caster).CharacterMp.Should().Be(mp);
        Info(member).X = Info(member).DestinationX = 50; Info(caster).SpawnedPlayers.Clear(); h.Cast(caster, 2);
        Info(caster).CharacterMp.Should().Be(mp);
    }

    [Test]
    public void A_region_without_the_caster_and_creature_flags_are_respected()
    {
        var h = new Harness(Fields(3, 302) with { UseOnSummon = false }); var caster = h.Player(1); var member = h.Player(2, 50);
        var summon = h.Summon(member, 100, 50); h.Cast(caster);
        Info(caster).ActiveBuffs.Should().BeEmpty(); Info(member).ActiveBuffs.Should().ContainSingle(); summon.ActiveBuffs.Should().BeEmpty();
    }

    [TestCase(521)] [TestCase(508)]
    public void Region_heals_use_caster_magic_target_maxima_and_publish_all_hits(int effect)
    {
        var fields = Fields(4, effect); fields.Vars[0] = 1; fields.Vars[10] = 10; fields.Vars[11] = 2; fields.Vars[12] = 3;
        if (effect == 508) fields.Vars[5] = 1;
        var h = new Harness(fields); var caster = h.Player(1); var member = h.Player(2, 60);
        var full = h.Player(3, 80); Info(full).CharacterHp = 1000; var dead = h.Player(4, 90); Info(dead).CharacterHp = 0;
        var far = h.Player(5, 121); var stranger = h.Player(6, 30, party: 11); var otherLayer = h.Player(7, layer: 1);
        var summon = h.Summon(member, 100, 60); h.Cast(caster);
        Info(caster).CharacterHp.Should().Be(600); Info(member).CharacterHp.Should().Be(600);
        Info(full).CharacterHp.Should().Be(1000); Info(dead).CharacterHp.Should().Be(0);
        Info(far).CharacterHp.Should().Be(500); Info(stranger).CharacterHp.Should().Be(500); Info(otherLayer).CharacterHp.Should().Be(500);
        summon.Hp.Should().Be(100); summon.Mp.Should().Be(effect == 508 ? 100 : 20);
        Info(caster).CharacterMp.Should().Be(effect == 508 ? 290 : 190); // One MP cost for the entire area.
        var fire = h.Fire(caster); BinaryPrimitives.ReadUInt16LittleEndian(fire.AsSpan(55)).Should().Be(4);
        fire[48].Should().Be(1); BinaryPrimitives.ReadSingleLittleEndian(fire.AsSpan(49)).Should().Be(120);
        var hit = fire.AsSpan(57, 45);
        if (effect == 508) { hit[0].Should().Be(22); BinaryPrimitives.ReadInt32LittleEndian(hit.Slice(9)).Should().Be(290);
            BinaryPrimitives.ReadInt32LittleEndian(hit.Slice(17)).Should().Be(100); }
        h.Wires[member].Sent.Should().Contain(p => Harness.Packet(p) == GamePackets.TM_SC_SKILL && p[31] == 0);
    }

    [Test]
    public void Region_hp_mp_healing_can_target_only_summons_and_is_centered_on_the_selected_ally()
    {
        var fields = Fields(4, 508); fields.Vars[0] = 1; fields.Vars[10] = 5; fields.Vars[11] = 2; fields.Vars[12] = 2;
        var h = new Harness(fields); var caster = h.Player(1); var member = h.Player(2, 100); var own = h.Summon(caster, 100);
        var near = h.Summon(member, 101, 140); h.Cast(caster, 2);
        own.Hp.Should().Be(50); near.Hp.Should().Be(100); Info(caster).CharacterHp.Should().Be(500); Info(member).CharacterHp.Should().Be(500);
    }

    [Test]
    public void The_target_leaving_during_casting_cancels_the_support_effect()
    {
        var h = new Harness(Fields(1, delay: 1)); var caster = h.Player(1); var member = h.Player(2, 50);
        h.Cast(caster, 2); var pending = Info(caster).PendingCast;
        pending.Should().NotBeNull(); Info(member).Layer = 1; h.Service.ProcessCasts(pending.FireTick);
        Info(member).ActiveBuffs.Should().BeEmpty(); Info(caster).ActiveBuffs.Should().BeEmpty();
        h.Wires[caster].Sent.Should().Contain(p => Harness.Packet(p) == GamePackets.TM_SC_SKILL && p[31] == 3);
    }

    [Test]
    public void Summon_buffs_expire_recompute_stats_and_are_shown_to_late_observers()
    {
        var h = new Harness(Fields()); var caster = h.Player(1); var summon = h.Summon(caster, 100); h.Cast(caster);
        var state = summon.ActiveBuffs.Single(); CompanionFrames.States(Info(caster)).Should().Contain(p =>
            Harness.Packet(p) == GamePackets.TM_SC_STATE && FrameHandle(p) == 100);
        summon.Stats.MaxHp.Should().Be(150); h.Service.ProcessBuffs(state.EndTick);
        summon.ActiveBuffs.Should().BeEmpty(); summon.Stats.MaxHp.Should().Be(100);
    }

    [Test]
    public void A_party_aura_tracks_range_party_changes_summons_and_toggle_off()
    {
        var h = new Harness(Fields(51, 701)); var caster = h.Player(1); var member = h.Player(2, 50); var summon = h.Summon(member, 100, 60);
        h.Cast(caster); Info(member).ActiveBuffs.Should().ContainSingle(s => s.AuraProjection); summon.ActiveBuffs.Should().ContainSingle();
        Info(member).X = Info(member).DestinationX = 121; h.Service.ProcessBuffs(ServerClock.Now);
        Info(member).ActiveBuffs.Should().BeEmpty(); summon.ActiveBuffs.Should().ContainSingle();
        Info(member).X = Info(member).DestinationX = 50; h.Service.ProcessBuffs(ServerClock.Now);
        Info(member).ActiveBuffs.Should().ContainSingle(); Info(member).PartyId = 10; h.Service.ProcessBuffs(ServerClock.Now);
        Info(member).ActiveBuffs.Should().BeEmpty(); summon.ActiveBuffs.Should().BeEmpty();
        Info(member).PartyId = 9; h.Service.ProcessBuffs(ServerClock.Now); h.Cast(caster);
        Info(caster).ActiveAuras.Should().BeEmpty(); Info(member).ActiveBuffs.Should().BeEmpty(); summon.ActiveBuffs.Should().BeEmpty();
    }

    [Test]
    public void A_new_service_and_session_restore_saved_player_and_summon_buffs()
    {
        // The async database round-trip is below; this checks loading, icon replay and summon attachment.
        var persistence = A.Fake<IBuffPersistence>(); var utc = DateTime.UtcNow;
        A.CallTo(() => persistence.LoadAsync(1)).Returns(new[] {
            new CharacterStateEntity { StateId = 42, SkillId = Skill, StateLevel = 2, RemainingTicks = 6000, SavedAtUtc = utc },
            new CharacterStateEntity { SummonCardId = 1100, StateId = 42, SkillId = Skill, StateLevel = 2, RemainingTicks = 6000, SavedAtUtc = utc } });
        var h = new Harness(Fields(), persistence); var caster = h.Player(1);
        h.Service.RestoreBuffsAsync(caster).GetAwaiter().GetResult(); h.Service.SynchronizeBuffs(caster);
        Info(caster).ActiveBuffs.Should().ContainSingle(); Info(caster).StoredSummonBuffs.Should().ContainKey(1100);
        var world = new SummonWorldService(states: h.States);
        world.Enter(Info(caster), "test", caster.Connection, new SummonWorldEntry { CardHandle = 1100, Hp = 50, MaxHp = 100, Mp = 20, MaxMp = 100 });
        var summon = Info(caster).Summons.Single(); summon.ActiveBuffs.Should().ContainSingle(); summon.Stats.MaxHp.Should().Be(150);
        Info(caster).StoredSummonBuffs.Should().BeEmpty(); world.Leave(Info(caster), "test", caster.Connection, summon.Handle);
        Info(caster).StoredSummonBuffs.Should().ContainKey(1100);
    }

    [Test]
    public void An_item_support_skill_uses_the_same_targets_without_spending_mp_or_requiring_a_learned_skill()
    {
        var fields = Fields(4, 508); fields.Vars[0] = 1; fields.Vars[5] = 1;
        fields.Vars[10] = 10; fields.Vars[11] = 2; fields.Vars[12] = 3;
        var h = new Harness(fields); var caster = h.Player(1); var member = h.Player(2, 30);
        Info(caster).LearnedSkills.Clear();
        h.Service.CheckItemSkillTarget(caster, Skill, 9999).Should().Be(ResultCode.NotExist);
        h.Service.ApplyItemSkill(caster, Skill, 1, 2).Should().BeTrue();
        Info(member).CharacterHp.Should().Be(600); Info(member).CharacterMp.Should().Be(300);
        Info(caster).CharacterHp.Should().Be(600); Info(caster).CharacterMp.Should().Be(300);
        Info(caster).SkillCooldowns.Should().BeEmpty();
    }

    [Test]
    public async Task Cast_buffs_survive_a_database_round_trip_and_cancellation_replaces_the_saved_snapshot()
    {
        var options = new DbContextOptionsBuilder<TelecasterContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var store = new BuffPersistence(options, new CharacterGate());
        var first = new Harness(Fields(), store); var caster = first.Player(1); var member = first.Player(2, 50);
        first.Summon(caster, 100, 30); first.Cast(caster);
        await first.Service.SaveBuffsAsync(caster); await first.Service.SaveBuffsAsync(member);
        (await store.LoadAsync(1)).Should().HaveCount(2);
        var second = new Harness(Fields(), new BuffPersistence(options, new CharacterGate())); var returning = second.Player(1);
        await second.Service.RestoreBuffsAsync(returning);
        Info(returning).ActiveBuffs.Should().ContainSingle(s => s.StateId == 42 && s.StateLevel == 2);
        Info(returning).StoredSummonBuffs[1100].Should().ContainSingle();
        second.Service.RemoveState(returning, 42).Should().BeTrue();
        await second.Service.SaveBuffsAsync(returning);
        (await store.LoadAsync(1)).Should().ContainSingle(s => s.SummonCardId == 1100);
        (await store.LoadAsync(2)).Should().ContainSingle(s => s.SummonCardId == 0);
    }

    [Test]
    public async Task An_active_aura_restores_its_toggle_group_but_borrowed_states_are_not_persisted()
    {
        var options = new DbContextOptionsBuilder<TelecasterContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var store = new BuffPersistence(options, new CharacterGate());
        var first = new Harness(Fields(51, 701), store); var caster = first.Player(1); var member = first.Player(2, 30);
        first.Cast(caster); await first.Service.SaveBuffsAsync(caster); await first.Service.SaveBuffsAsync(member);
        (await store.LoadAsync(1)).Should().ContainSingle(s => s.Infinite); (await store.LoadAsync(2)).Should().BeEmpty();
        first.Service.Unregister(caster); Info(member).ActiveBuffs.Should().BeEmpty();
        var second = new Harness(Fields(51, 701), store); var returning = second.Player(1); var ally = second.Player(2, 30);
        await second.Service.RestoreBuffsAsync(returning); Info(returning).ActiveAuras[3].Should().Be(Skill);
        second.Service.ProcessBuffs(ServerClock.Now); Info(ally).ActiveBuffs.Should().ContainSingle();
        second.Cast(returning); Info(returning).ActiveAuras.Should().BeEmpty(); Info(ally).ActiveBuffs.Should().BeEmpty();
    }

    [Test]
    public void A_region_heal_excludes_hostile_players_and_their_summons()
    {
        var fields = Fields(4, 521) with { UseOnNeutral = true }; fields.Vars[0] = 1; fields.Vars[10] = 10; fields.Vars[11] = 1;
        var h = new Harness(fields); var caster = h.Player(1); var enemy = h.Player(2, 30, party: 10); var summon = h.Summon(enemy, 100, 30);
        A.CallTo(() => h.Combat.ArePlayerEnemies(caster, enemy)).Returns(true);
        h.Cast(caster); Info(caster).CharacterHp.Should().Be(600); Info(enemy).CharacterHp.Should().Be(500); summon.Hp.Should().Be(50);
    }

    [Test]
    public async Task A_summon_only_aura_never_buffs_the_player_and_restores_its_activation()
    {
        var options = new DbContextOptionsBuilder<TelecasterContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var store = new BuffPersistence(options, new CharacterGate());
        var fields = Fields(31, 701) with { UseOnCharacter = false };
        var first = new Harness(fields, store); var caster = first.Player(1); var summon = first.Summon(caster, 100);
        first.Cast(caster); Info(caster).ActiveBuffs.Should().BeEmpty(); summon.ActiveBuffs.Should().ContainSingle();
        await first.Service.SaveBuffsAsync(caster); (await store.LoadAsync(1)).Should().ContainSingle(s => s.StateId == 0 && s.Infinite);
        var second = new Harness(fields, store); var returning = second.Player(1); var restoredSummon = second.Summon(returning, 100);
        await second.Service.RestoreBuffsAsync(returning); second.Service.ProcessBuffs(ServerClock.Now);
        Info(returning).ActiveBuffs.Should().BeEmpty(); Info(returning).ActiveAuras[3].Should().Be(Skill);
        restoredSummon.ActiveBuffs.Should().ContainSingle();
    }

    [Test]
    public void Expired_offline_states_and_states_marked_for_logout_are_not_restored()
    {
        var catalog = A.Fake<IStateCatalog>(); A.CallTo(() => catalog.Exists(42)).Returns(true);
        var utc = DateTime.UtcNow; var row = new CharacterStateEntity { StateId = 42, StateLevel = 1, RemainingTicks = 100,
            SavedAtUtc = utc.AddSeconds(-2) };
        A.CallTo(() => catalog.GetRule(42)).Returns(new StateRule(42, Array.Empty<int>(), 0,
            StateTimeType.TimeDecreaseOnLogout, 0, Array.Empty<decimal>()));
        BuffPersistenceRules.Restore(row, catalog, 100, utc, 1).Should().BeNull();
        row.Infinite = true;
        A.CallTo(() => catalog.GetRule(42)).Returns(new StateRule(42, Array.Empty<int>(), 0,
            StateTimeType.EraseOnLogout, 0, Array.Empty<decimal>()));
        BuffPersistenceRules.Restore(row, catalog, 100, utc, 1).Should().BeNull();
    }

    [Test]
    public async Task Buff_snapshots_survive_new_database_contexts_and_replace_removed_states_without_touching_other_characters()
    {
        var options = new DbContextOptionsBuilder<TelecasterContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var gate = new CharacterGate(); var store = new BuffPersistence(options, gate);
        await store.SaveAsync(1, new[] { new CharacterStateEntity { StateId = 42, StateLevel = 2, RemainingTicks = 1000, SavedAtUtc = DateTime.UtcNow } });
        await store.SaveAsync(2, new[] { new CharacterStateEntity { StateId = 43, StateLevel = 2, RemainingTicks = 1000, SavedAtUtc = DateTime.UtcNow } });
        (await new BuffPersistence(options, gate).LoadAsync(1)).Should().ContainSingle(s => s.StateId == 42);
        await store.SaveAsync(1, Array.Empty<CharacterStateEntity>());
        (await store.LoadAsync(1)).Should().BeEmpty(); (await store.LoadAsync(2)).Should().ContainSingle();
    }

    [Test]
    public async Task A_reconnection_waits_for_the_logout_snapshot_save_before_loading_states()
    {
        var pause = new BlockingSave(); var gate = new CharacterGate();
        var options = new DbContextOptionsBuilder<TelecasterContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(pause).Options;
        var store = new BuffPersistence(options, gate);
        var save = store.SaveAsync(1, new[] { new CharacterStateEntity { StateId = 42, StateLevel = 2, RemainingTicks = 1000,
            SavedAtUtc = DateTime.UtcNow } });
        await pause.Entered.Task;
        var load = new BuffPersistence(options, gate).LoadAsync(1);
        try { load.IsCompleted.Should().BeFalse("the logout snapshot is still being committed"); }
        finally { pause.Release.TrySetResult(true); }
        await save;
        (await load).Should().ContainSingle(s => s.StateId == 42);
    }

    [TestCase(StateTimeType.TimeDecreaseOnLogout, 9000)] [TestCase((StateTimeType)0, 10000)]
    public void Offline_time_is_deducted_only_when_the_resource_requests_it(StateTimeType flags, long expected)
    {
        var catalog = A.Fake<IStateCatalog>(); A.CallTo(() => catalog.Exists(42)).Returns(true);
        A.CallTo(() => catalog.GetRule(42)).Returns(new StateRule(42, Array.Empty<int>(), 0, flags, 0, Array.Empty<decimal>()));
        var utc = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
        var row = BuffPersistenceRules.Capture(new ActiveBuff(1, 42, 1, 2, 0, 10100), 0, catalog, 100, utc);
        var restored = BuffPersistenceRules.Restore(row, catalog, 200, utc.AddSeconds(10), 1);
        restored!.Value.EndTick.Should().Be((uint)(200 + expected));
    }

    [Test]
    public void Logout_expired_unknown_and_projected_states_do_not_reappear_and_tick_wrap_is_supported()
    {
        var catalog = A.Fake<IStateCatalog>(); A.CallTo(() => catalog.Exists(42)).Returns(true);
        var rule = new StateRule(42, Array.Empty<int>(), 0, StateTimeType.EraseOnLogout, 0, Array.Empty<decimal>());
        A.CallTo(() => catalog.GetRule(42)).Returns(rule); var utc = DateTime.UtcNow;
        var active = new ActiveBuff(1, 42, 1, 1, 0, 200);
        BuffPersistenceRules.Capture(active, 0, catalog, 100, utc).Should().BeNull();
        A.CallTo(() => catalog.GetRule(42)).Returns(StateRule.None);
        BuffPersistenceRules.Capture(active with { AuraProjection = true }, 0, catalog, 100, utc).Should().BeNull();
        BuffPersistenceRules.Capture(active, 0, catalog, 201, utc).Should().BeNull();
        var row = BuffPersistenceRules.Capture(active with { EndTick = 30 }, 0, catalog, uint.MaxValue - 10, utc);
        row.RemainingTicks.Should().Be(41);
        row.StateId = 99; BuffPersistenceRules.Restore(row, catalog, 100, utc, 1).Should().BeNull();
        row.StateId = 42; row.Infinite = true;
        BuffPersistenceRules.Restore(row, catalog, 100, utc, 1)!.Value.EndTick.Should().Be(uint.MaxValue);
    }
}
