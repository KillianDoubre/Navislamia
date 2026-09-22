using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;

namespace Tests.Game;

/// <summary>
/// TM_CS_DONATE_REWARD (259), the reward request of the donation window, is <c>8 + 3N</c> bytes on the
/// wire: a 7-byte header, the signed record count at offset 7, then N three-byte records
/// (reward_type at 8 + 3k, count at 9 + 3k). A 7.3 client only fills the four slots it declares, so
/// N stays in 0..4 and the frame is 8, 11, 14, 17 or 20 bytes. The answer is the generic
/// TM_SC_RESULT (0), 15 bytes, carrying 259 as its request id.
/// See docs/packet-specs/259-donate-reward.md.
/// </summary>
[TestFixture]
public class DonateRewardPacketsTests
{
    private const int HeaderSize = 7;
    private const int RecordSize = 3;
    private const int MinPacketLength = HeaderSize + 1;
    private const int ResultPacketLength = 15;
    private const int DeclaredRewardSlots = 4;

    /// <summary>Builds the frame the 7.3 client emits: header, record count at 7, then the records.</summary>
    private static byte[] ClientFrame(params (sbyte RewardType, ushort Count)[] rewards)
    {
        var packet = new byte[MinPacketLength + rewards.Length * RecordSize];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_DONATE_REWARD);

        packet[6] = Checksum(packet);
        packet[HeaderSize] = (byte)rewards.Length;

        for (var i = 0; i < rewards.Length; i++)
        {
            packet[MinPacketLength + i * RecordSize] = (byte)rewards[i].RewardType;
            BinaryPrimitives.WriteUInt16LittleEndian(
                packet.AsSpan(MinPacketLength + i * RecordSize + 1, 2), rewards[i].Count);
        }

        return packet;
    }

    /// <summary>
    /// Rebuilds a 259 frame with another announced length and another count byte, keeping a valid checksum:
    /// the receive loop drops an invalid one before any dispatch, which would hide what the test measures.
    /// </summary>
    private static byte[] MalformedFrame(int length, byte count)
    {
        var frame = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)length);

        if (length >= 8)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), (ushort)GamePackets.TM_CS_DONATE_REWARD);
            frame[HeaderSize] = count;
        }

        frame[6] = Checksum(frame);
        return frame;
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

    [Test]
    public void Ids_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_CS_DONATE_REWARD).Should().Be(259);

        // rzu gates the id to 1259 from EPIC_9_6_3 on; 0x070300 is below it, so 7.3 stays on 259 and the
        // 9.6.3 value must not be declared here.
        Enum.IsDefined(typeof(GamePackets), (ushort)1259).Should().BeFalse();

        // The id must be defined, otherwise OnDataReceived drops the frame as "Undefined packet ID" before
        // any dispatch.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_DONATE_REWARD).Should().BeTrue();
    }

    [TestCase(0, TestName = "ClientFrame_IsEightBytesWithoutReward")]
    [TestCase(1, TestName = "ClientFrame_IsElevenBytesWithOneReward")]
    [TestCase(2, TestName = "ClientFrame_IsFourteenBytesWithTwoRewards")]
    [TestCase(3, TestName = "ClientFrame_IsSeventeenBytesWithThreeRewards")]
    [TestCase(4, TestName = "ClientFrame_IsTwentyBytesWithFourRewards")]
    public void ClientFrame_DeclaresItsTotalLength(int rewardCount)
    {
        var rewards = new (sbyte, ushort)[rewardCount];
        for (var i = 0; i < rewardCount; i++)
        {
            rewards[i] = ((sbyte)i, (ushort)(i + 1));
        }

        var packet = ClientFrame(rewards);

        packet.Length.Should().Be(MinPacketLength + rewardCount * RecordSize);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be((uint)packet.Length,
            "the client writes the total length, header included");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(259);
        packet[6].Should().Be(Checksum(packet), "the checksum is the sum of the first six header bytes");
        packet[HeaderSize].Should().Be((byte)rewardCount, "the record count sits at offset 7");
    }

    [Test]
    public void ClientFrame_LaysOutThreeByteRecordsWithoutPadding()
    {
        var packet = ClientFrame(((sbyte)0, (ushort)30000), ((sbyte)1, (ushort)10000),
            ((sbyte)2, (ushort)5000), ((sbyte)3, (ushort)1000));

        packet.Length.Should().Be(20);
        packet[HeaderSize].Should().Be(4);

        packet[8].Should().Be(0);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(9, 2)).Should().Be((ushort)30000);

        packet[11].Should().Be(1);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(12, 2)).Should().Be((ushort)10000);

        packet[14].Should().Be(2);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(15, 2)).Should().Be((ushort)5000);

        packet[17].Should().Be(3);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(18, 2)).Should().Be((ushort)1000);
    }

    [Test]
    public void TryReadDonateReward_ReadsTheRecordCountAtOffsetSeven()
    {
        GameActionPackets.TryReadDonateReward(ClientFrame(((sbyte)2, (ushort)7)), out var rewards)
            .Should().BeTrue();

        rewards.Should().HaveCount(1);
        rewards[0].RewardType.Should().Be(2);
        rewards[0].Count.Should().Be(7);
    }

    [Test]
    public void TryReadDonateReward_ReadsEveryRecordInOrder()
    {
        var packet = ClientFrame(((sbyte)3, (ushort)65535), ((sbyte)0, (ushort)1), ((sbyte)2, (ushort)1000));

        GameActionPackets.TryReadDonateReward(packet, out var rewards).Should().BeTrue();

        rewards.Should().HaveCount(3);
        rewards[0].Should().Be(new GameActionPackets.DonateRewardEntry(3, 65535));
        rewards[1].Should().Be(new GameActionPackets.DonateRewardEntry(0, 1));
        rewards[2].Should().Be(new GameActionPackets.DonateRewardEntry(2, 1000));
    }

    [Test]
    public void TryReadDonateReward_AcceptsTheEmptySelection()
    {
        // N = 0 is a legitimate frame: nothing cancels the emission when all four lines are zero, so the
        // server must treat it as a selection, not as corruption.
        var packet = ClientFrame();

        packet.Length.Should().Be(8);
        GameActionPackets.TryReadDonateReward(packet, out var rewards).Should().BeTrue();
        rewards.Should().BeEmpty();
    }

    [Test]
    public void TryReadDonateReward_ReadsTheQuantityLittleEndian()
    {
        // The two payload bytes are laid down by hand — 04 03 — so a big-endian read would give 0x0403
        // (1027) instead of the little-endian 0x0304 (772).
        var frame = ClientFrame(((sbyte)0, (ushort)1));
        frame[9] = 0x04;
        frame[10] = 0x03;

        GameActionPackets.TryReadDonateReward(frame, out var rewards).Should().BeTrue();

        rewards[0].Count.Should().Be(772);
        rewards[0].Count.Should().NotBe(1027, "rzu writes the uint16 as it stands on x86, so little-endian");
    }

    [TestCase(0, TestName = "TryReadDonateReward_RejectsAnEmptyFrame")]
    [TestCase(6, TestName = "TryReadDonateReward_RejectsATruncatedHeader")]
    [TestCase(7, TestName = "TryReadDonateReward_RejectsAHeaderOnlyFrame")]
    public void TryReadDonateReward_RejectsAFrameShorterThanEightBytes(int length)
    {
        GameActionPackets.TryReadDonateReward(new byte[length], out var rewards).Should().BeFalse();
        rewards.Should().BeNull();
    }

    [TestCase(9, TestName = "TryReadDonateReward_RejectsANineByteFrame")]
    [TestCase(10, TestName = "TryReadDonateReward_RejectsATenByteFrame")]
    [TestCase(12, TestName = "TryReadDonateReward_RejectsATwelveByteFrame")]
    [TestCase(21, TestName = "TryReadDonateReward_RejectsAFrameOfFiveRecords")]
    public void TryReadDonateReward_RejectsALengthOutsideTheEightPlusThreeNFamily(int length)
    {
        var packet = MalformedFrame(length, 4);

        GameActionPackets.TryReadDonateReward(packet, out var rewards).Should().BeFalse();
        rewards.Should().BeNull();
    }

    [TestCase(2, TestName = "TryReadDonateReward_RejectsACountBelowTheRecords")]
    [TestCase(0, TestName = "TryReadDonateReward_RejectsACountAboveTheRecords")]
    public void TryReadDonateReward_RejectsACountDisagreeingWithTheLength(int announcedCount)
    {
        // 11 bytes announce N = 1: the client derives the length and the count from the same N, so a
        // disagreement is a non conforming frame rather than another accepted form.
        var packet = MalformedFrame(11, (byte)announcedCount);

        GameActionPackets.TryReadDonateReward(packet, out var rewards).Should().BeFalse();
        rewards.Should().BeNull();
    }

    [TestCase(5, TestName = "TryReadDonateReward_RejectsFiveRecords")]
    [TestCase(0x80, TestName = "TryReadDonateReward_RejectsANegativeSignedCount")]
    [TestCase(0xFF, TestName = "TryReadDonateReward_RejectsAMinusOneSignedCount")]
    public void TryReadDonateReward_RejectsACountAboveTheFourDeclaredSlots(int announcedCount)
    {
        // rzu types the count int8_t (so 0x80 and 0xFF are negative) and a 7.3 client only visits its four
        // declared lines once.
        var packet = MalformedFrame(MinPacketLength + 5 * RecordSize, (byte)announcedCount);

        GameActionPackets.TryReadDonateReward(packet, out var rewards).Should().BeFalse();
        rewards.Should().BeNull();
    }

    [TestCase(4, TestName = "TryReadDonateReward_RejectsTheFifthSlotIndex")]
    [TestCase(0xFF, TestName = "TryReadDonateReward_RejectsAMinusOneSlotIndex")]
    public void TryReadDonateReward_RejectsASlotIndexOutsideTheFourDeclaredOnes(byte rewardType)
    {
        var packet = ClientFrame(((sbyte)rewardType, (ushort)10));

        GameActionPackets.TryReadDonateReward(packet, out var rewards).Should().BeFalse();
        rewards.Should().BeNull();
    }

    [Test]
    public void TryReadDonateReward_RejectsADuplicatedSlotIndex()
    {
        // The client walks each of its four lines once, so the same slot cannot appear twice.
        var packet = ClientFrame(((sbyte)1, (ushort)10), ((sbyte)1, (ushort)20));

        GameActionPackets.TryReadDonateReward(packet, out var rewards).Should().BeFalse();
        rewards.Should().BeNull();
    }

    [Test]
    public void TryReadDonateReward_RejectsAZeroQuantity()
    {
        // The client writes a record only for a non zero line, so a zero quantity is a hand written frame.
        var packet = ClientFrame(((sbyte)0, (ushort)0));

        GameActionPackets.TryReadDonateReward(packet, out var rewards).Should().BeFalse();
        rewards.Should().BeNull();
    }

    [Test]
    public void TryReadDonateReward_RejectsAShortFrameBeforeReadingARecord()
    {
        // 13 bytes for N = 2: the second record is one byte short, and no byte of either record may be read.
        var packet = ClientFrame(((sbyte)0, (ushort)10), ((sbyte)1, (ushort)20));
        var truncated = packet.AsSpan(0, packet.Length - 1).ToArray();

        GameActionPackets.TryReadDonateReward(truncated, out var rewards).Should().BeFalse();
        rewards.Should().BeNull();
    }

    [Test]
    public void OnDataReceived_AnswersTheResultBlockOfRequest259()
    {
        var connection = new FrameConnection(ClientFrame(((sbyte)0, (ushort)30000), ((sbyte)2, (ushort)1000)));
        var client = NewGameClient(connection);
        var frameLength = connection.BytesAvailable;

        var receive = () => client.OnDataReceived(frameLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");

        connection.Sent.Should().HaveCount(1, "the client has a TM_SC_RESULT block for request id 259");

        var answer = connection.Sent[0];
        answer.Length.Should().Be(ResultPacketLength);
        BinaryPrimitives.ReadUInt32LittleEndian(answer.AsSpan(0, 4)).Should().Be(ResultPacketLength);
        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_RESULT);
        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(HeaderSize, 2)).Should().Be(259);
        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(HeaderSize + 2, 2))
            .Should().Be((ushort)ResultCode.Success);
        BinaryPrimitives.ReadInt32LittleEndian(answer.AsSpan(HeaderSize + 4, 4)).Should().Be(0,
            "no value is established for this request, and the client renders it as a plain code");
    }

    [Test]
    public void OnDataReceived_AcceptsTheEmptySelectionWithASuccess()
    {
        var connection = new FrameConnection(ClientFrame());
        var client = NewGameClient(connection);

        client.OnDataReceived(connection.BytesAvailable);

        connection.Sent.Should().HaveCount(1);
        BinaryPrimitives.ReadUInt16LittleEndian(connection.Sent[0].AsSpan(HeaderSize + 2, 2))
            .Should().Be((ushort)ResultCode.Success);
    }

    [TestCase(9, TestName = "OnDataReceived_RefusesANineByteFrame")]
    [TestCase(12, TestName = "OnDataReceived_RefusesATwelveByteFrame")]
    [TestCase(21, TestName = "OnDataReceived_RefusesAFrameOfFiveRecords")]
    public void OnDataReceived_AnswersInvalidArgumentOnANonConformingFrame(int length)
    {
        // A non conforming envelope gets an acknowledgement of refusal rather than a silent log, and the
        // receive loop must keep going: the frame is consumed either way.
        var frame = MalformedFrame(length, 4);
        var connection = new FrameConnection(frame);
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(length);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0);

        connection.Sent.Should().HaveCount(1);
        BinaryPrimitives.ReadUInt16LittleEndian(connection.Sent[0].AsSpan(HeaderSize, 2)).Should().Be(259);
        BinaryPrimitives.ReadUInt16LittleEndian(connection.Sent[0].AsSpan(HeaderSize + 2, 2))
            .Should().Be((ushort)ResultCode.InvalidArgument);
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        var keepalive = new byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), HeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[6] = Checksum(keepalive);

        var frame = ClientFrame(((sbyte)1, (ushort)500)).Concat(keepalive).ToArray();
        var connection = new FrameConnection(frame);
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
        connection.Sent.Should().HaveCount(1, "only the 259 frame is answered");
    }

    private static GameClient NewGameClient(FrameConnection connection)
    {
        var networkService = new NetworkService(
            A.Fake<ILogger<NetworkService>>(),
            Options.Create(new NetworkOptions { CipherKey = "donate-reward-test-key" }),
            A.Fake<ICharacterService>(),
            A.Fake<IBannedWordsRepository>(),
            A.Fake<IStatService>(),
            Options.Create(new ServerOptions()),
            A.Fake<INpcSpawnService>(),
            A.Fake<INpcDialogService>(),
            A.Fake<IMonsterSpawnService>(),
            A.Fake<ICombatService>(),
            A.Fake<ILevelingService>(),
            A.Fake<ISkillService>(),
            A.Fake<IEquipmentService>(),
            A.Fake<IInventoryService>(),
            A.Fake<IGroundItemService>(),
            A.Fake<ISkillCastService>(),
            A.Fake<IFieldPropService>(),
            A.Fake<IItemUseService>());

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        return new GameClient(socket, networkService) { Connection = connection };
    }

    /// <summary>
    /// In-memory replacement for the socket-backed connection: it serves a plaintext frame byte by byte
    /// and records everything the receive loop pushes back, so a test can tell an ignored packet from an
    /// answered one.
    /// </summary>
    private sealed class FrameConnection : Connection
    {
        private readonly byte[] _frame;
        private int _offset;

        public FrameConnection(byte[] frame)
            : base(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            _frame = frame;
        }

        public List<byte[]> Sent { get; } = new();

        public int BytesAvailable => _frame.Length - _offset;

        public override ReadOnlySpan<byte> Peek(int length) => new(_frame, _offset, length);

        public override byte[] Read(int input)
        {
            var length = Math.Min(BytesAvailable, input);
            var read = _frame.AsSpan(_offset, length).ToArray();
            _offset += length;
            return read;
        }

        public override void Send(byte[] buffer) => Sent.Add(buffer);
    }
}
