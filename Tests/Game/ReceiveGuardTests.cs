using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;

namespace Tests.Game;

/// <summary>
/// Frames the receive loop must survive. Each of these used to throw out of the I/O completion callback,
/// which terminates the process, or to spin that callback forever.
/// </summary>
[TestFixture]
public class ReceiveGuardTests
{
    [Test]
    public void MoveRequest_ClaimingMoreWaypointsThanItCarries_IsDroppedWithoutAnEcho()
    {
        // 26 fixed bytes and one waypoint, but a count of 5.
        var frame = MoveRequest(declaredCount: 5, carriedWaypoints: 1);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
    }

    [Test]
    public void MoveRequest_ShorterThanItsFixedPart_IsDroppedWithoutAnEcho()
    {
        var frame = Frame((ushort)GamePackets.TM_CS_MOVE_REQUEST, 20);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
    }

    [Test]
    public void MoveRequest_WellFormed_IsStillEchoed()
    {
        var frame = MoveRequest(declaredCount: 1, carriedWaypoints: 1);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        client.OnDataReceived(connection.BytesAvailable);

        connection.Sent.Should().ContainSingle();
        BinaryPrimitives.ReadUInt16LittleEndian(connection.Sent[0].AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_MOVE);
    }

    [Test]
    public void Frame_DeclaringALengthShorterThanItsHeader_DoesNotSpinTheReceiveLoop()
    {
        // A zero length with a valid checksum: the loop used to read zero bytes and never advance.
        var frame = Frame((ushort)GamePackets.TM_CS_GAME_TIME, 7);
        BinaryPrimitives.WriteUInt32LittleEndian(frame, 0);
        frame[6] = StorageTestHarness.Checksum(frame);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = Task.Run(() => client.OnDataReceived(connection.BytesAvailable));

        receive.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue("the loop must stop on an impossible length");
        connection.Sent.Should().BeEmpty();
    }

    // TM_CS_HUNTAHOLIC_LEAVE_LOBBY (4008): the Epic 7.3 client neither builds this frame nor routes it inbound,
    // but the official server handles it (onHuntaholicLeaveLobby), so since the HuntaHolic lobby exists it is
    // declared with its own arm (socle-huntaholic.md §3). The contract that stays is the one this file guards:
    // the frame never throws out of the receive loop. See docs/packet-specs/4008-huntaholic-leave-lobby.md.
    private const ushort HuntaHolicLeaveLobby = 4008;

    /// <summary>
    /// The id stays out of the enum, and the receive loop survives its frame: that pair is the whole
    /// contract for a packet the client can neither send nor receive.
    /// </summary>
    [Test]
    public void HuntaHolicLeaveLobby_IsDeclaredWithItsOwnArm()
    {
        Enum.IsDefined(typeof(GamePackets), HuntaHolicLeaveLobby).Should().BeTrue(
            "the official server answers 4008, and its dispatch arm keeps it away from the throwing switch");
    }

    [Test]
    public void HuntaHolicLeaveLobby_FrameIsSevenBytesOfHeaderAndNoPayload()
    {
        // Written out byte by byte instead of through a helper: the offsets are the assertion.
        var frame = new byte[] { 0x07, 0x00, 0x00, 0x00, 0xa8, 0x0f, 0x00 };
        frame[6] = StorageTestHarness.Checksum(frame);

        frame.Should().HaveCount(7, "the packet is header-only: 7 bytes, the size of the rzu header");
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(0, 4)).Should().Be(7);
        BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(0, 4)).Should().NotBe(7,
            "the length is little-endian on the wire");
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)).Should().Be(HuntaHolicLeaveLobby).And.Be(
            0x0FA8);
        BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(4, 2)).Should().NotBe(HuntaHolicLeaveLobby,
            "the id is little-endian on the wire");
        frame[6].Should().Be(StorageTestHarness.Checksum(frame), "the checksum sits at offset 6");

        // Nothing follows the header: the declared length and the bytes actually carried are the same.
        BinaryPrimitives.ReadUInt32LittleEndian(frame).Should().Be((uint)frame.Length,
            "no payload byte is carried for 4008");
        Marshal.SizeOf<Header>().Should().Be(7, "header-only means the frame ends at offset 6");

        // The same bytes through the production parser must land on the same fields.
        var header = new Header(frame);
        header.Length.Should().Be(7);
        header.ID.Should().Be(HuntaHolicLeaveLobby);
        header.Checksum.Should().Be(StorageTestHarness.Checksum(frame));
    }

    [Test]
    public void HuntaHolicLeaveLobby_FrameIsConsumedWithoutAnAnswerAndWithoutThrowing()
    {
        var frame = new byte[] { 0x07, 0x00, 0x00, 0x00, 0xa8, 0x0f, 0x00 };
        frame[6] = StorageTestHarness.Checksum(frame);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow("4008 has its own arm, never reaching the throwing switch");
        connection.Sent.Should().BeEmpty("without the HuntaHolic service nothing answers 4008");
        connection.BytesAvailable.Should().Be(0, "the frame is read out of the stream instead of being re-framed");
    }

    [Test]
    public void HuntaHolicLeaveLobby_FrameDeclaringAnotherLengthThanSevenIsStillSurvived()
    {
        // Longer than the header: the id is dropped before any payload byte is read, so the frame is
        // consumed and the stream stays in sync.
        var longer = HuntaHolicLeaveLobbyFrame(declaredLength: 8, carriedBytes: 8);
        var longerConnection = new StorageTestHarness.FrameConnection(longer);
        var longerClient = StorageTestHarness.NewGameClient(longerConnection);

        var longerReceive = () => longerClient.OnDataReceived(longerConnection.BytesAvailable);

        longerReceive.Should().NotThrow();
        longerConnection.Sent.Should().BeEmpty();
        longerConnection.BytesAvailable.Should().Be(0, "the declared length is what the loop reads");

        // Shorter than its own header: refused by the general header validation, not by this packet.
        var shorter = HuntaHolicLeaveLobbyFrame(declaredLength: 6, carriedBytes: 8);
        var shorterConnection = new StorageTestHarness.FrameConnection(shorter);
        var shorterClient = StorageTestHarness.NewGameClient(shorterConnection);

        var shorterReceive = () => shorterClient.OnDataReceived(shorterConnection.BytesAvailable);

        shorterReceive.Should().NotThrow();
        shorterConnection.Sent.Should().BeEmpty();
    }

    private static byte[] HuntaHolicLeaveLobbyFrame(uint declaredLength, int carriedBytes)
    {
        var frame = new byte[carriedBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, declaredLength);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), HuntaHolicLeaveLobby);
        frame[6] = StorageTestHarness.Checksum(frame);
        return frame;
    }

    private static byte[] MoveRequest(ushort declaredCount, int carriedWaypoints)
    {
        var frame = Frame((ushort)GamePackets.TM_CS_MOVE_REQUEST, 26 + carriedWaypoints * 8);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(24, 2), declaredCount);
        return frame;
    }

    private static byte[] Frame(ushort id, int length)
    {
        var frame = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), id);
        frame[6] = StorageTestHarness.Checksum(frame);
        return frame;
    }
}
