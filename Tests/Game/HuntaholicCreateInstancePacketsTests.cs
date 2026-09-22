using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// TM_CS_HUNTAHOLIC_CREATE_INSTANCE (4003), the HuntaHolic lobby room creation frame. The 7.3 client builds it
/// in <c>0x563c20</c>: <c>memset(frame, 0, 0x38)</c>, <c>Length = 0x38</c>, <c>ID = 0xfa3</c>, the room name
/// copied bounded to 31 bytes at +7, one byte at +38 and the password copied bounded to 17 bytes at +39, the
/// frame ending at +56. rzu's <c>X(4003, true)</c> means no version gating and no gated field.
/// See docs/packet-specs/4003-huntaholic-create-instance.md.
/// </summary>
[TestFixture]
public class HuntaholicCreateInstancePacketsTests
{
    private const int CreateInstanceLength = 56;
    private const int NameOffset = 7;
    private const int NameFieldLength = 31;
    private const int MaxMemberCountOffset = 38;
    private const int PasswordOffset = 39;
    private const int PasswordFieldLength = 17;

    private static byte[] ClientFrame(string name = "", sbyte maxMemberCount = 0, string password = "")
    {
        var packet = new byte[CreateInstanceLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), CreateInstanceLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2),
            (ushort)GamePackets.TM_CS_HUNTAHOLIC_CREATE_INSTANCE);

        Encoding.ASCII.GetBytes(name).CopyTo(packet.AsSpan(NameOffset, NameFieldLength));
        packet[MaxMemberCountOffset] = unchecked((byte)maxMemberCount);
        Encoding.ASCII.GetBytes(password).CopyTo(packet.AsSpan(PasswordOffset, PasswordFieldLength));

        packet[6] = Checksum(packet);
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

    [Test]
    public void HuntaholicCreateInstanceId_IsTheEpic73One()
    {
        ((ushort)GamePackets.TM_CS_HUNTAHOLIC_CREATE_INSTANCE).Should().Be(4003);
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_HUNTAHOLIC_CREATE_INSTANCE).Should().BeTrue();
    }

    [Test]
    public void HuntaholicCreateInstanceId_DoesNotCollideWithAnExistingMember()
    {
        // Two names sharing one value would silently route a foreign packet into this handler.
        var values = Enum.GetValues<GamePackets>().Select(value => (ushort)value).ToArray();

        values.Should().OnlyHaveUniqueItems();
        values.Should().Contain(4003);
    }

    [Test]
    public void ClientFrame_IsFiftySixBytesWithTheNameAtSevenTheCountAtThirtyEightAndThePasswordAtThirtyNine()
    {
        var packet = ClientFrame("HuntaHolic room", 12, "secret");

        packet.Length.Should().Be(CreateInstanceLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(CreateInstanceLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(4003);
        packet[6].Should().Be(Checksum(packet));

        // name: fixed 31-byte buffer starting at 7 — 7 + 31 = 38, where max_member_count lives.
        Encoding.ASCII.GetString(packet, NameOffset, NameFieldLength).TrimEnd('\0').Should().Be("HuntaHolic room");
        packet[NameOffset + "HuntaHolic room".Length].Should().Be(0);
        packet[MaxMemberCountOffset - 1].Should().Be(0);

        // max_member_count: one byte at 38.
        packet[MaxMemberCountOffset].Should().Be(12);

        // password: fixed 17-byte buffer starting at 39 — 39 + 17 = 56, the end of the frame.
        Encoding.ASCII.GetString(packet, PasswordOffset, PasswordFieldLength).TrimEnd('\0').Should().Be("secret");
        (PasswordOffset + PasswordFieldLength).Should().Be(CreateInstanceLength);
        packet[CreateInstanceLength - 1].Should().Be(0);
    }

    [Test]
    public void PacketConstants_FixTheFiftySixByteLayout()
    {
        GameHuntaholicPackets.NameOffset.Should().Be(NameOffset);
        GameHuntaholicPackets.NameFieldLength.Should().Be(NameFieldLength);
        GameHuntaholicPackets.MaxMemberCountOffset.Should().Be(MaxMemberCountOffset);
        GameHuntaholicPackets.PasswordOffset.Should().Be(PasswordOffset);
        GameHuntaholicPackets.PasswordFieldLength.Should().Be(PasswordFieldLength);
        GameHuntaholicPackets.PayloadLength.Should().Be(49);
        GameHuntaholicPackets.CreateInstanceLength.Should().Be(CreateInstanceLength);
    }

    [Test]
    public void TryReadCreateInstance_ReadsTheNameAtOffsetSeven()
    {
        GameHuntaholicPackets.TryReadCreateInstance(ClientFrame("HuntaHolic room", 4), out var request)
            .Should().BeTrue();

        request.Name.Should().Be("HuntaHolic room");
    }

    [Test]
    public void TryReadCreateInstance_ReadsTheFullThirtyCharacterName()
    {
        // 30 characters plus the NUL is what fills the 31-byte field exactly: the longest name the field holds.
        var name = new string('A', 30);

        GameHuntaholicPackets.TryReadCreateInstance(ClientFrame(name), out var request).Should().BeTrue();

        request.Name.Should().Be(name);
        request.Name.Length.Should().Be(NameFieldLength - 1);
    }

    [Test]
    public void TryReadCreateInstance_StopsTheNameAtItsNul()
    {
        // The name buffer is memset to zero before the bounded copy: whatever a caller left after the NUL is
        // padding, never part of the name.
        var packet = ClientFrame("abc", 3);
        for (var i = NameOffset + "abc".Length + 1; i < NameOffset + NameFieldLength; i++)
        {
            packet[i] = (byte)'x';
        }

        GameHuntaholicPackets.TryReadCreateInstance(packet, out var request).Should().BeTrue();

        request.Name.Should().Be("abc");
    }

    [TestCase(0, TestName = "TryReadCreateInstance_KeepsAZeroMemberCount")]
    [TestCase(1, TestName = "TryReadCreateInstance_KeepsAOneMemberCount")]
    [TestCase(40, TestName = "TryReadCreateInstance_KeepsAFortyMemberCount")]
    public void TryReadCreateInstance_ReadsMaxMemberCountAtOffsetThirtyEight(sbyte maxMemberCount)
    {
        // The value is not validated: nothing in rzu, NGemity or the client bounds it, so the reader only
        // reports the byte it found. Its domain is a game policy question (NON ÉTABLI (5) of the sheet).
        var packet = ClientFrame("room", maxMemberCount);

        GameHuntaholicPackets.TryReadCreateInstance(packet, out var request).Should().BeTrue();

        request.MaxMemberCount.Should().Be(maxMemberCount);
        packet[MaxMemberCountOffset].Should().Be(unchecked((byte)maxMemberCount));
    }

    [Test]
    public void TryReadCreateInstance_ReadsMaxMemberCountAsTheSignedInt8RzuDeclares()
    {
        // 0xFF at offset 38 is -1, not 255: rzu declares _(simple)(int8_t, max_member_count). The client copies
        // the byte out of its window with a plain byte load, so it agrees on the byte; the reader keeps the
        // declared type rather than reinterpreting it as unsigned.
        var packet = ClientFrame("room", 0);
        packet[MaxMemberCountOffset] = 0xFF;

        GameHuntaholicPackets.TryReadCreateInstance(packet, out var request).Should().BeTrue();

        request.MaxMemberCount.Should().Be(-1);
    }

    [Test]
    public void TryReadCreateInstance_ReadsMaxMemberCountNotTheByteBeforeOrAfterIt()
    {
        var packet = ClientFrame("room", 0);
        packet[MaxMemberCountOffset - 1] = 11;
        packet[MaxMemberCountOffset] = 22;
        packet[MaxMemberCountOffset + 1] = 33;

        GameHuntaholicPackets.TryReadCreateInstance(packet, out var request).Should().BeTrue();

        request.MaxMemberCount.Should().Be(22);
    }

    [Test]
    public void TryReadCreateInstance_ReadsThePasswordAtOffsetThirtyNine()
    {
        GameHuntaholicPackets.TryReadCreateInstance(ClientFrame("room", 8, "secret"), out var request)
            .Should().BeTrue();

        request.PasswordLength.Should().Be("secret".Length);
        request.HasPassword.Should().BeTrue();
    }

    [Test]
    public void TryReadCreateInstance_ReadsTheFullSixteenCharacterPassword()
    {
        // 16 characters plus the NUL fills the 17-byte field exactly.
        var password = new string('p', 16);

        GameHuntaholicPackets.TryReadCreateInstance(ClientFrame("room", 8, password), out var request)
            .Should().BeTrue();

        request.PasswordLength.Should().Be(PasswordFieldLength - 1);
        request.HasPassword.Should().BeTrue();
    }

    [Test]
    public void TryReadCreateInstance_ReportsAnEmptyPasswordAsNoPassword()
    {
        // The client skips the bounded copy when its password field is empty, so the 17 bytes stay as the
        // memset left them. Whether an empty password is a public room or a refusal is a game policy question
        // (NON ÉTABLI (6) of the sheet): the reader only reports that none was supplied.
        GameHuntaholicPackets.TryReadCreateInstance(ClientFrame("room", 8), out var request).Should().BeTrue();

        request.PasswordLength.Should().Be(0);
        request.HasPassword.Should().BeFalse();
    }

    [Test]
    public void TryReadCreateInstance_DoesNotCarryThePasswordValue()
    {
        // The frame is logged when it arrives and a room password is a secret: only its length leaves the
        // reader, so no log line can ever print it.
        var properties = typeof(GameHuntaholicPackets.HuntaholicCreateInstanceRequest)
            .GetProperties().Select(property => property.Name).ToArray();

        properties.Should().Contain(nameof(GameHuntaholicPackets.HuntaholicCreateInstanceRequest.PasswordLength));
        properties.Should().NotContain("Password");
    }

    [Test]
    public void TryReadCreateInstance_DoesNotReadTheNameOutOfThePasswordField()
    {
        // A name field holding a single NUL is empty, whatever the password field contains: had the reader
        // pointed at 39 (or at 38) the password would come back as the name.
        GameHuntaholicPackets.TryReadCreateInstance(ClientFrame("", 8, "secret"), out var request).Should().BeTrue();

        request.Name.Should().BeEmpty();
    }

    [Test]
    public void TryReadCreateInstance_DoesNotReadTheNamePastItsField()
    {
        // A name whose NUL sits at the last byte of its field (index 30) must not take max_member_count for a
        // 31st character.
        var packet = ClientFrame(new string('A', 30), 7);
        packet[NameOffset + NameFieldLength - 1].Should().Be(0);

        GameHuntaholicPackets.TryReadCreateInstance(packet, out var request).Should().BeTrue();

        request.Name.Should().Be(new string('A', 30));
        request.Name.Should().NotContain("\u0007");
        request.MaxMemberCount.Should().Be(7);
    }

    [Test]
    public void TryReadCreateInstance_RefusesANameWithoutNulInsideItsThirtyOneBytes()
    {
        // A 31-byte name leaves no NUL in the field: the 7.3 client cannot produce it (its window accepts 30
        // characters), so the frame is not one of ours. Refusing keeps the reader from ever stepping past the
        // field's last byte — the failure mode the sheet warns about is a name that spills over
        // max_member_count.
        var packet = ClientFrame(new string('A', 31), 9);

        GameHuntaholicPackets.TryReadCreateInstance(packet, out var request).Should().BeFalse();
        request.Should().Be(default(GameHuntaholicPackets.HuntaholicCreateInstanceRequest));
    }

    [Test]
    public void TryReadCreateInstance_RefusesAPasswordWithoutNulInsideItsSeventeenBytes()
    {
        // 17 password bytes leave no room for the NUL rzu's writeString reserves; the reader refuses instead of
        // treating max_member_count or a following byte as the terminator.
        var packet = ClientFrame("room", 9, new string('p', 17));

        packet[PasswordOffset + PasswordFieldLength - 1].Should().NotBe(0);

        GameHuntaholicPackets.TryReadCreateInstance(packet, out var request).Should().BeFalse();
        request.Should().Be(default(GameHuntaholicPackets.HuntaholicCreateInstanceRequest));
    }

    [TestCase(0, TestName = "TryReadCreateInstance_RejectsAnEmptyFrame")]
    [TestCase(6, TestName = "TryReadCreateInstance_RejectsAFrameShorterThanAHeader")]
    [TestCase(7, TestName = "TryReadCreateInstance_RejectsAHeaderOnlyFrame")]
    [TestCase(38, TestName = "TryReadCreateInstance_RejectsAFrameEndingAtMaxMemberCount")]
    [TestCase(39, TestName = "TryReadCreateInstance_RejectsAFrameWithoutItsPasswordField")]
    [TestCase(55, TestName = "TryReadCreateInstance_RejectsATruncatedFrame")]
    [TestCase(57, TestName = "TryReadCreateInstance_RejectsAPaddedFrame")]
    [TestCase(72, TestName = "TryReadCreateInstance_RejectsAnEnterFrameLength")]
    public void TryReadCreateInstance_RejectsAnyLengthOtherThanFiftySix(int length)
    {
        // The client writes Length = 0x38 in hard: a shorter frame has no password field at all and a longer
        // one was not built by the 7.3 client. Reading it anyway would take the fields of whatever follows it
        // in the receive buffer.
        var packet = new byte[length];
        ClientFrame("room", 8, "secret").AsSpan(0, Math.Min(length, CreateInstanceLength)).CopyTo(packet);

        GameHuntaholicPackets.TryReadCreateInstance(packet, out var request).Should().BeFalse();
        request.Should().Be(default(GameHuntaholicPackets.HuntaholicCreateInstanceRequest));
    }

    [Test]
    public void TryReadCreateInstance_AcceptsAFrameTheClientItselfWouldBuild()
    {
        // The round trip on all three fields at once, in the offsets the client constructor writes them to.
        var packet = ClientFrame("HuntaHolic room", 6, "hunter2");

        GameHuntaholicPackets.TryReadCreateInstance(packet, out var request).Should().BeTrue();

        request.Name.Should().Be("HuntaHolic room");
        request.MaxMemberCount.Should().Be(6);
        request.PasswordLength.Should().Be(7);
        request.HasPassword.Should().BeTrue();
    }
}
