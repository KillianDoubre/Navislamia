using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Party;
using Navislamia.Game.Services.Rates;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// The take window of a ground object end to end, driven by an injected <c>ar_time</c> clock so the 30 s
/// deadline is reached without sleeping for it. A stranger is refused in <c>TS_RESULT_ACCESS_DENIED</c> (6)
/// while the order's first slot does not name them and accepted once its deadline has passed — the loop of
/// the official <c>onTakeItem</c>, and the correction of the 3/4/5 second figure
/// (<c>docs/packet-specs/socle-partage-objets-sol.md</c> §5-§7.1).
/// </summary>
[TestFixture]
public class GroundItemTakeWindowTests
{
    private const uint OwnerHandle = 1000;
    private const uint StrangerHandle = 2000;

    /// <summary>The server clock, in <c>ar_time</c> ticks: read through the service's injected clock seam.</summary>
    private uint _now = 100_000;
    private ICharacterService _characters;
    private IPartyService _parties;
    private GroundItemService _ground;
    private GameClient _owner;
    private GameClient _stranger;
    private uint _handle;

    [SetUp]
    public void SetUp()
    {
        // One fixture instance serves every test (NUnit's default lifecycle), so the clock is reset here:
        // a field initializer alone would let one test's elapsed time leak into the next drop instant.
        _now = 100_000;
        _characters = A.Fake<ICharacterService>();
        _parties = A.Fake<IPartyService>();
        var players = new PlayerVisibilityService(A.Fake<ILogger<PlayerVisibilityService>>());
        var rates = new RateService(new StaticOptionsMonitor<RatesOptions>(new RatesOptions { EventStatePath = "" }));
        _ground = new GroundItemService(A.Fake<IMonsterDropCatalog>(), _characters, A.Fake<IItemGroupCatalog>(),
            rates, players, parties: _parties, clock: () => _now);

        _owner = NewPlayer("owner", OwnerHandle, 1100);
        _stranger = NewPlayer("stranger", StrangerHandle, 1100);
        players.Registry.Register(1, _owner);
        players.Registry.Register(2, _stranger);

        A.CallTo(() => _characters.AddItemAsync(A<string>._, A<int>._, A<long>._))
            .Returns(new ItemEntity { Id = 9, ItemResourceId = 603002, Amount = 1 });

        // A monster's gold pile: the kind of object that carries a pick_up_order (StructMonster::SetPickupOrder).
        _ground.DropGoldForMonster(_owner, 100, 1100, 1000, 0);
        var enter = Enter(_owner);
        _handle = BinaryPrimitives.ReadUInt32LittleEndian(enter.AsSpan(8, 4));
        // onTakeItem's reach is 20 + half the taker's size: both stand on the pile, the window is what is tested.
        StorageTestHarness.StandOn(_owner, enter);
        StorageTestHarness.StandOn(_stranger, enter);
    }

    /// <summary><c>IsTakeableQuestItem</c>: a quest item stays its owner's, whatever the time since the fall.</summary>
    [Test]
    public async Task Take_KeepsAQuestItemToItsOwnerAfterTheWindow()
    {
        _ground.DropQuestItem(_owner, 603002, 1100, 1000, 0);
        var questEnter = Enter(_owner);
        var quest = BinaryPrimitives.ReadUInt32LittleEndian(questEnter.AsSpan(8, 4));
        StorageTestHarness.StandOn(_owner, questEnter);
        StorageTestHarness.StandOn(_stranger, questEnter);
        _now += 10 * GroundItemPickupRules.FirstDeadlineTicks;

        await _ground.TakeAsync(_stranger, quest);
        Result(_stranger).Should().Be(((ushort)GamePackets.TM_CS_TAKE_ITEM, (ushort)ResultCode.AccessDenied));
        await _ground.TakeAsync(_owner, quest);
        Result(_owner).Should().Be(((ushort)GamePackets.TM_CS_TAKE_ITEM, (ushort)ResultCode.Success));
    }

    /// <summary>The range is judged before the order, like <c>onTakeItem</c>: far and early is <c>TOO_FAR</c>.</summary>
    [Test]
    public async Task Take_JudgesTheRangeBeforeTheOrder()
    {
        var far = NewPlayer("far", 3000, 5000);
        await _ground.TakeAsync(far, _handle);
        Result(far).Should().Be(((ushort)GamePackets.TM_CS_TAKE_ITEM, (ushort)ResultCode.TooFar));
    }

    /// <summary>The order's first slot names the character who dropped it, and it takes at once.</summary>
    [Test]
    public async Task Take_AcceptsTheCharacterTheFirstSlotNames()
    {
        await _ground.TakeAsync(_owner, _handle);

        Result(_owner).Should().Be(((ushort)GamePackets.TM_CS_TAKE_ITEM, (ushort)ResultCode.Success));
        Sent(_owner).Should().Contain(frame => Id(frame) == (ushort)GamePackets.TM_SC_TAKE_ITEM_RESULT,
            "the take is acknowledged by the animated result");
    }

    /// <summary>
    /// A stranger inside the window is refused in <c>ACCESS_DENIED</c> (6) — the object exists, its order
    /// just does not name them yet — and nothing is taken: no result frame, no <c>TS_SC_LEAVE</c>.
    /// </summary>
    [Test]
    public async Task Take_RefusesAStrangerWithAccessDeniedInsideTheWindow()
    {
        await _ground.TakeAsync(_stranger, _handle);

        Result(_stranger).Should().Be(((ushort)GamePackets.TM_CS_TAKE_ITEM, (ushort)ResultCode.AccessDenied));
        Sent(_stranger).Should().NotContain(frame => Id(frame) == (ushort)GamePackets.TM_SC_TAKE_ITEM_RESULT);
        Sent(_stranger).Should().NotContain(frame => Id(frame) == (ushort)GamePackets.TM_SC_LEAVE);
    }

    /// <summary>An object that is not there keeps its own answer: <c>NOT_EXIST</c> (1), not the window refusal.</summary>
    [Test]
    public async Task Take_AnswersNotExistForAnObjectThatIsNotThere()
    {
        await _ground.TakeAsync(_stranger, 0xDEADBEEF);

        Result(_stranger).Should().Be(((ushort)GamePackets.TM_CS_TAKE_ITEM, (ushort)ResultCode.NotExist));
    }

    /// <summary>
    /// The deadline is 3000 ticks — 30 s, not 3: refused one tick before it, accepted on it. Beyond it an
    /// unentitled player works, with no group condition left.
    /// </summary>
    [Test]
    public async Task Take_OpensTheObjectToEverybodyAtTheThirtiethSecond()
    {
        _now += GroundItemPickupRules.FirstDeadlineTicks - 1;
        await _ground.TakeAsync(_stranger, _handle);
        Result(_stranger).Should().Be(((ushort)GamePackets.TM_CS_TAKE_ITEM, (ushort)ResultCode.AccessDenied),
            "29.99 s after the fall");

        _now += 1;
        await _ground.TakeAsync(_stranger, _handle);
        Result(_stranger).Should().Be(((ushort)GamePackets.TM_CS_TAKE_ITEM, (ushort)ResultCode.Success),
            "30.00 s after the fall");
        Sent(_stranger).Should().Contain(frame => Id(frame) == (ushort)GamePackets.TM_SC_TAKE_ITEM_RESULT);
    }

    /// <summary>The party <c>nPartyID[0]</c> names is entitled like the killer: no waiting for its members.</summary>
    [Test]
    public async Task Take_AcceptsAPartyMemberTheOrderNamesAtOnce()
    {
        StorageTestHarness.Session(_owner).PartyId = 7;
        _ground.DropGoldForMonster(_owner, 100, 1100, 1000, 0);
        var handle = BinaryPrimitives.ReadUInt32LittleEndian(Enter(_owner).AsSpan(8, 4));
        BinaryPrimitives.ReadInt32LittleEndian(Enter(_owner).AsSpan(58, 4)).Should().Be(7, "nPartyID[0]");
        A.CallTo(() => _parties.CanTakeDrop(_owner, _stranger, 7L)).Returns(true);
        StorageTestHarness.StandOn(_stranger, Enter(_owner));

        await _ground.TakeAsync(_stranger, handle);

        Result(_stranger).Should().Be(((ushort)GamePackets.TM_CS_TAKE_ITEM, (ushort)ResultCode.Success));
    }

    /// <summary>
    /// Slot 1 of the order (StructMonster::onDead, the second contributing group) is written on the wire and takes once
    /// slot 0's deadline has passed, ten seconds before anybody else (3000 then 4000 ticks).
    /// </summary>
    [Test]
    public async Task Take_TheSecondContributingGroupTakesBetweenThirtyAndFortySeconds()
    {
        var third = NewPlayer("third", 3000, 1100);
        _ground.DropGoldForMonster(_owner, 100, 1100, 1000, 0, 0, new[] { _stranger }, false);
        var enter = Enter(_owner);
        var handle = BinaryPrimitives.ReadUInt32LittleEndian(enter.AsSpan(8, 4));
        BinaryPrimitives.ReadUInt32LittleEndian(enter.AsSpan(46, 4)).Should().Be(OwnerHandle, "hPlayer[0]");
        BinaryPrimitives.ReadUInt32LittleEndian(enter.AsSpan(50, 4)).Should().Be(StrangerHandle, "hPlayer[1]");
        StorageTestHarness.StandOn(_stranger, enter);
        StorageTestHarness.StandOn(third, enter);

        _now += GroundItemPickupRules.FirstDeadlineTicks - 1;
        await _ground.TakeAsync(_stranger, handle);
        Result(_stranger).Should().Be(((ushort)GamePackets.TM_CS_TAKE_ITEM, (ushort)ResultCode.AccessDenied));

        _now += 1;
        await _ground.TakeAsync(third, handle);
        Result(third).Should().Be(((ushort)GamePackets.TM_CS_TAKE_ITEM, (ushort)ResultCode.AccessDenied),
            "a group the order does not name waits for slot 1's deadline too");
        await _ground.TakeAsync(_stranger, handle);
        Result(_stranger).Should().Be(((ushort)GamePackets.TM_CS_TAKE_ITEM, (ushort)ResultCode.Success));
    }

    /// <summary>A raid boss's loot carries no order (StructMonster.cpp:2064-2068): anybody takes it at once.</summary>
    [Test]
    public async Task Take_ARaidBossLootIsOpenToEverybodyAtOnce()
    {
        _ground.DropGoldForMonster(_owner, 100, 1100, 1000, 0, 0, new[] { _stranger }, true);
        var enter = Enter(_owner);
        BinaryPrimitives.ReadUInt32LittleEndian(enter.AsSpan(46, 4)).Should().Be(0u);
        BinaryPrimitives.ReadUInt32LittleEndian(enter.AsSpan(50, 4)).Should().Be(0u);
        var third = NewPlayer("third", 3000, 1100);
        StorageTestHarness.StandOn(third, enter);

        await _ground.TakeAsync(third, BinaryPrimitives.ReadUInt32LittleEndian(enter.AsSpan(8, 4)));

        Result(third).Should().Be(((ushort)GamePackets.TM_CS_TAKE_ITEM, (ushort)ResultCode.Success));
    }

    /// <summary>The reach is the official 20 + half the taker's size: 26 units for a player.</summary>
    [Test]
    public async Task Take_RefusesTooFarBeyondTwentySixUnits()
    {
        var enter = Enter(_owner);
        var x = BinaryPrimitives.ReadSingleLittleEndian(enter.AsSpan(12, 4));
        var y = BinaryPrimitives.ReadSingleLittleEndian(enter.AsSpan(16, 4));
        StorageTestHarness.StandAt(_owner, x + 27, y);
        await _ground.TakeAsync(_owner, _handle);
        Result(_owner).Should().Be(((ushort)GamePackets.TM_CS_TAKE_ITEM, (ushort)ResultCode.TooFar));

        StorageTestHarness.StandAt(_owner, x + 25, y);
        await _ground.TakeAsync(_owner, _handle);
        Result(_owner).Should().Be(((ushort)GamePackets.TM_CS_TAKE_ITEM, (ushort)ResultCode.Success));
    }

    /// <summary>The pet path shares the window: nothing to go for before the deadline, the object after it.</summary>
    [Test]
    public void TryFindNearest_HoldsTheObjectBackUntilTheDeadline()
    {
        // The pet follows the client's SGameItem::IsPickable: its master's loot past 30 s, anybody's past 50 s.
        _ground.TryFindNearest(_owner, 1100, 1000, 0, 300, out _).Should().BeFalse("the pet waits even for its master");
        _now += GroundItemPickupRules.FirstDeadlineTicks + 1;
        _ground.TryFindNearest(_owner, 1100, 1000, 0, 300, out _).Should().BeTrue("state 1: slot 0");
        _ground.TryFindNearest(_stranger, 1100, 1000, 0, 300, out _).Should().BeFalse("a stranger's pet waits for state 3");

        _now += 2 * GroundItemPickupRules.SlotStepTicks;

        _ground.TryFindNearest(_stranger, 1100, 1000, 0, 300, out var spot).Should().BeTrue();
        spot.Handle.Should().Be(_handle);
    }

    /// <summary>
    /// <c>drop_time</c> carries the instant the object fell, never the instant of the send: a re-send to a
    /// player who re-acquires the view must not restart the client's window, or a late arrival would see the
    /// object locked 30 s longer than it is (§6.1 and §7.1 of the fiche).
    /// </summary>
    [Test]
    public void Enter_CarriesTheInstantOfTheFallAndKeepsItOnEveryReSend()
    {
        BinaryPrimitives.ReadUInt32LittleEndian(Enter(_owner).AsSpan(42, 4)).Should().Be(100_000,
            "drop_time is the instant the object fell");

        _now += 250;
        _ground.LeaveWorld(_owner);
        _ground.Sync(_owner);

        BinaryPrimitives.ReadUInt32LittleEndian(Enter(_owner).AsSpan(42, 4)).Should().Be(100_000,
            "2.5 s later the same instant is re-sent, not the moment of the re-send");
    }

    /// <summary>The instant is expressed in each recipient's own clock base, as the offset recalibration asks.</summary>
    [Test]
    public void Enter_MovesTheDropInstantIntoTheRecipientsClockBase()
    {
        StorageTestHarness.Session(_stranger).ClientClockOffset = 500;

        _now += 40;
        _ground.LeaveWorld(_stranger);
        _ground.Sync(_stranger);

        BinaryPrimitives.ReadUInt32LittleEndian(Enter(_stranger).AsSpan(42, 4)).Should().Be(100_500,
            "the same fall, moved into the recipient's clock");
    }

    private static GameClient NewPlayer(string name, uint handle, float x)
    {
        var client = StorageTestHarness.NewGameClient(
            new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = handle;
        info.CharacterName = name;
        info.X = x;
        info.Y = 1000;
        return client;
    }

    private static List<byte[]> Sent(GameClient client) =>
        ((StorageTestHarness.FrameConnection)client.Connection).Sent;

    private static ushort Id(byte[] frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2));

    private static byte[] Enter(GameClient client) =>
        Sent(client).Last(frame => Id(frame) == (ushort)GamePackets.TM_SC_ENTER);

    /// <summary>The <c>TM_SC_RESULT</c> (0) of a <c>TM_CS_TAKE_ITEM</c>: its request id at 7 and code at 9.</summary>
    private static (ushort Request, ushort Code) Result(GameClient client)
    {
        var frame = Sent(client).Last(one => Id(one) == (ushort)GamePackets.TM_SC_RESULT);
        return (BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(7, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(9, 2)));
    }
}
