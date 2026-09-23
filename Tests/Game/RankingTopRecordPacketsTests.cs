using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// The player-ranking family, lot K1 of docs/packet-specs/socle-classements.md:
/// TM_CS_RANKING_TOP_RECORD (5000) is a fixed 8-byte frame (7-byte header + int8 ranking_type at offset 7)
/// and TM_SC_RANKING_TOP_RECORD (5001) is 20 + 41 x records bytes (header, int8 ranking_type at 7,
/// uint16 requester_rank at 8, int64 requester_score at 10, uint16 records at 18, then 41-byte entries
/// starting at 20: uint16 rank +0, char[31] ranker_name +2, int64 score +33).
/// </summary>
[TestFixture]
public class RankingTopRecordPacketsTests
{
    private const int RequestLength = 8;
    private const int AnswerHeaderSize = 20;
    private const int EntrySize = 41;
    private const int NameOffsetInEntry = 2;
    private const int ScoreOffsetInEntry = 33;

    private static byte[] ClientFrame(sbyte rankingType)
    {
        var packet = new byte[RequestLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), RequestLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_RANKING_TOP_RECORD);

        packet[6] = Checksum(packet);
        packet[7] = unchecked((byte)rankingType);
        return packet;
    }

    private static byte Checksum(byte[] packet)
    {
        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        return checksum;
    }

    private static byte[] Build(sbyte rankingType, ushort requesterRank, long requesterScore,
        params GameRankingPackets.RankingRecord[] records)
    {
        return GameRankingPackets.BuildRankingTopRecord(rankingType, requesterRank, requesterScore, records);
    }

    private static string NameAt(byte[] packet, int entryOffset)
    {
        var field = packet.AsSpan(entryOffset + NameOffsetInEntry, GameRankingPackets.NameLength);
        var nul = field.IndexOf((byte)0);
        return Encoding.ASCII.GetString(field.Slice(0, nul < 0 ? field.Length : nul));
    }

    private static long ScoreAt(byte[] packet, int entryOffset)
    {
        return BinaryPrimitives.ReadInt64LittleEndian(
            packet.AsSpan(entryOffset + ScoreOffsetInEntry, 8));
    }

    [Test]
    public void RankingIds_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_CS_RANKING_TOP_RECORD).Should().Be(5000);
        ((ushort)GamePackets.TM_SC_RANKING_TOP_RECORD).Should().Be(5001);

        // rzu declares both ids as X(<id>, true): the literal C++ condition is substituted into
        // "if(condition_) id = id_;", so neither is gated by version and the 7.3 ids are the plain ones.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_RANKING_TOP_RECORD).Should().BeTrue();
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_SC_RANKING_TOP_RECORD).Should().BeTrue();
    }

    [Test]
    public void ClientPacket_UsesTheEpic73Layout()
    {
        var packet = ClientFrame(0);

        packet.Length.Should().Be(RequestLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(RequestLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(5000);
        packet[6].Should().Be(Checksum(packet));
        packet[7].Should().Be(0, "the 7.3 client writes ranking_type as a single byte at offset 7");
    }

    [TestCase(0, TestName = "TryReadRankingTopRecord_ReadsZero")]
    [TestCase(1, TestName = "TryReadRankingTopRecord_ReadsOne")]
    [TestCase(7, TestName = "TryReadRankingTopRecord_ReadsAnUnknownTypeUntouched")]
    public void TryReadRankingTopRecord_ReadsTheTypeAtOffsetSeven(sbyte rankingType)
    {
        GameActionPackets.TryReadRankingTopRecord(ClientFrame(rankingType), out var request).Should().BeTrue();

        request.RankingType.Should().Be(rankingType);
    }

    [Test]
    public void TryReadRankingTopRecord_KeepsTheSignedByteTheReferenceDeclares()
    {
        // rzu declares int8, so 0xff is -1 and not 255: reading it as a byte would silently change the
        // value that is echoed back into the answer.
        GameActionPackets.TryReadRankingTopRecord(ClientFrame(unchecked((sbyte)0xff)), out var request).Should().BeTrue();

        request.RankingType.Should().Be(-1);
    }

    [TestCase(0, TestName = "TryReadRankingTopRecord_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadRankingTopRecord_RejectsAHeaderOnlyFrame")]
    [TestCase(9, TestName = "TryReadRankingTopRecord_RejectsAPaddedFrame")]
    [TestCase(20, TestName = "TryReadRankingTopRecord_RejectsAnAnswerSizedFrame")]
    public void TryReadRankingTopRecord_RejectsAnyLengthOtherThanEight(int length)
    {
        // The 7.3 client writes the length 8 in hard (single emission site), so any other length is a non
        // conforming client; the specification decides no answer for it (log and drop).
        var packet = new byte[length];
        if (length >= RequestLength)
        {
            ClientFrame(3).CopyTo(packet, 0);
        }

        GameActionPackets.TryReadRankingTopRecord(packet, out var request).Should().BeFalse();
        request.Should().Be(default(GameActionPackets.RankingTopRecordRequest));
    }

    [Test]
    public void AnswerPacket_EmptyListLaysOutTheThirteenHeaderBytes()
    {
        var packet = Build(0, 0, 0);

        packet.Length.Should().Be(AnswerHeaderSize, "5001 with records = 0 is the 20-byte minimum");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(AnswerHeaderSize);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(5001);
        packet[6].Should().Be(Checksum(packet));
        packet[7].Should().Be(0, "ranking_type is a signed byte at offset 7");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(8, 2)).Should().Be(0, "requester_rank is at 8");
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(10, 8)).Should().Be(0, "requester_score is at 10");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(18, 2)).Should().Be(0, "records is a uint16 at 18");
    }

    [TestCase(0, 20, TestName = "AnswerSize_EmptyIsTwentyBytes")]
    [TestCase(1, 61, TestName = "AnswerSize_OneEntryIsSixtyOneBytes")]
    [TestCase(2, 102, TestName = "AnswerSize_TwoEntriesAreOneHundredAndTwoBytes")]
    [TestCase(10, 430, TestName = "AnswerSize_TenEntriesAreFourHundredAndThirtyBytes")]
    public void AnswerPacket_SizeFollowsTheTwentyPlusFortyOneTimesNFormula(int records, int expectedSize)
    {
        GameRankingPackets.GetAnswerSize(records).Should().Be(expectedSize);

        var entries = new GameRankingPackets.RankingRecord[records];
        var packet = Build(0, 0, 0, entries);

        packet.Length.Should().Be(expectedSize);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be((uint)expectedSize);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(18, 2)).Should().Be((ushort)records,
            "the counter must equal the number of entries actually written — the client loops on it and never reads Length");
        packet[6].Should().Be(Checksum(packet));
    }

    [Test]
    public void AnswerPacket_CountsOnlyWhatItWrites()
    {
        // A padded list is capped, so the counter stays honest instead of announcing entries the frame
        // does not carry.
        var entries = new List<GameRankingPackets.RankingRecord>();
        for (var i = 0; i < 14; i++)
        {
            entries.Add(new GameRankingPackets.RankingRecord((ushort)(i + 1), $"ranker{i}", i));
        }

        var packet = GameRankingPackets.BuildRankingTopRecord(0, 0, 0, entries);

        packet.Length.Should().Be(GameRankingPackets.GetAnswerSize(10));
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(18, 2)).Should().Be(10);
    }

    [Test]
    public void AnswerEntry_LaysOutRankThenNameThenScore()
    {
        // No padding between the counter at 18 and the first entry at 20: the client reads the rank at
        // buf+20 and starts the name at buf+22 inside the same entry.
        var packet = Build(0, 0, 0, new GameRankingPackets.RankingRecord(7, "Killian", 12345));

        packet.Length.Should().Be(AnswerHeaderSize + EntrySize);
        GameRankingPackets.GetRecordOffset(0).Should().Be(AnswerHeaderSize);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(18, 2)).Should().Be(1);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(20, 2)).Should().Be(7, "rank is the uint16 at +0 of the entry");
        NameAt(packet, 20).Should().Be("Killian", "ranker_name is a char[31] at +2 of the entry");
        ScoreAt(packet, 20).Should().Be(12345L, "score is the int64 at +33 of the entry");
        ScoreAt(packet, 20).Should().Be(BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(53, 8)),
            "the score of the first entry starts at absolute offset 20 + 33 = 53");
    }

    [Test]
    public void SecondEntry_StartsOnTheNextFortyOneByteBoundary()
    {
        var packet = Build(0, 0, 0,
            new GameRankingPackets.RankingRecord(1, "First", 100),
            new GameRankingPackets.RankingRecord(2, "Second", 200));

        GameRankingPackets.GetRecordOffset(1).Should().Be(61);

        packet.Length.Should().Be(AnswerHeaderSize + 2 * EntrySize);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(61, 2)).Should().Be(2);
        NameAt(packet, 61).Should().Be("Second");
        ScoreAt(packet, 61).Should().Be(200L);

        // Asymmetric values: a wrong stride would read the first entry's score here.
        ScoreAt(packet, 20).Should().Be(100L);
    }

    [Test]
    public void TenEntries_AreTheClientMaximum()
    {
        GameRankingPackets.MaxRecords.Should().Be(10);

        var entries = new GameRankingPackets.RankingRecord[10];
        for (var i = 0; i < entries.Length; i++)
        {
            entries[i] = new GameRankingPackets.RankingRecord((ushort)(i + 1), $"r{i}", i * 10);
        }

        var packet = Build(0, 0, 0, entries);

        packet.Length.Should().Be(430);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(18, 2)).Should().Be(10);
        GameRankingPackets.GetRecordOffset(9).Should().Be(389);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(389, 2)).Should().Be(10);
        NameAt(packet, 389).Should().Be("r9");
        ScoreAt(packet, 389).Should().Be(90L, "the tenth entry's score ends the frame at 430 bytes");
    }

    [Test]
    public void TheLayoutConstants_MatchTheSpecification()
    {
        GameRankingPackets.AnswerHeaderSize.Should().Be(AnswerHeaderSize);
        GameRankingPackets.RecordSize.Should().Be(EntrySize);
        GameRankingPackets.NameLength.Should().Be(31);
        GameRankingPackets.MaxNameLength.Should().Be(30);
    }

    [Test]
    public void Name_OfThirtyOneCharactersWithoutNul_IsTruncatedAndNulTerminated()
    {
        // The client copies ranker_name with a strcpy, so a name without a NUL inside its 31 bytes runs
        // into the entry's own score. The server must always leave a NUL in the field.
        const string name = "ABCDEFGHIJKLMNOPQRSTUVWXYZ01234"; // 31 characters
        name.Length.Should().Be(31);

        var packet = Build(0, 0, 0, new GameRankingPackets.RankingRecord(1, name, 4242));

        var field = packet.AsSpan(22, 31);
        field[30].Should().Be(0, "the 31st byte of the name field is the mandatory NUL");
        Encoding.ASCII.GetString(field.Slice(0, 30)).Should().Be(name.Substring(0, 30));
        ScoreAt(packet, 20).Should().Be(4242L, "the score must not have been overwritten by the name");
    }

    [Test]
    public void Name_OfThirtyCharactersFitsExactly()
    {
        var name = new string('a', 30);

        var packet = Build(0, 0, 0, new GameRankingPackets.RankingRecord(1, name, 1));

        Encoding.ASCII.GetString(packet.AsSpan(22, 30)).Should().Be(name);
        packet[52].Should().Be(0);
        ScoreAt(packet, 20).Should().Be(1L);
    }

    [Test]
    public void Name_LongerThanThirtyCharacters_IsTruncated()
    {
        var packet = Build(0, 0, 0, new GameRankingPackets.RankingRecord(1, new string('b', 40), 2));

        NameAt(packet, 20).Should().Be(new string('b', 30));
        packet[52].Should().Be(0);
    }

    [Test]
    public void Name_IsNulPadded()
    {
        var packet = Build(0, 0, 0, new GameRankingPackets.RankingRecord(1, "ab", 0));

        packet.AsSpan(24, 29).ToArray().Should().AllBeEquivalentTo((byte)0,
            "the remaining 29 bytes of a 31-byte field are NUL padding");
    }

    [Test]
    public void Name_OfTheFirstEntry_DoesNotOverrunTheSecondEntryScore()
    {
        // Two entries: the 31-byte name field of the first ends at 52 and the second entry starts at 61.
        var packet = Build(0, 0, 0,
            new GameRankingPackets.RankingRecord(1, new string('c', 31), 11),
            new GameRankingPackets.RankingRecord(2, new string('d', 31), 22));

        NameAt(packet, 20).Should().Be(new string('c', 30));
        ScoreAt(packet, 20).Should().Be(11L);
        NameAt(packet, 61).Should().Be(new string('d', 30));
        ScoreAt(packet, 61).Should().Be(22L);
    }

    [Test]
    public void RequesterRankAndScore_AreWrittenAtEightAndTen()
    {
        var packet = Build(0, 42, 1234567890123L);

        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(8, 2)).Should().Be(42);
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(10, 8)).Should().Be(1234567890123L);
        packet.Length.Should().Be(AnswerHeaderSize);
    }

    [Test]
    public void RequesterScore_KeepsTheWireScaleOfTenThousandPerUnit()
    {
        // The client divides both scores by 10 000 before using them, so the field carries the displayed
        // value x 10 000 raw: the writer must not scale or round it a second time.
        var packet = Build(0, 5, 15000L);

        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(10, 8)).Should().Be(15000L);
    }

    [TestCase(0, TestName = "RankingType_IsEchoedAsZero")]
    [TestCase(3, TestName = "RankingType_IsEchoedAsThree")]
    [TestCase(-1, TestName = "RankingType_IsEchoedAsASignedMinusOne")]
    public void RankingType_IsEchoedVerbatim(sbyte rankingType)
    {
        // The client never compares the value it receives, but echoing the request is the only unambiguous
        // answer, and the domain of ranking_type is not established: no value is invented or refused.
        var packet = Build(rankingType, 0, 0);

        packet[7].Should().Be(unchecked((byte)rankingType));
    }

    [Test]
    public void AnswerPacket_ChecksumIsTheSumOfTheFirstSixBytes()
    {
        var entries = new GameRankingPackets.RankingRecord[3];
        for (var i = 0; i < entries.Length; i++)
        {
            entries[i] = new GameRankingPackets.RankingRecord((ushort)(i + 1), $"name{i}", i);
        }

        var packet = Build(1, 2, 3, entries);

        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        packet[6].Should().Be(checksum);
    }

    [Test]
    public void NullRecords_AreTreatedAsAnEmptyList()
    {
        var packet = GameRankingPackets.BuildRankingTopRecord(0, 0, 0, null);

        packet.Length.Should().Be(AnswerHeaderSize);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(18, 2)).Should().Be(0);
    }

    [Test]
    public void NullName_WritesAnEmptyNameField()
    {
        var packet = Build(0, 0, 0, new GameRankingPackets.RankingRecord(1, null, 9));

        NameAt(packet, 20).Should().BeEmpty();
        packet[52].Should().Be(0);
        ScoreAt(packet, 20).Should().Be(9L);
    }

    [Test]
    public void AnswerPacket_HasNoFieldOutsideTheDeclaredLayout()
    {
        // 7 + 13 = 20 with no entries: any extra field would push the total past 20 and desynchronise the
        // client, which reads no length field at all.
        Build(0, 0, 0).Length.Should().Be(AnswerHeaderSize);
        Build(0, 0, 0, new GameRankingPackets.RankingRecord(1, "a", 0)).Length.Should().Be(AnswerHeaderSize + EntrySize);
    }
}
