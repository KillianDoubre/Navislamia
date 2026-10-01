using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// <c>TM_CS_END_QUEST</c> (605), lot (b1) of the quest cycle (docs/packet-specs/605-end-quest.md): the
/// 12-byte frame, its signed <c>nOptionalReward</c>, and refusals when no executable catalogue is supplied.
/// The complete cycle is exercised separately by QuestLifecycleTests.
/// </summary>
[TestFixture]
public class EndQuestTests
{
    private const string Character = "Questor";

    [Test]
    public void TheIdIs605()
    {
        ((ushort)GamePackets.TM_CS_END_QUEST).Should().Be(605);
        GameActionPackets.EndQuestRequestSize.Should().Be(12);
    }

    [Test]
    public void TryReadEndQuest_ReadsTheCodeAtSevenAndTheRewardSlotAtEleven()
    {
        GameActionPackets.TryReadEndQuest(Frame(4242, 3), out var request).Should().BeTrue();

        request.Should().Be(new GameActionPackets.EndQuestRequest(4242, 3));
    }

    [Test]
    public void TryReadEndQuest_ReadsTheClientsNoRewardByteAsMinusOne()
    {
        // The 7.3 client writes 0xff when no optional reward is chosen (SFrame.exe 0x0061aeae).
        GameActionPackets.TryReadEndQuest(Frame(10173, -1), out var request).Should().BeTrue();

        request.OptionalReward.Should().Be(-1, "read as a byte it would be slot 255");
    }

    [TestCase(7)]
    [TestCase(11)]
    [TestCase(13)]
    public void TryReadEndQuest_RefusesAnyLengthOtherThanTwelve(int length)
    {
        var packet = new byte[length];

        GameActionPackets.TryReadEndQuest(packet, out _).Should().BeFalse();
    }

    [Test]
    public void Rules_RefuseANegativeCodeAndASlotOutsideMinusOneToFive()
    {
        QuestEndRules.CheckRequest(1, -1).Should().Be(ResultCode.Success);
        QuestEndRules.CheckRequest(1, 0).Should().Be(ResultCode.Success);
        QuestEndRules.CheckRequest(1, 5).Should().Be(ResultCode.Success);

        QuestEndRules.CheckRequest(-1, -1).Should().Be(ResultCode.NotActable, "as 603 for a negative code");
        QuestEndRules.CheckRequest(1, 6).Should().Be(ResultCode.InvalidArgument, "the 9.4 data has six slots");
        QuestEndRules.CheckRequest(1, -2).Should().Be(ResultCode.InvalidArgument);
    }

    [Test]
    public async Task AQuestTheCharacterDoesNotCarry_IsNotActable()
    {
        var (service, client, connection, characters) = Harness(new CharacterQuestEntity { Code = 1 });

        await service.EndQuestAsync(client, new GameActionPackets.EndQuestRequest(2, -1));

        Result(connection).Should().Be(((ushort)605, ResultCode.NotActable));
        A.CallTo(() => characters.GetQuestsAsync(Character)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task ACarriedQuest_WithoutACatalogueIsRefused_AndNothingChanges()
    {
        var (service, client, connection, characters) = Harness(new CharacterQuestEntity { Code = 10173 });

        await service.EndQuestAsync(client, new GameActionPackets.EndQuestRequest(10173, 0));

        Result(connection).Should().Be(((ushort)605, ResultCode.NotActable),
            "without a catalogue and database, no reward may be announced that was not given");
        connection.Sent.Should().ContainSingle("no quest list is resent: the state did not change");
        A.CallTo(() => characters.DropQuestAsync(A<string>._, A<int>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task ABadSlot_IsRefusedBeforeTheStateIsRead()
    {
        var (service, client, connection, characters) = Harness();

        await service.EndQuestAsync(client, new GameActionPackets.EndQuestRequest(1, 9));

        Result(connection).Should().Be(((ushort)605, ResultCode.InvalidArgument));
        A.CallTo(() => characters.GetQuestsAsync(A<string>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task AnUnreadableState_IsDBError()
    {
        var (service, client, connection, characters) = Harness();
        A.CallTo(() => characters.GetQuestsAsync(Character)).Throws(new InvalidOperationException("down"));

        await service.EndQuestAsync(client, new GameActionPackets.EndQuestRequest(1, -1));

        Result(connection).Should().Be(((ushort)605, ResultCode.DBError));
    }

    [Test]
    public void ReceiveLoop_RoutesAMalformed605ToInvalidArgumentWithoutThrowing()
    {
        var frame = new byte[11];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), 11);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), (ushort)GamePackets.TM_CS_END_QUEST);
        frame[6] = StorageTestHarness.Checksum(frame);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow("605 is declared, so its arm keeps it out of the throwing switch");
        StorageTestHarness.WaitFor(() => connection.Sent.Count > 0);
        Result(connection).Should().Be(((ushort)605, ResultCode.InvalidArgument));
        connection.BytesAvailable.Should().Be(0);
    }

    private static (QuestService Service, GameClient Client, StorageTestHarness.FrameConnection Connection,
        ICharacterService Characters) Harness(params CharacterQuestEntity[] quests)
    {
        var characters = A.Fake<ICharacterService>();
        A.CallTo(() => characters.GetQuestsAsync(Character)).Returns(quests);
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection, characterService: characters);
        StorageTestHarness.Session(client).CharacterName = Character;
        return (new QuestService(characters), client, connection, characters);
    }

    private static byte[] Frame(int code, sbyte reward)
    {
        var frame = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), 12);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), (ushort)GamePackets.TM_CS_END_QUEST);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(7, 4), code);
        frame[11] = unchecked((byte)reward);
        frame[6] = StorageTestHarness.Checksum(frame);
        return frame;
    }

    private static (ushort Request, ResultCode Result) Result(StorageTestHarness.FrameConnection connection)
    {
        var frame = connection.Sent[^1];
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)).Should().Be((ushort)GamePackets.TM_SC_RESULT);
        return (BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(7, 2)),
            (ResultCode)BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(9, 2)));
    }
}
