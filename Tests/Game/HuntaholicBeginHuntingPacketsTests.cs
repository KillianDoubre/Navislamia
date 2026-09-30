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
/// TM_CS_HUNTAHOLIC_BEGIN_HUNTING (4011) is the bare 7-byte header on the wire — <c>Length</c> at 0, the id
/// <c>0x0FAB</c> at 4, the checksum at 6 and no payload byte at all — and it is answered by nothing: the 7.3
/// client's own receive dispatcher routes 4011 to its "unhandled message" branch, and no other packet of the
/// family carries this answer. The client writes the length 7 in hard (<c>0x4c93eb</c>), so any other length is
/// an anomaly rather than another form of the request.
/// See docs/packet-specs/4011-huntaholic-begin-hunting.md §3, §5.2, §5.3.
/// </summary>
[TestFixture]
public class HuntaholicBeginHuntingPacketsTests
{
    private const int PacketLength = 7;

    /// <summary>
    /// Builds the 7-byte client frame byte by byte: Length, ID and the checksum over the first six bytes. The
    /// frame has no payload, so the checksum is the only value that depends on the header — the id's two bytes
    /// alone make it non-zero (0x07 + 0xAB + 0x0F = 0xC1), which is what lets the checksum offset be asserted.
    /// </summary>
    private static byte[] ClientFrame()
    {
        var packet = new byte[PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2),
            (ushort)GamePackets.TM_CS_HUNTAHOLIC_BEGIN_HUNTING);
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
        ((ushort)GamePackets.TM_CS_HUNTAHOLIC_BEGIN_HUNTING).Should().Be(4011);
        ((ushort)GamePackets.TM_CS_HUNTAHOLIC_BEGIN_HUNTING).Should().Be(0x0FAB);

        // rzu declares it X(4011, true), which expands to `if (true) id = id_;` — the file holds that single
        // unconditional entry, so 7.3 keeps 4011 and no variant id has to be declared. The comment
        // "Since EPIC_6_3" only dates the family (EPIC_6_3 = 0x060300 < EPIC_7_3 = 0x070300). The id must be
        // defined, otherwise OnDataReceived drops the frame as "Undefined packet ID" before any arm can run.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_HUNTAHOLIC_BEGIN_HUNTING).Should().BeTrue();
    }

    [Test]
    public void Id_DoesNotCollideWithAnExistingMember()
    {
        // Two names sharing one value would silently route a foreign packet into this handler.
        var values = Enum.GetValues<GamePackets>().Select(value => (ushort)value).ToArray();

        values.Should().OnlyHaveUniqueItems();
        values.Should().Contain(4011);
    }

    [Test]
    public void Id_DoesNotCollideWithTheInstanceGameIds()
    {
        // The sheet warns about confusing the family with the instance game socle: both share the same 7-byte
        // header, so pinning the two id sets apart is worth an assertion. Only the ids the merged socle already
        // declares are named here — a test that listed the sibling HuntaHolic ids as "absent" would fail the day
        // the family lands, and a shared family class must not assert on its siblings' member set.
        var declared = (ushort)GamePackets.TM_CS_HUNTAHOLIC_BEGIN_HUNTING;

        declared.Should().NotBe((ushort)GamePackets.TM_CS_INSTANCE_GAME_ENTER);
        declared.Should().NotBe((ushort)GamePackets.TM_CS_INSTANCE_GAME_EXIT);
        declared.Should().NotBe((ushort)GamePackets.TM_CS_INSTANCE_GAME_SCORE_REQUEST);
        declared.Should().NotBe((ushort)GamePackets.TM_SC_INSTANCE_GAME_SCORE_REQUEST);
        declared.Should().Be(4011, "the instance game ids live in 4250-4253, not in the HuntaHolic range");
    }

    [Test]
    public void Id_IsDeclaredUnderTheEpic73IdOnly()
    {
        // The remapped variant of a family (a high/low id pair) would take the shape of a second declared name
        // for the same wire value. rzu has one entry, so there is exactly one name for 4011.
        Enum.GetValues<GamePackets>().Where(value => (ushort)value == 4011).Select(value => value.ToString())
            .Should().BeEquivalentTo(new[] { "TM_CS_HUNTAHOLIC_BEGIN_HUNTING" });
    }

    [Test]
    public void ClientPacket_UsesTheBareHeaderLayout()
    {
        var packet = ClientFrame();

        packet.Length.Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should()
            .Be(PacketLength, "Length sits at offset 0 and the client writes 7 (0x7) in hard at 0x4c93eb");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(4011, "ID sits at offset 4");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(0x0FAB,
            "the client moves the literal 0xfab into the two id bytes at 0x4c93e7");
        packet[6].Should().Be(Checksum(packet), "the checksum at offset 6 is the sum of the first six bytes");
        packet[6].Should().Be(0xC1, "7 + 0xAB + 0x0F: the id alone makes the checksum non-zero");
    }

    [Test]
    public void ClientPacket_HasNoFieldOutsideTheHeader()
    {
        // The header is the whole frame: rzu's DEF(_) is empty, NGemity's is empty too, and the client never
        // writes a byte past offset 6.
        Marshal.SizeOf<Header>().Should().Be(PacketLength);
        GameHuntaholicPackets.BeginHuntingLength.Should().Be(PacketLength,
            "the frame constant is the header size, with no payload added");
        GameHuntaholicPackets.BeginHuntingLength.Should().Be(Marshal.SizeOf<Header>(),
            "a payload field would show up as a length greater than the header");
    }

    [Test]
    public void ClientPacket_AnnouncesItsFieldsLittleEndian()
    {
        // The bytes are laid down by hand — 07 00 00 00 at 0 and AB 0F at 4 — so a big-endian read would give
        // 0x07000000 and 0xAB0F instead of the little-endian 7 and 4011.
        var packet = new byte[PacketLength];
        packet[0] = 0x07;
        packet[4] = 0xAB;
        packet[5] = 0x0F;

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(7);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().NotBe(0x07000000,
            "a big-endian read of the same four bytes");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(4011);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().NotBe(0xAB0F,
            "a big-endian read of the same two bytes");
    }

    [Test]
    public void IsBeginHunting_AcceptsTheSevenByteFrame()
    {
        GameHuntaholicPackets.IsBeginHunting(ClientFrame()).Should().BeTrue();
    }

    [Test]
    public void IsBeginHunting_TakesNoOutputValue()
    {
        // The reader's contract is that there is nothing to extract: no out parameter, no request type, no
        // field. A gate that returned a value would mean a field exists on the wire and the sheet is wrong.
        var method = typeof(GameHuntaholicPackets).GetMethod("IsBeginHunting",
            BindingFlags.Public | BindingFlags.Static);

        method.Should().NotBeNull("4011 needs its own reader");
        method!.ReturnType.Should().Be(typeof(bool));
        method.GetParameters().Should().HaveCount(1, "one input span is the whole signature");
        method.GetParameters()[0].ParameterType.Should().Be(typeof(ReadOnlySpan<byte>));
        method.GetParameters()[0].IsOut.Should().BeFalse("4011 carries no field to read out");
    }

    [TestCase(0, TestName = "IsBeginHunting_RejectsAnEmptyFrame")]
    [TestCase(1, TestName = "IsBeginHunting_RejectsASingleByte")]
    [TestCase(6, TestName = "IsBeginHunting_RejectsAFrameShorterThanTheHeader")]
    [TestCase(8, TestName = "IsBeginHunting_RejectsAPaddedFrame")]
    [TestCase(9, TestName = "IsBeginHunting_RejectsAFrameWithOneExtraByte")]
    [TestCase(11, TestName = "IsBeginHunting_RejectsTheHuntStartAnswerLength")]
    [TestCase(15, TestName = "IsBeginHunting_RejectsTheMonsterListLength")]
    [TestCase(28, TestName = "IsBeginHunting_RejectsTheJoinFrameLength")]
    [TestCase(48, TestName = "IsBeginHunting_RejectsTheHuntingInfoLength")]
    [TestCase(55, TestName = "IsBeginHunting_RejectsTheCreationFrameLength")]
    public void IsBeginHunting_RejectsAnyLengthOtherThanSeven(int length)
    {
        // The client writes 7 in hard: 6 bytes is truncated, and anything longer carries bytes no field of the
        // 4011 frame accounts for. 11, 15, 28, 48 and 55 are the lengths the sheet gives for the other frames
        // of the family (4009, 4007, 4004, 4006 and 4003), none of which is another form of the hunt start.
        var packet = new byte[length];
        ClientFrame().AsSpan(0, Math.Min(length, PacketLength)).CopyTo(packet);

        GameHuntaholicPackets.IsBeginHunting(packet).Should().BeFalse();
    }

    [Test]
    public void IsBeginHunting_IgnoresTheChecksumByte()
    {
        // No packet of the family verifies the checksum, and this one is no exception: the reader only measures
        // the frame. The receive loop does check it before dispatching (see
        // OnDataReceived_StopsOnAFrameWithABadChecksum), which is where a wrong checksum is caught.
        var frame = ClientFrame();
        frame[6] = (byte)(frame[6] ^ 0xFF);

        GameHuntaholicPackets.IsBeginHunting(frame).Should().BeTrue();
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
        // No reference answers a 4011: the client routes the id to its "unhandled message" branch, so an
        // answer would be logged as unknown by the client, and the server keeps no hunt state to report on.
        var connection = new StorageTestHarness.FrameConnection(ClientFrame());
        var client = StorageTestHarness.NewGameClient(connection);

        client.OnDataReceived(PacketLength);

        connection.Sent.Should().BeEmpty("no reference answers packet 4011");
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        // The frame must not swallow or desynchronise the one behind it: the keepalive is consumed too, and it
        // carries a valid checksum so that the loop's own guard is not what stops it.
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
    [TestCase(11, TestName = "OnDataReceived_ConsumesAHuntStartAnswerSizedFrame")]
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
        // is answered. Pinned here so that no later lot claims 4011 is dispatched with a wrong checksum.
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
            "header.ID == (ushort)GamePackets.TM_CS_HUNTAHOLIC_BEGIN_HUNTING", StringComparison.Ordinal);
        var finalSwitch = source.IndexOf("throw new Exception($\"Unknown Packet Type", StringComparison.Ordinal);

        branch.Should().BeGreaterThan(-1, "4011 needs a branch of its own in OnDataReceived");
        finalSwitch.Should().BeGreaterThan(-1, "the final switch is the guard this test is about");
        branch.Should().BeLessThan(finalSwitch, "4011 must be handled before the final switch throws");
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
