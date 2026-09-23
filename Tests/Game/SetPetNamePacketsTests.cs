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
/// TM_CS_SET_PET_NAME (354) is 30 bytes on the wire: the 7-byte header, a 4-byte <c>handle</c> at offsets
/// 7-10 and a single 19-byte <c>name</c> field at offsets 11-29. The 7.3 client really emits it — one frame
/// builder writes the id 0x162 and the length 0x1e in hard (SFrame.exe 0x48c6c5, 0x48c6d0) and the sender
/// 0x48e170 lays the two fields out exactly there — but only after it has received
/// TM_SC_SHOW_SET_PET_NAME (353), whose payload it echoes back as the handle.
/// <para>
/// The repository applies nothing yet: which pet the handle designates, what a pet name may contain,
/// whether it must be unique, where it is persisted and what it costs are all unsettled (fiche §7.2-4),
/// and no reference holds a result frame for a rename (fiche §5.2-6). The dispatch tests therefore assert
/// that the frame is consumed and journalised <strong>without any answer and without any rename</strong> —
/// not a working rename.
/// </para>
/// The dispatch is exercised against the real receive loop on purpose: a member of <see cref="GamePackets"/>
/// that no branch claims reaches the final switch and throws "Unknown Packet Type" inside that loop.
/// See docs/packet-specs/354-set-pet-name.md.
/// </summary>
[TestFixture]
public class SetPetNamePacketsTests
{
    private const int PacketLength = 30;
    private const int HandleOffset = 7;
    private const int NameOffset = 11;
    private const int NameFieldSize = 19;
    private const int UsableNameLength = 18;
    private const uint Handle = 0x40000222u;
    private const string Name = "Kitty";

    /// <summary>
    /// Builds the frame the way the client's own sender does: the handle is written at offset 7 and the
    /// name is <c>memcpy</c>'d into the 19 bytes at offset 11 — no NUL is forced anywhere, so a name longer
    /// than the field fills it entirely (0x48e199-0x48e19c). A frame of another announced length keeps a
    /// valid checksum, since the receive loop refuses an invalid one before it dispatches anything.
    /// </summary>
    private static byte[] ClientFrame(string name, uint handle = Handle, int length = PacketLength)
    {
        var packet = new byte[length];

        if (length < 7)
        {
            // No complete header: such a frame never reaches the dispatch, but the reader must still refuse
            // it without reading past the buffer.
            return packet;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_SET_PET_NAME);

        if (length >= HandleOffset + 4)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HandleOffset, 4), handle);
        }

        if (name is { Length: > 0 } && length > NameOffset)
        {
            var copy = Math.Min(name.Length, Math.Min(NameFieldSize, length - NameOffset));
            Encoding.ASCII.GetBytes(name, 0, copy, packet, NameOffset);
        }

        packet[6] = StorageTestHarness.Checksum(packet);
        return packet;
    }

    /// <summary>An 11-byte frame of the S→C twin 353, whose payload is a single handle.</summary>
    private static byte[] ShowSetPetNameFrame()
    {
        var packet = new byte[11];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), 11u);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), 353);
        packet[6] = StorageTestHarness.Checksum(packet);
        return packet;
    }

    [Test]
    public void Ids_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_CS_SET_PET_NAME).Should().Be(354);

        // rzu names 1354 from EPIC_9_6_3 on (0x090603, dated 20200713), above EPIC_7_3 = 0x070300: that id
        // is out of this repository's scope and must stay undeclared here.
        Enum.IsDefined(typeof(GamePackets), (ushort)1354).Should().BeFalse(
            "1354 is the 9.6.3 remap of the rename and is above EPIC_7_3");

        // The id must be defined, otherwise OnDataReceived drops the frame as "Undefined packet ID" before
        // any dispatch and a frame a real 7.3 client sends would be silently ignored.
        Enum.IsDefined(typeof(GamePackets), (ushort)354).Should().BeTrue();
    }

    [Test]
    public void TheServerToClientTwin353_IsNotDeclared()
    {
        // 353 TM_SC_SHOW_SET_PET_NAME is the only S→C frame of the pair (one handle, 11 bytes) and it
        // *precedes* the 354: it opens the client's name box. Nothing emits it yet — which gesture does is
        // not established (fiche §7.1) — and the frame has its own sheet and its own lot, so declaring it
        // here would add a member no branch claims, with no encoder to call. This lot therefore leaves it
        // undeclared, like lot 323 left its own twin 322 undeclared.
        Enum.IsDefined(typeof(GamePackets), (ushort)353).Should().BeFalse(
            "the S→C twin is declared by no lot yet and nothing in the server emits it");
    }

    [Test]
    public void LayoutConstants_NameTheHeaderAndThePayloadSeparately()
    {
        // 7 + 4 + 19: naming the parts apart means a change of the 7-byte header breaks this test even when
        // the total happens to stay at 30.
        Marshal.SizeOf<Header>().Should().Be(7);
        GameSummonPackets.SetPetNamePacketSize.Should().Be(PacketLength);
        GameSummonPackets.SetPetNameHandleOffset.Should().Be(HandleOffset);
        GameSummonPackets.SetPetNameNameOffset.Should().Be(NameOffset);
        GameSummonPackets.SetPetNameMaxLength.Should().Be(UsableNameLength,
            "the 19th byte of the field is the NUL the client leaves when the name is shorter");

        // Seen from the payload (fiche §3.1): the handle sits at offset 0, the name at offset 4.
        (GameSummonPackets.SetPetNameHandleOffset - Marshal.SizeOf<Header>()).Should().Be(0);
        (GameSummonPackets.SetPetNameNameOffset - Marshal.SizeOf<Header>()).Should().Be(4);
        (GameSummonPackets.SetPetNameNameOffset + GameSummonPackets.NameSize).Should().Be(PacketLength,
            "the name field is the last field of the frame");
    }

    [Test]
    public void ClientFrame_CarriesTheHandleAtSevenAndTheNameAtEleven()
    {
        var packet = ClientFrame(Name);

        packet.Should().HaveCount(PacketLength, "the 7.3 frame is length, id, checksum, a handle and a name");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(354);
        packet[6].Should().Be(StorageTestHarness.Checksum(packet),
            "the checksum is the sum of the first six header bytes");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(HandleOffset, 4)).Should().Be(Handle);
        Encoding.ASCII.GetString(packet, NameOffset, Name.Length).Should().Be(Name);
        packet[NameOffset + Name.Length].Should().Be(0);
        packet.Skip(NameOffset + Name.Length + 1).Should().OnlyContain(b => b == 0);

        new Header(packet).Length.Should().Be((uint)PacketLength);
        new Header(packet).ID.Should().Be(354);
    }

    [Test]
    public void TryReadSetPetName_ReadsTheHandleAndTheName()
    {
        GameSummonPackets.TryReadSetPetName(ClientFrame(Name), out var handle, out var name).Should().BeTrue();

        handle.Should().Be(Handle);
        name.Should().Be(Name);
    }

    [Test]
    public void TryReadSetPetName_IgnoresWhatFollowsTheFirstNul()
    {
        var frame = ClientFrame(Name);
        frame[NameOffset + Name.Length + 1] = 0x41;
        frame[NameOffset + Name.Length + 2] = 0x42;

        GameSummonPackets.TryReadSetPetName(frame, out _, out var name).Should().BeTrue();

        name.Should().Be(Name, "the name stops at its NUL, whatever a non conforming client leaves behind");
    }

    [Test]
    public void TryReadSetPetName_ReadsTheWholeFieldWhenTheClientSentNoNul()
    {
        // Unlike 323, whose builder zeroes the field and forces a NUL into its last byte, the 354 sender is
        // a raw memcpy of 19 bytes out of a std::string: a name longer than the field arrives with no NUL at
        // all (fiche §5.2-2), and the box's own input limit is not established (fiche §2.3). Refusing the
        // frame would refuse a name the client really typed, so the reader stays inside the field and reads
        // the 19 bytes it holds.
        var frame = new byte[PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), (ushort)GamePackets.TM_CS_SET_PET_NAME);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(HandleOffset, 4), Handle);
        for (var index = NameOffset; index < NameOffset + NameFieldSize; index++)
        {
            frame[index] = 0x41;
        }

        frame[6] = StorageTestHarness.Checksum(frame);

        GameSummonPackets.TryReadSetPetName(frame, out var handle, out var name).Should().BeTrue();

        handle.Should().Be(Handle);
        name.Should().HaveLength(NameFieldSize).And.Be(new string('A', NameFieldSize),
            "19 non-NUL bytes are the field's full content, not a reason to read further");
    }

    [Test]
    public void TryReadSetPetName_ReadsAnEmptyFieldAsAnEmptyName()
    {
        // An empty field is a read value, not a malformed frame: the NUL at offset 11 is what the client
        // sends for an empty input. No minimum length is enforced here — the accepted minimum is not
        // established (fiche §7.4) — so the caller receives the empty value as it stands and decides what a
        // journal line says about it.
        GameSummonPackets.TryReadSetPetName(ClientFrame(string.Empty), out var handle, out var name)
            .Should().BeTrue();

        handle.Should().Be(Handle);
        name.Should().BeEmpty();
    }

    [TestCase(0, TestName = "TryReadSetPetName_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadSetPetName_RejectsAHeaderOnlyFrame")]
    [TestCase(10, TestName = "TryReadSetPetName_RejectsAFrameWithoutTheHandle")]
    [TestCase(29, TestName = "TryReadSetPetName_RejectsAFrameWithoutTheLastNameByte")]
    [TestCase(31, TestName = "TryReadSetPetName_RejectsALongerFrame")]
    public void TryReadSetPetName_RejectsALengthOtherThanThirty(int length)
    {
        // The client writes the length 0x1e in hard from its only frame builder: another length is a
        // malformed frame, not a shorter or padded variant (fiche §5.2-1). A longer frame is refused too:
        // the name field would no longer be the last field of the frame.
        var frame = ClientFrame(Name, length: length);

        GameSummonPackets.TryReadSetPetName(frame, out var handle, out var name).Should().BeFalse();

        handle.Should().Be(0u, "a refused frame leaves no half read value behind");
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
        // No reference establishes a reply to 354: NGemity has no handler, rzu documents the shape only, and
        // the 7.3 client exposes no incoming result for a pet rename (fiche §5.1, §5.2-6). Sending anything
        // back — a TM_SC_RESULT, a 301 re-publication, a 353 — would be invented, and a 353 would even
        // reopen the client's name box (fiche §5.3).
        var connection = new StorageTestHarness.FrameConnection(ClientFrame(Name));
        var client = StorageTestHarness.NewGameClient(connection);

        client.OnDataReceived(PacketLength);

        connection.Sent.Should().BeEmpty("no reference answers packet 354");
    }

    [Test]
    public void OnDataReceived_AnswersNothingForAnEmptyNameEither()
    {
        // The specification counts an empty name among the failures it answers by a journal line only
        // (fiche §5.2-6): no refusal rule exists, so nothing may be sent.
        var connection = new StorageTestHarness.FrameConnection(ClientFrame(string.Empty));
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(PacketLength);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty("an empty name is journalised, not refused");
        connection.BytesAvailable.Should().Be(0);
    }

    [TestCase(7, TestName = "OnDataReceived_ConsumesAHeaderOnlyFrame")]
    [TestCase(29, TestName = "OnDataReceived_ConsumesAFrameWithoutTheLastNameByte")]
    [TestCase(31, TestName = "OnDataReceived_ConsumesALongerFrame")]
    public void OnDataReceived_ConsumesANonConformingLengthWithoutThrowing(int length)
    {
        // A malformed frame is journalised and dropped: it is consumed entirely so the stream stays in step,
        // and nothing is answered — no refusal rule is established for this packet (fiche §5.2-6).
        var frame = ClientFrame(Name, length: length);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "a dropped frame is still consumed entirely");
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

    [Test]
    public void OnDataReceived_DropsTheTwin353Frame()
    {
        // 353 is not declared by this lot, so the guard at the top of the receive loop drops the frame as
        // undefined and the loop stays alive: the S→C twin has its own sheet and its own lot.
        var frame = ShowSetPetNameFrame();
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "an undefined frame is dropped and consumed");
    }
}
