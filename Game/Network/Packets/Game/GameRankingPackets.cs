using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// The answer of the player-ranking family: <c>TM_SC_RANKING_TOP_RECORD</c> (5001). Its id is
/// <c>X(5001, true)</c> in rzu — no version gating — and the 7.3 value is 5001. The request
/// <c>TM_CS_RANKING_TOP_RECORD</c> (5000, a fixed 8-byte frame) is read by
/// <see cref="GameActionPackets.TryReadRankingTopRecord"/>. See docs/packet-specs/socle-classements.md.
///
/// Wire layout, 7-byte header included — 20 + 41 x <c>records</c> bytes:
/// <c>int8 ranking_type</c> (7), <c>uint16 requester_rank</c> (8), <c>int64 requester_score</c> (10),
/// <c>uint16 records</c> (18), then <c>records</c> entries of 41 bytes starting at offset 20 —
/// <c>uint16 rank</c> (+0), <c>char[31] ranker_name</c> (+2), <c>int64 score</c> (+33). There is no
/// padding between the counter and the first entry, which the client proves by reading both at buf+18
/// and buf+20.
/// </summary>
public static class GameRankingPackets
{
    private const int HeaderSize = 7;

    /// <summary>5001 frames start with 7 header bytes + 13 payload bytes (type, rank, score, count).</summary>
    public const int AnswerHeaderSize = HeaderSize + 13;

    public const int RecordSize = 41;

    /// <summary>Fixed name buffer, trailing NUL included: 31 bytes.</summary>
    public const int NameLength = 31;

    /// <summary>Characters a name may hold before the mandatory NUL: 30.</summary>
    public const int MaxNameLength = NameLength - 1;

    /// <summary>
    /// Hard protocol bound, not a content choice: the client copies the answer into a 442-byte message
    /// with a 32-byte header, so its entry area is 410 bytes = 41 x 10. An eleventh entry writes past
    /// that allocation, and nothing on the client side bounds the counter it reads.
    /// </summary>
    public const int MaxRecords = 10;

    /// <summary>
    /// One answer entry. <paramref name="Score"/> is the raw wire value, in units of 1/10 000: the
    /// client divides both scores by 10 000 before using them, so the wire value is the displayed
    /// value x 10 000 (spec §3.6, K-K4). <paramref name="Name"/> is truncated to 30 characters and
    /// NUL-terminated inside its 31-byte field, because the client copies it with a <c>strcpy</c>.
    /// </summary>
    public readonly record struct RankingRecord(ushort Rank, string Name, long Score);

    /// <summary>
    /// Total size of a 5001 frame carrying <paramref name="records"/> entries: 20 + 41 x records.
    /// </summary>
    public static int GetAnswerSize(int records) => AnswerHeaderSize + RecordSize * records;

    /// <summary>Absolute offset of entry <paramref name="index"/> in a 5001 frame.</summary>
    public static int GetRecordOffset(int index) => AnswerHeaderSize + RecordSize * index;

    /// <summary>
    /// TM_SC_RANKING_TOP_RECORD (5001). <paramref name="rankingType"/> is written back as received: the
    /// 7.3 client only ever asks for 0 and never compares the value it gets, but echoing the request is
    /// the only unambiguous answer. Both scores are raw wire values, in units of 1/10 000 — see
    /// <see cref="RankingRecord"/>.
    ///
    /// More than <see cref="MaxRecords"/> entries would overflow the client's own message buffer, so the
    /// list is capped there; the counter then still equals the number of entries actually written, which
    /// is the invariant the client depends on (it loops on the counter and never reads the length field).
    /// </summary>
    public static byte[] BuildRankingTopRecord(
        sbyte rankingType,
        ushort requesterRank,
        long requesterScore,
        IReadOnlyList<RankingRecord> records)
    {
        var count = records?.Count ?? 0;
        if (count > MaxRecords)
        {
            count = MaxRecords;
        }

        var packet = new byte[GetAnswerSize(count)];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_SC_RANKING_TOP_RECORD);

        packet[HeaderSize] = unchecked((byte)rankingType);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(HeaderSize + 1, 2), requesterRank);
        BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(HeaderSize + 3, 8), requesterScore);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(HeaderSize + 11, 2), (ushort)count);

        for (var i = 0; i < count; i++)
        {
            WriteRecord(packet.AsSpan(GetRecordOffset(i), RecordSize), records![i]);
        }

        WriteChecksum(packet);
        return packet;
    }

    private static void WriteRecord(Span<byte> span, RankingRecord record)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0, 2), record.Rank);
        WriteName(span.Slice(2, NameLength), record.Name);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(33, 8), record.Score);
    }

    /// <summary>
    /// The client copies the name with a <c>strcpy</c> into its 31-byte field, so a value without a NUL
    /// inside those 31 bytes overruns the entry's own score. The name is therefore truncated to 30
    /// characters and the field is always NUL-terminated (and NUL-padded, as the reference format is a
    /// fixed-size character array).
    /// </summary>
    private static void WriteName(Span<byte> span, string name)
    {
        span.Clear();

        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var length = Math.Min(name.Length, MaxNameLength);
        Encoding.ASCII.GetBytes(name.AsSpan(0, length), span.Slice(0, length));
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
