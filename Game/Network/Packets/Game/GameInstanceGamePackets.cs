using System;
using System.Buffers.Binary;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// TM_CS/SC_INSTANCE_GAME_* (4250-4253), the instance-game socle. All four ids are unconditional in rzu
/// (<c>X(&lt;id&gt;, true)</c>): the family only exists from EPIC_6_3 on and EPIC_7_3 (0x070300) is above it, so
/// 7.3 keeps 4250-4253 as they are. The client builds the three client to server frames with hard coded
/// lengths of 11, 7 and 7 bytes, and its score deserialiser reads 16 payload bytes; the answer is therefore
/// 7 + 16 = 23 bytes. The five <c>battle_arena_*</c> fields rzu declares in
/// <c>TS_SC_INSTANCE_GAME_SCORE_REQUEST</c> are gated <c>version &gt;= EPIC_8_1</c> and must not be written
/// for 7.3. See docs/packet-specs/socle-instances-jeu.md.
/// </summary>
public static class GameInstanceGamePackets
{
    private const int HeaderSize = 7;

    /// <summary>Total size of TM_CS_INSTANCE_GAME_ENTER (4250): header plus one int32.</summary>
    public const int EnterLength = HeaderSize + 4;

    /// <summary>
    /// Total size of TM_CS_INSTANCE_GAME_EXIT (4251) and TM_CS_INSTANCE_GAME_SCORE_REQUEST (4252): the
    /// header only, no payload at all.
    /// </summary>
    public const int EmptyLength = HeaderSize;

    /// <summary>Total size of TM_SC_INSTANCE_GAME_SCORE_REQUEST (4253) in 7.3: header plus four uint32.</summary>
    public const int ScoreResponseLength = HeaderSize + 16;

    public readonly record struct InstanceGameEnterRequest(int InstanceGameType);

    /// <summary>
    /// TM_CS_INSTANCE_GAME_ENTER (4250), <c>instance_game_type</c> at offset 7. Only the exact 11-byte form is
    /// accepted: the field is not optional, and a 7-byte frame would leave the client's own value missing.
    /// The value is chosen by the server, which copies it into the incoming message the client answers.
    /// </summary>
    public static bool TryReadEnter(ReadOnlySpan<byte> packet, out InstanceGameEnterRequest request)
    {
        if (packet.Length != EnterLength)
        {
            request = default;
            return false;
        }

        request = new InstanceGameEnterRequest(BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(HeaderSize, 4)));
        return true;
    }

    /// <summary>
    /// TM_CS_INSTANCE_GAME_EXIT (4251) and TM_CS_INSTANCE_GAME_SCORE_REQUEST (4252) carry no payload: the 7.3
    /// client writes exactly 7 bytes for both. Any other length is a malformed frame, not a shorter request.
    /// </summary>
    public static bool HasNoPayload(ReadOnlySpan<byte> packet)
    {
        return packet.Length == EmptyLength;
    }

    /// <summary>
    /// The Telecaster <c>huntaholic_point</c> as the unsigned field 4253 carries. The stored value is a signed
    /// int and the wire field is a uint32, so a negative stored point is written as zero rather than wrapped
    /// into a huge score.
    /// </summary>
    public static uint ToWireHolicPoint(int huntaholicPoint)
    {
        return huntaholicPoint < 0 ? 0u : (uint)huntaholicPoint;
    }

    /// <summary>
    /// TM_SC_INSTANCE_GAME_SCORE_REQUEST (4253), the answer to TM_CS_INSTANCE_GAME_SCORE_REQUEST (4252):
    /// <c>holicpoint</c> at 7, <c>bearroad_ranking</c> at 11, <c>deathmatch_kill_count</c> at 15 and
    /// <c>deathmatch_death_count</c> at 19, all unsigned 32-bit. Those 16 payload bytes are what the 7.3 client
    /// reads; the 32 <c>battle_arena_*</c> bytes of the 8.1 form are deliberately absent and the packet is
    /// never longer than 23 bytes.
    /// </summary>
    public static byte[] BuildScoreResponse(uint holicPoint, uint bearroadRanking, uint deathmatchKillCount,
        uint deathmatchDeathCount)
    {
        var packet = new byte[ScoreResponseLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2),
            (ushort)GamePackets.TM_SC_INSTANCE_GAME_SCORE_REQUEST);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), holicPoint);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize + 4, 4), bearroadRanking);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize + 8, 4), deathmatchKillCount);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize + 12, 4), deathmatchDeathCount);
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
