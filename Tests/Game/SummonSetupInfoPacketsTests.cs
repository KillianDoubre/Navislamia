using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
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
/// TM_CS_GET_SUMMON_SETUP_INFO (324) is 8 bytes on the wire (7-byte header + one show_dialog byte at offset 7)
/// and its answer is the very TM_EQUIP_SUMMON (303) of world entry: 32 bytes, an open_dialog byte at offset 7
/// and the six formation handles at offsets 8, 12, 16, 20, 24 and 28. The open_dialog byte replays the
/// show_dialog of the request; nothing else of the request is used.
/// See docs/packet-specs/324-get-summon-setup-info.md.
/// </summary>
[TestFixture]
public class SummonSetupInfoPacketsTests
{
    private const int PacketLength = 8;
    private const int ResponseLength = 32;
    private const int RequestOffset = 7;
    private const int DialogOffset = 7;
    private const int SlotsOffset = 8;

    /// <summary>Builds the 8-byte client frame: Length, ID, checksum, then show_dialog at offset 7.</summary>
    private static byte[] ClientFrame(byte showDialog)
    {
        var packet = new byte[PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_GET_SUMMON_SETUP_INFO);

        packet[6] = Checksum(packet);
        packet[RequestOffset] = showDialog;
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

    private static long HandleAt(byte[] frame, int slot)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(SlotsOffset + slot * 4, 4));
    }

    [Test]
    public void Ids_AreTheEpic73Ones()
    {
        // The 7.3 id is 324 in hard on the client side (0x144 at VA 0x48c662, Length = 8); rzu only remaps it
        // to 1324 from EPIC_9_6_3 on, and EPIC_7_3 = 0x070300 sits below 0x090603, so no 1324 variant may
        // exist in this enum. See section 4 of the sheet.
        ((ushort)GamePackets.TM_CS_GET_SUMMON_SETUP_INFO).Should().Be(324);
        ((ushort)GamePackets.TM_EQUIP_SUMMON).Should().Be(303);
        Enum.IsDefined(typeof(GamePackets), (ushort)1324).Should().BeFalse();

        // 324 must be defined, otherwise OnDataReceived drops the frame as "Undefined packet ID" before any
        // dispatch and the request is answered with nothing at all.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_GET_SUMMON_SETUP_INFO).Should().BeTrue();
    }

    [Test]
    public void ClientPacket_UsesTheEpic73Layout()
    {
        var packet = ClientFrame(1);

        packet.Length.Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(324);
        packet[6].Should().Be(Checksum(packet), "the checksum is the sum of the first six header bytes");
        packet[RequestOffset].Should().Be(1);
    }

    [Test]
    public void ClientPacket_HasNoFieldOutsideTheHeaderAndShowDialog()
    {
        // 7 + 1: the client writes both the id and the length in hard, so a padded frame is an anomaly, not
        // an extended form.
        ClientFrame(0).Length.Should().Be(PacketLength, "no payload byte exists past offset 7");
    }

    [TestCase(0, false, TestName = "TryReadGetSummonSetupInfo_ReadsFalseForAZeroByte")]
    [TestCase(1, true, TestName = "TryReadGetSummonSetupInfo_ReadsTrueForAOneByte")]
    public void TryReadGetSummonSetupInfo_ReadsTheShowDialogByteAtOffsetSeven(byte value, bool expected)
    {
        GameActionPackets.TryReadGetSummonSetupInfo(ClientFrame(value), out var request).Should().BeTrue();

        request.ShowDialog.Should().Be(expected);
    }

    [TestCase(2, TestName = "TryReadGetSummonSetupInfo_NormalisesTwoToTrue")]
    [TestCase(255, TestName = "TryReadGetSummonSetupInfo_NormalisesTwoHundredFiftyFiveToTrue")]
    public void TryReadGetSummonSetupInfo_ReadsAnyNonZeroByteAsTrue(byte value)
    {
        // A documented normalisation, not an observation: the 7.3 client only ever writes 0 or 1 (it stores
        // the inverted setting), so a wider value is read as "true" rather than refused.
        GameActionPackets.TryReadGetSummonSetupInfo(ClientFrame(value), out var request).Should().BeTrue();

        request.ShowDialog.Should().BeTrue();
    }

    [TestCase(0, TestName = "TryReadGetSummonSetupInfo_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadGetSummonSetupInfo_RejectsAHeaderOnlyFrame")]
    [TestCase(9, TestName = "TryReadGetSummonSetupInfo_RejectsAPaddedFrame")]
    [TestCase(15, TestName = "TryReadGetSummonSetupInfo_RejectsALongFrame")]
    public void TryReadGetSummonSetupInfo_RejectsAnyLengthOtherThanEight(int length)
    {
        var packet = new byte[length];
        if (length >= PacketLength)
        {
            ClientFrame(1).CopyTo(packet, 0);
        }

        GameActionPackets.TryReadGetSummonSetupInfo(packet, out var request).Should().BeFalse();
        request.ShowDialog.Should().BeFalse("a refused frame yields the default request");
    }

    [Test]
    public void Response_UsesTheEpic73Layout()
    {
        var slots = new long[] { 1, 2, 3, 4, 5, 6 };
        var summon = GameCharacterPackets.BuildEquipSummon(slots, openDialog: true);

        summon.Length.Should().Be(ResponseLength);
        BinaryPrimitives.ReadUInt32LittleEndian(summon.AsSpan(0, 4)).Should().Be(ResponseLength);
        BinaryPrimitives.ReadUInt16LittleEndian(summon.AsSpan(4, 2)).Should().Be(303);
        summon[6].Should().Be(Checksum(summon));
        summon[DialogOffset].Should().Be(1, "open_dialog replays the show_dialog of the request");
    }

    [Test]
    public void Response_KeepsTheSixHandlesAtTheirOwnOffsets()
    {
        // Asymmetric values: a shifted or swapped handle would fail here.
        var summon = GameCharacterPackets.BuildEquipSummon(new long[] { 0x01020304, 0x05060708, 0x090a0b0c,
            0x0d0e0f10, 0x11121314, 0x15161718 }, openDialog: true);

        HandleAt(summon, 0).Should().Be(0x01020304);
        HandleAt(summon, 1).Should().Be(0x05060708);
        HandleAt(summon, 2).Should().Be(0x090a0b0c);
        HandleAt(summon, 3).Should().Be(0x0d0e0f10);
        HandleAt(summon, 4).Should().Be(0x11121314);
        HandleAt(summon, 5).Should().Be(0x15161718);
        summon.Length.Should().Be(SlotsOffset + 6 * 4, "the frame stops after the sixth handle");
    }

    [Test]
    public void Response_WritesTheHandleLittleEndian()
    {
        // The bytes are laid down by reading the frame back byte by byte: a big-endian write would give
        // 04 03 02 01 at offset 8.
        var summon = GameCharacterPackets.BuildEquipSummon(new long[] { 0x01020304 }, openDialog: true);

        summon[SlotsOffset].Should().Be(0x04);
        summon[SlotsOffset + 1].Should().Be(0x03);
        summon[SlotsOffset + 2].Should().Be(0x02);
        summon[SlotsOffset + 3].Should().Be(0x01);
        HandleAt(summon, 0).Should().Be(0x01020304u);
    }

    [Test]
    public void Response_ClosesTheDialogByDefault()
    {
        // World entry sends the frame without the parameter: the dialog byte stays at zero, which is what
        // the 7.3 client expects when it has not asked for the window.
        var summon = GameCharacterPackets.BuildEquipSummon(new long[] { 7 });

        summon[DialogOffset].Should().Be(0);
        summon.Length.Should().Be(ResponseLength);
    }

    [Test]
    public void Response_ZerosTheSlotsMissingFromAShortFormation()
    {
        // The column holds at most six handles and may be shorter; the six slots are always written.
        var summon = GameCharacterPackets.BuildEquipSummon(new long[] { 11, 22 }, openDialog: true);

        HandleAt(summon, 0).Should().Be(11);
        HandleAt(summon, 1).Should().Be(22);
        HandleAt(summon, 2).Should().Be(0);
        HandleAt(summon, 3).Should().Be(0);
        HandleAt(summon, 4).Should().Be(0);
        HandleAt(summon, 5).Should().Be(0);
    }

    [TestCase(0, TestName = "OnDataReceived_ReplaysShowDialogFalse")]
    [TestCase(1, TestName = "OnDataReceived_ReplaysShowDialogTrue")]
    public void OnDataReceived_Answers303WithTheFormationAndTheReplayedDialog(byte showDialog)
    {
        var connection = new FrameConnection(ClientFrame(showDialog));
        var client = NewGameClient(connection);
        Session(client).SummonSlots = new long[] { 401, 0, 402 };
        Session(client).CharacterHandle = 77;

        var receive = () => client.OnDataReceived(PacketLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");

        connection.Sent.Should().HaveCount(1, "the 303 is the only answer to 324");
        var answer = connection.Sent[0];
        answer.Length.Should().Be(ResponseLength);
        BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(4, 2)).Should().Be(303);
        answer[DialogOffset].Should().Be(showDialog);
        HandleAt(answer, 0).Should().Be(401);
        HandleAt(answer, 1).Should().Be(0);
        HandleAt(answer, 2).Should().Be(402);
        HandleAt(answer, 5).Should().Be(0);
        answer[6].Should().Be(Checksum(answer));
    }

    [Test]
    public void OnDataReceived_AnswersNothingBeforeTheCharacterEnteredTheWorld()
    {
        // CharacterHandle is zero until the character is in the world: answering there would push a
        // formation belonging to nobody.
        var connection = new FrameConnection(ClientFrame(1));
        var client = NewGameClient(connection);

        var receive = () => client.OnDataReceived(PacketLength);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "the frame is consumed even when it is not answered");
    }

    [TestCase(7, TestName = "OnDataReceived_ConsumesAMalformedHeaderOnlyFrame")]
    [TestCase(9, TestName = "OnDataReceived_ConsumesAMalformedPaddedFrame")]
    public void OnDataReceived_ConsumesAMalformedFrameWithoutThrowing(int length)
    {
        // The client always writes 8: any other announced length is an anomaly, refused without an answer
        // and without leaving bytes in the stream.
        var connection = new FrameConnection(MalformedFrame(length));
        var client = NewGameClient(connection);
        Session(client).CharacterHandle = 77;

        var receive = () => client.OnDataReceived(length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0);
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[6] = Checksum(keepalive);

        var frame = ClientFrame(1).Concat(keepalive).ToArray();
        var connection = new FrameConnection(frame);
        var client = NewGameClient(connection);
        Session(client).CharacterHandle = 77;

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.Sent.Should().HaveCount(1);
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
    }

    /// <summary>
    /// Rebuilds frame 324 with another announced length, keeping a valid checksum: the receive loop rejects
    /// an invalid one before any dispatch, which would hide what this test measures.
    /// </summary>
    private static byte[] MalformedFrame(int length)
    {
        var frame = new byte[length];
        Array.Copy(ClientFrame(1), frame, Math.Min(length, PacketLength));
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)length);
        frame[6] = Checksum(frame);
        return frame;
    }

    /// <summary>
    /// The session of a real GameClient. Client.ConnectionInfo is internal and Tests is not a friend
    /// assembly, so the property is read by reflection: a test concession, not a design choice.
    /// </summary>
    private static ConnectionInfo Session(GameClient client)
    {
        var property = typeof(Client).GetProperty("ConnectionInfo",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        return (ConnectionInfo)property!.GetValue(client)!;
    }

    /// <summary>
    /// The shared harness builds the NetworkService with its current constructor, so this test does not
    /// break each time a service is added to it.
    /// </summary>
    private static GameClient NewGameClient(FrameConnection connection) =>
        StorageTestHarness.NewGameClient(connection);

    /// <summary>
    /// In-memory replacement for the socket-backed connection: it serves a plaintext frame byte by byte and
    /// records everything the receive loop pushes back, so a test can tell an ignored packet from an
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
