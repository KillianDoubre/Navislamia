using System;
using System.Buffers.Binary;
using System.Linq;
using FluentAssertions;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// TM_CS_TURN_OFF_PK_MODE (801) is the 7.3 client's PK mode switch in the extinguishing direction: a
/// <b>header-only</b> frame of exactly 7 bytes with an empty body (the client's own constructor writes
/// Length = 7, id 0x321, and no field after the header), and it has no server to client answer — the state
/// reaches the client through bit 11 of the status mask of TM_SC_STATUS_CHANGE (500) only, the same bit the
/// client tests to choose between its two packets. rzu renames the id to 1801 from EPIC_9_6_3 on, so 1801
/// must stay undeclared for this Epic.
/// See docs/packet-specs/801-turn-off-pk-mode.md §3.1, §5.1-§5.5.
/// </summary>
[TestFixture]
public class TurnOffPkModePacketsTests
{
    private const int PacketLength = 7;
    private const int StatusChangeLength = 15;
    private const int StatusOffset = 11;
    private const byte ClientChecksum = 0x2B;
    private const uint PkBit = 1u << 11;

    /// <summary>Builds the client frame exactly as SFrame.exe assembles it: Length, ID, checksum, no body.</summary>
    private static byte[] ClientFrame()
    {
        var frame = new byte[PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), (ushort)GamePackets.TM_CS_TURN_OFF_PK_MODE);
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

    /// <summary>The frame of another id, built the way the client would build it.</summary>
    private static byte[] FrameOf(ushort id)
    {
        var frame = ClientFrame();
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), id);
        frame[6] = StorageTestHarness.Checksum(frame);
        return frame;
    }

    private static (GameClient Client, StorageTestHarness.FrameConnection Connection,
        Navislamia.Game.Network.Clients.ConnectionInfo Info) NewClient(byte[] frame)
    {
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 0x40000001u;
        return (client, connection, info);
    }

    [Test]
    public void Ids_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_CS_TURN_OFF_PK_MODE).Should().Be(801);

        // rzu gates the id to 1801 from EPIC_9_6_3 on; 0x070300 is below it, so 7.3 stays on 801 and the
        // 9.6.3 value must not be declared here.
        Enum.IsDefined(typeof(GamePackets), (ushort)1801).Should().BeFalse();

        // The id must be defined, otherwise OnDataReceived drops the frame as "Undefined packet ID" before
        // any dispatch — the very bug this lot exists to fix.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_TURN_OFF_PK_MODE).Should().BeTrue();
    }

    [Test]
    public void ClientFrame_IsSevenBytes_WithLengthAtZeroIdAtFourAndChecksumAtSix()
    {
        var frame = ClientFrame();

        frame.Should().HaveCount(PacketLength, "the frame is its own header: an empty body");
        GameActionPackets.TurnOffPkModeLength.Should().Be(PacketLength);

        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(0, 4)).Should().Be(PacketLength,
            "Length sits at offset 0 and reads 7");
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)).Should().Be(801,
            "ID sits at offset 4 and reads 0x321");
        frame[6].Should().Be(StorageTestHarness.Checksum(frame),
            "the checksum is the sum of the first six header bytes");
        frame[6].Should().Be(ClientChecksum, "the client's own sum for 07 00 00 00 21 03 is 43");
        frame.Should().Equal(new byte[] { 0x07, 0x00, 0x00, 0x00, 0x21, 0x03, 0x2B },
            "the seven bytes the client puts on the wire, exactly as the fiche records them");
    }

    [Test]
    public void TryReadTurnOffPkMode_AcceptsTheSevenByteFrame()
    {
        GameActionPackets.TryReadTurnOffPkMode(ClientFrame()).Should().BeTrue();
    }

    [TestCase(6, TestName = "TryReadTurnOffPkMode_RefusesAFrameShorterThanTheHeader")]
    [TestCase(8, TestName = "TryReadTurnOffPkMode_RefusesAPaddedFrame")]
    [TestCase(15, TestName = "TryReadTurnOffPkMode_RefusesALongerFrame")]
    public void TryReadTurnOffPkMode_RefusesEveryOtherLength(int length)
    {
        GameActionPackets.TryReadTurnOffPkMode(MalformedFrame(length)).Should().BeFalse();
        GameActionPackets.TryReadTurnOffPkMode(new byte[length]).Should().BeFalse();
    }

    [Test]
    public void OnDataReceived_TurnsThePkModeOffAndPublishesTheStatusMaskAsTheOnlyAnswer()
    {
        var (client, connection, info) = NewClient(ClientFrame());
        info.PkMode = true;

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        info.PkMode.Should().BeFalse();
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");

        connection.Sent.Should().ContainSingle("no TM_SC_* PK answer exists: the mask is the only channel");
        var sent = connection.Sent[0];
        sent.Should().HaveCount(StatusChangeLength);
        BinaryPrimitives.ReadUInt32LittleEndian(sent.AsSpan(0, 4)).Should().Be(StatusChangeLength);
        BinaryPrimitives.ReadUInt16LittleEndian(sent.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_STATUS_CHANGE);
        sent[6].Should().Be(StorageTestHarness.Checksum(sent));
        BinaryPrimitives.ReadUInt32LittleEndian(sent.AsSpan(7, 4)).Should().Be(0x40000001u,
            "the handle sits at offset 7");
        BinaryPrimitives.ReadUInt32LittleEndian(sent.AsSpan(StatusOffset, 4)).Should().Be(0u,
            "the status sits at offset 11 and bit 11 is now clear");
    }

    [Test]
    public void OnDataReceived_ClearsThePkBitWithoutTouchingTheOtherFlagsOfTheSnapshot()
    {
        // The mask is an instant snapshot of all four flags, never a delta: a sitting player who turns PK
        // mode off must stay sitting. This is the same invariant the socle keeps for /sitdown and /battle.
        var (client, connection, info) = NewClient(ClientFrame());
        info.PkMode = true;
        info.IsSitting = true;
        info.IsBattleMode = true;

        client.OnDataReceived(connection.BytesAvailable);

        connection.Sent.Should().ContainSingle();
        var mask = BinaryPrimitives.ReadUInt32LittleEndian(connection.Sent[0].AsSpan(StatusOffset, 4));
        mask.Should().Be(ActorStatus.ForPlayer(pkModeOn: false, sitting: true, battleMode: true));
        (mask & PkBit).Should().Be(0u, "the PK bit is the one flag 801 clears");
        (mask & CreatureStatus.PlayerSitdown).Should().Be(CreatureStatus.PlayerSitdown);
        (mask & CreatureStatus.BattleMode).Should().Be(CreatureStatus.BattleMode);
    }

    [Test]
    public void OnDataReceived_TurningTheModeOffTwiceKeepsTheSameMask()
    {
        // The frame is not a toggle: the client sends 801 as long as its own bit 11 is set, and a second 801
        // must therefore republish the same instant snapshot, never flip the bit back on.
        var frame = ClientFrame().Concat(ClientFrame()).ToArray();
        var (client, connection, info) = NewClient(frame);
        info.PkMode = true;

        client.OnDataReceived(connection.BytesAvailable);

        info.PkMode.Should().BeFalse();
        connection.Sent.Should().HaveCount(2);
        connection.Sent[1].Should().Equal(connection.Sent[0]);
        BinaryPrimitives.ReadUInt32LittleEndian(connection.Sent[1].AsSpan(StatusOffset, 4)).Should().Be(0u);
    }

    [TestCase(8, TestName = "OnDataReceived_ConsumesAPaddedFrameWithoutTurningTheModeOff")]
    [TestCase(15, TestName = "OnDataReceived_ConsumesALongerFrameWithoutTurningTheModeOff")]
    public void OnDataReceived_ConsumesAMalformedFrameWithoutAnAnswer(int length)
    {
        var (client, connection, info) = NewClient(MalformedFrame(length));
        info.PkMode = true;

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        info.PkMode.Should().BeTrue("a frame of another length is refused before anything is applied");
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "a refused frame is still consumed entirely");
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        var frame = ClientFrame().Concat(KeepAliveFrame()).ToArray();
        var (client, connection, info) = NewClient(frame);
        info.PkMode = true;

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        info.PkMode.Should().BeFalse();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
        connection.Sent.Should().ContainSingle("the keepalive is consumed silently");
    }

    [Test]
    public void OnDataReceived_ConsumesTheUndeclaredTwin800WithoutThrowing()
    {
        // 800 (turn on) belongs to its own lot and stays undeclared while this branch is alone: an id outside
        // GamePackets is logged "Undefined packet ID" and dropped, which keeps the receive loop alive and the
        // connection open until that lot lands. The test is written to survive that merge: starting from a PK
        // mode already on, the session reads the same before (800 dropped) and after (800 sets it on), so
        // nothing here asserts a state the merge would change.
        var (client, connection, info) = NewClient(FrameOf(800));
        info.PkMode = true;

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "an undeclared frame is still consumed");
        info.PkMode.Should().BeTrue("801 clears the mode, it can never set it");
    }

    [Test]
    public void SendActorStatus_PublishesTheWholeSnapshot_WithThePkBitClear()
    {
        // The 801 arm goes through GameClient.SendActorStatus — the single site that composes a player's mask
        // — so the frame it publishes for a given session state is exactly the one a second call produces.
        var (client, connection, info) = NewClient(Array.Empty<byte>());
        info.PkMode = false;
        info.IsWalking = true;

        client.SendActorStatus();

        connection.Sent.Should().ContainSingle();
        var mask = BinaryPrimitives.ReadUInt32LittleEndian(connection.Sent[0].AsSpan(StatusOffset, 4));
        mask.Should().Be(ActorStatus.ForPlayer(pkModeOn: false, walking: true));
        (mask & PkBit).Should().Be(0u);
        (mask & CreatureStatus.PlayerWalking).Should().Be(CreatureStatus.PlayerWalking);
    }

    [Test]
    public void Packet801AndTheGmCommandPublishTheSameFrame()
    {
        // The fiche (§5.3, §5.6) requires the packet and the /pk command to publish the same mask, through one
        // composition site. The command /pk off now reaches the same GameClient.SendActorStatus as this arm
        // (once the twin 800 has merged and removed its private copy); this is the frame-level proof that the
        // state left by the packet is exactly the state the command would publish.
        var (client, connection, info) = NewClient(ClientFrame());
        info.PkMode = true;

        client.OnDataReceived(connection.BytesAvailable);

        connection.Sent.Should().ContainSingle();
        var fromThePacket = connection.Sent[0];

        // Exactly what /pk off reaches after its own mutation: same state, same snapshot, same bytes.
        client.SendActorStatus();

        connection.Sent.Should().HaveCount(2);
        connection.Sent[1].Should().Equal(fromThePacket);
    }
}
