using System;
using System.Buffers.Binary;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// The two frames a monster's death puts on the wire for everyone around it
/// (docs/packet-specs/socle-recompenses-monstres.md §3.2, §3.5).
/// </summary>
/// <remarks>
/// Both are server to client only: the 7.3 client builds neither, so an incoming one is logged and dropped
/// in <c>GameClient.cs</c> instead of reaching the "Unknown Packet Type" throw.
/// </remarks>
public static class GameRewardPackets
{
    private const int HeaderSize = 7;

    /// <summary>
    /// <c>TM_SC_GET_CHAOS</c> (213), 25 bytes: <c>hPlayer</c> @7, <c>hCorpse</c> @11, <c>nChaos</c> @15,
    /// <c>nBonusType</c> @19, <c>nBonusPercent</c> @20, <c>nBonus</c> @21. Built by the official
    /// <c>SendGoldChaosUpdateMsg</c>'s neighbour <c>addChaos</c> path (<c>0x1400b6d08</c> writes the length
    /// <c>0x19</c> and the id <c>0xd5</c>) and broadcast to the region, so the two bonus fields are one byte
    /// each in 7.3 (<c>TS_SC_GET_CHAOS.h:13-18</c>).
    /// </summary>
    /// <param name="playerHandle">The handle of the character that gained the chaos.</param>
    /// <param name="corpseHandle">The handle of the killed monster, as the receiving client knows it.</param>
    /// <param name="chaos">The chaos gained, the only value of the frame that is not a bonus.</param>
    public static byte[] BuildGetChaos(uint playerHandle, uint corpseHandle, int chaos) =>
        BuildGetChaos(playerHandle, corpseHandle, chaos, 0, 0, 0);

    /// <inheritdoc cref="BuildGetChaos(uint, uint, int)"/>
    /// <remarks>
    /// The three bonus fields carry the PC-bang and game-time-limit bonuses, which this server does not
    /// have: the callers pass zeros, the value the official writes outside those modes.
    /// </remarks>
    public static byte[] BuildGetChaos(uint playerHandle, uint corpseHandle, int chaos, sbyte bonusType,
        sbyte bonusPercent, int bonus)
    {
        const int payload = 4 + 4 + 4 + 1 + 1 + 4;
        var total = HeaderSize + payload;
        var packet = new byte[total];
        var span = packet.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0, 4), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4, 2), (ushort)GamePackets.TM_SC_GET_CHAOS);

        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(7, 4), playerHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(11, 4), corpseHandle);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(15, 4), chaos);
        span[19] = unchecked((byte)bonusType);
        span[20] = unchecked((byte)bonusPercent);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(21, 4), bonus);

        WriteChecksum(packet);
        return packet;
    }

    /// <summary>
    /// <c>TM_SC_ITEM_DROP_INFO</c> (282), 15 bytes: <c>monster_handle</c> @7, <c>item_handle</c> @11. The
    /// official <c>MonsterDropItemToWorld</c> (<c>0x140043cc0</c>, length <c>0xf</c> at <c>0x140043cf5</c>,
    /// id <c>0x11a</c> at <c>0x140043cee</c>) sends it for everything a monster leaves on the ground — loot
    /// <b>and</b> gold — right before the object's own <c>ENTER</c>.
    /// </summary>
    public static byte[] BuildItemDropInfo(uint monsterHandle, uint itemHandle)
    {
        var packet = new byte[HeaderSize + 8];
        var span = packet.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4, 2), (ushort)GamePackets.TM_SC_ITEM_DROP_INFO);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(7, 4), monsterHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(11, 4), itemHandle);

        WriteChecksum(packet);
        return packet;
    }

    private static void WriteChecksum(byte[] packet)
    {
        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        packet[6] = checksum;
    }
}
