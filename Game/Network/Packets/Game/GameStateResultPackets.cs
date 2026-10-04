using System;
using System.Buffers.Binary;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

public enum StateResultType : ushort { DamageHp = 1, DamageMp = 2, DamageSp = 3, HealHp = 4, HealMp = 5, HealSp = 6 }

/// <summary>Layouts measured in SFrame: 0x6718c0, 0x671380 and 0x66ed80.</summary>
public static class GameStateResultPackets
{
    public const int StateResultSize = 36;
    public const int EnergySize = 13;
    public const int CantAttackSize = 19;

    public static byte[] StateResult(uint caster, uint target, int code, ushort level, StateResultType type,
        int value, int targetValue, bool final, int total)
    {
        var p = Frame(GamePackets.TM_SC_STATE_RESULT, StateResultSize);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(7), caster);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(11), target);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(15), code);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(19), level);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(21), (ushort)type);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(23), value);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(27), targetValue);
        p[31] = final ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(32), total);
        return p;
    }

    public static byte[] Energy(uint handle, int energy)
    {
        var p = Frame(GamePackets.TM_SC_ENERGY, EnergySize);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(7), handle);
        BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(11), checked((short)energy));
        return p;
    }

    public static byte[] CantAttack(uint attacker, uint target, ResultCode reason)
    {
        var p = Frame(GamePackets.TM_SC_CANT_ATTACK, CantAttackSize);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(7), attacker);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(11), target);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(15), (int)reason);
        return p;
    }

    private static byte[] Frame(GamePackets id, int size)
    {
        var p = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(p, (uint)size);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4), (ushort)id);
        for (var i = 0; i < 6; i++) p[6] = unchecked((byte)(p[6] + p[i]));
        return p;
    }
}
