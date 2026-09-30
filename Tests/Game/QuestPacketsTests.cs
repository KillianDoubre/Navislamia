using System;
using System.Buffers.Binary;
using System.Linq;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// Offset tests for the quest socle: <c>TM_CS_DROP_QUEST</c> (603), <c>TM_CS_QUEST_INFO</c> (604),
/// <c>TM_SC_QUEST_LIST</c> (600) and <c>TM_SC_QUEST_STATUS</c> (601). Both requests are 11 bytes
/// (<c>length = 0xb</c>, the signed code at 7); 600 is <c>11 + 61·N</c> bytes (two <c>uint16</c> counts,
/// then the <c>TS_QUEST_INFO</c> elements, whose step is the client's own <c>0x3d</c>); 601 is a fixed
/// 40 bytes. Every size and offset comes from the Epic 7.3 client structures recorded in
/// <c>docs/packet-specs/socle-quetes.md</c> §3.4-3.5 and §4, and, for 604,
/// <c>docs/packet-specs/604-quest-info.md</c> §3.1.
/// </summary>
[TestFixture]
public class QuestPacketsTests
{
    private const int HeaderSize = 7;

    /// <summary>7 byte header plus the two <c>uint16</c> counters: the client reads its table here.</summary>
    private const int QuestListHeaderSize = HeaderSize + 4;

    private const int QuestInfoSize = 61;
    private const int QuestStatusSize = 40;
    private const int DropQuestRequestSize = HeaderSize + 4;
    private const int QuestInfoRequestSize = HeaderSize + 4;

    [Test]
    public void TryReadDropQuest_ReadsTheSignedCodeAtSeven()
    {
        var packet = new byte[DropQuestRequestSize];
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_DROP_QUEST);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(7, 4), 4242);

        GameActionPackets.TryReadDropQuest(packet, out var request).Should().BeTrue();

        packet.Length.Should().Be(11);
        request.Code.Should().Be(4242);
    }

    [Test]
    public void TryReadDropQuest_KeepsANegativeCodeSigned()
    {
        var packet = new byte[DropQuestRequestSize];
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(7, 4), -1);

        GameActionPackets.TryReadDropQuest(packet, out var request).Should().BeTrue();

        // code is an int32_t in both references: a negative value must keep its sign and be refused as
        // such, never folded into the unsigned 4294967295 (fiche §3.1).
        request.Code.Should().Be(-1);
    }

    [Test]
    public void TryReadDropQuest_RejectsATruncatedFrame()
    {
        var packet = new byte[DropQuestRequestSize - 1];
        packet[7] = 1;

        GameActionPackets.TryReadDropQuest(packet, out var request).Should().BeFalse();

        request.Code.Should().Be(0);
    }

    [Test]
    public void TryReadQuestInfo_ReadsTheSignedCodeAtSeven()
    {
        var packet = new byte[QuestInfoRequestSize];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)QuestInfoRequestSize); // Length @0
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_QUEST_INFO); // ID @4

        // The code is laid out byte by byte so the assertion proves the wire order instead of
        // round-tripping through the same reader: 0x00001092 = 4242.
        packet[7] = 0x92;
        packet[8] = 0x10;
        packet[9] = 0x00;
        packet[10] = 0x00;
        packet[6] = Checksum(packet); // Checksum @6 = sum of bytes 0..5

        GameActionPackets.TryReadQuestInfo(packet, out var request).Should().BeTrue();

        packet.Length.Should().Be(11, "the client's constructor writes Length = 0xb and stops at offset 10");
        GameActionPackets.QuestInfoRequestSize.Should().Be(11);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(11); // Length @0
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(604); // ID @4
        packet[6].Should().Be(Checksum(packet));
        request.Code.Should().Be(4242);
        request.Code.Should().NotBe(unchecked((int)0x92100000u), "code is little-endian on the wire");
    }

    [Test]
    public void TryReadQuestInfo_KeepsANegativeCodeSigned()
    {
        var packet = new byte[QuestInfoRequestSize];
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(7, 4), -1);

        GameActionPackets.TryReadQuestInfo(packet, out var request).Should().BeTrue();

        // code is an int32_t in rzu and in NGemity, and the client copies the dword as is: 0xFFFFFFFF
        // must stay -1, never fold into the unsigned 4294967295 (fiche §3.1, §6 écart 4).
        request.Code.Should().Be(-1);
    }

    [TestCase(7, TestName = "TryReadQuestInfo_RejectsAHeaderOnlyFrame")]
    [TestCase(10, TestName = "TryReadQuestInfo_RejectsATruncatedFrame")]
    [TestCase(12, TestName = "TryReadQuestInfo_RejectsAPaddedFrame")]
    [TestCase(QuestStatusSize, TestName = "TryReadQuestInfo_RejectsAQuestStatusSizedFrame")]
    public void TryReadQuestInfo_RejectsAnyLengthOtherThanEleven(int length)
    {
        // The constructor is linear and writes offsets 0 to 10 only, so 604 has no hidden field and no
        // variable tail: any other size is an anomaly, refused rather than partially read — the model of
        // packet 57 and of TM_CS_GET_REGION_INFO.
        var packet = new byte[length];
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_QUEST_INFO);
        if (length >= QuestInfoRequestSize)
        {
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(7, 4), 4242);
        }
        else
        {
            packet[length - 1] = 0x2a;
        }

        GameActionPackets.TryReadQuestInfo(packet, out var request).Should().BeFalse();

        request.Code.Should().Be(0);
    }

    [Test]
    public void OnDataReceived_ConsumesQuestInfoWithoutThrowingOrAnswering()
    {
        var frame = QuestInfoFrame(4242);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(QuestInfoRequestSize);

        receive.Should().NotThrow("604 is declared in GamePackets, so its arm must keep the frame out of the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
        connection.Sent.Should().BeEmpty("nothing answers 604: not 602, not 601");
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopOnAQuestInfoFrameCoalescedWithTheNextOne()
    {
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[6] = Checksum(keepalive);

        var frame = QuestInfoFrame(int.MinValue).Concat(keepalive).ToArray();
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
    }

    /// <summary>Builds a valid <c>TM_CS_QUEST_INFO</c> frame, checksum included.</summary>
    private static byte[] QuestInfoFrame(int code)
    {
        var frame = new byte[QuestInfoRequestSize];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)QuestInfoRequestSize);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), (ushort)GamePackets.TM_CS_QUEST_INFO);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(7, 4), code);
        frame[6] = Checksum(frame);
        return frame;
    }

    [Test]
    public void BuildQuestList_EmitsAnElevenByteFrameWithBothCountsAtZero()
    {
        var packet = GameQuestPackets.BuildQuestList(Array.Empty<QuestListEntry>());

        packet.Should().HaveCount(11);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(600);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(7, 2)).Should().Be(0);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(9, 2)).Should().Be(0);
    }

    [Test]
    public void BuildQuestList_LaysOutTheQuestInfoElementFromEleven()
    {
        var quest = new QuestListEntry(
            Code: 4242,
            StartId: 99,
            Value: new[] { 1, 2, 3, 4, 5, 6 },
            Status: new[] { 10, 20, 30, 40, 50, 60 },
            Progress: 7,
            TimeLimit: 0x12345678);

        var packet = GameQuestPackets.BuildQuestList(new[] { quest });

        packet.Should().HaveCount(QuestListHeaderSize + QuestInfoSize);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(600);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(7, 2)).Should().Be(1);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(9, 2)).Should().Be(0);

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(4242);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(15, 4)).Should().Be(99);

        for (var i = 0; i < 6; i++)
        {
            BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(19 + i * 4, 4)).Should().Be((uint)(i + 1),
                $"value[{i}] sits at element offset {8 + i * 4}, that is frame offset {19 + i * 4}");
            BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(43 + i * 4, 4)).Should().Be((uint)((i + 1) * 10),
                $"status[{i}] sits at element offset {32 + i * 4}, that is frame offset {43 + i * 4}");
        }

        packet[67].Should().Be(7); // element offset 56
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(68, 4)).Should().Be(0x12345678u); // offset 57
    }

    [Test]
    public void BuildQuestList_StepsBySixtyOneBytesPerActiveQuest()
    {
        var packet = GameQuestPackets.BuildQuestList(new[]
        {
            new QuestListEntry(1, 0, null, null, 0, 0),
            new QuestListEntry(2, 0, null, null, 0, 0)
        });

        packet.Should().HaveCount(QuestListHeaderSize + 2 * QuestInfoSize);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(7, 2)).Should().Be(2);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(1);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11 + QuestInfoSize, 4)).Should().Be(2);
    }

    [Test]
    public void BuildQuestList_LeavesThePendingTableEmpty()
    {
        // The 8-byte pending entries are never walked by the 7.3 handler and their content is NOT
        // ESTABLISHED (fiche §6 écart 10, §8.7): the socle writes the count at 9 as 0 and appends
        // nothing, so the frame stops right after the active table.
        var packet = GameQuestPackets.BuildQuestList(new[] { new QuestListEntry(1, 0, null, null, 0, 0) });

        packet.Should().HaveCount(QuestListHeaderSize + QuestInfoSize);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(9, 2)).Should().Be(0);
    }

    [Test]
    public void BuildQuestList_ZeroPadsTheSlotsAStoredStateDoesNotFill()
    {
        var stored = new CharacterQuestEntity
        {
            Code = 5,
            StartId = 6,
            Value = new[] { 1, 2 },
            Status = null,
            Progress = 3,
            TimeLimit = 4
        };

        var packet = GameQuestPackets.BuildQuestList(new[] { stored });

        packet.Should().HaveCount(QuestListHeaderSize + QuestInfoSize);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(5);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(15, 4)).Should().Be(6);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(19, 4)).Should().Be(1);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(23, 4)).Should().Be(2);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(27, 4)).Should().Be(0);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(39, 4)).Should().Be(0); // value[5]
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(43, 4)).Should().Be(0); // status[0]
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(63, 4)).Should().Be(0); // status[5]
        packet[67].Should().Be(3);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(68, 4)).Should().Be(4u);
    }

    [Test]
    public void BuildQuestList_RefusesMoreThanSixSlots()
    {
        var quest = new QuestListEntry(1, 0, new[] { 1, 2, 3, 4, 5, 6, 7 }, null, 0, 0);

        FluentActions.Invoking(() => GameQuestPackets.BuildQuestList(new[] { quest }))
            .Should().Throw<ArgumentException>();
    }

    [Test]
    public void BuildQuestStatus_LaysOutTheSixSlotsAtEleven()
    {
        var quest = new QuestListEntry(
            Code: 4242,
            StartId: 99,
            Value: new[] { 1, 2, 3, 4, 5, 6 },
            Status: new[] { 1, 2, 3, 4, 5, 6 },
            Progress: 5,
            TimeLimit: 0x0A0B0C0D);

        var packet = GameQuestPackets.BuildQuestStatus(quest);

        packet.Should().HaveCount(QuestStatusSize);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(601);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(4242);

        for (var i = 0; i < 6; i++)
        {
            BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11 + i * 4, 4)).Should().Be((uint)(i + 1),
                $"status[{i}] sits at frame offset {11 + i * 4}");
        }

        packet[35].Should().Be(5); // nProgress, the only byte the 7.3 client reads
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(36, 4)).Should().Be(0x0A0B0C0Du);
    }

    [Test]
    public void BuildQuestStatus_KeepsTheTopBitOfAStatusSlot()
    {
        var quest = new QuestListEntry(1, 0, null, new[] { int.MinValue, 0, 0, 0, 0, 0 }, 0, 0);

        var packet = GameQuestPackets.BuildQuestStatus(quest);

        // Status words are opaque uint32 on the wire; the entity keeps the same bits in a signed column,
        // so the frame must carry 0x80000000 and not a folded value.
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(0x80000000u);
    }

    [Test]
    public void QuestFrames_CarryAValidChecksum()
    {
        var list = GameQuestPackets.BuildQuestList(new[] { new QuestListEntry(4242, 99, null, null, 1, 2) });
        var status = GameQuestPackets.BuildQuestStatus(new QuestListEntry(4242, 99, null, null, 1, 2));

        list[6].Should().Be(Checksum(list));
        status[6].Should().Be(Checksum(status));
    }

    [Test]
    public void QuestPackets_CarryTheirEpic73Ids()
    {
        ((ushort)GamePackets.TM_SC_QUEST_LIST).Should().Be(600);
        ((ushort)GamePackets.TM_SC_QUEST_STATUS).Should().Be(601);
        ((ushort)GamePackets.TM_CS_DROP_QUEST).Should().Be(603);
        ((ushort)GamePackets.TM_CS_QUEST_INFO).Should().Be(604);

        Enum.IsDefined(typeof(GamePackets), (ushort)603).Should().BeTrue();

        // 604 is declared and armed: its arm reads the 11-byte frame and answers nothing, so the frame
        // never reaches the final "Unknown Packet Type" throw of GameClient. 602 (quest information, no
        // client handler) and 605 (accept, its own card and branch) are still deliberately absent: a
        // member the receive chain does not handle would kill the receive loop. Enum and dispatch move
        // together (fiche §5.3, §5.4).
        Enum.IsDefined(typeof(GamePackets), (ushort)604).Should().BeTrue();
        Enum.IsDefined(typeof(GamePackets), (ushort)602).Should().BeFalse();
        Enum.IsDefined(typeof(GamePackets), (ushort)605).Should().BeFalse();
    }

    [Test]
    public void QuestDropRules_RefuseANegativeCode()
    {
        QuestDropRules.CheckRequest(0).Should().Be(ResultCode.Success);
        QuestDropRules.CheckRequest(4242).Should().Be(ResultCode.Success);
        QuestDropRules.CheckRequest(int.MaxValue).Should().Be(ResultCode.Success);

        // A negative code can never name a stored quest: NGemity's answer for a quest the player does not
        // carry applies, and no distinct verdict is invented (fiche §8.1).
        QuestDropRules.CheckRequest(-1).Should().Be(ResultCode.NotActable);
        QuestDropRules.CheckRequest(int.MinValue).Should().Be(ResultCode.NotActable);
    }

    private static byte Checksum(byte[] packet)
    {
        byte sum = 0;
        for (var i = 0; i < 6; i++) sum += packet[i];
        return sum;
    }
}
