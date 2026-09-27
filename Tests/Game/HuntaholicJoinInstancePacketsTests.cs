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
/// TM_CS_HUNTAHOLIC_JOIN_INSTANCE (4004) is 28 bytes on the wire — the 7-byte header, a signed int32
/// <c>instance_no</c> at offset 7 and a fixed 17-byte password buffer at 11 — and is answered by nothing in this
/// lot: no reference declares a server to client packet for 4004, and the one client reaction identified (the
/// daily quota refusal with <c>result = 0x20</c>) prints the room *creation* notice, so it is not emitted.
/// The client writes the length 28 in hard, so a short or padded frame is refused before any field is read.
/// See docs/packet-specs/4004-huntaholic-join-instance.md §3, §3.4, §5.2, §5.3.
/// </summary>
[TestFixture]
public class HuntaholicJoinInstancePacketsTests
{
    private const int PacketLength = 28;

    /// <summary>
    /// An arbitrary, clearly non-zero room number whose four bytes differ in every position (DE C0 AD 0B on the
    /// wire), so a read at offset 6 or 8, or a big-endian read, returns something visibly different.
    /// </summary>
    private const int InstanceNo = 0x0BADC0DE;

    /// <summary>
    /// Builds the 28-byte client frame: Length, ID, checksum, <c>instance_no</c> at 7 and the password buffer at
    /// 11. The password is written as the client's bounded copy does — at most 16 characters then a NUL, with
    /// nothing after it — and an empty password leaves the whole 17-byte field at zero, which is the public-room
    /// form the client produces.
    /// </summary>
    private static byte[] ClientFrame(int instanceNo, string password = "")
    {
        var packet = new byte[PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2),
            (ushort)GamePackets.TM_CS_HUNTAHOLIC_JOIN_INSTANCE);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(7, 4), instanceNo);

        for (var index = 0; index < password.Length && index < 16; index++)
        {
            packet[11 + index] = (byte)password[index];
        }

        packet[6] = Checksum(packet);
        return packet;
    }

    private static byte Checksum(byte[] packet)
    {
        byte checksum = 0;
        for (var index = 0; index < 6; index++)
        {
            checksum += packet[index];
        }

        return checksum;
    }

    [Test]
    public void Ids_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_CS_HUNTAHOLIC_JOIN_INSTANCE).Should().Be(4004);
        ((ushort)GamePackets.TM_CS_HUNTAHOLIC_JOIN_INSTANCE).Should().Be(0x0FA4);

        // rzu declares it X(4004, true), which expands to `if (true) id = id_;` — a single unconditional entry,
        // so 7.3 keeps 4004 (EPIC_9_6_3's 1000 remap in this family concerns TS_SC_RESULT, not this frame). The
        // id must be defined, otherwise OnDataReceived drops the frame as "Undefined packet ID" before any
        // dispatch arm can run.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_HUNTAHOLIC_JOIN_INSTANCE).Should().BeTrue();
    }

    [Test]
    public void Ids_DoNotCollideWithAnExistingMember()
    {
        // Two names sharing one value would silently route a foreign packet into this handler.
        var values = Enum.GetValues<GamePackets>().Select(value => (ushort)value).ToArray();

        values.Should().OnlyHaveUniqueItems();
        values.Should().Contain(4004);
    }

    [Test]
    public void ClientPacket_UsesTheEpic73Layout()
    {
        var packet = ClientFrame(InstanceNo);

        packet.Length.Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should()
            .Be(PacketLength, "Length sits at offset 0 and the client writes 28 (0x1c) in hard");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(4004, "ID sits at offset 4");
        packet[6].Should().Be(Checksum(packet), "the checksum at offset 6 is the sum of the first six bytes");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(7, 4)).Should()
            .Be(InstanceNo, "instance_no is the first payload field and sits at offset 7");
        packet.AsSpan(11, 17).ToArray().Should().BeEquivalentTo(new byte[17],
            "an empty password leaves the whole 17-byte field at zero");
        packet[27].Should().Be(0, "the password field's last byte is the frame's last byte (11 + 17 - 1)");
    }

    [Test]
    public void ClientPacket_HasNoFieldOutsideTheHeaderInstanceNoAndPassword()
    {
        // 7 + 4 + 17: the two fields of the rzu declaration — `_(simple)(int32_t, instance_no)` and
        // `_(string)(password, 17)` — are the whole payload, with no gap and no padding between them.
        Marshal.SizeOf<Header>().Should().Be(7);
        GameHuntaholicPackets.JoinInstanceLength.Should().Be(PacketLength);
        GameHuntaholicPackets.JoinInstanceNoOffset.Should().Be(7, "instance_no starts right after the header");
        GameHuntaholicPackets.JoinInstanceNoFieldLength.Should().Be(4);
        GameHuntaholicPackets.JoinInstancePasswordOffset.Should().Be(11, "the password starts right after the int32");
        GameHuntaholicPackets.JoinInstancePasswordFieldLength.Should().Be(17);
        (GameHuntaholicPackets.JoinInstancePasswordOffset + GameHuntaholicPackets.JoinInstancePasswordFieldLength)
            .Should().Be(PacketLength, "7 + 4 + 17 covers the frame exactly");
    }

    [Test]
    public void ClientPacket_ChecksumIgnoresThePayload()
    {
        // The client sums the six header bytes only, so editing instance_no or the password in transit does not
        // break the checksum.
        var frame = ClientFrame(InstanceNo, "secret");
        var modified = ClientFrame(InstanceNo + 1, "another");

        modified[6].Should().Be(frame[6]);
    }

    [Test]
    public void TryReadJoinInstance_ReadsInstanceNoAtOffsetSeven()
    {
        GameHuntaholicPackets.TryReadJoinInstance(ClientFrame(InstanceNo), out var request).Should().BeTrue();

        request.InstanceNo.Should().Be(InstanceNo, "the payload starts at offset 7, not 6 and not 8");
    }

    [Test]
    public void TryReadJoinInstance_ReadsInstanceNoLittleEndian()
    {
        // The four payload bytes are laid down by hand — 04 03 02 01 — so a big-endian read would give
        // 0x04030201 (67305985) instead of the little-endian 0x01020304 (16909060).
        var frame = ClientFrame(0);
        frame[7] = 0x04;
        frame[8] = 0x03;
        frame[9] = 0x02;
        frame[10] = 0x01;

        GameHuntaholicPackets.TryReadJoinInstance(frame, out var request).Should().BeTrue();

        request.InstanceNo.Should().Be(0x01020304);
    }

    [TestCase(0, TestName = "TryReadJoinInstance_KeepsZero")]
    [TestCase(1, TestName = "TryReadJoinInstance_KeepsOne")]
    [TestCase(-1, TestName = "TryReadJoinInstance_KeepsMinusOne")]
    [TestCase(int.MinValue, TestName = "TryReadJoinInstance_KeepsIntMinValue")]
    [TestCase(int.MaxValue, TestName = "TryReadJoinInstance_KeepsIntMaxValue")]
    public void TryReadJoinInstance_KeepsInstanceNoSigned(int instanceNo)
    {
        // The field is the client's own value, declared `int32_t` (not a uint32): negative and extreme values
        // must survive the round trip unchanged, since nothing here knows the domain of the room numbers.
        GameHuntaholicPackets.TryReadJoinInstance(ClientFrame(instanceNo), out var request).Should().BeTrue();

        request.InstanceNo.Should().Be(instanceNo);
    }

    [Test]
    public void TryReadJoinInstance_DoesNotReadAnyOtherOffset()
    {
        // A frame carrying the room number at offset 7 is read as such; had the reader pointed at offset 11 the
        // first password byte (zero here) would come back instead.
        var frame = ClientFrame(0);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(7, 4), unchecked((int)0x0BADF00D));

        GameHuntaholicPackets.TryReadJoinInstance(frame, out var request).Should().BeTrue();

        request.InstanceNo.Should().Be(unchecked((int)0x0BADF00D));
    }

    [Test]
    public void TryReadJoinInstance_SeesNoPasswordWhenTheWholeFieldIsZero()
    {
        // The public-room path: the client zeroes the 17-byte buffer, so all zeroes is the only form a public
        // room takes. An empty password and "no password at all" are the same bytes on this wire.
        GameHuntaholicPackets.TryReadJoinInstance(ClientFrame(InstanceNo), out var request).Should().BeTrue();

        request.PasswordLength.Should().Be(0);
        request.HasPassword.Should().BeFalse();
    }

    [Test]
    public void TryReadJoinInstance_MeasuresThePasswordWithoutCarryingIt()
    {
        var frame = ClientFrame(InstanceNo, "abc");

        GameHuntaholicPackets.TryReadJoinInstance(frame, out var request).Should().BeTrue();

        request.PasswordLength.Should().Be(3, "the NUL that follows the three characters ends the field");
        request.HasPassword.Should().BeTrue();
    }

    [Test]
    public void TryReadJoinInstance_AcceptsAFullSixteenCharacterPassword()
    {
        // 16 usable characters plus the NUL is the widest form: rzu's `_(string)(password, 17)` copies at most 16
        // and the client's own bounded copy is capped at 17 bytes, NUL included. The NUL still fits inside the
        // field, at index 16, which is the frame's last byte.
        var frame = ClientFrame(InstanceNo, new string('x', 16));

        GameHuntaholicPackets.TryReadJoinInstance(frame, out var request).Should().BeTrue();

        request.PasswordLength.Should().Be(16);
        request.PasswordLength.Should().BeLessThan(GameHuntaholicPackets.JoinInstancePasswordFieldLength,
            "the terminating NUL is part of the 17-byte field");
    }

    [Test]
    public void TryReadJoinInstance_RefusesAPasswordFieldWithNoNul()
    {
        // The 7.3 client cannot produce this: both of its send paths leave the field at zero or copy into it
        // with a 17-byte bound. A field with no NUL means the sender is another program, and refusing keeps the
        // reader from ever looking past the field.
        var frame = ClientFrame(InstanceNo);
        frame.AsSpan(11, 17).Fill((byte)'y');
        frame[6] = Checksum(frame);

        GameHuntaholicPackets.TryReadJoinInstance(frame, out var request).Should().BeFalse();
        request.Should().Be(default(GameHuntaholicPackets.HuntaholicJoinInstanceRequest),
            "a refused frame leaves no half-read value behind");
    }

    [TestCase(0, TestName = "TryReadJoinInstance_RejectsAnEmptyFrame")]
    [TestCase(6, TestName = "TryReadJoinInstance_RejectsAFrameShorterThanAHeader")]
    [TestCase(7, TestName = "TryReadJoinInstance_RejectsAHeaderOnlyFrame")]
    [TestCase(11, TestName = "TryReadJoinInstance_RejectsAHeaderPlusInstanceNoFrame")]
    [TestCase(27, TestName = "TryReadJoinInstance_RejectsATruncatedFrame")]
    [TestCase(29, TestName = "TryReadJoinInstance_RejectsAPaddedFrame")]
    [TestCase(55, TestName = "TryReadJoinInstance_RejectsAnotherFamilyFrameLength")]
    [TestCase(56, TestName = "TryReadJoinInstance_RejectsTheCreationFrameLength")]
    public void TryReadJoinInstance_RejectsAnyLengthOtherThanTwentyEight(int length)
    {
        // The client writes 28 in hard: 7 bytes at the header, 4 for instance_no, 17 for the password field. A
        // 27-byte frame has no room for the password's last byte, and 55 is the length of the sibling 4003
        // creation frame — neither is a shorter or longer form of this request.
        var packet = new byte[length];
        ClientFrame(InstanceNo).AsSpan(0, Math.Min(length, PacketLength)).CopyTo(packet);

        GameHuntaholicPackets.TryReadJoinInstance(packet, out var request).Should().BeFalse();
        request.Should().Be(default(GameHuntaholicPackets.HuntaholicJoinInstanceRequest));
    }

    [Test]
    public void Request_KeepsNoCopyOfThePassword()
    {
        // The reader's contract is that a room password never leaves it: the request carries the room number, the
        // password's length, and nothing else. A string or byte[] member here would be the place a password
        // reached a log line.
        var members = typeof(GameHuntaholicPackets.HuntaholicJoinInstanceRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        members.Should().BeEquivalentTo(new[] { "HasPassword", "InstanceNo", "PasswordLength" });
        typeof(GameHuntaholicPackets.HuntaholicJoinInstanceRequest).GetFields(BindingFlags.Public |
            BindingFlags.Instance).Should().BeEmpty("a record struct's members must stay the three read values");
    }

    [Test]
    public void OnDataReceived_ConsumesThePacketWithoutThrowing()
    {
        var connection = new StorageTestHarness.FrameConnection(ClientFrame(1, "secret"));
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(PacketLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
    }

    [Test]
    public void OnDataReceived_AnswersNothing()
    {
        // Neither rzu nor NGemity declares a server to client packet for 4004. The only refusal instrument the
        // client understands would print the room creation notice, and without lobby state the server cannot
        // tell whether a quota is exhausted: answering, or sending a TS_SC_RESULT, would be invented here.
        var connection = new StorageTestHarness.FrameConnection(ClientFrame(1, "secret"));
        var client = StorageTestHarness.NewGameClient(connection);

        client.OnDataReceived(PacketLength);

        connection.Sent.Should().BeEmpty("no reference answers packet 4004");
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        // The frame must not swallow or desynchronise the one behind it: the keepalive is consumed too.
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[6] = Checksum(keepalive);

        var frame = ClientFrame(1).Concat(keepalive).ToArray();
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
    }

    [TestCase(7, TestName = "OnDataReceived_ConsumesAMalformedHeaderOnlyFrame")]
    [TestCase(11, TestName = "OnDataReceived_ConsumesATruncatedFrame")]
    [TestCase(29, TestName = "OnDataReceived_ConsumesAMalformedPaddedFrame")]
    public void OnDataReceived_ConsumesAMalformedFrameWithoutThrowing(int length)
    {
        // The client always writes 28, so any other length is an anomaly; it must be refused without sending an
        // answer and without leaving bytes in the stream.
        var frame = MalformedFrame(length);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "a refused frame is still consumed entirely");
    }

    /// <summary>
    /// Rebuilds frame 4004 with another announced length, keeping a valid checksum: the receive loop rejects an
    /// invalid one before any dispatch, which would hide what this test measures.
    /// </summary>
    private static byte[] MalformedFrame(int length)
    {
        var frame = new byte[length];
        Array.Copy(ClientFrame(InstanceNo), frame, Math.Min(length, PacketLength));
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)length);
        frame[6] = Checksum(frame);
        return frame;
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
            "header.ID == (ushort)GamePackets.TM_CS_HUNTAHOLIC_JOIN_INSTANCE", StringComparison.Ordinal);
        var finalSwitch = source.IndexOf("throw new Exception($\"Unknown Packet Type", StringComparison.Ordinal);

        branch.Should().BeGreaterThan(-1, "4004 needs a branch of its own in OnDataReceived");
        finalSwitch.Should().BeGreaterThan(-1, "the final switch is the guard this test is about");
        branch.Should().BeLessThan(finalSwitch, "4004 must be handled before the final switch throws");
    }

    [Test]
    public void Packet_IsDeclaredUnderTheEpic73IdOnly()
    {
        // rzu holds a single `X(4004, true)`: no version-gated variant of this id exists, so the family's only
        // gated id is the *answer* (TS_SC_RESULT, 0 below EPIC_9_6_3). A second declared name for 4004 — the
        // shape a high/low id variant would take — would let two names share one wire value.
        Enum.GetValues<GamePackets>().Where(value => (ushort)value == 4004).Select(value => value.ToString())
            .Should().BeEquivalentTo(new[] { "TM_CS_HUNTAHOLIC_JOIN_INSTANCE" });
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
