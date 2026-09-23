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

        harness.Connection.Sent.Should().ContainSingle();
        var result = ResultOf(harness.Connection.Sent[0]);
        result.RequestMsgID.Should().Be(212);
        result.Result.Should().Be((ushort)ResultCode.NotActable);
        result.Value.Should().Be(unchecked((int)Handle), "NGemity copies item_handle into the result");
        A.CallTo(() => harness.Repository.MoveAsync(A<string>._, A<uint>._, A<bool>._, A<long>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task Handle_RefusesAModeOutsideTheFive()
    {
        var harness = Build();

        await Send(harness, 5, 5);

        ResultOf(harness.Connection.Sent[0]).Result.Should().Be((ushort)ResultCode.NotActable);
        A.CallTo(() => harness.Repository.MoveAsync(A<string>._, A<uint>._, A<bool>._, A<long>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task Handle_ClosesTheWindowWithoutAnAnswer()
    {
        var harness = Build();

        await Send(harness, StorageRules.CloseMode, 0);

        harness.Session.StorageSecurityCheck.Should().BeFalse();
        harness.Connection.Sent.Should().BeEmpty("the close has no answer in the reference either");
    }

    [TestCase(0, 0)]
    [TestCase(0, -5)]
    [TestCase(1, 0)]
    [TestCase(2, -1)]
    public async Task Handle_RefusesANonPositiveCountWithNotEnoughMoney(int mode, long count)
    {
        var harness = Build();

        await Send(harness, (byte)mode, count);

        ResultOf(harness.Connection.Sent[0]).Result.Should().Be((ushort)ResultCode.NotEnoughMoney);
        A.CallTo(() => harness.Repository.MoveAsync(A<string>._, A<uint>._, A<bool>._, A<long>._)).MustNotHaveHappened();
    }

    [TestCase(2)]
    [TestCase(3)]
    public async Task Handle_RefusesTheGoldModesWhileTheStoredGoldHasNoPlace(int mode)
    {
        // The stored gold has no column in this repository and NGemity keeps it in a dummy item row of code
        // 0 (CharacterDatabase.cpp:97): the scope is an open decision (§7.5), so no gold moves in either
        // direction rather than moving into a value the server could not give back.
        var harness = Build();

        await Send(harness, (byte)mode, 100);

        ResultOf(harness.Connection.Sent[0]).Result.Should().Be((ushort)ResultCode.NotActable);
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
