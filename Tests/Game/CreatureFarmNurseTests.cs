using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Creatures;
using Navislamia.Game.Scripting;

namespace Tests.Game;

/// <summary>
/// The nursing gesture, docs/packet-specs/6006-nurse-creature.md: TM_CS_NURSE_CREATURE (6006) is the 11-byte
/// frame whose handle sits at +7, its answer is TM_SC_RESULT_NURSE (6007), 8 bytes with the verdict at +7 —
/// 0 FAILED, 1 NO_REWARD, 2 REWARDED — and the verdict follows the four conditions of <c>NurseSummon</c>:
/// the handle must resolve a card, carry <c>ITEM_FLAG_FARMED_SUMMON</c>, be named by a farm row, and that
/// entry must not have been nursed since the last 06:00. One test per refusal and per verdict value.
/// </summary>
[TestFixture]
public class CreatureFarmNurseTests
{
    private const int HeaderSize = 7;
    private const int NurseRequestLength = 11;
    private const int NurseResultLength = 8;
    private const ushort ResultNurseId = 6007;

    /// <summary>The local clock of the fixtures: 10:00, i.e. after the 06:00 nursing reset of the same day.</summary>
    private static readonly DateTime Morning = new(2026, 10, 7, 10, 0, 0, DateTimeKind.Local);

    private static ItemFlag DepositedCard => CreatureFarmRules.WithFarmedSummon(ItemFlag.None);

    // --- the two frames on the wire ---------------------------------------------------------------------

    [Test]
    public void TheResultNurseFrameIsEightBytesWithItsIdInFourAndItsResultInSeven()
    {
        GameFarmPackets.ResultNurseLength.Should().Be(NurseResultLength);
        GameFarmPackets.ResultNurseResultOffset.Should().Be(HeaderSize);

        foreach (var result in new[] { NurseResult.Failed, NurseResult.NoReward, NurseResult.Rewarded })
        {
            var frame = GameFarmPackets.BuildResultNurse(result);

            frame.Should().HaveCount(NurseResultLength);
            BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(0, 4)).Should().Be(NurseResultLength);
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)).Should().Be(ResultNurseId);
            frame[7].Should().Be((byte)result);
            frame[6].Should().Be(StorageTestHarness.Checksum(frame));
        }
    }

    [Test]
    public void TheVerdictValuesAreTheThreeTheClientDistinguishes()
    {
        // 0 is the only value that leaves the client silent (SFrame.exe 0x613c94-0x613d24).
        ((byte)NurseResult.Failed).Should().Be(0);
        ((byte)NurseResult.NoReward).Should().Be(1);
        ((byte)NurseResult.Rewarded).Should().Be(2);
    }

    [Test]
    public void TheNurseRequestIsElevenBytesWithItsHandleInSeven()
    {
        GameFarmPackets.CreatureCardHandleLength.Should().Be(NurseRequestLength);

        var frame = ClientFrame((ushort)GamePackets.TM_CS_NURSE_CREATURE, NurseRequestLength, 0x1234ABCD);

        GameFarmPackets.TryReadNurseCreature(frame, out var handle).Should().BeTrue();
        handle.Should().Be(0x1234ABCD);
    }

    // --- the four conditions of NurseSummon, one by one --------------------------------------------------

    [Test]
    public void AHandleResolvingNoCardIsRefused()
    {
        CreatureFarmRules.CanNurse(null, Morning).Should().BeFalse();
    }

    [Test]
    public void ACardThatIsNotDepositedIsRefused()
    {
        // bit 27 clear: the card is in the player's inventory, not in the farm.
        var target = new FarmNursingTarget(42, ItemFlag.Card, true, null);

        CreatureFarmRules.CanNurse(target, Morning).Should().BeFalse();
        CreatureFarmRules.CanNurse(new FarmNursingTarget(42, ItemFlag.None, true, null), Morning).Should().BeFalse();
    }

    [Test]
    public void ACardThatNamesNoFarmEntryIsRefused()
    {
        var target = new FarmNursingTarget(42, DepositedCard, false, null);

        CreatureFarmRules.CanNurse(target, Morning).Should().BeFalse();
    }

    [Test]
    public void AnEntryAlreadyNursedSinceSixIsRefused()
    {
        // Nursed at 07:00 today, asked again at 10:00: the next window opens tomorrow at 06:00.
        var target = new FarmNursingTarget(42, DepositedCard, true,
            new DateTime(2026, 10, 7, 7, 0, 0, DateTimeKind.Local));

        CreatureFarmRules.CanNurse(target, Morning).Should().BeFalse();
        CreatureFarmRules.RefreshSeconds(target.NursingTime, Morning).Should().BePositive();
    }

    [Test]
    public void AnEntryNursedBeforeSixCanBeNursedAgain()
    {
        // Nursed at 23:30 yesterday: that sits before today's 06:00 reset, so the window is open again.
        var target = new FarmNursingTarget(42, DepositedCard, true,
            new DateTime(2026, 10, 6, 23, 30, 0, DateTimeKind.Local));

        CreatureFarmRules.CanNurse(target, Morning).Should().BeTrue();
        CreatureFarmRules.RefreshSeconds(target.NursingTime, Morning).Should().Be(0);
    }

    [Test]
    public void AnEntryNeverNursedCanBeNursed()
    {
        CreatureFarmRules.CanNurse(new FarmNursingTarget(42, DepositedCard, true, null), Morning).Should().BeTrue();
    }

    [Test]
    public void TheScriptsOwnAnswerNamesTheVerdictAndItsAbsenceIsNeverAFailed()
    {
        CreatureFarmRules.NurseHandlerFunction.Should().Be("NPC_Creature_Farm_nurse_handler");

        CreatureFarmRules.NurseVerdict("1").Should().Be(NurseResult.Rewarded);
        CreatureFarmRules.NurseVerdict("0").Should().Be(NurseResult.NoReward);
        CreatureFarmRules.NurseVerdict(null).Should().Be(NurseResult.NoReward, "an absent script is not a refusal");
        CreatureFarmRules.NurseVerdict(string.Empty).Should().Be(NurseResult.NoReward);
        CreatureFarmRules.NurseVerdict("true").Should().Be(NurseResult.NoReward);
    }

    // --- the gesture, end to end through the service -----------------------------------------------------

    [Test]
    public async Task AHandleResolvingNoCardIsAnsweredZeroAndTheScriptIsNeverCalled()
    {
        var bench = new Bench(Morning);
        A.CallTo(() => bench.Store.LoadNursingTargetAsync("Killian", 99)).Returns((FarmNursingTarget)null);

        var verdict = await bench.Service.NurseAsync(bench.Client, 99);

        verdict.Should().Be(NurseResult.Failed);
        bench.Frame.Should().HaveCount(NurseResultLength);
        BinaryPrimitives.ReadUInt16LittleEndian(bench.Frame.AsSpan(4, 2)).Should().Be(ResultNurseId);
        bench.Frame[7].Should().Be(0);
        A.CallTo(() => bench.Store.SetNursingTimeAsync(A<string>._, A<long>._, A<DateTime>._))
            .MustNotHaveHappened();
        A.CallTo(() => bench.Scripts.CallGlobalFunction(A<string>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task AnUndepositedCardIsAnsweredZeroAndNothingIsWritten()
    {
        var bench = new Bench(Morning);
        A.CallTo(() => bench.Store.LoadNursingTargetAsync("Killian", 7))
            .Returns(new FarmNursingTarget(7, ItemFlag.Card, true, null));

        var verdict = await bench.Service.NurseAsync(bench.Client, 7);

        verdict.Should().Be(NurseResult.Failed);
        bench.Frame[7].Should().Be(0);
        A.CallTo(() => bench.Store.SetNursingTimeAsync(A<string>._, A<long>._, A<DateTime>._))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task AnEntryNursedSinceSixIsAnsweredZeroAndNothingIsRewritten()
    {
        var bench = new Bench(Morning);
        A.CallTo(() => bench.Store.LoadNursingTargetAsync("Killian", 7)).Returns(
            new FarmNursingTarget(7, DepositedCard, true, new DateTime(2026, 10, 7, 7, 0, 0, DateTimeKind.Local)));

        var verdict = await bench.Service.NurseAsync(bench.Client, 7);

        verdict.Should().Be(NurseResult.Failed);
        bench.Frame[7].Should().Be(0);
        A.CallTo(() => bench.Store.SetNursingTimeAsync(A<string>._, A<long>._, A<DateTime>._))
            .MustNotHaveHappened();
        A.CallTo(() => bench.Scripts.CallGlobalFunction(A<string>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task AFarmEntryThatVanishedBeforeTheWriteIsAnsweredZero()
    {
        var bench = new Bench(Morning);
        A.CallTo(() => bench.Store.LoadNursingTargetAsync("Killian", 7))
            .Returns(new FarmNursingTarget(7, DepositedCard, true, null));
        A.CallTo(() => bench.Store.SetNursingTimeAsync("Killian", 7, Morning)).Returns(false);

        var verdict = await bench.Service.NurseAsync(bench.Client, 7);

        verdict.Should().Be(NurseResult.Failed);
        bench.Frame[7].Should().Be(0);
        A.CallTo(() => bench.Scripts.CallGlobalFunction(A<string>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task ANursingAfterSixWritesTheTimeThenAnswersTheScriptsNoReward()
    {
        var bench = new Bench(Morning);
        var nursed = new DateTime(2026, 10, 6, 23, 30, 0, DateTimeKind.Local);
        A.CallTo(() => bench.Store.LoadNursingTargetAsync("Killian", 7))
            .Returns(new FarmNursingTarget(7, DepositedCard, true, nursed));
        A.CallTo(() => bench.Store.SetNursingTimeAsync("Killian", 7, Morning)).Returns(true);
        A.CallTo(() => bench.Scripts.CallGlobalFunction(CreatureFarmRules.NurseHandlerFunction)).Returns("0");

        var verdict = await bench.Service.NurseAsync(bench.Client, 7);

        verdict.Should().Be(NurseResult.NoReward);
        bench.Frame[7].Should().Be(1);
        A.CallTo(() => bench.Store.SetNursingTimeAsync("Killian", 7, Morning)).MustHaveHappenedOnceExactly();
        A.CallTo(() => bench.Scripts.CallGlobalFunction(CreatureFarmRules.NurseHandlerFunction))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task AOneFromTheScriptIsTheRewardedVerdict()
    {
        var bench = new Bench(Morning);
        A.CallTo(() => bench.Store.LoadNursingTargetAsync("Killian", 7))
            .Returns(new FarmNursingTarget(7, DepositedCard, true, null));
        A.CallTo(() => bench.Store.SetNursingTimeAsync("Killian", 7, Morning)).Returns(true);
        A.CallTo(() => bench.Scripts.CallGlobalFunction(CreatureFarmRules.NurseHandlerFunction)).Returns("1");

        var verdict = await bench.Service.NurseAsync(bench.Client, 7);

        verdict.Should().Be(NurseResult.Rewarded);
        bench.Frame[7].Should().Be(2);
    }

    [Test]
    public async Task AMissingScriptIsANoRewardNeverAFailed()
    {
        // The farm's script is not in the repository: the nursing still goes through and is written, and the
        // client is told NO_REWARD (1) — the official server never turns a missing script into FAILED (0).
        var bench = new Bench(Morning);
        A.CallTo(() => bench.Store.LoadNursingTargetAsync("Killian", 7))
            .Returns(new FarmNursingTarget(7, DepositedCard, true, null));
        A.CallTo(() => bench.Store.SetNursingTimeAsync("Killian", 7, Morning)).Returns(true);
        A.CallTo(() => bench.Scripts.CallGlobalFunction(A<string>._)).Returns(null);

        var verdict = await bench.Service.NurseAsync(bench.Client, 7);

        verdict.Should().Be(NurseResult.NoReward);
        bench.Frame[7].Should().Be(1);
        A.CallTo(() => bench.Store.SetNursingTimeAsync("Killian", 7, Morning)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task ASessionWithoutACharacterIsAnsweredNothingAtAll()
    {
        var bench = new Bench(Morning, characterName: null);

        var verdict = await bench.Service.NurseAsync(bench.Client, 7);

        verdict.Should().Be(NurseResult.Failed);
        bench.Connection.Sent.Should().BeEmpty("the client is not in the world yet: there is no farm to nurse");
        A.CallTo(() => bench.Store.LoadNursingTargetAsync(A<string>._, A<long>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task ARealLuaHandlerReturningOneIsServedAsTheRewardedVerdict()
    {
        // The official contract end to end on the repository's own interpreter: the chunk returns 1, and that
        // is what makes the frame carry 2. Nothing else can turn it into a REWARDED.
        var scripts = new ScriptService(A.Fake<ILogger<ScriptService>>());
        scripts.RunString("function NPC_Creature_Farm_nurse_handler() return 1 end").Should().Be(1);

        var bench = new Bench(Morning, scripts: scripts);
        A.CallTo(() => bench.Store.LoadNursingTargetAsync("Killian", 7))
            .Returns(new FarmNursingTarget(7, DepositedCard, true, null));
        A.CallTo(() => bench.Store.SetNursingTimeAsync("Killian", 7, Morning)).Returns(true);

        var verdict = await bench.Service.NurseAsync(bench.Client, 7);

        verdict.Should().Be(NurseResult.Rewarded);
        bench.Frame[7].Should().Be(2);
    }

    [Test]
    public void TheGlobalCallReadsTheChunksOwnValue()
    {
        // The seam the lot added (IScriptService.CallGlobalFunction): RunString renders the success of the
        // execution, this one renders the value the chunk hands back.
        var scripts = new ScriptService(A.Fake<ILogger<ScriptService>>());

        scripts.CallGlobalFunction(CreatureFarmRules.NurseHandlerFunction).Should().BeNull("no script is loaded");
        scripts.RunString("function NPC_Creature_Farm_nurse_handler() return 1 end").Should().Be(1);
        scripts.CallGlobalFunction(CreatureFarmRules.NurseHandlerFunction).Should().Be("1");
        scripts.RunString("function NPC_Creature_Farm_nurse_handler() return 0 end").Should().Be(1);
        scripts.CallGlobalFunction(CreatureFarmRules.NurseHandlerFunction).Should().Be("0");
    }

    [Test]
    public void TheGlobalCallOfABrokenChunkIsNullRatherThanAThrow()
    {
        var scripts = new ScriptService(A.Fake<ILogger<ScriptService>>());
        scripts.RunString("function NPC_Creature_Farm_nurse_handler() error('boom') end").Should().Be(1);

        scripts.CallGlobalFunction(CreatureFarmRules.NurseHandlerFunction).Should().BeNull();
    }

    // --- the receive loop ---------------------------------------------------------------------------------

    [Test]
    public void TheNurseRequestIsDispatchedAndAnsweredWithTheVerdict()
    {
        // Without a farm service the arm still owes the frame, and it must not reach the throwing switch.
        var frame = ClientFrame((ushort)GamePackets.TM_CS_NURSE_CREATURE, NurseRequestLength, 7);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        StorageTestHarness.WaitFor(() => connection.Sent.Count > 0);
        connection.Sent.Should().HaveCount(1);
        BinaryPrimitives.ReadUInt16LittleEndian(connection.Sent[0].AsSpan(4, 2)).Should().Be(ResultNurseId);
        connection.Sent[0][7].Should().Be(0);
    }

    [Test]
    public void AMalformedNurseRequestIsOnlyLogged()
    {
        var frame = ClientFrame((ushort)GamePackets.TM_CS_NURSE_CREATURE, NurseRequestLength + 1, 7);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty("no reference establishes an answer to a frame the client cannot build");
    }

    [Test]
    public void AnIncomingResultNurseIsLoggedAndDropped()
    {
        // 6007 is declared in GamePackets, so it needs an arm before the throwing switch even though only the
        // server emits it: the 7.3 client routes it and builds none.
        var frame = ClientFrame(ResultNurseId, NurseResultLength, 2);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
    }

    // --- fixtures ---------------------------------------------------------------------------------------

    private static byte[] ClientFrame(ushort id, int length, uint cardHandle)
    {
        var packet = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), id);
        packet[6] = StorageTestHarness.Checksum(packet);

        if (length >= NurseRequestLength)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), cardHandle);
        }

        return packet;
    }

    /// <summary>
    /// A real <see cref="GameClient"/> on a recording connection, a fake store and a fake interpreter: the
    /// three collaborators the gesture reads, so that every branch can be reached without a database.
    /// </summary>
    private sealed class Bench
    {
        private readonly StorageTestHarness.FrameConnection _connection = new(Array.Empty<byte>());

        public Bench(DateTime now, string characterName = "Killian", IScriptService scripts = null)
        {
            Store = A.Fake<ICreatureFarmStore>();
            Scripts = scripts ?? A.Fake<IScriptService>();
            Client = StorageTestHarness.NewGameClient(_connection);
            StorageTestHarness.Session(Client).CharacterName = characterName;
            Service = new CreatureFarmService(Store, () => now, scripts: Scripts);
        }

        public ICreatureFarmStore Store { get; }

        public IScriptService Scripts { get; }

        public GameClient Client { get; }

        public CreatureFarmService Service { get; }

        /// <summary>The recording connection, so a test can tell an answered gesture from a silent one.</summary>
        public StorageTestHarness.FrameConnection Connection => _connection;

        /// <summary>The single frame the service pushed back.</summary>
        public byte[] Frame
        {
            get
            {
                _connection.Sent.Should().ContainSingle();
                return _connection.Sent[0];
            }
        }
    }
}
