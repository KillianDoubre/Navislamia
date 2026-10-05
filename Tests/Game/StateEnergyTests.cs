using System.Buffers.Binary;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

[TestFixture]
public class StateEnergyTests
{
    [Test]
    public void State_result_has_the_36_bytes_and_offsets_read_by_SFrame()
    {
        var p = GameStateResultPackets.StateResult(0x12345678, 0x87654321, 4001, 258,
            StateResultType.DamageHp, 123, 456, true, 789);
        p.Length.Should().Be(36);
        BinaryPrimitives.ReadUInt32LittleEndian(p).Should().Be(36);
        BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4)).Should().Be(406);
        p[6].Should().Be(StorageTestHarness.Checksum(p));
        BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(7)).Should().Be(0x12345678);
        BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(11)).Should().Be(0x87654321);
        BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(15)).Should().Be(4001);
        BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(19)).Should().Be(258);
        BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(21)).Should().Be(1);
        BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(23)).Should().Be(123);
        BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(27)).Should().Be(456);
        p[31].Should().Be(1);
        BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(32)).Should().Be(789);
    }

    [TestCase(StateResultType.DamageMp, 2)]
    [TestCase(StateResultType.DamageSp, 3)]
    [TestCase(StateResultType.HealHp, 4)]
    [TestCase(StateResultType.HealMp, 5)]
    [TestCase(StateResultType.HealSp, 6)]
    public void State_result_kind_is_a_short_and_final_is_one_byte(StateResultType type, int expected)
    {
        var p = GameStateResultPackets.StateResult(1, 2, 3, 4, type, 5, 6, false, 7);
        BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(21)).Should().Be((ushort)expected);
        p[31].Should().Be(0);
    }

    [Test]
    public void Energy_and_cant_attack_match_the_client_layouts()
    {
        var e = GameStateResultPackets.Energy(0x81234567, 10);
        e.Length.Should().Be(13);
        BinaryPrimitives.ReadUInt32LittleEndian(e).Should().Be(13);
        BinaryPrimitives.ReadUInt16LittleEndian(e.AsSpan(4)).Should().Be(515);
        e[6].Should().Be(StorageTestHarness.Checksum(e));
        BinaryPrimitives.ReadUInt32LittleEndian(e.AsSpan(7)).Should().Be(0x81234567);
        BinaryPrimitives.ReadInt16LittleEndian(e.AsSpan(11)).Should().Be(10);
        var c = GameStateResultPackets.CantAttack(1, 2, ResultCode.NotOwn);
        c.Length.Should().Be(19);
        BinaryPrimitives.ReadUInt32LittleEndian(c).Should().Be(19);
        BinaryPrimitives.ReadUInt16LittleEndian(c.AsSpan(4)).Should().Be(102);
        c[6].Should().Be(StorageTestHarness.Checksum(c));
        BinaryPrimitives.ReadUInt32LittleEndian(c.AsSpan(7)).Should().Be(1);
        BinaryPrimitives.ReadUInt32LittleEndian(c.AsSpan(11)).Should().Be(2);
        BinaryPrimitives.ReadInt32LittleEndian(c.AsSpan(15)).Should().Be((int)ResultCode.NotOwn);
    }

    [TestCase(ResultCode.NotOwn, 3)]
    [TestCase(ResultCode.NotExist, 1)]
    [TestCase(ResultCode.NotActable, 5)]
    [TestCase(ResultCode.NotEnoughBullet, 32)]
    public void The_cant_attack_frame_keeps_the_same_19_bytes_for_every_refusal_code(ResultCode reason, int code)
    {
        var c = GameStateResultPackets.CantAttack(0x11223344, 0x55667788, reason);
        c.Length.Should().Be(19);
        BinaryPrimitives.ReadUInt32LittleEndian(c).Should().Be(19);
        BinaryPrimitives.ReadUInt16LittleEndian(c.AsSpan(4)).Should().Be(102);
        c[6].Should().Be(StorageTestHarness.Checksum(c));
        BinaryPrimitives.ReadUInt32LittleEndian(c.AsSpan(7)).Should().Be(0x11223344);
        BinaryPrimitives.ReadUInt32LittleEndian(c.AsSpan(11)).Should().Be(0x55667788);
        BinaryPrimitives.ReadInt32LittleEndian(c.AsSpan(15)).Should().Be(code,
            "reason is a signed 32-bit code at @15, not a short");
    }

    [Test]
    public void Energy_capacity_is_passive_1082_capped_at_ten()
    {
        var info = new ConnectionInfo();
        EnergyCounter.Capacity(info).Should().Be(0);
        info.LearnedSkills[1082] = 5;
        EnergyCounter.Capacity(info).Should().Be(5);
        info.LearnedSkills[1082] = 20;
        EnergyCounter.Capacity(info).Should().Be(10);
        info.Energy.Add(12, 5, 100).Should().Be(5);
        info.Energy.Add(1, 5, 100).Should().Be(0);
        info.Energy.Consume(6).Should().BeFalse();
        info.Energy.Count.Should().Be(5);
        info.Energy.Consume(2).Should().BeTrue();
        info.Energy.Count.Should().Be(3);
        info.ClearCharacterSession();
        info.Energy.Count.Should().Be(0);
    }

    [TestCase(100u)]
    [TestCase(4294967200u)]
    public void Energy_consumes_oldest_and_expires_each_unit_strictly_after_its_own_deadline(uint start)
    {
        var e = new EnergyCounter();
        e.Add(2, 5, start); e.Add(1, 5, unchecked(start + 100));
        e.Consume(1);
        e.Expire(unchecked(start + EnergyCounter.UpkeepTicks)).Should().BeFalse();
        e.Expire(unchecked(start + EnergyCounter.UpkeepTicks + 1)).Should().BeTrue();
        e.Count.Should().Be(1);
        e.Expire(unchecked(start + EnergyCounter.UpkeepTicks + 101)).Should().BeTrue();
        e.Count.Should().Be(0);
    }

    [TestCase(1, 75, 0)]
    [TestCase(2, 75, 0)]
    [TestCase(3, 75, 0)]
    [TestCase(4, 75, 0)]
    [TestCase(11, 75, 0)]
    [TestCase(12, 0, 75)]
    [TestCase(21, 25, 0)]
    [TestCase(22, 0, 25)]
    [TestCase(24, 25, 25)]
    public void Periodic_flat_amounts_use_the_snapshot_only_for_the_official_base_effects(int effect, int hp, int mp)
    {
        var r = new PeriodicStateRule(42, effect, 2, 0, .2m, .05m, 5, 10, new decimal[20]);
        r.Amounts(2, 100, 1000, 2000).Should().Be((hp: effect is 1 or 2 or 3 or 4 or 11 ? 55 : hp,
            mp: effect == 12 ? 55 : mp));
        r.Interval.Should().Be(200);
    }

    [Test]
    public void Percentage_damage_and_heal_use_different_mp_columns()
    {
        var values = new decimal[] { .1m, .02m, .3m, .04m, .05m };
        var rule = new PeriodicStateRule(42, 6, 0, 0, 0, 0, 0, 0, values);
        rule.Amounts(2, 0, 1000, 2000).Should().Be((140, 760));
        (rule with { BaseEffect = 25 }).Amounts(2, 0, 1000, 2000).Should().Be((140, 280));
        rule.Interval.Should().Be(0, "zero interval fires once on each creature update");
    }

    [Test]
    public void Periodic_snapshot_and_next_fire_survive_a_logout_without_replaying_offline_ticks()
    {
        var catalog = A.Fake<IStateCatalog>();
        A.CallTo(() => catalog.Exists(42)).Returns(true);
        A.CallTo(() => catalog.Periodic(42)).Returns(new PeriodicStateRule(42, 11, 2, 0, 0, 0, 0, 0, new decimal[20]));
        var buff = new ActiveBuff(1, 42, 7001, 1, 100, 1100, Pulse: new StatePulse(100, 777));
        var utc = DateTime.UtcNow;
        var row = BuffPersistenceRules.Capture(buff, 0, catalog, 150, utc);
        row.PeriodicBaseDamage.Should().Be(777);
        row.RemainingFireTicks.Should().Be(150);
        var restored = BuffPersistenceRules.Restore(row, catalog, 10000, utc.AddMinutes(1), 2)!.Value;
        restored.Pulse.BaseDamage.Should().Be(777);
        (restored.Pulse.LastFire + 200).Should().Be(10150);
        restored.EndTick.Should().Be(10950);
    }
}
