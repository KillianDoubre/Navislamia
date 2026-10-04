using System.Buffers.Binary;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Casting;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

public partial class CastMechanicsTests
{
    private static ushort PacketId(byte[] p) => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4));
    private static IStateCatalog PeriodicCatalog(int effect, int amount = 10, decimal amplify = 0)
    {
        var states = A.Fake<IStateCatalog>();
        A.CallTo(() => states.Exists(42)).Returns(true);
        A.CallTo(() => states.GetRule(A<int>._)).Returns(new StateRule(42, Array.Empty<int>(), 0, 0, 0, new decimal[20]));
        A.CallTo(() => states.Periodic(42)).Returns(new PeriodicStateRule(42, effect, 1, 0, amplify, 0, amount, 0, new decimal[20]));
        return states;
    }

    [Test]
    public void Energy_is_checked_before_cast_and_spent_on_first_fire_but_not_cancel()
    {
        var h = Build(Attack() with { CostEnergy = 1, CostEnergyPerSkl = 1 });
        h.Service.Cast(h.Client, Request());
        ErrorOf(h.Connection.Sent.Single()).Should().Be((ushort)ResultCode.NotEnoughEnergy);
        h.Info.PendingCast.Should().BeNull(); h.Info.SkillCooldowns.Should().BeEmpty();
        h.Info.Energy.Add(3, 5, ServerClock.Now); h.Connection.Sent.Clear();
        h.Service.Cast(h.Client, Request());
        h.Info.Energy.Count.Should().Be(3);
        h.Service.CancelCast(h.Client);
        h.Info.Energy.Count.Should().Be(3);
        h.Service.Cast(h.Client, Request());
        h.Service.ProcessCasts(unchecked(ServerClock.Now + 200));
        h.Info.Energy.Count.Should().Be(1);
        var packet = h.Connection.Sent.Single(p => PacketId(p) == 515);
        BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(11)).Should().Be(1);
    }

    [Test]
    public void Energy_expiring_during_cast_refuses_fire_without_damage()
    {
        var h = Build(Attack() with { CostEnergy = 1 });
        h.Info.Energy.Add(1, 5, ServerClock.Now);
        h.Service.Cast(h.Client, Request());
        h.Info.Energy.Consume(1);
        h.Service.ProcessCasts(unchecked(ServerClock.Now + 200));
        ErrorOf(h.Connection.Sent.Last()).Should().Be((ushort)ResultCode.NotEnoughEnergy);
        A.CallTo(() => h.Combat.ApplyDamage(A<GameClient>._, A<long>._, A<uint>._, A<int>._, A<int>._)).MustNotHaveHappened();
    }

    [TestCase(125, 6)]
    [TestCase(30003, 4)]
    public void Producer_skills_add_energy_even_when_the_attack_misses(int effect, int countIndex)
    {
        var vars = new decimal[20]; vars[countIndex] = 1; vars[countIndex + 1] = 1;
        var h = Build(Attack(0) with { EffectType = effect, Vars = vars });
        h.Info.LearnedSkills[1082] = 5;
        h.Service.Cast(h.Client, Request());
        h.Info.Energy.Count.Should().Be(2);
        h.Connection.Sent.Should().Contain(p => PacketId(p) == 515);
        Steps(h).Should().Equal(SkillPacketType.Casting, SkillPacketType.Fire, SkillPacketType.Complete);
    }

    [Test]
    public void The_add_energy_skill_adds_the_requested_level_up_to_capacity()
    {
        var h = Build(Attack(0) with { Kind = SkillCastKind.Energy, EffectType = 0, IsHarmful = false,
            RequiredTarget = 0, CastRange = 0 });
        h.Info.LearnedSkills[1082] = 1;
        h.Service.Cast(h.Client, Request() with { Target = 1 });
        h.Info.Energy.Count.Should().Be(1);
    }

    [Test]
    public void Tick_is_strict_at_the_interval_and_final_is_set_on_the_last_scheduled_fire()
    {
        var h = Build(Attack(), states: PeriodicCatalog(21));
        h.Service.ApplyState(h.Client, 42, 1, 200);
        var start = h.Info.ActiveBuffs.Single().StartTick;
        h.Connection.Sent.Clear();
        h.Service.ProcessPeriodicStates(start + 100);
        h.Info.CharacterHp.Should().Be(500);
        h.Service.ProcessPeriodicStates(start + 101);
        h.Info.CharacterHp.Should().Be(510);
        var first = h.Connection.Sent.Single(p => PacketId(p) == 406);
        first[31].Should().Be(0);
        BinaryPrimitives.ReadInt32LittleEndian(first.AsSpan(32)).Should().Be(10);
        h.Service.ProcessPeriodicStates(start + 202);
        var last = h.Connection.Sent.Last(p => PacketId(p) == 406);
        last[31].Should().Be(1);
        BinaryPrimitives.ReadInt32LittleEndian(last.AsSpan(32)).Should().Be(20);
        h.Service.ProcessPeriodicStates(start + 500);
        h.Connection.Sent.Count(p => PacketId(p) == 406).Should().Be(2);
    }

    [Test]
    public void Both_item_heals_cap_to_actual_hp_mp_and_do_not_revive_a_dead_player()
    {
        var h = Build(Attack(), states: PeriodicCatalog(24, 100), total: new StatBlock { MaxHp = 1000, MaxMp = 1000 });
        h.Info.CharacterHp = 950; h.Info.CharacterMp = 980;
        h.Service.ApplyState(h.Client, 42, 1, 1000);
        var start = h.Info.ActiveBuffs.Single().StartTick; h.Connection.Sent.Clear();
        h.Service.ProcessPeriodicStates(start + 101);
        var pulses = h.Connection.Sent.Where(p => PacketId(p) == 406).ToArray();
        pulses.Should().HaveCount(2);
        BinaryPrimitives.ReadUInt16LittleEndian(pulses[0].AsSpan(21)).Should().Be(4);
        BinaryPrimitives.ReadInt32LittleEndian(pulses[0].AsSpan(23)).Should().Be(50);
        BinaryPrimitives.ReadUInt16LittleEndian(pulses[1].AsSpan(21)).Should().Be(5);
        BinaryPrimitives.ReadInt32LittleEndian(pulses[1].AsSpan(23)).Should().Be(20);
        BinaryPrimitives.ReadInt32LittleEndian(pulses[1].AsSpan(32)).Should().Be(0);
        h.Info.CharacterHp = 0; h.Connection.Sent.Clear();
        h.Service.ProcessPeriodicStates(start + 202);
        h.Info.CharacterHp.Should().Be(0); h.Connection.Sent.Should().BeEmpty();
    }

    [Test]
    public void Magic_regeneration_uses_power_at_application_and_not_at_each_tick()
    {
        var stats = new StatBlock { MaxHp = 1000, MagicPoint = 100 };
        var h = Build(Attack(), states: PeriodicCatalog(11, 10, .2m), total: stats);
        h.Service.ApplyState(h.Client, 42, 1, 1000);
        stats.MagicPoint = 1000;
        h.Service.ProcessPeriodicStates(h.Info.ActiveBuffs.Single().StartTick + 101);
        h.Info.CharacterHp.Should().Be(530);
    }

    [Test]
    public void Damage_ticks_enter_DamagePlayer_and_never_the_heal_path()
    {
        var h = Build(Attack(), states: PeriodicCatalog(2, 600), random: new Rolls(99));
        A.CallTo(() => h.Combat.DamagePlayer(h.Client, 600)).Returns(0);
        h.Service.ApplyState(h.Client, 42, 1, 1000);
        h.Service.ProcessPeriodicStates(h.Info.ActiveBuffs.Single().StartTick + 101);
        A.CallTo(() => h.Combat.DamagePlayer(h.Client, 600)).MustHaveHappenedOnceExactly();
        var p = h.Connection.Sent.Single(p => PacketId(p) == 406);
        BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(27)).Should().Be(0);
        BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(32)).Should().Be(600);
    }

    [Test]
    public void A_monster_poison_passes_its_kill_through_ApplyDamage_once()
    {
        var catalog = PeriodicCatalog(2, 100);
        var h = Build(Attack(), states: catalog, random: new Rolls(99));
        h.World.TryAddState(InstanceId, 42, 7001, 1, 100, 1000, catalog.GetRule(42), catalog.GetRule,
            out _, out _, h.Info.CharacterHandle, new StatePulse(100, 0)).Should().BeTrue();
        A.CallTo(() => h.Combat.ApplyDamage(h.Client, InstanceId, MonsterHandle, 100))
            .ReturnsLazily(() => { h.World.Kill(InstanceId, DateTime.UtcNow.AddMinutes(1)); return 0; });
        h.Service.ProcessPeriodicStates(201);
        h.Service.ProcessPeriodicStates(301);
        A.CallTo(() => h.Combat.ApplyDamage(h.Client, InstanceId, MonsterHandle, 100)).MustHaveHappenedOnceExactly();
        BinaryPrimitives.ReadInt32LittleEndian(h.Connection.Sent.Single(p => PacketId(p) == 406).AsSpan(27)).Should().Be(0);
    }
}
