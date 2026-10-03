using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>One room of the lobby list, <c>TS_HUNTAHOLIC_INSTANCE_INFO</c> (38 bytes).</summary>
public readonly record struct HuntaholicInstanceInfo(int InstanceNo, string Name, byte CurrentMemberCount,
    byte MaxMemberCount, bool RequirePassword);

/// <summary>
/// The server to client half of the HuntaHolic family, sized in docs/packet-specs/socle-instances-jeu.md §3.4
/// (rzu, and the 7.3 client's deserialisers): 4001 = 23 + 38·N, 4002 = 45, 4006 = 48, 4007 = 15, 4009 = 11,
/// 4010 = 7, 4012 = 7. The password readers of 4003/4004 live here too: the request readers deliberately keep the
/// password out of their records (it never reaches a log line), and the room only needs it to compare.
/// </summary>
public static class GameHuntaholicServerPackets
{
    private const int HeaderSize = 7;
    public const int InstanceInfoSize = 38;
    public const int InstanceListHeaderSize = 23;
    public const int NameFieldLength = 31;

    public static byte[] BuildInstanceList(int huntaholicId, int page, int totalPage,
        IReadOnlyList<HuntaholicInstanceInfo> infos)
    {
        var packet = Create(GamePackets.TM_SC_HUNTAHOLIC_INSTANCE_LIST, InstanceListHeaderSize + InstanceInfoSize * infos.Count);
        var span = packet.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(7, 4), huntaholicId);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(11, 4), page);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(15, 4), infos.Count);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(19, 4), totalPage);
        for (var i = 0; i < infos.Count; i++)
            WriteInfo(span.Slice(InstanceListHeaderSize + i * InstanceInfoSize, InstanceInfoSize), infos[i]);
        return Checksum(packet);
    }

    public static byte[] BuildInstanceInfo(HuntaholicInstanceInfo info)
    {
        var packet = Create(GamePackets.TM_SC_HUNTAHOLIC_INSTANCE_INFO, HeaderSize + InstanceInfoSize);
        WriteInfo(packet.AsSpan(HeaderSize, InstanceInfoSize), info);
        return Checksum(packet);
    }

    /// <summary>TM_SC_HUNTAHOLIC_HUNTING_SCORE (4006), 48 bytes: the end-of-hunt scoreboard of one player.</summary>
    public static byte[] BuildHuntingScore(int huntaholicId, int personalKillCount, int personalScore, int killCount,
        int score, double pointAdvantage, double pointRate, int gainPoint, byte resultType)
    {
        var packet = Create(GamePackets.TM_SC_HUNTAHOLIC_HUNTING_SCORE, 48);
        var span = packet.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(7, 4), huntaholicId);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(11, 4), personalKillCount);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(15, 4), personalScore);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(19, 4), killCount);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(23, 4), score);
        BinaryPrimitives.WriteDoubleLittleEndian(span.Slice(27, 8), pointAdvantage);
        BinaryPrimitives.WriteDoubleLittleEndian(span.Slice(35, 8), pointRate);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(43, 4), gainPoint);
        span[47] = resultType;
        return Checksum(packet);
    }

    /// <summary>TM_SC_HUNTAHOLIC_UPDATE_SCORE (4007), 15 bytes: the player's kills and the room's score.</summary>
    public static byte[] BuildUpdateScore(int killCount, int score)
    {
        var packet = Create(GamePackets.TM_SC_HUNTAHOLIC_UPDATE_SCORE, 15);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(7, 4), killCount);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(11, 4), score);
        return Checksum(packet);
    }

    /// <summary>TM_SC_HUNTAHOLIC_BEGIN_HUNTING (4009), 11 bytes: <c>begin_time</c> as an ar_time of the client clock.</summary>
    public static byte[] BuildBeginHunting(uint beginTime)
    {
        var packet = Create(GamePackets.TM_SC_HUNTAHOLIC_BEGIN_HUNTING, 11);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), beginTime);
        return Checksum(packet);
    }

    public static byte[] BuildMaxPointAchieved() => Checksum(Create(GamePackets.TM_SC_HUNTAHOLIC_MAX_POINT_ACHIEVED, HeaderSize));

    public static byte[] BuildBeginCountdown() => Checksum(Create(GamePackets.TM_SC_HUNTAHOLIC_BEGIN_COUNTDOWN, HeaderSize));

    /// <summary>The password of a 56-byte 4003 (17 bytes at 39, NUL terminated), for the room to keep.</summary>
    public static string ReadCreatePassword(ReadOnlySpan<byte> packet) =>
        packet.Length == GameHuntaholicPackets.CreateInstanceLength
            ? ReadFixed(packet.Slice(GameHuntaholicPackets.PasswordOffset, GameHuntaholicPackets.PasswordFieldLength))
            : string.Empty;

    /// <summary>The password of a 28-byte 4004 (17 bytes at 11, NUL terminated), only to compare.</summary>
    public static string ReadJoinPassword(ReadOnlySpan<byte> packet) =>
        packet.Length == GameHuntaholicPackets.JoinInstanceLength
            ? ReadFixed(packet.Slice(GameHuntaholicPackets.JoinInstancePasswordOffset,
                GameHuntaholicPackets.JoinInstancePasswordFieldLength))
            : string.Empty;

    private static string ReadFixed(ReadOnlySpan<byte> field)
    {
        var end = field.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? field : field[..end]);
    }

    private static void WriteInfo(Span<byte> span, HuntaholicInstanceInfo info)
    {
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), info.InstanceNo);
        var name = Encoding.ASCII.GetBytes(info.Name ?? string.Empty);
        name.AsSpan(0, Math.Min(name.Length, NameFieldLength - 1)).CopyTo(span.Slice(4, NameFieldLength));
        span[35] = info.CurrentMemberCount;
        span[36] = info.MaxMemberCount;
        span[37] = info.RequirePassword ? (byte)1 : (byte)0;
    }

    private static byte[] Create(GamePackets id, int total)
    {
        var packet = new byte[total];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)id);
        return packet;
    }

    private static byte[] Checksum(byte[] packet)
    {
        byte checksum = 0;
        for (var i = 0; i < 6; i++) checksum += packet[i];
        packet[6] = checksum;
        return packet;
    }
}
