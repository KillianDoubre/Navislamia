using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// The Epic 7.3 conduct of <c>TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT</c> (264) on the socle
/// service: the frame carries a rate and no handle at all, the rate is bounded to the (0,1] domain the 7.3
/// client is measured to write, and whatever the outcome nothing is restored and nothing is consumed.
///
/// An out-of-domain rate and a well-formed one are answered with the very same <c>TM_SC_RESULT</c> +
/// <c>InvalidArgument</c> + value 0, so this fixture cannot tell the two branches apart by itself: what
/// locks the bound is the rule's own test (<c>CraftingSoclePacketsTests.IsRestorableRate_*</c>) and what
/// locks the wiring is the source scan at the end of this file.
/// See docs/packet-specs/264-transmit-ethereal-durability-to-equipment.md §5.5.
/// </summary>
[TestFixture]
public class CraftingSocleServiceTests
{
    private const ushort TransmitToEquipment = (ushort)GamePackets.TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT;

    private sealed record Harness(CraftingSocleService Service, GameClient Client,
        StorageTestHarness.FrameConnection Connection);

    private static Harness Build()
    {
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var session = StorageTestHarness.Session(client);
        session.CharacterName = "Killian";
        session.CharacterHandle = 42;

        // The 264 frame names no handle, so the character service is never asked for an item on this path.
        return new Harness(new CraftingSocleService(A.Fake<ICharacterService>()), client, connection);
    }

    /// <summary>The 7.3 frame: 7 bytes of header, then the four bytes of the float rate and nothing else.</summary>
    private static byte[] Frame(float rate)
    {
        var packet = new byte[11];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), TransmitToEquipment);
        BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(7, 4), rate);
        return packet;
    }

    private static Task Handle(Harness harness, byte[] packet)
        => harness.Service.HandleAsync(harness.Client, TransmitToEquipment, packet);

    private static TS_SC_RESULT ResultOf(byte[] packet)
        => new Packet<TS_SC_RESULT>(packet).GetDataStruct<TS_SC_RESULT>();

    [TestCase(0f, TestName = "Handle264_RefusesAZeroRate")]
    [TestCase(-1.5f, TestName = "Handle264_RefusesANegativeRate")]
    [TestCase(100f, TestName = "Handle264_RefusesAPercentStyleRate")]
    [TestCase(float.NaN, TestName = "Handle264_RefusesNaN")]
    [TestCase(float.PositiveInfinity, TestName = "Handle264_RefusesPositiveInfinity")]
    [TestCase(float.NegativeInfinity, TestName = "Handle264_RefusesNegativeInfinity")]
    public async Task Handle264_RefusesARateOutsideTheDomainTheClientCanProduce(float rate)
    {
        var harness = Build();

        await Handle(harness, Frame(rate));

        harness.Connection.Sent.Should().ContainSingle("an out-of-domain rate is refused, and nothing else is sent");
        var result = ResultOf(harness.Connection.Sent[0]);
        result.RequestMsgID.Should().Be(264);
        result.Result.Should().Be((ushort)ResultCode.InvalidArgument);
        result.Value.Should().Be(0);
    }

    [TestCase(0.5f, TestName = "Handle264_RefusesThePartialRestitutionInsteadOfCreditingIt")]
    [TestCase(1f, TestName = "Handle264_RefusesTheFullRestitutionInsteadOfCreditingIt")]
    public async Task Handle264_AnswersTheSocleRefusalWithoutCreditingAnything(float rate)
    {
        // The rate is inside the domain, so the frame reaches the socle's usual refusal: what a restitution
        // would credit, spend or destroy is not written in any of the three references (spec §5.3, §7), and
        // answering anything else here would invent it.
        var harness = Build();

        await Handle(harness, Frame(rate));

        harness.Connection.Sent.Should().ContainSingle("no descending packet exists until the restitution is settled");
        var result = ResultOf(harness.Connection.Sent[0]);
        result.RequestMsgID.Should().Be(264);
        result.Result.Should().Be((ushort)ResultCode.InvalidArgument);
        result.Value.Should().Be(0);
    }

    [Test]
    public async Task Handle264_RefusesTheTwelveByteEightOneForm()
    {
        // EPIC_8_1 adds a trailing target byte; 7.3 has no such field, so the 12-byte form is refused rather
        // than read with a stray byte — the same conduct as the reader's, exercised through the service.
        var harness = Build();
        var frame = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), 12u);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), TransmitToEquipment);
        BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(7, 4), 1f);
        frame[11] = 0;

        await Handle(harness, frame);

        harness.Connection.Sent.Should().ContainSingle();
        ResultOf(harness.Connection.Sent[0]).Result.Should().Be((ushort)ResultCode.InvalidArgument);
    }

    [Test]
    public async Task Handle264_SendsNothingBeforeTheCharacterEnteredTheWorld()
    {
        var harness = Build();
        StorageTestHarness.Session(harness.Client).CharacterHandle = 0;

        await Handle(harness, Frame(1f));

        harness.Connection.Sent.Should().BeEmpty("there is no session to answer to");
    }

    [Test]
    public void TheTwoSixFourArm_BoundsTheRateWithTheSocleRule()
    {
        // The 264 arm answers an out-of-domain rate with the same TM_SC_RESULT as the socle's usual refusal
        // (spec §5.5.3: InvalidArgument, value 0), so no black-box test can tell the two branches apart.
        // Nothing smaller than a source scan can lock the wiring between the domain rule and the arm.
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "Game", "Services", "CraftingSocleService.cs"));

        var arm = source.IndexOf(
            "case (ushort)GamePackets.TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT:", StringComparison.Ordinal);
        var nextArm = source.IndexOf("default:", arm, StringComparison.Ordinal);
        var rule = source.IndexOf("CraftingSocleRules.IsRestorableRate(", arm, StringComparison.Ordinal);

        arm.Should().BeGreaterThan(-1, "the 264 arm is what this test is about");
        nextArm.Should().BeGreaterThan(arm, "the arm ends at the default branch of the same switch");
        rule.Should().BeGreaterThan(arm, "264 must be bounded by CraftingSocleRules.IsRestorableRate");
        rule.Should().BeLessThan(nextArm, "the bound has to sit inside the 264 arm");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Navislamia.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the repository root is needed to check the socle source");

        return directory!.FullName;
    }
}
