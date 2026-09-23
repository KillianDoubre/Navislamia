using System;
using System.Buffers.Binary;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// TM_CS_CHANGE_SUMMON_NAME (323) is 26 bytes on the wire: the 7-byte header and a single 19-byte
/// <c>name</c> field at offsets 7-25, no handle and no other field. Unlike the other frames of the summon
/// family this one is really emitted by the client: SFrame.exe owns one frame builder for it (0x48c5e0,
/// id 0x143 and length 0x1a written in hard) fed by a single interface path, so the shape read here is the
/// shape a real 7.3 client sends.
/// <para>
/// The repository applies nothing yet: which of a character's two summons a rename targets, the accepted
/// length of a name and the answer to send are all unsettled (fiche §7(c), §7(d), §7(e)). The dispatch
/// tests therefore assert that the frame is consumed and journalised <strong>without any answer and
/// without any rename</strong> — not a working rename.
/// </para>
/// The dispatch is exercised against the real receive loop on purpose: a member of <see cref="GamePackets"/>
/// that no branch claims reaches the final switch and throws "Unknown Packet Type" inside that loop.
/// See docs/packet-specs/323-change-summon-name.md.
/// </summary>
[TestFixture]
public class ChangeSummonNamePacketsTests
{
    private const int PacketLength = 26;
    private const int NameOffset = 7;
    private const int NameFieldSize = 19;
    private const int UsableNameLength = 18;
    private const string Name = "Kitty";

    /// <summary>
    /// Builds the frame the way the client's own builder does: the 19 bytes of the field are zeroed first,
    /// the name is copied until the NUL, then a NUL is forced into the last byte of the field. A frame of
    /// another announced length keeps a valid checksum, since the receive loop refuses an invalid one
    /// before it dispatches anything.
    /// </summary>
    private static byte[] ClientFrame(string name, int length = PacketLength)
    {
        var packet = new byte[length];

        if (length < 7)
        {
            // No complete header: such a frame never reaches the dispatch, but the reader must still refuse
            // it without reading past the buffer.
            return packet;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2),
            (ushort)GamePackets.TM_CS_CHANGE_SUMMON_NAME);

        if (name is { Length: > 0 } && length > NameOffset)
        {
            var copy = Math.Min(name.Length, Math.Min(UsableNameLength, length - NameOffset));
            Encoding.ASCII.GetBytes(name, 0, copy, packet, NameOffset);
        }

        // The client's own guarantee: the last byte of the field holds a NUL, so a longer name is cut there.
        if (length >= NameOffset + NameFieldSize)
        {
            packet[NameOffset + NameFieldSize - 1] = 0;
        }

        packet[6] = StorageTestHarness.Checksum(packet);
        return packet;
    }

    /// <summary>An 11-byte frame of the S→C twin, whose payload is a single handle.</summary>
    private static byte[] ShowSummonNameChangeFrame()
    {
        var packet = new byte[11];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), 11u);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), 322);
        packet[6] = StorageTestHarness.Checksum(packet);
        return packet;
    }

    [Test]
    public void Ids_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_CS_CHANGE_SUMMON_NAME).Should().Be(323);

        // rzu names 1323 from EPIC_9_6_3 on (0x090603), above EPIC_7_3 = 0x070300: that id is out of this
        // repository's scope and must stay undeclared here.
        Enum.IsDefined(typeof(GamePackets), (ushort)1323).Should().BeFalse(
            "1323 is the 9.6.3 remap of the rename and is above EPIC_7_3");

        // The id must be defined, otherwise OnDataReceived drops the frame as "Undefined packet ID" before
        // any dispatch and a frame a real 7.3 client sends would be silently ignored.
        Enum.IsDefined(typeof(GamePackets), (ushort)323).Should().BeTrue();
    }

    [Test]
    public void TheServerToClientTwin322_IsNotDeclared()
    {
        // 322 TM_SC_SHOW_SUMMON_NAME_CHANGE is the S→C twin (one handle, 11 bytes). The 7.3 client only
        // receives it and nothing emits it yet, so declaring it would add a member no branch claims — the
        // very thing the dispatch rule forbids. See fiche §3.4 and §7(b).
        Enum.IsDefined(typeof(GamePackets), (ushort)322).Should().BeFalse(
            "the twin is server to client and the repository emits it nowhere");
    }

    [Test]
    public void LayoutConstants_NameTheHeaderAndThePayloadSeparately()
    {
        // 7 + 19: naming the parts apart means a change of the 7-byte header breaks this test even when the
        // total happens to stay at 26.
        Marshal.SizeOf<Header>().Should().Be(7);
        GameActionPackets.ChangeSummonNamePacketSize.Should().Be(PacketLength);
        GameActionPackets.ChangeSummonNamePayloadSize.Should().Be(NameFieldSize,
            "the whole payload is the name field");
        GameActionPackets.ChangeSummonNameOffset.Should().Be(NameOffset);
        GameActionPackets.ChangeSummonNameFieldSize.Should().Be(NameFieldSize);
        GameActionPackets.ChangeSummonNameMaxLength.Should().Be(UsableNameLength,
            "the 19th byte of the field is the NUL terminator");

        // Seen from the payload (fiche §3.2): the name sits at offset 0.
        (GameActionPackets.ChangeSummonNameOffset - Marshal.SizeOf<Header>()).Should().Be(0);
        (GameActionPackets.ChangeSummonNameOffset + GameActionPackets.ChangeSummonNameFieldSize)
            .Should().Be(PacketLength, "the name field is the last field of the frame");
    }

    [Test]
    public void ClientFrame_CarriesTheNameAtOffsetSevenToTwentyFive()
    {
        var packet = ClientFrame(Name);

        packet.Should().HaveCount(PacketLength, "the 7.3 frame is length, id, checksum and a 19-byte name");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(323);
        packet[6].Should().Be(StorageTestHarness.Checksum(packet),
            "the checksum is the sum of the first six header bytes");
        Encoding.ASCII.GetString(packet, NameOffset, Name.Length).Should().Be(Name);
        packet[NameOffset + Name.Length].Should().Be(0, "the client terminates the name with a NUL");
        packet.Skip(NameOffset + Name.Length + 1).Should().OnlyContain(b => b == 0,
            "the rest of the field is zeroed by the client");

        new Header(packet).Length.Should().Be((uint)PacketLength);
        new Header(packet).ID.Should().Be(323);
    }

    [Test]
    public void TryReadChangeSummonName_ReadsTheNameUpToTheFirstNul()
    {
        GameActionPackets.TryReadChangeSummonName(ClientFrame(Name), out var name).Should().BeTrue();

        name.Should().Be(Name);
    }

    [Test]
    public void TryReadChangeSummonName_ReadsTheEighteenUsableCharacters()
    {
        // 18 characters fill the field up to the NUL the client forces into byte 25: the frame is still the
        // full 26 bytes and the value is not truncated by the reader.
        var longest = new string('a', UsableNameLength);
        var frame = ClientFrame(longest);

        frame[NameOffset + NameFieldSize - 1].Should().Be(0);

        GameActionPackets.TryReadChangeSummonName(frame, out var name).Should().BeTrue();

        name.Should().HaveLength(UsableNameLength).And.Be(longest);
    }

    [Test]
    public void TryReadChangeSummonName_ReadsOnlyTheEndOfAnOverlongName()
    {
        // The client's copy loop is not bounded: a longer name fills the 19 bytes of the field and the NUL
        // it forces into the last one is what terminates it. The server reads 18 characters at most and
        // ignores the rest instead of reading past the field.
        var frame = ClientFrame(new string('b', 30));

        GameActionPackets.TryReadChangeSummonName(frame, out var name).Should().BeTrue();

        name.Should().HaveLength(UsableNameLength).And.Be(new string('b', UsableNameLength),
            "the 19th byte of the field is the forced NUL");
    }

    [Test]
    public void TryReadChangeSummonName_IgnoresWhatFollowsTheFirstNul()
    {
        var frame = ClientFrame(Name);
        frame[NameOffset + Name.Length + 1] = 0x41;
        frame[NameOffset + Name.Length + 2] = 0x42;

        GameActionPackets.TryReadChangeSummonName(frame, out var name).Should().BeTrue();

        name.Should().Be(Name, "the name stops at its NUL, whatever a non conforming client leaves behind");
    }

    [Test]
    public void TryReadChangeSummonName_ReadsAnEmptyFieldAsAnEmptyName()
    {
        // An empty field is a read value, not a malformed frame: the NUL at offset 7 is what the client
        // writes for an empty input. No minimum length is enforced here — the accepted minimum is not
        // established (fiche §7(e)) — so the caller receives the empty value as it stands.
        GameActionPackets.TryReadChangeSummonName(ClientFrame(string.Empty), out var name).Should().BeTrue();

        name.Should().BeEmpty();
    }

    [Test]
    public void TryReadChangeSummonName_RefusesAFieldWithoutNul()
    {
        // 19 non-zero bytes cannot come from the 7.3 frame builder, which forces a NUL into the last byte of
        // the field. Reading one would spill the byte that follows into the name, so it is refused.
        var frame = ClientFrame(Name);
        for (var index = NameOffset; index < NameOffset + NameFieldSize; index++)
        {
            frame[index] = 0x41;
        }

        frame[6] = StorageTestHarness.Checksum(frame);

        GameActionPackets.TryReadChangeSummonName(frame, out var name).Should().BeFalse();

        name.Should().BeNull("a refused frame leaves no half read value behind");
    }

    [TestCase(0, TestName = "TryReadChangeSummonName_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadChangeSummonName_RejectsAHeaderOnlyFrame")]
    [TestCase(25, TestName = "TryReadChangeSummonName_RejectsAFrameWithoutTheLastNameByte")]
    [TestCase(27, TestName = "TryReadChangeSummonName_RejectsALongerFrame")]
    public void TryReadChangeSummonName_RejectsALengthOtherThanTwentySix(int length)
    {
        // The client writes the length 0x1a in hard from its only frame builder: another length is a
        // malformed frame, not a shorter or padded variant (fiche §5.3.1). A longer frame is refused too,
        // unlike the 304 reader, and for that very reason.
        var frame = ClientFrame(Name, length);

        GameActionPackets.TryReadChangeSummonName(frame, out var name).Should().BeFalse();

        name.Should().BeNull();
    }

    [Test]
    public void OnDataReceived_ConsumesThePacketWithoutThrowing()
    {
        var connection = new StorageTestHarness.FrameConnection(ClientFrame(Name));
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(PacketLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
    }

    [Test]
    public void OnDataReceived_AnswersNothing()
    {
        // No reference establishes a reply to 323: NGemity has no handler, rzu documents the shape only, and
        // the 7.3 client exposes no incoming result for a summon rename (fiche §5.5, §7(c)). Sending anything
        // back — a TM_SC_RESULT, a 301 re-publication, a 322 — would be invented.
        var connection = new StorageTestHarness.FrameConnection(ClientFrame(Name));
        var client = StorageTestHarness.NewGameClient(connection);

        client.OnDataReceived(PacketLength);

        connection.Sent.Should().BeEmpty("no reference answers packet 323");
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        // The frame must not swallow or desynchronise the one behind it: the keepalive is consumed too.
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7u);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[6] = StorageTestHarness.Checksum(keepalive);

        var stream = ClientFrame(Name).Concat(keepalive).ToArray();
        var connection = new StorageTestHarness.FrameConnection(stream);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(stream.Length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
    }

    [TestCase(7, TestName = "OnDataReceived_ConsumesAHeaderOnlyFrame")]
    [TestCase(27, TestName = "OnDataReceived_ConsumesALongerFrame")]
    public void OnDataReceived_ConsumesANonConformingLengthWithoutThrowing(int length)
    {
        // A malformed frame is journalised and dropped: it is consumed entirely so the stream stays in step,
        // and nothing is answered — no refusal rule is established for this packet (fiche §7(c)).
        var frame = ClientFrame(Name, length);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "a dropped frame is still consumed entirely");
    }

    [Test]
    public void OnDataReceived_DropsTheTwin322Frame()
    {
        // 322 is not declared, so the guard at the top of the receive loop drops the frame as undefined and
        // the loop stays alive: that is the current, intended behaviour for the S→C twin.
        var frame = ShowSummonNameChangeFrame();
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "an undefined frame is dropped and consumed");
    }
}
