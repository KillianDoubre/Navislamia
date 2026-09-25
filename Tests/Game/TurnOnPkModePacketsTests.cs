using System;
using System.Buffers.Binary;
using System.Linq;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// TM_CS_TURN_ON_PK_MODE (800) is the 7.3 client's PK mode switch: a <b>header-only</b> frame of exactly
/// 7 bytes with an empty body (the client's own constructor writes Length = 7, id 0x320, and no field
/// after the header), and it has no server to client answer — the state reaches the client through bit 11
/// of the status mask of TM_SC_STATUS_CHANGE (500) only. rzu renames the id to 1800 from EPIC_9_6_3 on,
/// so 1800 must stay undeclared for this Epic.
/// See docs/packet-specs/800-turn-on-pk-mode.md §3.1, §5.1-§5.3.
/// </summary>
[TestFixture]
public class TurnOnPkModePacketsTests
{
    private const int PacketLength = 7;
    private const int StatusChangeLength = 15;
    private const int StatusOffset = 11;
    private const uint PkBit = 1u << 11;

    /// <summary>Builds the client frame exactly as SFrame.exe assembles it: Length, ID, checksum, no body.</summary>
    private static byte[] ClientFrame()
    {
        var frame = new byte[PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), (ushort)GamePackets.TM_CS_TURN_ON_PK_MODE);
        frame[6] = StorageTestHarness.Checksum(frame);
        return frame;
    }

    /// <summary>
    /// Rebuilds the frame with another announced length, keeping a valid checksum: the receive loop refuses
    /// an invalid checksum before any dispatch, which would hide what these tests measure. A frame shorter
    /// than seven bytes has no checksum byte at all — the reader is what refuses it.
    /// </summary>
    private static byte[] MalformedFrame(int length)
    {
        var frame = new byte[length];
        Array.Copy(ClientFrame(), frame, Math.Min(length, PacketLength));
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)length);

        if (length >= PacketLength)
        {
            frame[6] = StorageTestHarness.Checksum(frame);
        }

        return frame;
    }

    private static byte[] KeepAliveFrame()
    {
        var frame = new byte[PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        frame[6] = StorageTestHarness.Checksum(frame);
        return frame;
    }

    [Test]
    public void Ids_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_CS_TURN_ON_PK_MODE).Should().Be(800);

        // rzu gates the id to 1800 from EPIC_9_6_3 on; 0x070300 is below it, so 7.3 stays on 800 and the
        // 9.6.3 value must not be declared here.
        Enum.IsDefined(typeof(GamePackets), (ushort)1800).Should().BeFalse();

        // The id must be defined, otherwise OnDataReceived drops the frame as "Undefined packet ID" before
        // any dispatch — the very bug this lot exists to fix.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_TURN_ON_PK_MODE).Should().BeTrue();
    }

    [Test]
    public void ClientFrame_IsSevenBytes_WithLengthAtZeroIdAtFourAndChecksumAtSix()
    {
        var frame = ClientFrame();

        frame.Should().HaveCount(PacketLength, "the frame is its own header: an empty body");
        GameActionPackets.TurnOnPkModeLength.Should().Be(PacketLength);

        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(0, 4)).Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)).Should().Be(800);
        frame[6].Should().Be(StorageTestHarness.Checksum(frame),
            "the checksum is the sum of the first six header bytes");
        frame[6].Should().Be(0x2A, "the client's own sum for 07 00 00 00 20 03 is 42");
    }

    [Test]
    public void TryReadTurnOnPkMode_AcceptsTheSevenByteFrame()
    {
        GameActionPackets.TryReadTurnOnPkMode(ClientFrame()).Should().BeTrue();
    }

    [TestCase(6, TestName = "TryReadTurnOnPkMode_RefusesAFrameShorterThanTheHeader")]
    [TestCase(8, TestName = "TryReadTurnOnPkMode_RefusesAPaddedFrame")]
    [TestCase(15, TestName = "TryReadTurnOnPkMode_RefusesALongerFrame")]
    public void TryReadTurnOnPkMode_RefusesEveryOtherLength(int length)
    {
        GameActionPackets.TryReadTurnOnPkMode(MalformedFrame(length)).Should().BeFalse();
        GameActionPackets.TryReadTurnOnPkMode(new byte[length]).Should().BeFalse();
    }

    [Test]
    public void OnDataReceived_TurnsThePkModeOnAndPublishesTheStatusMaskAsTheOnlyAnswer()
    {
        var frame = ClientFrame();
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);
        var session = StorageTestHarness.Session(client);
        session.CharacterHandle = 0x40000001u;
        session.PkMode = false;

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        session.PkMode.Should().BeTrue();
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");

        connection.Sent.Should().ContainSingle("no TM_SC_* PK answer exists: the mask is the only channel");
        var sent = connection.Sent[0];
        sent.Should().HaveCount(StatusChangeLength);
        BinaryPrimitives.ReadUInt32LittleEndian(sent.AsSpan(0, 4)).Should().Be(StatusChangeLength);
        BinaryPrimitives.ReadUInt16LittleEndian(sent.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_STATUS_CHANGE);
        sent[6].Should().Be(StorageTestHarness.Checksum(sent));
        BinaryPrimitives.ReadUInt32LittleEndian(sent.AsSpan(7, 4)).Should().Be(0x40000001u);
        BinaryPrimitives.ReadUInt32LittleEndian(sent.AsSpan(StatusOffset, 4)).Should().Be(PkBit);
    }

    [Test]
    public void OnDataReceived_TurningTheModeOnTwiceKeepsTheSameMask()
    {
        // The frame is not a toggle: the client sends 800 as long as its own bit 11 is clear, and 801 once
        // it is set. A second 800 must therefore republish the same instant snapshot, never flip the bit back.
        var frame = ClientFrame().Concat(ClientFrame()).ToArray();
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);
        var session = StorageTestHarness.Session(client);
        session.CharacterHandle = 0x40000001u;

        client.OnDataReceived(connection.BytesAvailable);

        session.PkMode.Should().BeTrue();
        connection.Sent.Should().HaveCount(2);
        connection.Sent[1].Should().Equal(connection.Sent[0]);
        BinaryPrimitives.ReadUInt32LittleEndian(connection.Sent[1].AsSpan(StatusOffset, 4)).Should().Be(PkBit);
    }

    [TestCase(8, TestName = "OnDataReceived_ConsumesAPaddedFrameWithoutTurningTheModeOn")]
    [TestCase(15, TestName = "OnDataReceived_ConsumesALongerFrameWithoutTurningTheModeOn")]
    public void OnDataReceived_ConsumesAMalformedFrameWithoutAnAnswer(int length)
    {
        var frame = MalformedFrame(length);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);
        var session = StorageTestHarness.Session(client);
        session.CharacterHandle = 0x40000001u;

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        session.PkMode.Should().BeFalse("a frame of another length is refused before anything is applied");
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "a refused frame is still consumed entirely");
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        var frame = ClientFrame().Concat(KeepAliveFrame()).ToArray();
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);
        var session = StorageTestHarness.Session(client);
        session.CharacterHandle = 0x40000001u;

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        session.PkMode.Should().BeTrue();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
        connection.Sent.Should().ContainSingle("the keepalive is consumed silently");
    }

    [Test]
    public void SendActorStatus_PublishesTheWholeSnapshot_TogetherWithThePkBit()
    {
        // Both send sites — the /pk command and the 800 arm — go through GameClient.SendActorStatus: the
        // mask is a snapshot of all four flags, so the PK bit must not clear the sitting flag.
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var session = StorageTestHarness.Session(client);
        session.CharacterHandle = 0x40000001u;
        session.PkMode = true;
        session.IsSitting = true;

        client.SendActorStatus();

        connection.Sent.Should().ContainSingle();
        var mask = BinaryPrimitives.ReadUInt32LittleEndian(connection.Sent[0].AsSpan(StatusOffset, 4));
        mask.Should().Be(ActorStatus.ForPlayer(pkModeOn: true, sitting: true));
        (mask & PkBit).Should().Be(PkBit);
        (mask & CreatureStatus.PlayerSitdown).Should().Be(CreatureStatus.PlayerSitdown);
    }

    [Test]
    public void Packet800AndTheGmCommandPublishTheSameFrame()
    {
        // The fiche (§5.2) requires both send sites to publish the same mask. The command /pk and the
        // client's own packet now share GameClient.SendActorStatus; this is the frame-level proof that a
        // state set before the packet arrives survives the mask the packet republishes.
        var frame = ClientFrame();
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);
        var session = StorageTestHarness.Session(client);
        session.CharacterHandle = 0x40000001u;
        session.IsWalking = true;

        client.OnDataReceived(connection.BytesAvailable);

        connection.Sent.Should().ContainSingle();
        var fromThePacket = connection.Sent[0];

        // Exactly what the /pk command reaches after its own mutation: same state, same snapshot, same bytes.
        client.SendActorStatus();

        connection.Sent.Should().HaveCount(2);
        connection.Sent[1].Should().Equal(fromThePacket);

        var mask = BinaryPrimitives.ReadUInt32LittleEndian(connection.Sent[1].AsSpan(StatusOffset, 4));
        mask.Should().Be(ActorStatus.ForPlayer(pkModeOn: true, walking: true));
    }

    [Test]
    public void OnDataReceived_ConsumesTheUndeclaredTwin801WithoutThrowing()
    {
        // 801 (turn off) belongs to its own lot and stays undeclared here: an id outside GamePackets is
        // logged "Undefined packet ID" and dropped, which keeps the receive loop alive and the connection
        // open until that lot lands. The test is written to survive that merge — it asserts nothing about
        // what 801 will then publish, only that such a frame never breaks the loop.
        var frame = ClientFrame();
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), 801);
        frame[6] = StorageTestHarness.Checksum(frame);

        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);
        var session = StorageTestHarness.Session(client);
        session.CharacterHandle = 0x40000001u;

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "an undeclared frame is still consumed");
        session.PkMode.Should().BeFalse("801 turns the mode off, it can never turn it on");
    }
}
