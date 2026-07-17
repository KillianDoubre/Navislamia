using System;
using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

[TestFixture]
public class GameSkillPacketsTests
{
    private static byte Checksum(byte[] packet)
    {
        byte c = 0;
        for (var i = 0; i < 6; i++) c += packet[i];
        return c;
    }

    [Test]
    public void BuildSkill_IsFiftySevenBytesWithTheEpic73Int32Costs()
    {
        var packet = GameSkillPackets.BuildSkill(2622, 3, 0x1001, 0x1001, 1f, 2f, 3f, 0,
            SkillPacketType.Fire, 0, 42, 500, 300);

        packet.Should().HaveCount(57, "41 fixed bytes plus the 9-byte union, on top of the 7-byte header");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(57);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_SC_SKILL);
        packet[6].Should().Be(Checksum(packet));
    }

    [Test]
    public void BuildSkill_WritesEveryFieldAtItsEpic73Offset()
    {
        var packet = GameSkillPackets.BuildSkill(2622, 3, 0x11223344, 0x55667788, 1.5f, 2.5f, 3.5f, 7,
            SkillPacketType.Fire, 11, 42, 500, 300);
        var s = packet.AsSpan();

        BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(7, 2)).Should().Be(2622);
        s[9].Should().Be(3);
        BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(10, 4)).Should().Be(0x11223344);
        BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(14, 4)).Should().Be(0x55667788);
        BinaryPrimitives.ReadSingleLittleEndian(s.Slice(18, 4)).Should().Be(1.5f);
        BinaryPrimitives.ReadSingleLittleEndian(s.Slice(22, 4)).Should().Be(2.5f);
        BinaryPrimitives.ReadSingleLittleEndian(s.Slice(26, 4)).Should().Be(3.5f);
        s[30].Should().Be(7);
        s[31].Should().Be((byte)SkillPacketType.Fire);
        BinaryPrimitives.ReadInt32LittleEndian(s.Slice(32, 4)).Should().Be(11);
        BinaryPrimitives.ReadInt32LittleEndian(s.Slice(36, 4)).Should().Be(42);
        BinaryPrimitives.ReadInt32LittleEndian(s.Slice(40, 4)).Should().Be(500);
        BinaryPrimitives.ReadInt32LittleEndian(s.Slice(44, 4)).Should().Be(300);
    }

    [Test]
    public void BuildSkill_LeavesTheFireHeaderZeroSoABuffFiresWithNoHits()
    {
        var packet = GameSkillPackets.BuildSkill(1, 1, 2, 2, 0f, 0f, 0f, 0, SkillPacketType.Fire,
            0, 0, 1, 1);

        packet.AsSpan(48, 9).ToArray().Should().OnlyContain(b => b == 0,
            "a state-applying skill produces no hit records; the buff travels in TS_SC_STATE");
    }

    [Test]
    public void BuildSkill_WritesTheCastDelayAndErrorCodeIntoTheUnionForCasting()
    {
        var packet = GameSkillPackets.BuildSkill(1, 1, 2, 2, 0f, 0f, 0f, 0, SkillPacketType.Casting,
            0, 42, 1, 1, castDelayTicks: 30, errorCode: 21);

        packet.Should().HaveCount(57);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(48, 4)).Should().Be(30);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(52, 2)).Should().Be(21);
    }

    [Test]
    public void BuildAura_IsFourteenBytes()
    {
        var packet = GameSkillPackets.BuildAura(0x11223344, 1201, true);
        var s = packet.AsSpan();

        packet.Should().HaveCount(14);
        BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(0, 4)).Should().Be(14);
        BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(4, 2)).Should().Be((ushort)GamePackets.TM_SC_AURA);
        packet[6].Should().Be(Checksum(packet));
        BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(7, 4)).Should().Be(0x11223344);
        BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(11, 2)).Should().Be(1201);
        packet[13].Should().Be(1);

        GameSkillPackets.BuildAura(1, 1201, false)[13].Should().Be(0);
    }

    [Test]
    public void BuildSkill_WithAHitIsOneHundredAndTwoBytesWithA45ByteStride()
    {
        var hit = new SkillHit(SkillHitType.AddHp, 0x55667788, 480, 250);
        var packet = GameSkillPackets.BuildSkill(3202, 1, 0x1001, 0x1001, 0f, 0f, 0f, 0,
            SkillPacketType.Fire, 0, 35, 480, 300, hit: hit);
        var s = packet.AsSpan();

        packet.Should().HaveCount(102, "48 fixed + the 9-byte FIRE header + one 45-byte hit stride");
        BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(0, 4)).Should().Be(102);

        // FIRE header: bMultiple @48, range @49, target_count @53, fire_count @54, hits @55 = 9 bytes.
        s[48].Should().Be(0, "a single-target heal is not multiple");
        BinaryPrimitives.ReadSingleLittleEndian(s.Slice(49, 4)).Should()
            .Be(0f, "range must stay intact: target_count and hits sit after it, not inside it");
        s[53].Should().Be(1, "target_count");
        s[54].Should().Be(1, "fire_count");
        BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(55, 2)).Should().Be(1, "one hit");
    }

    [Test]
    public void BuildSkill_WritesTheHitRecordAndLeavesTheRestOfTheStrideZero()
    {
        var hit = new SkillHit(SkillHitType.AddHp, 0x55667788, 480, 250);
        var packet = GameSkillPackets.BuildSkill(3202, 1, 0x1001, 0x1001, 0f, 0f, 0f, 0,
            SkillPacketType.Fire, 0, 35, 480, 300, hit: hit);
        var record = packet.AsSpan(57, 45);

        record[0].Should().Be((byte)SkillHitType.AddHp, "SHT_ADD_HP is 20");
        BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(1, 4)).Should().Be(0x55667788);
        BinaryPrimitives.ReadInt32LittleEndian(record.Slice(5, 4)).Should().Be(480, "hp after the heal");
        BinaryPrimitives.ReadInt32LittleEndian(record.Slice(9, 4)).Should().Be(250, "amount healed");
        record.Slice(13).ToArray().Should().OnlyContain(b => b == 0,
            "rzu reads two int32 for SHT_ADD_HP; the rest of the stride is padding");
    }

    [Test]
    public void BuildSkill_WritesADamageHitAsHitDamageInfo()
    {
        var hit = new SkillHit(SkillHitType.Damage, 0x55667788, 120, 400);
        var packet = GameSkillPackets.BuildSkill(30001, 1, 0x1001, 0x55667788, 0f, 0f, 0f, 0,
            SkillPacketType.Fire, 0, 50, 500, 300, hit: hit);
        var record = packet.AsSpan(57, 45);

        packet.Should().HaveCount(102);
        record[0].Should().Be((byte)SkillHitType.Damage, "SHT_DAMAGE is 0");
        BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(1, 4)).Should().Be(0x55667788);
        BinaryPrimitives.ReadInt32LittleEndian(record.Slice(5, 4)).Should().Be(120, "target_hp after the hit");
        record[9].Should().Be(0, "damage_type is a single byte before damage, unlike HIT_ADD_STAT");
        BinaryPrimitives.ReadInt32LittleEndian(record.Slice(10, 4)).Should().Be(400, "damage");
    }

    [Test]
    public void BuildSkill_TagsAMagicHitAsMagicDamage()
    {
        var hit = new SkillHit(SkillHitType.MagicDamage, 1, 10, 20);
        var packet = GameSkillPackets.BuildSkill(231, 1, 2, 1, 0f, 0f, 0f, 0, SkillPacketType.Fire,
            0, 0, 1, 1, hit: hit);

        packet.AsSpan(57, 45)[0].Should().Be((byte)SkillHitType.MagicDamage, "SHT_MAGIC_DAMAGE is 1");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(57 + 10, 4)).Should().Be(20);
    }

    [Test]
    public void BuildState_IsSixtyThreeBytesWithStateLevelAfterStateCode()
    {
        var packet = GameSkillPackets.BuildState(0x11223344, 7, 2622, 12, 5000, 3000, 99);
        var s = packet.AsSpan();

        packet.Should().HaveCount(63);
        BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(0, 4)).Should().Be(63);
        BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(4, 2)).Should().Be((ushort)GamePackets.TM_SC_STATE);
        packet[6].Should().Be(Checksum(packet));

        BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(7, 4)).Should().Be(0x11223344);
        BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(11, 2)).Should().Be(7);
        BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(13, 4)).Should()
            .Be(2622, "state_code precedes state_level below Epic 9.5.2");
        BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(17, 2)).Should().Be(12);
        BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(19, 4)).Should().Be(5000);
        BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(23, 4)).Should().Be(3000);
        BinaryPrimitives.ReadInt32LittleEndian(s.Slice(27, 4)).Should().Be(99);
        s.Slice(31, 32).ToArray().Should().OnlyContain(b => b == 0, "state_string_value is unused");
    }

    [Test]
    public void BuildStateRemoval_ZeroesTheLevelAndBothTimes()
    {
        var packet = GameSkillPackets.BuildStateRemoval(0x11223344, 7, 2622);
        var s = packet.AsSpan();

        BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(13, 4)).Should().Be(2622);
        BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(17, 2)).Should().Be(0);
        BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(19, 4)).Should().Be(0);
        BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(23, 4)).Should().Be(0);
    }

    [Test]
    public void TryReadSkill_ParsesTheThirtyOneByteRequest()
    {
        var packet = new byte[31];
        var s = packet.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(7, 2), 2622);
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(9, 4), 0x11223344);
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(13, 4), 0x55667788);
        BinaryPrimitives.WriteSingleLittleEndian(s.Slice(17, 4), 1.5f);
        BinaryPrimitives.WriteSingleLittleEndian(s.Slice(21, 4), 2.5f);
        BinaryPrimitives.WriteSingleLittleEndian(s.Slice(25, 4), 3.5f);
        packet[29] = 4;
        packet[30] = 6;

        GameActionPackets.TryReadSkill(packet, out var request).Should().BeTrue();
        request.SkillId.Should().Be(2622);
        request.Caster.Should().Be(0x11223344);
        request.Target.Should().Be(0x55667788);
        request.X.Should().Be(1.5f);
        request.Y.Should().Be(2.5f);
        request.Z.Should().Be(3.5f);
        request.Layer.Should().Be(4);
        request.SkillLevel.Should().Be(6);
    }

    [Test]
    public void TryReadSkill_RefusesAShortPacket()
    {
        GameActionPackets.TryReadSkill(new byte[30], out _).Should().BeFalse();
    }

    [Test]
    public void BuildSkillList_WritesTheCooldownIntoTheReservedFields()
    {
        var packet = GameCharacterPackets.BuildSkillList(0x1001,
            new[] { new SkillListEntry(2622, 3, 10800, 5000) });

        packet.Should().HaveCount(7 + 7 + 14);
        var s = packet.AsSpan(7);
        BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(0, 4)).Should().Be(0x1001);
        BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(4, 2)).Should().Be(1);
        BinaryPrimitives.ReadInt32LittleEndian(s.Slice(7, 4)).Should().Be(2622);
        s[11].Should().Be(3);
        s[12].Should().Be(3);
        BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(13, 4)).Should().Be(10800);
        BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(17, 4)).Should().Be(5000);
    }
}
