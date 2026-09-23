using System;
using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// The two client to server frames of the player competition family, TM_CS_COMPETE_REQUEST (4500) and
/// TM_CS_COMPETE_ANSWER (4502), are 39 and 9 bytes on the wire: a 7-byte header, then <c>compete_type</c> at
/// offset 7 for both, the target's name as a fixed 31-byte C string at offset 8 for 4500, and
/// <c>answer_type</c> at offset 8 for 4502. The 31-byte width is proven twice by the client itself: the handle
/// of 4504 is read at offset 39 and the second name of 4506 at offset 40.
/// See docs/packet-specs/socle-competition-joueurs.md.
/// </summary>
[TestFixture]
public class CompetePacketsTests
{
    private const int HeaderLength = 7;
    private const int NameLength = 31;
    private const int RequestLength = 39;
    private const int AnswerLength = 9;

    private static byte[] ClientFrame(int packetLength, ushort id)
    {
        var packet = new byte[packetLength];
        if (packetLength < HeaderLength)
        {
            return packet;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)packetLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), id);
        packet[6] = Checksum(packet);
        return packet;
    }

    private static byte[] RequestFrame(sbyte competeType, string requestee)
    {
        var packet = ClientFrame(RequestLength, (ushort)GamePackets.TM_CS_COMPETE_REQUEST);
        packet[7] = unchecked((byte)competeType);
        Encoding.ASCII.GetBytes(requestee, packet.AsSpan(8, NameLength));
        return packet;
    }

    private static byte[] AnswerFrame(sbyte competeType, sbyte answerType)
    {
        var packet = ClientFrame(AnswerLength, (ushort)GamePackets.TM_CS_COMPETE_ANSWER);
        packet[7] = unchecked((byte)competeType);
        packet[8] = unchecked((byte)answerType);
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
    public void CompeteIds_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_CS_COMPETE_REQUEST).Should().Be(4500);
        ((ushort)GamePackets.TM_CS_COMPETE_ANSWER).Should().Be(4502);

        // rzu declares all seven ids of the family as X(<id>, true), which expands to if(true) id = id_: no
        // version is tested, so 7.3 keeps 4500-4506 unchanged. Both ids must be defined, otherwise
        // OnDataReceived drops the frame as "Undefined packet ID" before any dispatch.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_COMPETE_REQUEST).Should().BeTrue();
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_COMPETE_ANSWER).Should().BeTrue();
    }

    [Test]
    public void ClientFrames_UseTheEpic73Lengths()
    {
        GameCompetePackets.RequestLength.Should().Be(RequestLength);
        GameCompetePackets.AnswerLength.Should().Be(AnswerLength);
        GameCompetePackets.NameLength.Should().Be(NameLength);
        GameCompetePackets.CompeteTypeOffset.Should().Be(HeaderLength);
        GameCompetePackets.AnswerTypeOffset.Should().Be(HeaderLength + 1);
        GameCompetePackets.RequesteeOffset.Should().Be(HeaderLength + 1);

        // The client writes the total length in hard in its own constructors (0x27 for 4500, 0x9 for 4502).
        BinaryPrimitives.ReadUInt32LittleEndian(RequestFrame(0, "Rival").AsSpan(0, 4)).Should()
            .Be((uint)RequestLength);
        BinaryPrimitives.ReadUInt32LittleEndian(AnswerFrame(0, 0).AsSpan(0, 4)).Should()
            .Be((uint)AnswerLength);
    }

    [Test]
    public void RequestFrame_PlacesCompeteTypeAtSevenAndTheNameAtEight()
    {
        var packet = RequestFrame(0, "Rival");

        packet.Length.Should().Be(RequestLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(4500);
        packet[6].Should().Be(Checksum(packet));
        unchecked((sbyte)packet[7]).Should().Be(0);

        Encoding.ASCII.GetString(packet, 8, 5).Should().Be("Rival");
        packet[13].Should().Be(0, "the name is NUL terminated");
        packet[38].Should().Be(0, "the name field is 31 bytes wide and covers offsets 8 to 38");
    }

    [Test]
    public void TryReadRequest_ReadsCompeteTypeAtSevenAndTheNameAtEight()
    {
        GameCompetePackets.TryReadRequest(RequestFrame(1, "Rival"), out var request).Should().BeTrue();

        request.CompeteType.Should().Be(1);
        request.Requestee.Should().Be("Rival");
    }

    [Test]
    public void TryReadRequest_KeepsFieldOrder()
    {
        // Asymmetric values: swapping compete_type and the name, or shifting the name by one byte, fails here.
        GameCompetePackets.TryReadRequest(RequestFrame(3, "Killian"), out var request).Should().BeTrue();

        request.CompeteType.Should().Be(3);
        request.Requestee.Should().Be("Killian");
    }

    [Test]
    public void TryReadRequest_StopsAtTheNul()
    {
        var packet = RequestFrame(0, "Bob");
        Encoding.ASCII.GetBytes("ZZZ", packet.AsSpan(12, 3));

        GameCompetePackets.TryReadRequest(packet, out var request).Should().BeTrue();

        // The client copies these names with a loop stopped by the NUL, so anything after it is not the name.
        request.Requestee.Should().Be("Bob");
    }

    [Test]
    public void TryReadRequest_AcceptsAFullThirtyCharacterName()
    {
        var name = new string('A', 30);
        var packet = RequestFrame(0, name);

        packet[38].Should().Be(0, "the 31st byte of the field is the NUL");

        GameCompetePackets.TryReadRequest(packet, out var request).Should().BeTrue();
        request.Requestee.Should().Be(name);
        request.Requestee.Length.Should().Be(30);
    }

    [Test]
    public void TryReadRequest_RejectsANameWithoutItsNulInsideTheThirtyOneBytes()
    {
        var packet = RequestFrame(0, new string('A', 30));
        packet[38] = (byte)'B';

        // The client reads a fixed 31-byte buffer as a C string: without a NUL the name would spill over the
        // byte that follows, so the frame is refused rather than guessed.
        GameCompetePackets.TryReadRequest(packet, out _).Should().BeFalse();
    }

    [TestCase(0, TestName = "TryReadRequest_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadRequest_RejectsTheHeaderOnlyFrame")]
    [TestCase(8, TestName = "TryReadRequest_RejectsAFrameWithoutItsName")]
    [TestCase(38, TestName = "TryReadRequest_RejectsARequestOneByteShort")]
    [TestCase(40, TestName = "TryReadRequest_RejectsARequestOneByteLong")]
    [TestCase(9, TestName = "TryReadRequest_RejectsTheAnswerLength")]
    [TestCase(43, TestName = "TryReadRequest_RejectsTheCountdownLength")]
    [TestCase(71, TestName = "TryReadRequest_RejectsTheEndLength")]
    public void TryReadRequest_RejectsEveryLengthOtherThanThirtyNine(int packetLength)
    {
        var packet = ClientFrame(packetLength, (ushort)GamePackets.TM_CS_COMPETE_REQUEST);

        GameCompetePackets.TryReadRequest(packet, out _).Should().BeFalse();
    }

    [Test]
    public void AnswerFrame_PlacesCompeteTypeAtSevenAndAnswerTypeAtEight()
    {
        var packet = AnswerFrame(0, 1);

        packet.Length.Should().Be(AnswerLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(4502);
        packet[6].Should().Be(Checksum(packet));
        unchecked((sbyte)packet[7]).Should().Be(0);
        unchecked((sbyte)packet[8]).Should().Be(1);
    }

    [Test]
    public void TryReadAnswer_ReadsCompeteTypeAtSevenAndAnswerTypeAtEight()
    {
        GameCompetePackets.TryReadAnswer(AnswerFrame(0, 1), out var answer).Should().BeTrue();

        answer.CompeteType.Should().Be(0);
        answer.AnswerType.Should().Be(1);
    }

    [Test]
    public void TryReadAnswer_KeepsFieldOrder()
    {
        // Asymmetric values: the two bytes are one apart and must not be swapped.
        GameCompetePackets.TryReadAnswer(AnswerFrame(1, 2), out var answer).Should().BeTrue();

        answer.CompeteType.Should().Be(1);
        answer.AnswerType.Should().Be(2);
    }

    [TestCase(0, TestName = "TryReadAnswer_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadAnswer_RejectsTheHeaderOnlyFrame")]
    [TestCase(8, TestName = "TryReadAnswer_RejectsAnAnswerMissingItsSecondByte")]
    [TestCase(10, TestName = "TryReadAnswer_RejectsAnAnswerOneByteLong")]
    [TestCase(39, TestName = "TryReadAnswer_RejectsTheRequestLength")]
    public void TryReadAnswer_RejectsEveryLengthOtherThanNine(int packetLength)
    {
        var packet = ClientFrame(packetLength, (ushort)GamePackets.TM_CS_COMPETE_ANSWER);

        GameCompetePackets.TryReadAnswer(packet, out _).Should().BeFalse();
    }

    [TestCase(0, TestName = "IsObservedAnswerType_AcceptsZeroFromTheBattleStartControl")]
    [TestCase(1, TestName = "IsObservedAnswerType_AcceptsOneFromTheBattleRejectControl")]
    [TestCase(2, TestName = "IsObservedAnswerType_AcceptsTwoFromTheDefaultBranch")]
    public void IsObservedAnswerType_AcceptsTheThreeEmittedValues(sbyte answerType)
    {
        GameCompetePackets.IsObservedAnswerType(answerType).Should().BeTrue();
    }

    [TestCase(3, TestName = "IsObservedAnswerType_RejectsThree")]
    [TestCase(-1, TestName = "IsObservedAnswerType_RejectsMinusOne")]
    [TestCase(127, TestName = "IsObservedAnswerType_RejectsTheHighSignedValues")]
    [TestCase(-128, TestName = "IsObservedAnswerType_RejectsTheLowSignedValues")]
    public void IsObservedAnswerType_RejectsEverythingElse(sbyte answerType)
    {
        // No source defines the domain of the field; only the three values the client emits are known.
        GameCompetePackets.IsObservedAnswerType(answerType).Should().BeFalse();
    }

    [Test]
    public void RefusalCodes_AreTheCodesTheClientDisplaysForEachFrame()
    {
        // Message box 1636 is shown for 65 on both frames, box 1633 for these two sets; every other code is
        // displayed silently, so the socle only uses codes from these sets.
        var displayedForRequest = new[] { 65, 26, 61, 64, 67, 68, 63, 1, 2 };
        var displayedForAnswer = new[] { 61, 65, 62, 64, 68, 1 };

        displayedForRequest.Should().Contain((int)GameCompetePackets.RequestRefusalCode);
        displayedForAnswer.Should().Contain((int)GameCompetePackets.AnswerRefusalCode);

        ((ushort)GameCompetePackets.RequestRefusalCode).Should().Be(64);
        ((ushort)GameCompetePackets.AnswerRefusalCode).Should().Be(62);
    }

    [Test]
    public void RefusalCodes_ComeFromTheCompetitionFamilyAndAreDistinct()
    {
        // Six of the eight family codes suit a request, five suit an answer. Their values coincide with rzu
        // (TS_RESULT_ALREADY_IN_COMPETE = 0x3D ... TS_RESULT_TARGET_NOT_IN_COMPETIBLE_PLACE = 0x44).
        ((ushort)ResultCode.AlreadyInCompete).Should().Be(61);
        ((ushort)ResultCode.NotInCompete).Should().Be(62);
        ((ushort)ResultCode.NotInCompetablePlace).Should().Be(64);
        ((ushort)ResultCode.TargetWaitingCompeteRequestAnswer).Should().Be(67);
        ((ushort)ResultCode.TargetNotInCompeteablePlace).Should().Be(68);

        GameCompetePackets.RequestRefusalCode.Should().NotBe(GameCompetePackets.AnswerRefusalCode);

        // Codes 1, 2 and 26 are tested by the client but carry no competition name: they are never sent.
        ((ushort)GameCompetePackets.RequestRefusalCode).Should().BeInRange((ushort)61, (ushort)68);
        ((ushort)GameCompetePackets.AnswerRefusalCode).Should().BeInRange((ushort)61, (ushort)68);
    }

    [Test]
    public void RefusalCodes_AvoidTheCodesTheClientWouldShowSilently()
    {
        // 66 has no box for a 4500, and 63, 66 and 67 none for a 4502: those refusals would be invisible.
        GameCompetePackets.RequestRefusalCode.Should().NotBe(ResultCode.TargetNotInCompete);
        GameCompetePackets.AnswerRefusalCode.Should().NotBe(ResultCode.WaitingCompeteRequestAnswer);
        GameCompetePackets.AnswerRefusalCode.Should().NotBe(ResultCode.TargetNotInCompete);
        GameCompetePackets.AnswerRefusalCode.Should().NotBe(ResultCode.TargetWaitingCompeteRequestAnswer);
    }
}
