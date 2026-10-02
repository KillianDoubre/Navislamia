using System;
using System.Buffers.Binary;
using System.Text;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// The server to client half of the competition family (docs/packet-specs/socle-competition-joueurs.md §3):
/// 4501 (39 bytes), 4503 (40), 4504 (43), 4505 (39) and 4506 (71). Every name is a fixed 31-byte C string.
/// </summary>
public static class GameCompeteServerPackets
{
    private const int HeaderSize = 7;
    private const int NameLength = GameCompetePackets.NameLength;

    /// <summary><c>TM_SC_COMPETE_REQUEST</c>: <c>compete_type</c> @7, the requester's name @8.</summary>
    public static byte[] BuildRequest(byte competeType, string requester)
    {
        var p = Frame(GamePackets.TM_SC_COMPETE_REQUEST, HeaderSize + 1 + NameLength);
        p[7] = competeType;
        WriteName(p, 8, requester);
        return Checksum(p);
    }

    /// <summary><c>TM_SC_COMPETE_ANSWER</c>: <c>compete_type</c> @7, <c>answer_type</c> @8, the answering player's name @9.</summary>
    public static byte[] BuildAnswer(byte competeType, byte answerType, string requestee)
    {
        var p = Frame(GamePackets.TM_SC_COMPETE_ANSWER, HeaderSize + 2 + NameLength);
        p[7] = competeType;
        p[8] = answerType;
        WriteName(p, 9, requestee);
        return Checksum(p);
    }

    /// <summary><c>TM_SC_COMPETE_COUNTDOWN</c>: <c>compete_type</c> @7, the competitor's name @8, its handle @39.</summary>
    public static byte[] BuildCountdown(byte competeType, string competitor, uint competitorHandle)
    {
        var p = Frame(GamePackets.TM_SC_COMPETE_COUNTDOWN, HeaderSize + 1 + NameLength + 4);
        p[7] = competeType;
        WriteName(p, 8, competitor);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(8 + NameLength, 4), competitorHandle);
        return Checksum(p);
    }

    /// <summary><c>TM_SC_COMPETE_START</c>: <c>compete_type</c> @7, the competitor's name @8.</summary>
    public static byte[] BuildStart(byte competeType, string competitor)
    {
        var p = Frame(GamePackets.TM_SC_COMPETE_START, HeaderSize + 1 + NameLength);
        p[7] = competeType;
        WriteName(p, 8, competitor);
        return Checksum(p);
    }

    /// <summary><c>TM_SC_COMPETE_END</c>: <c>compete_type</c> @7, <c>end_type</c> @8, the winner @9, the loser @40.</summary>
    public static byte[] BuildEnd(byte competeType, byte endType, string winner, string loser)
    {
        var p = Frame(GamePackets.TM_SC_COMPETE_END, HeaderSize + 2 + NameLength * 2);
        p[7] = competeType;
        p[8] = endType;
        WriteName(p, 9, winner);
        WriteName(p, 9 + NameLength, loser);
        return Checksum(p);
    }

    private static byte[] Frame(GamePackets id, int size)
    {
        var p = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), (uint)size);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4, 2), (ushort)id);
        return p;
    }

    /// <summary>At most 30 characters, so the NUL always fits in the 31 bytes.</summary>
    private static void WriteName(byte[] p, int offset, string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name ?? string.Empty);
        bytes.AsSpan(0, Math.Min(bytes.Length, NameLength - 1)).CopyTo(p.AsSpan(offset, NameLength));
    }

    private static byte[] Checksum(byte[] p)
    {
        byte sum = 0;
        for (var i = 0; i < 6; i++)
        {
            sum += p[i];
        }

        p[6] = sum;
        return p;
    }
}
