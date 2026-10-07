using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Creatures;

namespace Tests.Game;

/// <summary>
/// The farm's deposition, docs/packet-specs/6002-foster-creature.md. The frame side: 6002 is 19 + 8T + 8C bytes
/// (card handle +7, both counters +11/+15, both arrays +19) and its answer, TM_SC_RESULT_FOSTER (6003), is the
/// 8-byte frame whose <c>result</c> sits at +7. The gesture side: the reference's order — the card, the ticket
/// cost of its summon's key, the ticket stacks, the cracker stacks, the free slot, the row, then the
/// consumption — so that a refusal never costs a ticket.
/// </summary>
[TestFixture]
public class CreatureFarmDepositTests
{
    private const int HeaderSize = 7;
    private const uint CardHandle = 4242;
    private const uint TicketHandle = 21;
    private const uint CrackerHandle = 33;
    private const int CardResource = 12001;
    private const int SummonResource = 5001;
    private const int TicketResource = 710007;
    private const int CrackerResource = 640002;
    private const int TicketDuration = 432_000;
    private const string Farmer = "Farmer";
    private const int FarmerLevel = 42;

    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0);

    // --- the answer frame ---------------------------------------------------------------------------------

    [Test]
    public void ResultFoster_IsTheEightByteFrameWithItsResultAtSeven()
    {
        GameFarmPackets.FosterResultLength.Should().Be(8);
        GameFarmPackets.FosterResultOffset.Should().Be(HeaderSize, "the result is the first byte after the header");

        var packet = GameFarmPackets.BuildResultFoster(GameFarmPackets.FosterResultAccepted);

        packet.Length.Should().Be(8);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(8, "the length counts the header");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(6003);
        packet[6].Should().Be(StorageTestHarness.Checksum(packet), "the client sums 0..5 the same way");
        packet[7].Should().Be(1);
    }

    [TestCase(0, TestName = "ResultFoster_WritesARefusalAtSeven")]
    [TestCase(1, TestName = "ResultFoster_WritesAnAcceptanceAtSeven")]
    [TestCase(255, TestName = "ResultFoster_WritesAnyByteVerbatim")]
    public void ResultFoster_WritesTheResultByteAtSeven(byte result)
    {
        // The client never tests the byte, it copies it into its internal event: the server writes it verbatim.
        GameFarmPackets.BuildResultFoster(result)[GameFarmPackets.FosterResultOffset].Should().Be(result);
    }

    [Test]
    public void TheTwoResultValues_AreTheReferenceValues()
    {
        // GameMessage.cpp:11905 (`? 1 : 0`) and the 7.3 binary's setne.
        GameFarmPackets.FosterResultAccepted.Should().Be(1);
        GameFarmPackets.FosterResultRefused.Should().Be(0);
    }

    // --- the gesture --------------------------------------------------------------------------------------

    [Test]
    public async Task AValidDeposition_WritesTheRowAndThenConsumesTheStack()
    {
        var farm = new Harness();
        farm.Items.Add(TicketResource, ItemType.FarmPass, TicketDuration, isCash: false);
        farm.Holds(TicketHandle, TicketResource, 5);

        var taken = await farm.Service.FosterCreatureAsync(farm.Client, Tickets(2));

        taken.Should().BeTrue();
        farm.Deposits.Should().ContainSingle();
        var deposit = farm.Deposits[0];
        deposit.CharacterName.Should().Be(Farmer);
        deposit.Slot.Should().Be(0, "slot 0 is the ordinary ticket's, and the farm is empty");
        deposit.CardItemId.Should().Be(CardHandle);
        deposit.MaxLevel.Should().Be(FarmerLevel, "the level a non premium deposition freezes is the player's own");
        deposit.IsCash.Should().BeFalse();
        deposit.IsUsingCracker.Should().BeFalse();
        deposit.Duration.Should().Be(TicketDuration, "the duration comes from the ticket's OptVar1[0]");
        deposit.RegistrationTime.Should().Be(Now);

        // GameContent::GetCreatureFarmTicketCount(GetRate(), GetTransformLevel(), GetEnhance()).
        farm.Cost.LastKey.Should().Be((4, 2, 3));

        farm.Erased.Should().ContainSingle();
        farm.Erased[0].ItemHandle.Should().Be(TicketHandle);
        farm.Erased[0].Count.Should().Be(2);
        farm.Connection.Sent.Should().NotBeEmpty("the consumed stack is announced, as EraseItem does");
    }

    [Test]
    public async Task APremiumTicketWithACracker_TakesASecondSlotAndFreezesHundred()
    {
        var farm = new Harness(cost: 3, taken: new[] { 1 });
        farm.Items.Add(TicketResource, ItemType.FarmPass, durationSeconds: 86_400, isCash: true);
        farm.Items.Add(CrackerResource, ItemType.CreatureFood);
        farm.Holds(TicketHandle, TicketResource, 3);
        farm.Holds(CrackerHandle, CrackerResource, 8);

        var taken = await farm.Service.FosterCreatureAsync(farm.Client,
            new GameFarmPackets.FosterCreatureRequest(CardHandle,
                new[] { new GameFarmPackets.FosterTicket(TicketHandle, 3) },
                new[] { new GameFarmPackets.FosterCracker(CrackerHandle, 3) }));

        taken.Should().BeTrue();
        farm.Deposits.Should().ContainSingle();
        var deposit = farm.Deposits[0];
        deposit.Slot.Should().Be(2, "slot 1 is taken and slots 1..2 are the premium ticket's");
        deposit.MaxLevel.Should().Be(CreatureFarmRules.MaxLevel, "a premium ticket freezes FARM_MAX_LEVEL");
        deposit.IsCash.Should().BeTrue("OptVar2[0] of the ticket is 1");
        deposit.IsUsingCracker.Should().BeTrue();
        deposit.Duration.Should().Be(86_400);

        farm.Erased.Should().HaveCount(2, "both stacks are consumed, tickets first");
        farm.Erased[0].ItemHandle.Should().Be(TicketHandle);
        farm.Erased[1].ItemHandle.Should().Be(CrackerHandle);
    }

    // --- FarmSummon's own refusals (StructPlayer.cpp:11264-11300) ------------------------------------------

    [Test]
    public async Task AnOrdinaryTicket_RefusesASummonNotBelowItsMaster_AndNothingIsSpent()
    {
        var farm = new Harness(summonLevel: FarmerLevel);
        farm.Items.Add(TicketResource, ItemType.FarmPass, TicketDuration, isCash: false);
        farm.Holds(TicketHandle, TicketResource, 5);

        (await farm.Service.FosterCreatureAsync(farm.Client, Tickets(2))).Should().BeFalse();

        farm.Deposits.Should().BeEmpty();
        farm.Erased.Should().BeEmpty();
    }

    [Test]
    public async Task APremiumTicket_TakesASummonAboveItsMaster()
    {
        var farm = new Harness(summonLevel: FarmerLevel + 10);
        farm.Items.Add(TicketResource, ItemType.FarmPass, TicketDuration, isCash: true);
        farm.Holds(TicketHandle, TicketResource, 5);

        (await farm.Service.FosterCreatureAsync(farm.Client, Tickets(2))).Should().BeTrue();
    }

    [Test]
    public async Task ASummonAtTheFarmsCeiling_IsRefused()
    {
        var farm = new Harness(summonLevel: CreatureFarmRules.MaxLevel);
        farm.Items.Add(TicketResource, ItemType.FarmPass, TicketDuration, isCash: true);
        farm.Holds(TicketHandle, TicketResource, 5);

        (await farm.Service.FosterCreatureAsync(farm.Client, Tickets(2))).Should().BeFalse();
        farm.Erased.Should().BeEmpty();
    }

    [Test]
    public async Task AFormedCard_StaysOutOfTheFarm()
    {
        var farm = new Harness();
        farm.Items.Add(TicketResource, ItemType.FarmPass, TicketDuration, isCash: false);
        farm.Holds(TicketHandle, TicketResource, 5);
        StorageTestHarness.Session(farm.Client).SummonSlots = new long[] { CardHandle, 0, 0, 0, 0, 0 };

        (await farm.Service.FosterCreatureAsync(farm.Client, Tickets(2))).Should().BeFalse();
        farm.Deposits.Should().BeEmpty();
    }

    [Test]
    public async Task ADeposition_TakesTheCardOutOfTheClientsBag()
    {
        // FarmSummon's PopItem and RemoveSummon: the session and the client follow the card to the farm.
        var farm = new Harness();
        farm.Items.Add(TicketResource, ItemType.FarmPass, TicketDuration, isCash: false);
        farm.Holds(TicketHandle, TicketResource, 5);

        (await farm.Service.FosterCreatureAsync(farm.Client, Tickets(2))).Should().BeTrue();

        A.CallTo(() => farm.CreatureService.OnCardFarmedAsync(farm.Client, CardHandle, true))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public void TheCostSeam_ReadsTheSocleTable()
    {
        var cost = CreatureFarmTicketCost.FromOptions(new CreatureFarmTicketCostOptions
        {
            Rows = new List<CreatureFarmTicketCostRowOptions> { new() { Rate = 3, Form = 2, EnhanceLevel = 5, TicketCount = 15 } },
        });

        cost.GetTicketCount(3, 2, 5).Should().Be(15);
        cost.GetTicketCount(3, 3, 5).Should().Be(0);
    }

    [Test]
    public async Task WithoutATicketCost_EveryDepositionIsRefused()
    {
        // The cost table is not loaded (docs/packet-specs/6002-foster-creature.md §5.5): the seam answers 0 for
        // every key, and no frame may be accepted on an unverified cost.
        var farm = new Harness(cost: 0);
        farm.Items.Add(TicketResource, ItemType.FarmPass, TicketDuration, isCash: false);
        farm.Holds(TicketHandle, TicketResource, 5);

        var taken = await farm.Service.FosterCreatureAsync(farm.Client, Tickets(1));

        taken.Should().BeFalse();
        farm.Cost.Calls.Should().Be(1, "the key is asked for before anything else is refused");
        farm.Deposits.Should().BeEmpty();
        farm.Erased.Should().BeEmpty("a refusal consumes nothing");
    }

    [Test]
    public async Task AZeroTicketFrame_IsRefused()
    {
        var farm = new Harness();

        var taken = await farm.Service.FosterCreatureAsync(farm.Client, Tickets());

        taken.Should().BeFalse();
        farm.Deposits.Should().BeEmpty();
    }

    [TestCase(0, TestName = "ANegativeTicketCount_IsRefused")]
    [TestCase(-3, TestName = "ABigNegativeTicketCount_IsRefused")]
    public async Task ANonPositiveTicketCount_IsRefused(int count)
    {
        // The parser bounds the frame, not the counters inside it: a negative count would otherwise shrink the
        // sum below the cost and never be erased.
        var farm = new Harness(cost: 1);
        farm.Items.Add(TicketResource, ItemType.FarmPass, TicketDuration, isCash: false);
        farm.Holds(TicketHandle, TicketResource, 5);

        var taken = await farm.Service.FosterCreatureAsync(farm.Client, Tickets(count));

        taken.Should().BeFalse();
        farm.Deposits.Should().BeEmpty();
    }

    [Test]
    public async Task AnUnknownCard_IsRefused()
    {
        var farm = new Harness(cardHandle: 999);
        farm.Items.Add(TicketResource, ItemType.FarmPass, TicketDuration, isCash: false);
        farm.Holds(TicketHandle, TicketResource, 5);

        var taken = await farm.Service.FosterCreatureAsync(farm.Client, Tickets(1));

        taken.Should().BeFalse();
        farm.Deposits.Should().BeEmpty();
    }

    [Test]
    public async Task ACardOutsideTheBag_IsRefused()
    {
        var farm = new Harness(storageId: 7);
        farm.Items.Add(TicketResource, ItemType.FarmPass, TicketDuration, isCash: false);
        farm.Holds(TicketHandle, TicketResource, 5);

        (await farm.Service.FosterCreatureAsync(farm.Client, Tickets(1))).Should().BeFalse();
        farm.Deposits.Should().BeEmpty();
    }

    [Test]
    public async Task ACardWithoutASummon_IsRefused()
    {
        var farm = new Harness(withSummon: false);
        farm.Items.Add(TicketResource, ItemType.FarmPass, TicketDuration, isCash: false);
        farm.Holds(TicketHandle, TicketResource, 5);

        (await farm.Service.FosterCreatureAsync(farm.Client, Tickets(1))).Should().BeFalse();
        farm.Deposits.Should().BeEmpty();
    }

    [Test]
    public async Task ACardAlreadyFarmed_IsRefused()
    {
        var farm = new Harness(flag: ItemFlag.FarmedSummon);
        farm.Items.Add(TicketResource, ItemType.FarmPass, TicketDuration, isCash: false);
        farm.Holds(TicketHandle, TicketResource, 5);

        (await farm.Service.FosterCreatureAsync(farm.Client, Tickets(1))).Should().BeFalse();
        farm.Deposits.Should().BeEmpty();
    }

    [Test]
    public async Task AnUnknownSummonResource_IsRefused()
    {
        var farm = new Harness(summonResource: 999_999);
        farm.Items.Add(TicketResource, ItemType.FarmPass, TicketDuration, isCash: false);
        farm.Holds(TicketHandle, TicketResource, 5);

        (await farm.Service.FosterCreatureAsync(farm.Client, Tickets(1))).Should().BeFalse();
        farm.Cost.Calls.Should().Be(0, "without a rate, a form and an enhance there is no key to ask for");
    }

    [Test]
    public async Task AStackThatIsNotAFarmPass_IsRefused()
    {
        var farm = new Harness(cost: 1);
        farm.Items.Add(TicketResource, ItemType.Etc, TicketDuration, isCash: false);
        farm.Holds(TicketHandle, TicketResource, 5);

        (await farm.Service.FosterCreatureAsync(farm.Client, Tickets(1))).Should().BeFalse();
        farm.Deposits.Should().BeEmpty();
    }

    [Test]
    public async Task AStackTooSmallForWhatItOffers_IsRefused()
    {
        var farm = new Harness(cost: 2);
        farm.Items.Add(TicketResource, ItemType.FarmPass, TicketDuration, isCash: false);
        farm.Holds(TicketHandle, TicketResource, 1);

        (await farm.Service.FosterCreatureAsync(farm.Client, Tickets(2))).Should().BeFalse();
        farm.Erased.Should().BeEmpty();
    }

    [Test]
    public async Task ASumDifferentFromTheCost_IsRefused()
    {
        var farm = new Harness(cost: 3);
        farm.Items.Add(TicketResource, ItemType.FarmPass, TicketDuration, isCash: false);
        farm.Holds(TicketHandle, TicketResource, 5);

        (await farm.Service.FosterCreatureAsync(farm.Client, Tickets(2))).Should().BeFalse();
        farm.Deposits.Should().BeEmpty();
    }

    [Test]
    public async Task ATicketWithoutADuration_IsRefused()
    {
        var farm = new Harness(cost: 1);
        farm.Items.Add(TicketResource, ItemType.FarmPass, durationSeconds: 0, isCash: false);
        farm.Holds(TicketHandle, TicketResource, 5);

        (await farm.Service.FosterCreatureAsync(farm.Client, Tickets(1))).Should().BeFalse();
        farm.Deposits.Should().BeEmpty();
    }

    [Test]
    public async Task TwoTicketsOfDifferentDurations_AreRefused()
    {
        // From the second entry on, duration and premium must match the first (:11787-11796).
        var farm = new Harness(cost: 4);
        farm.Items.Add(TicketResource, ItemType.FarmPass, TicketDuration, isCash: false);
        farm.Items.Add(710_008, ItemType.FarmPass, durationSeconds: 86_400, isCash: false);
        farm.Holds(TicketHandle, TicketResource, 2);
        farm.Holds(22, 710_008, 2);

        var request = new GameFarmPackets.FosterCreatureRequest(CardHandle,
            new[] { new GameFarmPackets.FosterTicket(TicketHandle, 2), new GameFarmPackets.FosterTicket(22, 2) },
            Array.Empty<GameFarmPackets.FosterCracker>());

        (await farm.Service.FosterCreatureAsync(farm.Client, request)).Should().BeFalse();
        farm.Deposits.Should().BeEmpty();
    }

    [Test]
    public async Task TwoTicketsOfTheSameDuration_SumToTheCost()
    {
        var farm = new Harness(cost: 4);
        farm.Items.Add(TicketResource, ItemType.FarmPass, TicketDuration, isCash: false);
        farm.Holds(TicketHandle, TicketResource, 2);
        farm.Holds(22, TicketResource, 2);

        var request = new GameFarmPackets.FosterCreatureRequest(CardHandle,
            new[] { new GameFarmPackets.FosterTicket(TicketHandle, 2), new GameFarmPackets.FosterTicket(22, 2) },
            Array.Empty<GameFarmPackets.FosterCracker>());

        (await farm.Service.FosterCreatureAsync(farm.Client, request)).Should().BeTrue();
        farm.Deposits.Should().ContainSingle();
        farm.Erased.Should().HaveCount(2);
    }

    [Test]
    public async Task ACrackerThatIsNotCreatureFood_IsRefused()
    {
        var farm = new Harness(cost: 1);
        farm.Items.Add(TicketResource, ItemType.FarmPass, TicketDuration, isCash: false);
        farm.Items.Add(CrackerResource, ItemType.Etc);
        farm.Holds(TicketHandle, TicketResource, 1);
        farm.Holds(CrackerHandle, CrackerResource, 4);

        var request = new GameFarmPackets.FosterCreatureRequest(CardHandle,
            new[] { new GameFarmPackets.FosterTicket(TicketHandle, 1) },
            new[] { new GameFarmPackets.FosterCracker(CrackerHandle, 4) });

        (await farm.Service.FosterCreatureAsync(farm.Client, request)).Should().BeFalse();
        farm.Deposits.Should().BeEmpty();
    }

    [Test]
    public async Task CrackersPricedWithTheTicketsNumber_AreRefusedWhenTheyDiffer()
    {
        var farm = new Harness(cost: 2);
        farm.Items.Add(TicketResource, ItemType.FarmPass, TicketDuration, isCash: false);
        farm.Items.Add(CrackerResource, ItemType.CreatureFood);
        farm.Holds(TicketHandle, TicketResource, 2);
        farm.Holds(CrackerHandle, CrackerResource, 4);

        var request = new GameFarmPackets.FosterCreatureRequest(CardHandle,
            new[] { new GameFarmPackets.FosterTicket(TicketHandle, 2) },
            new[] { new GameFarmPackets.FosterCracker(CrackerHandle, 1) });

        (await farm.Service.FosterCreatureAsync(farm.Client, request)).Should().BeFalse();
        farm.Deposits.Should().BeEmpty();
    }

    [Test]
    public async Task AFullFarm_IsRefused()
    {
        // Slot 0 is the only one an ordinary ticket may take: filling it leaves nowhere to write.
        var farm = new Harness(cost: 1, taken: new[] { 0, 1, 2 });
        farm.Items.Add(TicketResource, ItemType.FarmPass, TicketDuration, isCash: false);
        farm.Holds(TicketHandle, TicketResource, 1);

        (await farm.Service.FosterCreatureAsync(farm.Client, Tickets(1))).Should().BeFalse();
        farm.Deposits.Should().BeEmpty();
        farm.Erased.Should().BeEmpty();
    }

    [Test]
    public async Task AStoreThatWritesNoRow_RefusesTheDepositionAndConsumesNothing()
    {
        var farm = new Harness(cost: 1, rowId: 0);
        farm.Items.Add(TicketResource, ItemType.FarmPass, TicketDuration, isCash: false);
        farm.Holds(TicketHandle, TicketResource, 1);

        var taken = await farm.Service.FosterCreatureAsync(farm.Client, Tickets(1));

        taken.Should().BeFalse();
        farm.Deposits.Should().ContainSingle("the store was asked, and it refused the write");
        farm.Erased.Should().BeEmpty("the stacks are consumed only after the row exists");
        farm.Connection.Sent.Should().BeEmpty();
    }

    [Test]
    public async Task ASessionWithoutACharacter_IsRefused()
    {
        var farm = new Harness(cost: 1, characterName: null);
        farm.Items.Add(TicketResource, ItemType.FarmPass, TicketDuration, isCash: false);
        farm.Holds(TicketHandle, TicketResource, 1);

        (await farm.Service.FosterCreatureAsync(farm.Client, Tickets(1))).Should().BeFalse();
        farm.Deposits.Should().BeEmpty();
    }

    // --- the receive loop ---------------------------------------------------------------------------------

    [Test]
    public void AWellFormedDeposit_IsAnsweredWithTheServiceVerdict()
    {
        // 27 bytes: card handle at +7, ticket_info = 1 at +11, cracker_info = 0 at +15, one 8-byte entry at +19.
        var frame = FosterFrame(CardHandle, new[] { TicketHandle }, Array.Empty<uint>());
        var deposits = A.Fake<ICreatureFarmDepositService>();
        GameFarmPackets.FosterCreatureRequest seen = default;
        A.CallTo(() => deposits.FosterCreatureAsync(A<GameClient>._, A<GameFarmPackets.FosterCreatureRequest>._))
            .Invokes(call => seen = (GameFarmPackets.FosterCreatureRequest)call.Arguments[1])
            .Returns(true);

        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection, creatureFarmDepositService: deposits);

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        StorageTestHarness.WaitFor(() => connection.Sent.Count > 0);

        seen.CreatureCardHandle.Should().Be(CardHandle);
        seen.Tickets.Should().ContainSingle();
        seen.Tickets[0].TicketHandle.Should().Be(TicketHandle);
        seen.Tickets[0].TicketCount.Should().Be(2);
        seen.Crackers.Should().BeEmpty();

        connection.Sent.Should().ContainSingle();
        connection.Sent[0].Length.Should().Be(8);
        BinaryPrimitives.ReadUInt16LittleEndian(connection.Sent[0].AsSpan(4, 2)).Should().Be(6003);
        connection.Sent[0][7].Should().Be(1);
    }

    [Test]
    public void ARefusedDeposition_IsAnsweredZero()
    {
        var frame = FosterFrame(CardHandle, new[] { TicketHandle }, Array.Empty<uint>());
        var deposits = A.Fake<ICreatureFarmDepositService>();
        A.CallTo(() => deposits.FosterCreatureAsync(A<GameClient>._, A<GameFarmPackets.FosterCreatureRequest>._))
            .Returns(false);

        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection, creatureFarmDepositService: deposits);

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        StorageTestHarness.WaitFor(() => connection.Sent.Count > 0);

        connection.Sent.Should().ContainSingle("a well formed frame is always answered (§5.6)");
        connection.Sent[0][7].Should().Be(0);
    }

    [Test]
    public void AWellFormedDepositWithoutADepositService_IsRefusedRatherThanAccepted()
    {
        var frame = FosterFrame(CardHandle, new[] { TicketHandle }, Array.Empty<uint>());
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        StorageTestHarness.WaitFor(() => connection.Sent.Count > 0);

        connection.Sent.Should().ContainSingle();
        connection.Sent[0].Length.Should().Be(8);
        BinaryPrimitives.ReadUInt16LittleEndian(connection.Sent[0].AsSpan(4, 2)).Should().Be(6003);
        connection.Sent[0][7].Should().Be(0);
    }

    [Test]
    public void AMalformedDeposit_IsLoggedAndNotAnswered()
    {
        // 27 bytes announced but both counters at zero: the reader bounds the frame, and the deposit's
        // convention keeps a malformed one unanswered (§5.6).
        var frame = new byte[27];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), 27);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), (ushort)GamePackets.TM_CS_FOSTER_CREATURE);
        frame[6] = StorageTestHarness.Checksum(frame);

        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
    }

    [Test]
    public void AnIncomingResultFoster_IsLoggedAndDroppedWithoutThrowing()
    {
        // 6003 is declared because this lot emits it: a declared id must be routed before the throwing switch,
        // or an incoming one breaks the receive loop.
        var frame = GameFarmPackets.BuildResultFoster(GameFarmPackets.FosterResultAccepted);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(connection.BytesAvailable);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
    }

    // --- fixtures -----------------------------------------------------------------------------------------

    private static GameFarmPackets.FosterCreatureRequest Tickets(params int[] counts) =>
        new(CardHandle, counts.Select(count => new GameFarmPackets.FosterTicket(TicketHandle, count)).ToArray(),
            Array.Empty<GameFarmPackets.FosterCracker>());

    private static byte[] FosterFrame(uint cardHandle, uint[] ticketHandles, uint[] crackerHandles)
    {
        var length = GameFarmPackets.GetFosterSize(ticketHandles.Length, crackerHandles.Length);
        var packet = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_FOSTER_CREATURE);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(GameFarmPackets.FosterCardHandleOffset, 4), cardHandle);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(GameFarmPackets.FosterTicketCountOffset, 4),
            ticketHandles.Length);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(GameFarmPackets.FosterCrackerCountOffset, 4),
            crackerHandles.Length);

        var offset = GameFarmPackets.FosterArraysOffset;
        foreach (var handle in ticketHandles)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(offset, 4), handle);
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(offset + 4, 4), 2);
            offset += GameFarmPackets.FosterStackEntrySize;
        }

        foreach (var handle in crackerHandles)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(offset, 4), handle);
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(offset + 4, 4), 1);
            offset += GameFarmPackets.FosterStackEntrySize;
        }

        packet[6] = StorageTestHarness.Checksum(packet);
        return packet;
    }

    private static FarmedSummon Taken(int slot) =>
        new(0, slot, 0, 0, false, false, Now, 0, null, 0, string.Empty, default);

    /// <summary>What a ticket or a cracker is, and what a ticket buys, as the frozen catalogue reads it.</summary>
    private sealed class FarmItems : ICreatureFarmItemCatalog
    {
        private readonly Dictionary<int, (ItemType Type, int Duration, bool IsCash)> _rows = new();

        public void Add(int itemResourceId, ItemType type, int durationSeconds = 0, bool isCash = false) =>
            _rows[itemResourceId] = (type, durationSeconds, isCash);

        public bool TryGetClass(int itemResourceId, out ItemType itemType)
        {
            if (_rows.TryGetValue(itemResourceId, out var row))
            {
                itemType = row.Type;
                return true;
            }

            itemType = default;
            return false;
        }

        public bool TryGetTicket(int itemResourceId, out int durationSeconds, out bool isCash)
        {
            durationSeconds = 0;
            isCash = false;
            if (!_rows.TryGetValue(itemResourceId, out var row))
            {
                return false;
            }

            durationSeconds = row.Duration;
            isCash = row.IsCash;
            return true;
        }
    }

    /// <summary>The ticket cost seam: a fixed answer for every key, and the key it was asked for.</summary>
    private sealed class TicketCost : ICreatureFarmTicketCost
    {
        public int Count { get; set; }
        public int Calls { get; private set; }
        public (int Rate, int Form, int Enhance) LastKey { get; private set; }

        public int GetTicketCount(int rate, int form, int enhanceLevel)
        {
            Calls++;
            LastKey = (rate, form, enhanceLevel);
            return Count;
        }
    }

    /// <summary>The whole wiring of one deposition, with fakes a test can look into.</summary>
    private sealed class Harness
    {
        private readonly long _rowId;

        public Harness(int cost = 2, int[] taken = null, bool withSummon = true, ItemFlag flag = ItemFlag.None,
            int summonResource = SummonResource, int? storageId = null, uint cardHandle = CardHandle,
            int rowId = 77, string characterName = Farmer, int summonLevel = 1)
        {
            _rowId = rowId;
            Cost.Count = cost;
            Items = new FarmItems();

            var summon = withSummon ? new SummonEntity { SummonResourceId = summonResource, Lv = summonLevel } : null;
            var card = new ItemEntity
            {
                Id = cardHandle,
                ItemResourceId = CardResource,
                Amount = 1,
                Flag = flag,
                Enhance = 3,
                WearInfo = ItemWearType.None,
                StorageId = storageId,
            };

            A.CallTo(() => Characters.GetCreatureStateAsync(A<string>._, A<IReadOnlyCollection<int>>._))
                .Returns(Task.FromResult(new CreatureState(new[] { new CreatureCardRecord(card, summon) },
                    new long[6], 0)));
            A.CallTo(() => Store.LoadAsync(A<string>._))
                .Returns(Task.FromResult<IReadOnlyList<FarmedSummon>>((taken ?? Array.Empty<int>())
                    .Select(Taken).ToList()));
            A.CallTo(() => Store.InsertAsync(A<FarmedSummonDeposit>._)).ReturnsLazily(call =>
            {
                Deposits.Add((FarmedSummonDeposit)call.Arguments[0]);
                return Task.FromResult(_rowId);
            });
            A.CallTo(() => Characters.EraseItemsAsync(A<string>._, A<IReadOnlyList<GameActionPackets.EraseItemRequest>>._))
                .ReturnsLazily(call =>
                {
                    var requests = (IReadOnlyList<GameActionPackets.EraseItemRequest>)call.Arguments[1];
                    Erased.AddRange(requests);
                    return Task.FromResult<IReadOnlyList<(uint Handle, long Count)>>(
                        requests.Select(request => (request.ItemHandle, request.Count)).ToList());
                });

            Connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
            Client = StorageTestHarness.NewGameClient(Connection, characterService: Characters);
            StorageTestHarness.Session(Client).CharacterName = characterName;
            StorageTestHarness.Session(Client).CharacterLevel = FarmerLevel;

            Service = new CreatureFarmDepositService(Characters, Store, Creatures, Items, Cost, () => Now,
                CreatureService);
        }

        public ICharacterService Characters { get; } = A.Fake<ICharacterService>();
        public ICreatureFarmStore Store { get; } = A.Fake<ICreatureFarmStore>();
        public ICreatureService CreatureService { get; } = A.Fake<ICreatureService>();
        public ICreatureCatalog Creatures { get; } = new CreatureCatalog(Options.Create(new CreatureCatalogOptions
        {
            Summons = new List<SummonResourceOptions>
            {
                new() { Id = SummonResource, Rate = 4, Form = 2, CardId = CardResource, Name = "Lamia" },
            },
        }));
        public FarmItems Items { get; }
        public TicketCost Cost { get; } = new();
        public StorageTestHarness.FrameConnection Connection { get; }
        public GameClient Client { get; }
        public ICreatureFarmDepositService Service { get; }
        public List<FarmedSummonDeposit> Deposits { get; } = new();
        public List<GameActionPackets.EraseItemRequest> Erased { get; } = new();

        /// <summary>One stack of the bag, as <c>GetItemByHandleAsync</c> resolves it.</summary>
        public void Holds(uint handle, int itemResourceId, long amount) =>
            A.CallTo(() => Characters.GetItemByHandleAsync(Farmer, handle)).Returns(Task.FromResult(new ItemEntity
            {
                Id = handle,
                ItemResourceId = itemResourceId,
                Amount = amount,
                WearInfo = ItemWearType.None,
            }));
    }
}
