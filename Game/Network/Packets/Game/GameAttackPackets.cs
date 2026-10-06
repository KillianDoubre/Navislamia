using System;
using System.Buffers.Binary;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// One hit of a swing: its damage (elemental part included), its ATTACK_INFO__FLAG and the target HP and MP after.
/// </summary>
public readonly record struct AttackHit(int Damage, byte Flag, int TargetHp, int TargetMp, int[] ElementalDamage = null);

public static class GameAttackPackets
{
    public const byte ActionAttack = 3;
    public const byte ActionEndAttack = 1;

    private const int HeaderSize = 7;
    private const int EventHeaderSize = 15;
    private const int AttackInfoSize = 61;
    private const int AttackInfoOffset = HeaderSize + EventHeaderSize;
    private const int DamageOffset = 0;
    private const int FlagOffset = 8;
    private const int ElementalOffset = 9;
    private const int TargetHpOffset = 37;
    private const int TargetMpOffset = 41;
    private const int AttackerHpOffset = 53;
    private const int AttackerMpOffset = 57;
    private const byte AttackFlagNone = 0;

    public static uint ReadAttackTarget(ReadOnlySpan<byte> packet)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(11, 4));
    }

    /// <summary>
    /// One hit. The client sets both actors' HP and MP gauges from <c>target_hp/target_mp</c> and
    /// <c>attacker_hp/attacker_mp</c> (rzu <c>ATTACK_INFO</c>, int32 @37/@41 and @53/@57): an MP left at 0 empties the
    /// gauge of the player at each hit, given or taken, until the next regeneration fills it again.
    /// </summary>
    public static byte[] BuildAttackEvent(uint attackerHandle, uint targetHandle, ushort attackSpeed,
        ushort attackDelay, byte action, int damage, int targetHp, int targetMp, int attackerHp, int attackerMp,
        byte hitFlag = 0)
    {
        var total = HeaderSize + EventHeaderSize + AttackInfoSize;
        var packet = new byte[total];
        var p = packet.AsSpan();

        WriteHeader(p, total);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(7, 4), attackerHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(11, 4), targetHandle);
        BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(15, 2), attackSpeed);
        BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(17, 2), attackDelay);
        p[19] = action;
        p[20] = AttackFlagNone;
        p[21] = 1;

        var info = p.Slice(AttackInfoOffset);
        BinaryPrimitives.WriteInt32LittleEndian(info.Slice(DamageOffset, 4), damage);
        // ATTACK_INFO__FLAG after damage and mp_damage: 1 perfect block, 2 block, 4 miss, 8 critical.
        info[FlagOffset] = hitFlag;
        BinaryPrimitives.WriteInt32LittleEndian(info.Slice(TargetHpOffset, 4), targetHp);
        BinaryPrimitives.WriteInt32LittleEndian(info.Slice(TargetMpOffset, 4), targetMp);
        BinaryPrimitives.WriteInt32LittleEndian(info.Slice(AttackerHpOffset, 4), attackerHp);
        BinaryPrimitives.WriteInt32LittleEndian(info.Slice(AttackerMpOffset, 4), attackerMp);

        WriteChecksum(packet);
        return packet;
    }

    /// <summary>
    /// A swing of several hits (two weapons, double attack): <c>count</c> ATTACK_INFO of 61 bytes each, with the
    /// elemental damage array (<c>int32[7]</c> @9) and the <c>attack_flag</c> of the swing. An aiming bow sends
    /// no hit at all.
    /// </summary>
    public static byte[] BuildAttackEvent(uint attackerHandle, uint targetHandle, ushort attackSpeed,
        ushort attackDelay, byte action, byte attackFlag, System.Collections.Generic.IReadOnlyList<AttackHit> hits,
        int attackerHp, int attackerMp)
    {
        var count = hits?.Count ?? 0;
        var total = HeaderSize + EventHeaderSize + AttackInfoSize * count;
        var packet = new byte[total];
        var p = packet.AsSpan();

        WriteHeader(p, total);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(7, 4), attackerHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(11, 4), targetHandle);
        BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(15, 2), attackSpeed);
        BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(17, 2), attackDelay);
        p[19] = action;
        p[20] = attackFlag;
        p[21] = (byte)count;

        for (var i = 0; i < count; i++)
        {
            var hit = hits[i];
            var info = p.Slice(AttackInfoOffset + AttackInfoSize * i, AttackInfoSize);
            BinaryPrimitives.WriteInt32LittleEndian(info.Slice(DamageOffset, 4), hit.Damage);
            info[FlagOffset] = hit.Flag;
            if (hit.ElementalDamage is not null)
            {
                for (var e = 0; e < Math.Min(7, hit.ElementalDamage.Length); e++)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(info.Slice(ElementalOffset + 4 * e, 4), hit.ElementalDamage[e]);
                }
            }

            BinaryPrimitives.WriteInt32LittleEndian(info.Slice(TargetHpOffset, 4), hit.TargetHp);
            BinaryPrimitives.WriteInt32LittleEndian(info.Slice(TargetMpOffset, 4), hit.TargetMp);
            BinaryPrimitives.WriteInt32LittleEndian(info.Slice(AttackerHpOffset, 4), attackerHp);
            BinaryPrimitives.WriteInt32LittleEndian(info.Slice(AttackerMpOffset, 4), attackerMp);
        }

        WriteChecksum(packet);
        return packet;
    }

    public static byte[] BuildEndAttack(uint attackerHandle, uint targetHandle)
    {
        var total = HeaderSize + EventHeaderSize;
        var packet = new byte[total];
        var p = packet.AsSpan();

        WriteHeader(p, total);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(7, 4), attackerHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(11, 4), targetHandle);
        p[19] = ActionEndAttack;
        p[20] = AttackFlagNone;
        p[21] = 0;

        WriteChecksum(packet);
        return packet;
    }

    private static void WriteHeader(Span<byte> packet, int total)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(0, 4), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.Slice(4, 2), (ushort)GamePackets.TM_SC_ATTACK_EVENT);
    }

    private static void WriteChecksum(byte[] packet)
    {
        byte checksum = 0;
        for (var i = 0; i < 6; i++) checksum += packet[i];
        packet[6] = checksum;
    }
}
