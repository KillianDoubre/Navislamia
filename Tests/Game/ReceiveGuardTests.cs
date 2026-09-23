using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using FluentAssertions;
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
