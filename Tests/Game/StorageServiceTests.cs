using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// The six modes of <c>WorldSession::onStorage</c> on this repository's container: the window opens from
/// the NPC trigger (211 + the contents in 207 + the storage_gold property), the item modes move a stack
/// between the character rows and the account rows of the item table and answer through the item frames,
/// and the refusals are the ones the fiche assumes. See docs/packet-specs/211-212-storage.md §5.3.
/// </summary>
[TestFixture]
public class StorageServiceTests
{
    private const uint Handle = 0x80000123u;
    private const string Character = "Killian";

    private sealed record Harness(StorageService Service, IStorageRepository Repository, GameClient Client,
        StorageTestHarness.FrameConnection Connection, ConnectionInfo Session);

    private static Harness Build(bool open = true, string characterName = Character)
    {
        var repository = A.Fake<IStorageRepository>();
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var session = StorageTestHarness.Session(client);
        session.CharacterName = characterName;
        session.CharacterHandle = 42;
        session.StorageSecurityCheck = open;

        return new Harness(new StorageService(repository, new CharacterGate()), repository, client, connection, session);
    }

    private static Task Send(Harness harness, byte mode, long count, uint handle = Handle)
        => harness.Service.HandleAsync(harness.Client, new GameActionPackets.StorageRequest(handle, mode, count));

    private static ItemEntity Row(uint id = 12, long amount = 100, int idx = 3, long? characterId = null,
        int? accountId = null)
        => new() { Id = id, ItemResourceId = 240100, Amount = amount, Idx = idx, CharacterId = characterId, AccountId = accountId };

    private static ushort IdOf(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));

    private static TS_SC_RESULT ResultOf(byte[] packet)
        => new Packet<TS_SC_RESULT>(packet).GetDataStruct<TS_SC_RESULT>();

    /// <summary>The inventory chunk (207): a uint16 count then the 85-byte item records.</summary>
    private static IReadOnlyList<(uint Handle, long Amount, int Idx)> InventoryOf(byte[] packet)
    {
        var count = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(7, 2));
        var items = new List<(uint, long, int)>(count);

        for (var index = 0; index < count; index++)
        {
            var offset = 9 + index * 85;
            items.Add((BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(offset, 4)),
                BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(offset + 16, 8)),
                BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(offset + 81, 4))));
        }

        return items;
    }

    private static string PropertyNameOf(byte[] packet)
        => Encoding.ASCII.GetString(packet, 12, 16).TrimEnd('\0');

    private static long PropertyValueOf(byte[] packet) => BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(28, 8));

    /// <summary>
    /// The frame every refusal of this lot rides on. <c>TM_SC_RESULT</c> keeps the Epic 7.3 id <b>0</b> —
    /// rzu only moves it to 1000 from <c>EPIC_9_6_3</c> on (<c>TS_SC_RESULT.h:12-14</c>) — and its payload
    /// is eight bytes: the id of the frame that caused it at 7, the code at 9 and the four-byte value at 11
    /// (<c>TS_SC_RESULT.h:7-10</c>). The value is read as an <c>int32</c> because the repository fills it
    /// with the item handle, unsigned 32 bits on the wire.
    /// </summary>
    private static void AssertRefusal(byte[] packet, ushort result, int value)
    {
        packet.Should().HaveCount(15, "eight payload bytes after the seven-byte header");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(15, "Length");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(0, "TM_SC_RESULT is id 0 in Epic 7.3");
        packet[6].Should().Be(StorageTestHarness.Checksum(packet), "the checksum sums the first six header bytes");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(7, 2)).Should().Be(212, "request_msg_id");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(9, 2)).Should().Be(result, "result");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(value, "value");
    }

    /// <summary>
    /// The refusal frame of this lot on its own: the frame the four refusal paths share, with the gating of
    /// the id read in rzu. 1000 is the 9.6.3 id of <c>TS_SC_RESULT</c> and is already taken by
    /// <c>TM_SC_STAT_INFO</c> here, which is exactly why the query result frame stays on 0.
    /// </summary>
    [Test]
    public void SendResult_LaysOutTheFifteenByteEpic73ResultFrame()
    {
        var harness = Build();

        harness.Client.SendResult(212, (ushort)ResultCode.TooMuchMoney, -1);

        var packet = harness.Connection.Sent.Should().ContainSingle().Subject;
        ((ushort)GamePackets.TM_SC_RESULT).Should().Be(0, "1000 only exists from EPIC_9_6_3 on");
        ((ushort)GamePackets.TM_SC_STAT_INFO).Should().Be(1000, "the 9.6.3 id of TS_SC_RESULT is taken here");
        AssertRefusal(packet, (ushort)ResultCode.TooMuchMoney, -1);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(uint.MaxValue,
            "a value of -1 is all four bytes set, so a 64-bit value would have shifted the total length");
    }

    [Test]
    public async Task Open_SendsTheWindowTheContentsAndTheStoredGold()
    {
        var harness = Build();
        var item = Row(characterId: null, accountId: 3);
        A.CallTo(() => harness.Repository.GetStorageItemsAsync(Character)).Returns(Task.FromResult(new[] { item }));

        await harness.Service.OpenAsync(harness.Client);

        harness.Connection.Sent.Should().HaveCount(3);
        IdOf(harness.Connection.Sent[0]).Should().Be(211, "the window is opened first");
        harness.Connection.Sent[0].Length.Should().Be(7, "Epic 7.3 sends no payload with the opening");

        IdOf(harness.Connection.Sent[1]).Should().Be(207, "the contents follow in the inventory frame");
        InventoryOf(harness.Connection.Sent[1]).Should().Equal(((uint)item.Id, item.Amount, item.Idx));

        IdOf(harness.Connection.Sent[2]).Should().Be(507);
        PropertyNameOf(harness.Connection.Sent[2]).Should().Be("storage_gold");
        PropertyValueOf(harness.Connection.Sent[2]).Should().Be(0, "nothing is stored yet in this repository");
    }

    [Test]
    public async Task Open_RecordsTheWindowOnTheSession()
    {
        var harness = Build(open: false);
        A.CallTo(() => harness.Repository.GetStorageItemsAsync(Character))
            .Returns(Task.FromResult(Array.Empty<ItemEntity>()));

        await harness.Service.OpenAsync(harness.Client);

        harness.Session.StorageSecurityCheck.Should().BeTrue("WorldSession::onStorage answers on this state");
    }

    [Test]
    public async Task Open_SendsAnEmptyInventoryFrameWhenTheCounterIsEmpty()
    {
        var harness = Build();
        A.CallTo(() => harness.Repository.GetStorageItemsAsync(Character))
            .Returns(Task.FromResult(Array.Empty<ItemEntity>()));

        await harness.Service.OpenAsync(harness.Client);

        harness.Connection.Sent.Should().HaveCount(3);
        IdOf(harness.Connection.Sent[1]).Should().Be(207);
        harness.Connection.Sent[1].Length.Should().Be(9, "an empty 207 still carries its two-byte count");
        InventoryOf(harness.Connection.Sent[1]).Should().BeEmpty();
    }

    [Test]
    public async Task Open_SendsNothingWhenNoCharacterIsInSession()
    {
        var harness = Build(open: false, characterName: string.Empty);

        await harness.Service.OpenAsync(harness.Client);

        harness.Connection.Sent.Should().BeEmpty();
        harness.Session.StorageSecurityCheck.Should().BeFalse();
        A.CallTo(() => harness.Repository.GetStorageItemsAsync(A<string>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task Handle_RefusesWhenNoCounterIsOpen()
    {
        var harness = Build(open: false);

        await Send(harness, StorageRules.ItemToStorage, 5);

        harness.Connection.Sent.Should().ContainSingle("a refused request gets one result frame and nothing else");
        AssertRefusal(harness.Connection.Sent[0], (ushort)ResultCode.NotActable, unchecked((int)Handle));
        harness.Session.StorageSecurityCheck.Should().BeFalse("a refusal does not open the counter");
        var result = ResultOf(harness.Connection.Sent[0]);
        result.RequestMsgID.Should().Be(212);
        result.Result.Should().Be((ushort)ResultCode.NotActable);
        result.Value.Should().Be(unchecked((int)Handle), "NGemity copies item_handle into the result");
        A.CallTo(() => harness.Repository.MoveAsync(A<string>._, A<uint>._, A<bool>._, A<long>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task Handle_AnswersNotActableForACloseWhenNoCounterIsOpen()
    {
        // The session state is asked before the mode is looked at (WorldSession.cpp:1595-1598): a close on a
        // counter that is not open answers like any other mode instead of being taken as a close.
        var harness = Build(open: false);

        await Send(harness, StorageRules.CloseMode, 0);

        harness.Connection.Sent.Should().ContainSingle();
        AssertRefusal(harness.Connection.Sent[0], (ushort)ResultCode.NotActable, unchecked((int)Handle));
    }

    [TestCase(5, TestName = "Handle_RefusesAModeOutsideTheFive_5")]
    [TestCase(6, TestName = "Handle_RefusesAModeOutsideTheFive_6")]
    [TestCase(127, TestName = "Handle_RefusesAModeOutsideTheFive_127")]
    // rzu types the field int8_t, so 128..255 are negative for the reference client and reach its silent
    // default: break (WorldSession.cpp:1673-1675). The field is a byte here and refusals are the assumed
    // divergence (§6): both ends of that range answer NotActable rather than nothing.
    [TestCase(128, TestName = "Handle_RefusesAModeOutsideTheFive_128IsMinus128Signed")]
    [TestCase(255, TestName = "Handle_RefusesAModeOutsideTheFive_255IsMinusOneSigned")]
    public async Task Handle_RefusesAModeOutsideTheFive(byte mode)
    {
        var harness = Build();

        await Send(harness, mode, 5);

        harness.Connection.Sent.Should().ContainSingle();
        AssertRefusal(harness.Connection.Sent[0], (ushort)ResultCode.NotActable, unchecked((int)Handle));
        harness.Session.StorageSecurityCheck.Should().BeTrue("an unknown mode does not close the counter");
        A.CallTo(() => harness.Repository.MoveAsync(A<string>._, A<uint>._, A<bool>._, A<long>._)).MustNotHaveHappened();
    }

    [TestCase(0, TestName = "Handle_ClosesTheWindowWithoutAnAnswer_0")]
    [TestCase(100, TestName = "Handle_ClosesTheWindowWithoutAnAnswer_100")]
    [TestCase(100000000000, TestName = "Handle_ClosesTheWindowWithoutAnAnswer_TheReferenceGoldBound")]
    [TestCase(-1, TestName = "Handle_ClosesTheWindowWithoutAnAnswer_MinusOne")]
    public async Task Handle_ClosesTheWindowWithoutAnAnswer(long count)
    {
        // The close is looked at before the count (WorldSession.cpp:1670-1672), so neither a zero nor a
        // negative count turns it into a refusal: the only effect is the session state.
        var harness = Build();

        await Send(harness, StorageRules.CloseMode, count);

        harness.Session.StorageSecurityCheck.Should().BeFalse();
        harness.Connection.Sent.Should().BeEmpty("the close has no answer in the reference either");
        A.CallTo(() => harness.Repository.MoveAsync(A<string>._, A<uint>._, A<bool>._, A<long>._)).MustNotHaveHappened();
    }

    [TestCase(0, 0)]
    [TestCase(0, -5)]
    [TestCase(1, 0)]
    [TestCase(2, -1)]
    [TestCase(2, 0)]
    [TestCase(3, -1)]
    public async Task Handle_RefusesANonPositiveCountWithNotEnoughMoney(int mode, long count)
    {
        // WorldSession.cpp:1603-1606 answers NOT_ENOUGH_MONEY for a unit count of zero or less. The counter
        // of the gold modes is read the same way here, before the mode family is looked at (§5.2, §6.2):
        // both gold modes answer NotEnoughMoney rather than NotActable for a count of zero or less.
        var harness = Build();

        await Send(harness, (byte)mode, count);

        harness.Connection.Sent.Should().ContainSingle();
        AssertRefusal(harness.Connection.Sent[0], (ushort)ResultCode.NotEnoughMoney, unchecked((int)Handle));
        A.CallTo(() => harness.Repository.MoveAsync(A<string>._, A<uint>._, A<bool>._, A<long>._)).MustNotHaveHappened();
    }

    [TestCase(2, 1, TestName = "Handle_RefusesTheGoldModesWhileTheStoredGoldHasNoPlace_OneUnit")]
    [TestCase(3, 1, TestName = "Handle_RefusesTheGoldModesWhileTheStoredGoldHasNoPlace_OneUnitBack")]
    [TestCase(2, 100000000000, TestName = "Handle_RefusesTheGoldModesWhileTheStoredGoldHasNoPlace_TheReferenceBound")]
    [TestCase(3, 9223372036854775807, TestName = "Handle_RefusesTheGoldModesWhileTheStoredGoldHasNoPlace_MaxInt64")]
    public async Task Handle_RefusesTheGoldModesWhileTheStoredGoldHasNoPlace(int mode, long count)
    {
        // The stored gold has no column in this repository and NGemity keeps it in a dummy item row of code
        // 0 (CharacterDatabase.cpp:97): the scope is an open decision (§7.5), so no gold moves in either
        // direction rather than moving into a value the server could not give back. Every amount is refused
        // the same way — no bound of the gold is decided here, NGemity's own 1e11 included (§7.5, A
        // VERIFIER 3), and the item path is never reached.
        var harness = Build();

        await Send(harness, (byte)mode, count);

        harness.Connection.Sent.Should().ContainSingle();
        AssertRefusal(harness.Connection.Sent[0], (ushort)ResultCode.NotActable, unchecked((int)Handle));
        A.CallTo(() => harness.Repository.MoveAsync(A<string>._, A<uint>._, A<bool>._, A<long>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task Handle_HandsTheModeAndTheCountToTheRepository()
    {
        var harness = Build();
        A.CallTo(() => harness.Repository.MoveAsync(Character, Handle, false, 5))
            .Returns(Task.FromResult(StorageMoveResult.Refused(StorageMoveOutcome.Ignored)));

        await Send(harness, StorageRules.ItemToInventory, 5);

        A.CallTo(() => harness.Repository.MoveAsync(Character, Handle, false, 5)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task Handle_ReportsAWholeStackMoveAsADestroyThenAnAdd()
    {
        var harness = Build();
        // A whole move re-owns the very row that left the inventory, so the source and the destination
        // handles are the same one on the wire.
        var moved = Row(id: Handle, amount: 100, idx: 4, characterId: null, accountId: 3);
        A.CallTo(() => harness.Repository.MoveAsync(Character, Handle, true, 100))
            .Returns(Task.FromResult(new StorageMoveResult(StorageMoveOutcome.Moved, moved, null, 0)));

        await Send(harness, StorageRules.ItemToStorage, 100);

        harness.Connection.Sent.Should().HaveCount(2);
        IdOf(harness.Connection.Sent[0]).Should().Be(254, "the stack leaves the inventory list");
        BinaryPrimitives.ReadUInt32LittleEndian(harness.Connection.Sent[0].AsSpan(7, 4)).Should().Be(Handle);
        IdOf(harness.Connection.Sent[1]).Should().Be(207, "the counter list draws the same handle");
        InventoryOf(harness.Connection.Sent[1]).Should().ContainSingle()
            .Which.Should().Be((Handle, 100L, 4));
    }

    [Test]
    public async Task Handle_ReportsAPartialMoveAsACountThenAnAdd()
    {
        var harness = Build();
        var destination = Row(id: 21, amount: 40, idx: 0, characterId: null, accountId: 3);
        var source = Row(id: Handle, amount: 60, idx: 2, characterId: 7);
        A.CallTo(() => harness.Repository.MoveAsync(Character, Handle, true, 40))
            .Returns(Task.FromResult(new StorageMoveResult(StorageMoveOutcome.Split, destination, source, 60)));

        await Send(harness, StorageRules.ItemToStorage, 40);

        harness.Connection.Sent.Should().HaveCount(2);
        IdOf(harness.Connection.Sent[0]).Should().Be(255, "the source keeps its handle and loses units");
        BinaryPrimitives.ReadUInt32LittleEndian(harness.Connection.Sent[0].AsSpan(7, 4)).Should().Be(Handle);
        BinaryPrimitives.ReadInt64LittleEndian(harness.Connection.Sent[0].AsSpan(11, 8)).Should().Be(60);

        IdOf(harness.Connection.Sent[1]).Should().Be(207);
        InventoryOf(harness.Connection.Sent[1]).Should().ContainSingle().Which.Should().Be((21u, 40L, 0));
    }

    [Test]
    public async Task Handle_AnswersNotExistForAnUnknownHandle()
    {
        var harness = Build();
        A.CallTo(() => harness.Repository.MoveAsync(Character, Handle, true, 5))
            .Returns(Task.FromResult(StorageMoveResult.Refused(StorageMoveOutcome.UnknownHandle)));

        await Send(harness, StorageRules.ItemToStorage, 5);

        ResultOf(harness.Connection.Sent[0]).Result.Should().Be((ushort)ResultCode.NotExist);
    }

    [Test]
    public async Task Handle_AnswersAccessDeniedForAnItemOfAnotherPlayer()
    {
        var harness = Build();
        A.CallTo(() => harness.Repository.MoveAsync(Character, Handle, true, 5))
            .Returns(Task.FromResult(StorageMoveResult.Refused(StorageMoveOutcome.AccessDenied)));

        await Send(harness, StorageRules.ItemToStorage, 5);

        ResultOf(harness.Connection.Sent[0]).Result.Should().Be((ushort)ResultCode.AccessDenied);
    }

    [Test]
    public async Task Handle_AnswersNothingWhenTheItemAlreadySitsOnTheAskedSide()
    {
        // NGemity acts only on an item of the side the mode names (WorldSession.cpp:1628-1637): an item
        // already in its destination list is left alone without a word.
        var harness = Build();
        A.CallTo(() => harness.Repository.MoveAsync(Character, Handle, true, 5))
            .Returns(Task.FromResult(StorageMoveResult.Refused(StorageMoveOutcome.Ignored)));

        await Send(harness, StorageRules.ItemToStorage, 5);

        harness.Connection.Sent.Should().BeEmpty();
    }

    [Test]
    public async Task Handle_AnswersNotActableWhenTheCharacterRowIsGone()
    {
        var harness = Build();
        A.CallTo(() => harness.Repository.MoveAsync(Character, Handle, true, 5))
            .Returns(Task.FromResult(StorageMoveResult.Refused(StorageMoveOutcome.UnknownCharacter)));

        await Send(harness, StorageRules.ItemToStorage, 5);

        ResultOf(harness.Connection.Sent[0]).Result.Should().Be((ushort)ResultCode.NotActable);
    }

    [Test]
    public async Task Handle_AnswersDBErrorWhenTheMoveFails()
    {
        var harness = Build();
        A.CallTo(() => harness.Repository.MoveAsync(Character, Handle, true, 5))
            .Throws(new InvalidOperationException("the item table is unreachable"));

        await Send(harness, StorageRules.ItemToStorage, 5);

        var result = ResultOf(harness.Connection.Sent[0]);
        result.RequestMsgID.Should().Be(212);
        result.Result.Should().Be((ushort)ResultCode.DBError);
    }
}
