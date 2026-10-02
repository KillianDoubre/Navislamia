using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

public enum SkillPacketType : byte
{
    Fire = 0,
    Casting = 1,
    CastingUpdate = 2,
    Cancel = 3,
    RegionFire = 4,
    Complete = 5
}

public enum SkillHitType : byte
{
    Damage = 0,
    MagicDamage = 1,
    Result = 10,
    AddHp = 20,
    AddMp = 21
}

/// <summary>
/// One <c>TS_SC_SKILL__HIT_DETAILS</c>. <see cref="TargetStat"/> is the target's value after the effect
/// and <see cref="IncStat"/> the amount applied.
/// </summary>
/// <remarks>
/// The two payloads differ: <c>SHT_ADD_HP</c> carries <c>HIT_ADD_STAT</c> (two int32), while
/// <c>SHT_DAMAGE</c> carries <c>HIT_DAMAGE_INFO</c> (<c>target_hp</c>, <c>damage_type</c>, <c>damage</c>,
/// <c>flag</c>, then seven uint16 of elemental damage). Both fit the fixed 45-byte stride. <see cref="Flag"/>
/// is the damage hit's <c>flag</c> (int32 @14), with the <c>ATTACK_INFO__FLAG</c> values.
/// </remarks>
public readonly record struct SkillHit(SkillHitType Type, uint TargetHandle, int TargetStat, int IncStat,
    byte Flag = 0);

/// <summary>
/// <c>TS_SC_SKILL</c> (401) and <c>TS_SC_STATE</c> (505), Epic 7.3 layouts.
/// </summary>
/// <remarks>
/// At Epic 7.3 <c>hp_cost</c>, <c>mp_cost</c> and <c>caster_mp</c> are int32; the int16 variant the
/// reference emulator writes is the <c>&lt; EPIC_7_3</c> layout, the same version boundary as
/// <c>ATTACK_INFO</c>.
/// </remarks>
public static class GameSkillPackets
{
    private const int HeaderSize = 7;

    /// <summary>The union region after <c>type</c>: the FIRE header, or the cast fields, or padding.</summary>
    private const int UnionSize = 9;

    private const int SkillFixedSize = 41;

    /// <summary>
    /// Each hit occupies a fixed 45-byte stride, zero-filled then overwritten. rzu declares it as
    /// <c>_(pad)(45 * hits.size())</c> and the reference emulator implements it literally.
    /// </summary>
    private const int HitStride = 45;

    public static byte[] BuildSkill(ushort skillId, byte skillLevel, uint caster, uint target,
        float x, float y, float z, byte layer, SkillPacketType type, int hpCost, int mpCost,
        int casterHp, int casterMp, uint castDelayTicks = 0, ushort errorCode = 0, SkillHit? hit = null,
        IReadOnlyList<SkillHit> hits = null, bool multiple = false, float range = 0, byte fireCount = 1)
    {
        hits ??= hit is { } single ? new[] { single } : Array.Empty<SkillHit>();
        var hitCount = hits.Count;
        if (hitCount > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(hits));
        var total = HeaderSize + SkillFixedSize + UnionSize + hitCount * HitStride;
        var p = new byte[total];
        var s = p.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(0, 4), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(4, 2), (ushort)GamePackets.TM_SC_SKILL);

        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(7, 2), skillId);
        s[9] = skillLevel;
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(10, 4), caster);
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(14, 4), target);
        BinaryPrimitives.WriteSingleLittleEndian(s.Slice(18, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(s.Slice(22, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(s.Slice(26, 4), z);
        s[30] = layer;
        s[31] = (byte)type;
        BinaryPrimitives.WriteInt32LittleEndian(s.Slice(32, 4), hpCost);
        BinaryPrimitives.WriteInt32LittleEndian(s.Slice(36, 4), mpCost);
        BinaryPrimitives.WriteInt32LittleEndian(s.Slice(40, 4), casterHp);
        BinaryPrimitives.WriteInt32LittleEndian(s.Slice(44, 4), casterMp);

        if (type is SkillPacketType.Casting or SkillPacketType.CastingUpdate or SkillPacketType.Complete)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(48, 4), castDelayTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(52, 2), errorCode);
        }
        else if (type is SkillPacketType.Fire or SkillPacketType.RegionFire)
        {
            // FIRE: bMultiple @48, range @49, target_count @53, fire_count @54, hits @55.
            s[48] = multiple ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteSingleLittleEndian(s.Slice(49, 4), range);
            s[53] = (byte)Math.Min(byte.MaxValue, hits.Select(h => h.TargetHandle).Distinct().Count());
            s[54] = hitCount > 0 ? fireCount : (byte)0;
            BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(55, 2), (ushort)hitCount);

            for (var i = 0; i < hitCount; i++)
            {
                var entry = hits[i];
                var record = s.Slice(HeaderSize + SkillFixedSize + UnionSize + i * HitStride, HitStride);
                record[0] = (byte)entry.Type;
                BinaryPrimitives.WriteUInt32LittleEndian(record.Slice(1, 4), entry.TargetHandle);
                if (entry.Type is SkillHitType.Damage or SkillHitType.MagicDamage)
                {
                    // HIT_DAMAGE_INFO: target_hp, damage_type, damage, flag, elemental_damage[7].
                    BinaryPrimitives.WriteInt32LittleEndian(record.Slice(5, 4), entry.TargetStat);
                    record[9] = 0;
                    BinaryPrimitives.WriteInt32LittleEndian(record.Slice(10, 4), entry.IncStat);
                    BinaryPrimitives.WriteInt32LittleEndian(record.Slice(14, 4), entry.Flag);
                }
                else
                {
                    // HIT_ADD_STAT: target_stat, nIncStat.
                    BinaryPrimitives.WriteInt32LittleEndian(record.Slice(5, 4), entry.TargetStat);
                    BinaryPrimitives.WriteInt32LittleEndian(record.Slice(9, 4), entry.IncStat);
                }
            }
        }

        // States travel in TS_SC_STATE; an empty ground FIRE can still carry its radius.
        WriteChecksum(p);
        return p;
    }

    public static byte[] BuildAura(uint caster, ushort skillId, bool status)
    {
        const int payload = 4 + 2 + 1;
        var total = HeaderSize + payload;
        var p = new byte[total];
        var s = p.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(0, 4), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(4, 2), (ushort)GamePackets.TM_SC_AURA);

        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(7, 4), caster);
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(11, 2), skillId);
        s[13] = status ? (byte)1 : (byte)0;

        WriteChecksum(p);
        return p;
    }

    public static byte[] BuildState(uint handle, ushort stateHandle, uint stateCode, ushort stateLevel,
        uint endTick, uint startTick, int stateValue = 0)
    {
        const int payload = 4 + 2 + 4 + 2 + 4 + 4 + 4 + 32;
        var total = HeaderSize + payload;
        var p = new byte[total];
        var s = p.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(0, 4), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(4, 2), (ushort)GamePackets.TM_SC_STATE);

        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(7, 4), handle);
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(11, 2), stateHandle);
        // state_level sits AFTER state_code at this epic; rzu only moves it before from 9.5.2.
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(13, 4), stateCode);
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(17, 2), stateLevel);
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(19, 4), endTick);
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(23, 4), startTick);
        BinaryPrimitives.WriteInt32LittleEndian(s.Slice(27, 4), stateValue);

        WriteChecksum(p);
        return p;
    }

    public static byte[] BuildStateRemoval(uint handle, ushort stateHandle, uint stateCode)
    {
        return BuildState(handle, stateHandle, stateCode, 0, 0, 0);
    }

    private static void WriteChecksum(byte[] p)
    {
        byte c = 0;
        for (var i = 0; i < 6; i++) c += p[i];
        p[6] = c;
    }
}
