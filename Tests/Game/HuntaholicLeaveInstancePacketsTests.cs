using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// TM_CS_HUNTAHOLIC_LEAVE_INSTANCE (4005) is the bare 7-byte header on the wire — <c>Length</c> at 0, the id
/// <c>0x0FA5</c> at 4, the checksum at 6 and no payload byte at all — and it is answered by nothing: the 7.3
/// client's own receive dispatcher routes 4005 to its "unhandled message" branch, and no other packet of the
/// family carries this answer. The client writes the length 7 in hard (0x4c939b), so any other length is an
/// anomaly rather than another form of the request.
/// See docs/packet-specs/4005-huntaholic-leave-instance.md §3, §5.2, §5.3.
/// </summary>
[TestFixture]
public class HuntaholicLeaveInstancePacketsTests
{
    private const int PacketLength = 7;

    /// <summary>
    /// Builds the 7-byte client frame byte by byte: Length, ID and the checksum over the first six bytes. The
    /// frame has no payload, so the checksum is the only value that depends on the header — the id's two bytes
    /// alone make it non-zero (0x07 + 0xA5 + 0x0F = 0xBB), which is what lets the checksum offset be asserted.
    /// </summary>
    private static byte[] ClientFrame()
    {
        var packet = new byte[PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2),
            (ushort)GamePackets.TM_CS_HUNTAHOLIC_LEAVE_INSTANCE);
        packet[6] = Checksum(packet);
        return packet;
    }

    private static byte Checksum(byte[] packet) => StorageTestHarness.Checksum(packet);

    private static byte[] MalformedFrame(int length)
    {
        // The receive loop rejects an invalid checksum before any dispatch, which would hide what these tests
        // measure, so the rebuilt frame keeps a valid one.
        var frame = new byte[length];
        Array.Copy(ClientFrame(), frame, Math.Min(length, PacketLength));
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)length);
        frame[6] = Checksum(frame);
        return frame;
    }

    [Test]
    public void Id_IsTheEpic73One()
    {
        ((ushort)GamePackets.TM_CS_HUNTAHOLIC_LEAVE_INSTANCE).Should().Be(4005);
        ((ushort)GamePackets.TM_CS_HUNTAHOLIC_LEAVE_INSTANCE).Should().Be(0x0FA5);

        // rzu declares it X(4005, true), which expands to `if (true) id = id_;` — the file holds that single
        // unconditional entry, so 7.3 keeps 4005 and no variant id has to be declared. The id must be defined,
        // otherwise OnDataReceived drops the frame as "Undefined packet ID" before any dispatch arm can run.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_HUNTAHOLIC_LEAVE_INSTANCE).Should().BeTrue();
    }

    [Test]
    public void Id_DoesNotCollideWithAnExistingMember()
    {
        // Two names sharing one value would silently route a foreign packet into this handler.
        var values = Enum.GetValues<GamePackets>().Select(value => (ushort)value).ToArray();

        values.Should().OnlyHaveUniqueItems();
        values.Should().Contain(4005);
    }

    [Test]
    public void Id_IsDeclaredUnderTheEpic73IdOnly()
    {
        // The remapped variant of this family (a high/low id pair) would take the shape of a second declared
        // name for the same wire value. rzu has one entry, so there is exactly one name for 4005.
        Enum.GetValues<GamePackets>().Where(value => (ushort)value == 4005).Select(value => value.ToString())
            .Should().BeEquivalentTo(new[] { "TM_CS_HUNTAHOLIC_LEAVE_INSTANCE" });
    }

    [Test]
    public void ClientPacket_UsesTheBareHeaderLayout()
    {
        var packet = ClientFrame();

        packet.Length.Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should()
            .Be(PacketLength, "Length sits at offset 0 and the client writes 7 (0x7) in hard at 0x4c939b");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(4005, "ID sits at offset 4");
        packet[6].Should().Be(Checksum(packet), "the checksum at offset 6 is the sum of the first six bytes");
        packet[6].Should().Be(0xBB, "7 + 0xA5 + 0x0F: the id alone makes the checksum non-zero");
    }

    [Test]
    public void ClientPacket_HasNoFieldOutsideTheHeader()
    {
        // The header is the whole frame: rzu's DEF(_) is empty, NGemity's is empty too, and the client never
        // writes a byte past offset 6.
        Marshal.SizeOf<Header>().Should().Be(PacketLength);
        GameHuntaholicPackets.LeaveInstanceLength.Should().Be(PacketLength,
            "the family constant is the header size, with no payload added");
        GameHuntaholicPackets.LeaveInstanceLength.Should().Be(Marshal.SizeOf<Header>(),
            "a payload field would show up as a length greater than the header");
    }

    [Test]
    public void ClientPacket_AnnouncesItsFieldsLittleEndian()
    {
        // The bytes are laid down by hand — 07 00 00 00 at 0 and A5 0F at 4 — so a big-endian read would give
        // 0x07000000 and 0xA50F instead of the little-endian 7 and 4005.
        var packet = new byte[PacketLength];
        packet[0] = 0x07;
        packet[4] = 0xA5;
        packet[5] = 0x0F;

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(7);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().NotBe(0x07000000,
            "a big-endian read of the same four bytes");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(4005);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().NotBe(0xA50F,
            "a big-endian read of the same two bytes");
    }

    [Test]
    public void IsLeaveInstance_AcceptsTheSevenByteFrame()
    {
        GameHuntaholicPackets.IsLeaveInstance(ClientFrame()).Should().BeTrue();
    }

    [Test]
    public void IsLeaveInstance_TakesNoOutputValue()
    {
        // The reader's contract is that there is nothing to extract: no out parameter, no request type, no
        // field. A gate that returned a value would mean a field exists on the wire and the sheet is wrong.
        var method = typeof(GameHuntaholicPackets).GetMethod("IsLeaveInstance",
            BindingFlags.Public | BindingFlags.Static);

        method.Should().NotBeNull("4005 needs its own reader");
        method!.ReturnType.Should().Be(typeof(bool));
        method.GetParameters().Should().HaveCount(1, "one input span is the whole signature");
        method.GetParameters()[0].ParameterType.Should().Be(typeof(ReadOnlySpan<byte>));
        method.GetParameters()[0].IsOut.Should().BeFalse("4005 carries no field to read out");
    }

    [TestCase(0, TestName = "IsLeaveInstance_RejectsAnEmptyFrame")]
    [TestCase(1, TestName = "IsLeaveInstance_RejectsASingleByte")]
    [TestCase(6, TestName = "IsLeaveInstance_RejectsAFrameShorterThanTheHeader")]
    [TestCase(8, TestName = "IsLeaveInstance_RejectsAPaddedFrame")]
    [TestCase(9, TestName = "IsLeaveInstance_RejectsAFrameWithOneExtraByte")]
    [TestCase(11, TestName = "IsLeaveInstance_RejectsTheLobbyListFrameLength")]
    [TestCase(28, TestName = "IsLeaveInstance_RejectsTheJoinFrameLength")]
    [TestCase(55, TestName = "IsLeaveInstance_RejectsTheCreationFrameLength")]
    public void IsLeaveInstance_RejectsAnyLengthOtherThanSeven(int length)
    {
        // The client writes 7 in hard: 6 bytes is truncated, and anything longer carries bytes no field of the
        // 4005 frame accounts for. 11, 28 and 55 are the lengths of the sibling frames of the family (4000,
        // 4004 and 4003), none of which is another form of the leave gesture.
        var packet = new byte[length];
        ClientFrame().AsSpan(0, Math.Min(length, PacketLength)).CopyTo(packet);

        GameHuntaholicPackets.IsLeaveInstance(packet).Should().BeFalse();
    }

    [Test]
    public void IsLeaveInstance_IgnoresTheChecksumByte()
    {
        // No packet of the family verifies the checksum, and this one is no exception: the reader only measures
        // the frame. The receive loop does check it before dispatching (see
        // OnDataReceived_StopsOnAFrameWithABadChecksum), which is where a wrong checksum is caught.
        var frame = ClientFrame();
        frame[6] = (byte)(frame[6] ^ 0xFF);

        GameHuntaholicPackets.IsLeaveInstance(frame).Should().BeTrue();
    }

    [Test]
    public void OnDataReceived_ConsumesThePacketWithoutThrowing()
    {
        var connection = new StorageTestHarness.FrameConnection(ClientFrame());
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(PacketLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
    }

    [Test]
    public void OnDataReceived_AnswersNothing()
    {
        // No reference answers a 4005: the client routes the id to its "unhandled message" branch, so an
        // answer would be logged as unknown by the client, and the server holds no lobby state to report on.
        var connection = new StorageTestHarness.FrameConnection(ClientFrame());
        var client = StorageTestHarness.NewGameClient(connection);

        client.OnDataReceived(PacketLength);

        connection.Sent.Should().BeEmpty("no reference answers packet 4005");
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        // The frame must not swallow or desynchronise the one behind it: the keepalive is consumed too.
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[6] = Checksum(keepalive);

        var frame = ClientFrame().Concat(keepalive).ToArray();
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
    }

    [TestCase(8, TestName = "OnDataReceived_ConsumesAPaddedFrame")]
    [TestCase(9, TestName = "OnDataReceived_ConsumesAFrameWithOneExtraByte")]
    [TestCase(11, TestName = "OnDataReceived_ConsumesALobbyListSizedFrame")]
    public void OnDataReceived_ConsumesAMalformedFrameWithoutThrowing(int length)
    {
        // The client always writes 7, so a longer frame is an anomaly; it must be refused without sending an
        // answer and without leaving bytes in the stream.
        var frame = MalformedFrame(length);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "a refused frame is still consumed entirely");
    }

    [Test]
    public void OnDataReceived_StopsOnAFrameWithABadChecksum()
    {
        // Pre-existing behaviour of the receive loop, not of this packet: the checksum is verified before any
        // dispatch, and a bad one means the stream is desynchronised, so the frame is left unread and nothing
        // is answered. Pinned here so that no later lot claims 4005 is dispatched with a wrong checksum.
        var frame = ClientFrame();
        frame[6] = (byte)(frame[6] ^ 0xFF);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(PacketLength);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(PacketLength, "the loop stops instead of reading the frame");
    }

    [Test]
    public void Packet_IsDispatchedBeforeTheUnknownPacketThrow()
    {
        // GameClient's dispatch is a chain of ifs, so a member added to the enum without a branch reaches the
        // final switch and its `throw` kills the receive loop. Nothing smaller than a source scan can check that
        // without a live socket.
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "Game", "Network", "Clients", "GameClient.cs"));

        var branch = source.IndexOf(
            "header.ID == (ushort)GamePackets.TM_CS_HUNTAHOLIC_LEAVE_INSTANCE", StringComparison.Ordinal);
        var finalSwitch = source.IndexOf("throw new Exception($\"Unknown Packet Type", StringComparison.Ordinal);

        branch.Should().BeGreaterThan(-1, "4005 needs a branch of its own in OnDataReceived");
        finalSwitch.Should().BeGreaterThan(-1, "the final switch is the guard this test is about");
        branch.Should().BeLessThan(finalSwitch, "4005 must be handled before the final switch throws");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Navislamia.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the repository root is needed to check the dispatch chain");

        return directory!.FullName;
    }
}
