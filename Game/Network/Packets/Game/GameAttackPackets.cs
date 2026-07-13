using System;
using System.Buffers.Binary;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

public static class GameAttackPackets
{
    public const byte ActionAttack = 3;
    public const byte ActionEndAttack = 1;

    private const int HeaderSize = 7;
    private const int EventHeaderSize = 15;
    private const int AttackInfoSize = 61;
    private const int AttackInfoOffset = HeaderSize + EventHeaderSize;
    private const int DamageOffset = 0;
    private const int TargetHpOffset = 37;
    private const int AttackerHpOffset = 53;
    private const byte AttackFlagNone = 0;

    public static uint ReadAttackTarget(ReadOnlySpan<byte> packet)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(11, 4));
    }

    public static byte[] BuildAttackEvent(uint attackerHandle, uint targetHandle, ushort attackSpeed,
        ushort attackDelay, byte action, int damage, int targetHp, int attackerHp)
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
        BinaryPrimitives.WriteInt32LittleEndian(info.Slice(TargetHpOffset, 4), targetHp);
        BinaryPrimitives.WriteInt32LittleEndian(info.Slice(AttackerHpOffset, 4), attackerHp);

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
