using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// The creature farm socle, docs/packet-specs/socle-ferme-creatures.md:
/// 6000 and 6008 are header-only 7-byte frames, 6004 and 6006 are 7 + 4, 6002 is 19 + 8T + 8C and the one
/// answer the server emits, 6001, is 8 + 120 x N (8 bytes for an empty farm).
/// </summary>
[TestFixture]
public class FarmPacketsTests
{
    private const int HeaderSize = 7;
    private const int FarmInfoHeaderSize = 8;
    private const int EntrySize = 120;

    // Entry-relative offsets of TS_FARM_SUMMON_INFO, from the sheet's §3.2 table.
    private const int IndexInEntry = 0;
    private const int ExpInEntry = 4;
    private const int NameInEntry = 12;
    private const int NameLength = 19;
    private const int DurationInEntry = 31;
    private const int ElaspedTimeInEntry = 35;
    private const int RefreshTimeInEntry = 39;
    private const int UsingCashInEntry = 43;
    private const int UsingCrackerInEntry = 44;
    private const int CardInfoInEntry = 45;

    // Absolute offsets of the first entry of a 6001 frame and of its card_info motif.
    private const int FirstEntry = FarmInfoHeaderSize;
    private const int FirstCardInfo = FirstEntry + CardInfoInEntry;
    private const int SecondEntry = FarmInfoHeaderSize + EntrySize;

    private static byte Checksum(byte[] packet)
    {
        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        return checksum;
    }

    private static byte[] ClientFrame(ushort id, int length)
    {
        var packet = new byte[length];
        if (length < HeaderSize)
        {
            // A buffer too short to hold a header cannot carry the id: only the reader's length check counts.
            return packet;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), id);
        packet[6] = Checksum(packet);
        return packet;
    }

    private static GameFarmPackets.FarmSummonInfo Summon(int index, long exp, string name, int duration,
        int elaspedTime, int refreshTime, byte usingCash, byte usingCracker)
    {
        return new GameFarmPackets.FarmSummonInfo(index, exp, name, duration, elaspedTime, refreshTime,
            usingCash, usingCracker, CardInfo());
    }

    private static ItemFixedInfo CardInfo()
    {
        return new ItemFixedInfo(
            Handle: 0x11111111u,
            Code: 12001,
            Uid: 987654321L,
            Count: 1L,
            EtherealDurability: 99,
            Endurance: 100u,
            Enhance: 3,
            Level: 4,
            Flag: 0x0Fu,
            Sockets: new long[] { 7, 0, 0, 0 },
            RemainTime: 1234,
            ElementalEffectType: 2,
            ElementalEffectRemainTime: 0,
            ElementalEffectAttackPoint: 11,
            ElementalEffectMagicPoint: 22,
            AppearanceCode: 0);
    }

    private static string NameAt(byte[] packet, int entryOffset)
    {
        var field = packet.AsSpan(entryOffset + NameInEntry, NameLength);
        var nul = field.IndexOf((byte)0);
        return Encoding.ASCII.GetString(field.Slice(0, nul < 0 ? field.Length : nul));
    }

    [Test]
    public void FarmIds_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_CS_REQUEST_FARM_INFO).Should().Be(6000);
        ((ushort)GamePackets.TM_SC_FARM_INFO).Should().Be(6001);
        ((ushort)GamePackets.TM_CS_FOSTER_CREATURE).Should().Be(6002);
        ((ushort)GamePackets.TM_CS_RETRIEVE_CREATURE).Should().Be(6004);
        ((ushort)GamePackets.TM_CS_NURSE_CREATURE).Should().Be(6006);
        ((ushort)GamePackets.TM_CS_REQUEST_FARM_MARKET).Should().Be(6008);

        // Every id of the family is X(<id>, true) in rzu under "// Since EPIC_7_3": no remapping for 7.3, so
        // the plain ids above are the ones on the wire.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_REQUEST_FARM_INFO).Should().BeTrue();
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_SC_FARM_INFO).Should().BeTrue();
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_FOSTER_CREATURE).Should().BeTrue();
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_RETRIEVE_CREATURE).Should().BeTrue();
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_NURSE_CREATURE).Should().BeTrue();
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_REQUEST_FARM_MARKET).Should().BeTrue();
    }

    [Test]
    public void TheThreeResultIds_AreNotDeclared()
    {
        // 6003, 6005 and 6007 carry a `result` byte whose values no reference establishes: the lot emits none
        // of them, and a declared member must be routed (transversal rule 4).
        Enum.IsDefined(typeof(GamePackets), (ushort)6003).Should().BeFalse();
        Enum.IsDefined(typeof(GamePackets), (ushort)6005).Should().BeFalse();
        Enum.IsDefined(typeof(GamePackets), (ushort)6007).Should().BeFalse();
    }

    [Test]
    public void FarmInfoEntry_HasNoEpic98UnknownField()
    {
        // Gating 7.3: the entry is 45 bytes of fields plus the 75-byte card_info and nothing else — rzu only
        // appends its `unknown` field from EPIC_9_8_1 on. A frame of N entries is therefore exactly
        // 8 + 120N bytes, with the last byte inside the last entry's card_info.
        var packet = GameFarmPackets.BuildFarmInfo(new[]
        {
            Summon(0, 0, "a", 0, 0, 0, 0, 0),
            Summon(1, 0, "b", 0, 0, 0, 0, 0)
        });

        packet.Length.Should().Be(248);
        var lastEntry = GameFarmPackets.GetSummonEntryOffset(1);
        var lastCardInfo = lastEntry + CardInfoInEntry;
        (lastCardInfo + ItemFixedInfoWriter.Size).Should().Be(packet.Length);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(packet.Length - 4, 4))
            .Should().Be(0, "the last four bytes are the appearance_code of the second card_info");
    }

    [Test]
    public void EmptyFarmInfo_IsEightBytesAndAnnouncesNoSummon()
    {
        var packet = GameFarmPackets.BuildEmptyFarmInfo();

        packet.Length.Should().Be(FarmInfoHeaderSize, "6001 with summons = 0 is the 8-byte minimum");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(FarmInfoHeaderSize);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(6001);
        packet[6].Should().Be(Checksum(packet));
        packet[7].Should().Be(0, "summons is the int8 at offset 7");
    }

    [TestCase(0, 8, TestName = "FarmInfoSize_EmptyIsEightBytes")]
    [TestCase(1, 128, TestName = "FarmInfoSize_OneEntryIsOneHundredAndTwentyEightBytes")]
    [TestCase(2, 248, TestName = "FarmInfoSize_TwoEntriesAreTwoHundredAndFortyEightBytes")]
    public void FarmInfoSize_FollowsTheEightPlusOneHundredAndTwentyTimesNFormula(int summons, int expectedSize)
    {
        GameFarmPackets.GetFarmInfoSize(summons).Should().Be(expectedSize);

        var entries = new GameFarmPackets.FarmSummonInfo[summons];
        var packet = GameFarmPackets.BuildFarmInfo(entries);

        packet.Length.Should().Be(expectedSize);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be((uint)expectedSize);
        packet[7].Should().Be((byte)summons,
            "the counter must equal the number of entries actually written — the client loops on it");
        packet[6].Should().Be(Checksum(packet));
    }

    [Test]
    public void FarmInfoEntry_LaysOutTheNineEpic73Fields()
    {
        var packet = GameFarmPackets.BuildFarmInfo(new[]
        {
            Summon(2, 1234567890123L, "Killian", 3600, 120, 60, 1, 0)
        });

        packet.Length.Should().Be(FarmInfoHeaderSize + EntrySize);
        packet[7].Should().Be(1);

        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(FirstEntry + IndexInEntry, 4)).Should().Be(2,
            "index is the int32 at +0 of the entry, absolute offset 8");
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(FirstEntry + ExpInEntry, 8))
            .Should().Be(1234567890123L, "exp is the int64 at +4, absolute offset 12");
        NameAt(packet, FirstEntry).Should().Be("Killian",
            "name is the char[19] at +12, absolute offset 20");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(FirstEntry + DurationInEntry, 4)).Should().Be(3600,
            "duration is at +31, absolute offset 39");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(FirstEntry + ElaspedTimeInEntry, 4)).Should().Be(120,
            "elasped_time (the reference's spelling) is at +35, absolute offset 43");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(FirstEntry + RefreshTimeInEntry, 4)).Should().Be(60,
            "refresh_time is at +39, absolute offset 47");
        packet[FirstEntry + UsingCashInEntry].Should().Be(1, "using_cash is the int8 at +43, absolute offset 51");
        packet[FirstEntry + UsingCrackerInEntry].Should().Be(0,
            "using_cracker is the int8 at +44, absolute offset 52");
    }

    [Test]
    public void FarmInfoEntry_OffsetsAreTheAbsoluteOnesOfTheSheet()
    {
        // The sheet's list, asserted literally: a shift of one byte anywhere in the entry would break the
        // client's own 120-byte memcpy.
        FirstEntry.Should().Be(8);
        (FirstEntry + ExpInEntry).Should().Be(12);
        (FirstEntry + NameInEntry).Should().Be(20);
        (FirstEntry + DurationInEntry).Should().Be(39);
        (FirstEntry + ElaspedTimeInEntry).Should().Be(43);
        (FirstEntry + RefreshTimeInEntry).Should().Be(47);
        (FirstEntry + UsingCashInEntry).Should().Be(51);
        (FirstEntry + UsingCrackerInEntry).Should().Be(52);
        (FirstEntry + CardInfoInEntry).Should().Be(53);
        (FarmInfoHeaderSize + EntrySize).Should().Be(128, "the first entry ends at 128");

        GameFarmPackets.GetSummonEntryOffset(0).Should().Be(8);
        GameFarmPackets.GetSummonEntryOffset(1).Should().Be(128);
    }

    [Test]
    public void FarmInfoEntry_CardInfoIsTheSeventyFiveByteItemMotifAtOffsetFiftyThree()
    {
        var packet = GameFarmPackets.BuildFarmInfo(new[] { Summon(0, 0, "card", 0, 0, 0, 0, 0) });

        // 120 - 45 = 75: the motif's own size is reconfirmed by the entry arithmetic.
        (EntrySize - CardInfoInEntry).Should().Be(ItemFixedInfoWriter.Size);

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(FirstCardInfo, 4)).Should().Be(0x11111111u,
            "handle is at +0 of the motif");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(FirstCardInfo + 4, 4)).Should().Be(12001,
            "code is at +4 of the motif");
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(FirstCardInfo + 8, 8)).Should().Be(987654321L,
            "uid is at +8 of the motif");
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(FirstCardInfo + 16, 8)).Should().Be(1L,
            "count is at +16 of the motif");
        packet[FirstCardInfo + 33].Should().Be(4, "level is at +33 of the motif");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(FirstCardInfo + 71, 4)).Should().Be(0,
            "appearance_code is at +71 of the motif and stays 0");
        (FirstCardInfo + ItemFixedInfoWriter.Size).Should().Be(128);
    }

    [Test]
    public void SecondEntry_StartsOnTheNextOneHundredAndTwentyByteBoundary()
    {
        // Asymmetric values: a wrong stride would read the first entry's fields here.
        var packet = GameFarmPackets.BuildFarmInfo(new[]
        {
            Summon(1, 11L, "First", 100, 10, 1, 0, 0),
            Summon(2, 22L, "Second", 200, 20, 2, 0, 1)
        });

        packet.Length.Should().Be(248);
        packet[7].Should().Be(2);

        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(SecondEntry + IndexInEntry, 4)).Should().Be(2);
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(SecondEntry + ExpInEntry, 8)).Should().Be(22L);
        NameAt(packet, SecondEntry).Should().Be("Second");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(SecondEntry + DurationInEntry, 4)).Should().Be(200);
        packet[SecondEntry + UsingCrackerInEntry].Should().Be(1);

        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(FirstEntry + IndexInEntry, 4)).Should().Be(1);
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(FirstEntry + ExpInEntry, 8)).Should().Be(11L);
        NameAt(packet, FirstEntry).Should().Be("First");
    }

    [Test]
    public void FarmInfoName_IsTruncatedToEighteenCharactersAndNulPadded()
    {
        var packet = GameFarmPackets.BuildFarmInfo(new[] { Summon(0, 0, new string('z', 30), 7, 0, 0, 0, 0) });

        var field = packet.AsSpan(FirstEntry + NameInEntry, NameLength);
        Encoding.ASCII.GetString(field.Slice(0, GameFarmPackets.SummonNameMaxLength)).Should().Be(new string('z', 18));
        field[18].Should().Be(0, "the 19th byte of the name field is the mandatory NUL");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(FirstEntry + DurationInEntry, 4)).Should().Be(7,
            "the duration must not have been overwritten by the name");
    }

    [Test]
    public void FarmInfoName_IsZeroPaddedAfterAShorterValue()
    {
        var packet = GameFarmPackets.BuildFarmInfo(new[] { Summon(0, 0, "ab", 1, 0, 0, 0, 0) });

        NameAt(packet, FirstEntry).Should().Be("ab");
        packet.AsSpan(FirstEntry + NameInEntry + 2, NameLength - 2).ToArray().Should()
            .AllBeEquivalentTo((byte)0, "the rest of the fixed 19-byte field is NUL padding");
    }

    [Test]
    public void FarmInfoName_NullOrEmpty_WritesAnEmptyField()
    {
        var packet = GameFarmPackets.BuildFarmInfo(new[]
        {
            Summon(0, 0, null!, 1, 0, 0, 0, 0),
            Summon(1, 0, string.Empty, 2, 0, 0, 0, 0)
        });

        NameAt(packet, FirstEntry).Should().BeEmpty();
        NameAt(packet, SecondEntry).Should().BeEmpty();
    }

    [Test]
    public void FarmInfo_NullList_IsAnEmptyFarm()
    {
        var packet = GameFarmPackets.BuildFarmInfo(null);

        packet.Length.Should().Be(FarmInfoHeaderSize);
        packet[7].Should().Be(0);
    }

    [Test]
    public void FarmInfo_CapsTheCounterAtTheSignedByteTheReferenceSerialises()
    {
        // Protocol bound, not a content choice: rzu writes summons as an int8_t clamped to 127, which is what
        // caps the frame at 8 + 120 x 127 = 15 248 bytes.
        GameFarmPackets.MaxSummons.Should().Be(127);
        GameFarmPackets.GetFarmInfoSize(GameFarmPackets.MaxSummons).Should().Be(15248);

        var entries = new List<GameFarmPackets.FarmSummonInfo>();
        for (var i = 0; i < 130; i++)
        {
            entries.Add(Summon(i, i, $"s{i}", 0, 0, 0, 0, 0));
        }

        var packet = GameFarmPackets.BuildFarmInfo(entries);

        packet.Length.Should().Be(15248);
        packet[7].Should().Be(127, "the counter stays honest instead of wrapping");
    }

    [TestCase((ushort)GamePackets.TM_CS_REQUEST_FARM_INFO, TestName = "HasNoPayload_AcceptsTheSevenByteFarmInfoRequest")]
    [TestCase((ushort)GamePackets.TM_CS_REQUEST_FARM_MARKET,
        TestName = "HasNoPayload_AcceptsTheSevenByteFarmMarketRequest")]
    public void HasNoPayload_AcceptsTheSevenByteFrames(ushort id)
    {
        GameFarmPackets.EmptyLength.Should().Be(HeaderSize);

        var packet = ClientFrame(id, HeaderSize);

        GameFarmPackets.HasNoPayload(packet).Should().BeTrue();
    }

    [TestCase(0, TestName = "HasNoPayload_RejectsAnEmptyBuffer")]
    [TestCase(6, TestName = "HasNoPayload_RejectsATruncatedHeader")]
    [TestCase(8, TestName = "HasNoPayload_RejectsAPaddedFrame")]
    [TestCase(11, TestName = "HasNoPayload_RejectsAHandleFrame")]
    public void HasNoPayload_RejectsAnyOtherLength(int length)
    {
        GameFarmPackets.HasNoPayload(new byte[length]).Should().BeFalse();
    }

    // --- TM_CS_FOSTER_CREATURE (6002) ------------------------------------------------------------------

    private static byte[] FosterFrame(uint cardHandle, int[] ticketHandles, int[] crackerHandles)
    {
        var length = GameFarmPackets.GetFosterSize(ticketHandles.Length, crackerHandles.Length);
        var packet = ClientFrame((ushort)GamePackets.TM_CS_FOSTER_CREATURE, length);

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), cardHandle);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(11, 4), ticketHandles.Length);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(15, 4), crackerHandles.Length);

        var offset = 19;
        for (var i = 0; i < ticketHandles.Length; i++, offset += 8)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(offset, 4), (uint)ticketHandles[i]);
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(offset + 4, 4), i + 1);
        }

        for (var i = 0; i < crackerHandles.Length; i++, offset += 8)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(offset, 4), (uint)crackerHandles[i]);
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(offset + 4, 4), i + 5);
        }

        return packet;
    }

    [Test]
    public void FosterRequest_TheRealClientFormIsTwentySevenBytes()
    {
        // The client always sends one ticket and zero or one cracker: its own length arithmetic ends at
        // 19 + 8 x T + 8 x C, i.e. 27 here.
        GameFarmPackets.GetFosterSize(1, 0).Should().Be(27);
        GameFarmPackets.GetFosterSize(1, 1).Should().Be(35);

        var packet = FosterFrame(0x0A0B0C0D, new[] { 21 }, Array.Empty<int>());

        packet.Length.Should().Be(27);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x0A0B0C0Du,
            "creature_card_handle is the uint32 at offset 7");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(1, "ticket_info count is at 11");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(15, 4)).Should().Be(0, "cracker_info count is at 15");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(19, 4)).Should().Be(21u,
            "the first ticket_info entry starts at 19, ticket_handle at +0");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(23, 4)).Should().Be(1, "ticket_count is at +4");

        GameFarmPackets.TryReadFosterCreature(packet, out var request).Should().BeTrue();
        request.CreatureCardHandle.Should().Be(0x0A0B0C0Du);
        request.Tickets.Should().HaveCount(1);
        request.Tickets[0].TicketHandle.Should().Be(21u);
        request.Tickets[0].TicketCount.Should().Be(1);
        request.Crackers.Should().BeEmpty();
    }

    [Test]
    public void FosterRequest_OneTicketAndOneCracker_ChainsBothArrays()
    {
        var packet = FosterFrame(99, new[] { 21 }, new[] { 33 });

        packet.Length.Should().Be(35);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(27, 4)).Should().Be(33u,
            "cracker_info follows ticket_info with no padding: 19 + 8 = 27");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(31, 4)).Should().Be(5, "cracker_count is at +4");

        GameFarmPackets.TryReadFosterCreature(packet, out var request).Should().BeTrue();
        request.Tickets.Should().HaveCount(1);
        request.Crackers.Should().HaveCount(1);
        request.Crackers[0].CrackerHandle.Should().Be(33u);
        request.Crackers[0].CrackerCount.Should().Be(5);
    }

    [Test]
    public void FosterRequest_TwoTicketsAndTwoCrackers_AreFiftyOneBytes()
    {
        // 19 + 8 x 2 + 8 x 2 = 51, with four distinct handles so a wrong stride cannot pass.
        GameFarmPackets.GetFosterSize(2, 2).Should().Be(51);

        var packet = FosterFrame(7, new[] { 101, 102 }, new[] { 201, 202 });

        packet.Length.Should().Be(51);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(19, 4)).Should().Be(101u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(27, 4)).Should().Be(102u,
            "the second ticket starts at 19 + 8");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(35, 4)).Should().Be(201u,
            "the first cracker starts at 19 + 16");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(43, 4)).Should().Be(202u,
            "the second cracker ends the frame at 51");

        GameFarmPackets.TryReadFosterCreature(packet, out var request).Should().BeTrue();
        request.CreatureCardHandle.Should().Be(7u);
        request.Tickets.Should().HaveCount(2);
        request.Tickets[0].TicketHandle.Should().Be(101u);
        request.Tickets[1].TicketHandle.Should().Be(102u);
        request.Tickets[1].TicketCount.Should().Be(2);
        request.Crackers.Should().HaveCount(2);
        request.Crackers[0].CrackerHandle.Should().Be(201u);
        request.Crackers[1].CrackerHandle.Should().Be(202u);
        request.Crackers[1].CrackerCount.Should().Be(6);
    }

    [Test]
    public void FosterRequest_WithNoStackAtAll_IsTheNineteenByteMinimum()
    {
        var packet = FosterFrame(5, Array.Empty<int>(), Array.Empty<int>());

        packet.Length.Should().Be(19);
        GameFarmPackets.TryReadFosterCreature(packet, out var request).Should().BeTrue();
        request.CreatureCardHandle.Should().Be(5u);
        request.Tickets.Should().BeEmpty();
        request.Crackers.Should().BeEmpty();
    }

    [TestCase(0, TestName = "FosterRequest_RejectsAnEmptyBuffer")]
    [TestCase(11, TestName = "FosterRequest_RejectsAHandleOnlyFrame")]
    [TestCase(18, TestName = "FosterRequest_RejectsATruncatedHeader")]
    public void FosterRequest_RejectsAFrameShorterThanItsNineteenByteHeader(int length)
    {
        GameFarmPackets.TryReadFosterCreature(new byte[length], out var request).Should().BeFalse();
        request.Should().Be(default(GameFarmPackets.FosterCreatureRequest));
    }

    [Test]
    public void FosterRequest_RejectsALengthThatDoesNotMatchItsCounters()
    {
        // The client writes the length in hard, so a frame announcing one ticket and one cracker but carrying
        // only the ticket array is a malformed frame, not a request with a different count.
        var packet = FosterFrame(1, new[] { 21 }, Array.Empty<int>());
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(15, 4), 1);

        GameFarmPackets.TryReadFosterCreature(packet, out _).Should().BeFalse();
    }

    [Test]
    public void FosterRequest_RejectsAPaddedFrame()
    {
        var packet = new byte[28];
        FosterFrame(1, new[] { 21 }, Array.Empty<int>()).CopyTo(packet, 0);

        GameFarmPackets.TryReadFosterCreature(packet, out _).Should().BeFalse();
    }

    [TestCase(11, TestName = "FosterRequest_RejectsANegativeTicketCount")]
    [TestCase(15, TestName = "FosterRequest_RejectsANegativeCrackerCount")]
    public void FosterRequest_RejectsANegativeCounter(int offset)
    {
        // Both counters are int32 in the reference: a negative one has no meaning and must not be turned into
        // a length or an array size.
        var packet = FosterFrame(1, new[] { 21 }, Array.Empty<int>());
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(offset, 4), -1);

        GameFarmPackets.TryReadFosterCreature(packet, out _).Should().BeFalse();
    }

    [Test]
    public void FosterRequest_RejectsAHugeCounterInsteadOfOverflowing()
    {
        // A 27-byte frame declaring 2 000 000 000 tickets: the declared size is computed in 64 bits, so it
        // cannot wrap into a plausible length, and no array is allocated before the check.
        var packet = FosterFrame(1, new[] { 21 }, Array.Empty<int>());
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(11, 4), int.MaxValue);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(15, 4), int.MaxValue);

        GameFarmPackets.TryReadFosterCreature(packet, out _).Should().BeFalse();
    }

    // --- TM_CS_RETRIEVE_CREATURE (6004) and TM_CS_NURSE_CREATURE (6006) ---------------------------------

    [TestCase((ushort)GamePackets.TM_CS_RETRIEVE_CREATURE, TestName = "RetrieveCreature_ReadsTheHandleAtSeven")]
    [TestCase((ushort)GamePackets.TM_CS_NURSE_CREATURE, TestName = "NurseCreature_ReadsTheHandleAtSeven")]
    public void HandleOnlyFrame_IsElevenBytesAndCarriesTheCardHandle(ushort id)
    {
        GameFarmPackets.CreatureCardHandleLength.Should().Be(11);

        var packet = ClientFrame(id, 11);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), 0xAABBCCDD);

        if (id == (ushort)GamePackets.TM_CS_RETRIEVE_CREATURE)
        {
            GameFarmPackets.TryReadRetrieveCreature(packet, out var handle).Should().BeTrue();
            handle.Should().Be(0xAABBCCDDu, "creature_card_handle is the uint32 at offset 7");
        }
        else
        {
            GameFarmPackets.TryReadNurseCreature(packet, out var handle).Should().BeTrue();
            handle.Should().Be(0xAABBCCDDu, "creature_card_handle is the uint32 at offset 7");
        }
    }

    [TestCase(0, TestName = "HandleOnlyFrame_RetrieveRejectsAnEmptyBuffer")]
    [TestCase(7, TestName = "HandleOnlyFrame_RetrieveRejectsAHeaderOnlyFrame")]
    [TestCase(10, TestName = "HandleOnlyFrame_RetrieveRejectsATruncatedHandle")]
    [TestCase(12, TestName = "HandleOnlyFrame_RetrieveRejectsAPaddedFrame")]
    [TestCase(15, TestName = "HandleOnlyFrame_RetrieveRejectsALongerFrame")]
    public void RetrieveCreature_RejectsAnyLengthOtherThanEleven(int length)
    {
        var packet = ClientFrame((ushort)GamePackets.TM_CS_RETRIEVE_CREATURE, length);

        GameFarmPackets.TryReadRetrieveCreature(packet, out var handle).Should().BeFalse();
        handle.Should().Be(0u);
    }

    [TestCase(0, TestName = "HandleOnlyFrame_NurseRejectsAnEmptyBuffer")]
    [TestCase(7, TestName = "HandleOnlyFrame_NurseRejectsAHeaderOnlyFrame")]
    [TestCase(10, TestName = "HandleOnlyFrame_NurseRejectsATruncatedHandle")]
    [TestCase(12, TestName = "HandleOnlyFrame_NurseRejectsAPaddedFrame")]
    [TestCase(19, TestName = "HandleOnlyFrame_NurseRejectsAFosterSizedFrame")]
    public void NurseCreature_RejectsAnyLengthOtherThanEleven(int length)
    {
        var packet = ClientFrame((ushort)GamePackets.TM_CS_NURSE_CREATURE, length);

        GameFarmPackets.TryReadNurseCreature(packet, out var handle).Should().BeFalse();
        handle.Should().Be(0u);
    }

    [Test]
    public void HandleOnlyFrame_KeepsTheUnsignedHandleTheReferenceDeclares()
    {
        // ar_handle_t is a uint32: 0xFFFFFFFF is a valid handle, not -1.
        var packet = ClientFrame((ushort)GamePackets.TM_CS_RETRIEVE_CREATURE, 11);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), uint.MaxValue);

        GameFarmPackets.TryReadRetrieveCreature(packet, out var handle).Should().BeTrue();
        handle.Should().Be(uint.MaxValue);
    }

    [Test]
    public void FosterFrame_RejectsAFrameBuiltForAnotherId()
    {
        // Same length as the real 27-byte 6002 frame, wrong id: nothing here may answer a frame that is not
        // the farm's.
        var packet = FosterFrame(1, new[] { 21 }, Array.Empty<int>());
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_NURSE_CREATURE);
        packet[6] = Checksum(packet);

        GameFarmPackets.TryReadFosterCreature(packet, out _).Should().BeTrue("the reader only bounds the frame");
        GameFarmPackets.TryReadNurseCreature(packet, out _).Should().BeFalse();
    }

    // --- the receive loop -------------------------------------------------------------------------------

    [Test]
    public void FarmInfoRequest_IsAnsweredWithAnEmptyFarmInfo()
    {
        // The dispatch arm exists, so 6000 cannot reach the "Unknown Packet Type" throw: the reply is the
        // 8-byte 6001 with summons = 0.
        var frame = ClientFrame((ushort)GamePackets.TM_CS_REQUEST_FARM_INFO, HeaderSize);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        connection.Sent.Should().ContainSingle();
        connection.Sent[0].Length.Should().Be(8);
        BinaryPrimitives.ReadUInt16LittleEndian(connection.Sent[0].AsSpan(4, 2)).Should().Be(6001);
        connection.Sent[0][7].Should().Be(0);
    }

    [TestCase((ushort)GamePackets.TM_CS_REQUEST_FARM_INFO, 8, TestName = "Receive_MalformedFarmInfoRequestIsDropped")]
    [TestCase((ushort)GamePackets.TM_CS_FOSTER_CREATURE, 27, TestName = "Receive_FosterCreatureIsReadAndNotAnswered")]
    [TestCase((ushort)GamePackets.TM_CS_RETRIEVE_CREATURE, 11,
        TestName = "Receive_RetrieveCreatureIsReadAndNotAnswered")]
    [TestCase((ushort)GamePackets.TM_CS_NURSE_CREATURE, 11, TestName = "Receive_NurseCreatureIsReadAndNotAnswered")]
    [TestCase((ushort)GamePackets.TM_CS_REQUEST_FARM_MARKET, 7,
        TestName = "Receive_FarmMarketRequestIsReadAndNotAnswered")]
    public void FarmFrames_NeverThrowAndOnlyTheFarmInfoRequestIsAnswered(ushort id, int length)
    {
        var frame = ClientFrame(id, length);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty("no reference establishes an answer for 6002, 6004, 6006 and 6008, and a "
            + "malformed 6000 is only logged");
    }

    [Test]
    public void IncomingServerToClientFarmInfo_IsLoggedAndDroppedWithoutThrowing()
    {
        // 6001 is declared in GamePackets, so it needs an arm before the throwing switch even though the 7.3
        // client never sends it: an incoming one is a protocol anomaly.
        var frame = ClientFrame((ushort)GamePackets.TM_SC_FARM_INFO, 8);
        frame[7] = 0;
        frame[6] = Checksum(frame);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
    }
}
