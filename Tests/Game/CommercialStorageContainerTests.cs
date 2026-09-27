using System;
using System.Buffers.Binary;
using System.Linq;
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
/// The commercial storage (item shop) container on this repository's table: the pair pushed at world entry
/// reads the rows of the character, the takeout hands the goods through the ordinary inventory frames and
/// consumes the line, and every refusal writes nothing and answers nothing — the family has no result code
/// and a 10005 is never emitted. See docs/packet-specs/socle-stockage-commercial-conteneur.md §5.5-§5.7.
/// <para>
/// The frames are measured on real values here, not on the builders alone: 10003 is 11 bytes with the two
/// counters at 7 and 9, 10004 is 9 + 10 x n bytes with the lines from offset 9 (uid u32 @0, code i32 @4,
/// count u16 @8), and the uid a line announces is the identity key of the row (§3.2, §5.9).
/// </para>
/// </summary>
[TestFixture]
public class CommercialStorageContainerTests
{
    private const string Character = "Killian";
    private const int ItemCode = 240100;
    private const uint Uid = 0x48CE60u;
    private const int TakeoutLength = 13;

    private sealed record Harness(CommercialStorageService Service, IPaidItemRepository Repository,
        ICharacterService Characters, GameClient Client, StorageTestHarness.FrameConnection Connection,
        ConnectionInfo Session);

    private static Harness Build(string characterName = Character, bool knownCode = true)
    {
        var repository = A.Fake<IPaidItemRepository>();
        var characters = A.Fake<ICharacterService>();
        var catalog = A.Fake<IItemSortCatalog>();
        A.CallTo(() => catalog.Contains(A<long>._)).Returns(knownCode);

        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var session = StorageTestHarness.Session(client);
        session.CharacterName = characterName;
        session.CharacterHandle = 42;

        var service = new CommercialStorageService(repository, new CharacterGate(), characters, catalog);

        return new Harness(service, repository, characters, client, connection, session);
    }

    private static PaidItemEntity Row(long id = Uid, int code = ItemCode, int rest = 5, int confirmed = 0)
        => new()
        {
            Id = id,
            AccountId = 7,
            CharacterId = 41,
            ItemCode = code,
            ItemCount = rest,
            RestItemCount = rest,
            Confirmed = confirmed
        };

    private static ushort IdOf(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));

    private static ushort CountOf(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(7, 2));

    private static uint UidOfLine(byte[] packet, int index) =>
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(9 + (index * 10), 4));

    private static int CodeOfLine(byte[] packet, int index) =>
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(13 + (index * 10), 4));

    private static ushort CountOfLine(byte[] packet, int index) =>
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(17 + (index * 10), 2));

    private static Task Takeout(Harness harness, uint uid = Uid, ushort count = 5)
        => harness.Service.HandleTakeoutAsync(harness.Client,
            new GameActionPackets.TakeoutCommercialItemRequest(uid, count));

    private static ItemEntity Added(uint id = 77, int amount = 5)
        => new() { Id = id, ItemResourceId = ItemCode, Amount = amount, Idx = 3 };

    /// <summary>The 13-byte client frame: Length, ID, checksum, uid at 7, count at 11.</summary>
    private static byte[] TakeoutFrame(uint uid, ushort count)
    {
        var packet = new byte[TakeoutLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), TakeoutLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2),
            (ushort)GamePackets.TM_CS_TAKEOUT_COMMERCIAL_ITEM);
        packet[6] = StorageTestHarness.Checksum(packet);

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), uid);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(11, 2), count);
        return packet;
    }

    [Test]
    public async Task Send_PushesTheTwoCountersThenTheLinesOfTheContainer()
    {
        var harness = Build();
        A.CallTo(() => harness.Repository.GetVisibleAsync(Character)).Returns(Task.FromResult(new[]
        {
            Row(id: 12, rest: 5, confirmed: 0),
            Row(id: 30, rest: 70000, confirmed: 1)
        }));

        await harness.Service.SendContainerAsync(harness.Client);

        harness.Connection.Sent.Should().HaveCount(2);
        A.CallTo(() => harness.Repository.GetVisibleAsync(Character)).MustHaveHappenedOnceExactly();

        var info = harness.Connection.Sent[0];
        IdOf(info).Should().Be(10003);
        info.Length.Should().Be(11);
        CountOf(info).Should().Be(2, "total_item_count is the number of lines emitted");
        BinaryPrimitives.ReadUInt16LittleEndian(info.AsSpan(9, 2)).Should().Be(1,
            "new_item_count counts the lines whose confirmed is zero");
    }

    [Test]
    public async Task Send_LaysOutTheLinesFromOffsetNine()
    {
        var harness = Build();
        A.CallTo(() => harness.Repository.GetVisibleAsync(Character)).Returns(Task.FromResult(new[]
        {
            Row(id: 0x11223344u, code: 4001, rest: 3),
            Row(id: 0xAABBCCDDu, code: 4002, rest: 65535)
        }));

        await harness.Service.SendContainerAsync(harness.Client);

        var list = harness.Connection.Sent[1];
        IdOf(list).Should().Be(10004);
        list.Length.Should().Be(9 + (10 * 2));
        BinaryPrimitives.ReadUInt32LittleEndian(list.AsSpan(0, 4)).Should().Be(29);
        CountOf(list).Should().Be(2);
        list[6].Should().Be(StorageTestHarness.Checksum(list));

        UidOfLine(list, 0).Should().Be(0x11223344u);
        CodeOfLine(list, 0).Should().Be(4001);
        CountOfLine(list, 0).Should().Be(3);

        UidOfLine(list, 1).Should().Be(0xAABBCCDDu);
        CodeOfLine(list, 1).Should().Be(4002);
        CountOfLine(list, 1).Should().Be(65535);
    }

    [Test]
    public async Task Send_AnnouncesTheLineIdAsTheUidAndTheRestAsTheCount()
    {
        var harness = Build();
        A.CallTo(() => harness.Repository.GetVisibleAsync(Character)).Returns(Task.FromResult(new[]
        {
            Row(id: 9, rest: 12)
        }));

        await harness.Service.SendContainerAsync(harness.Client);

        var list = harness.Connection.Sent[1];
        CountOf(list).Should().Be(1);
        UidOfLine(list, 0).Should().Be(9u, "commercial_item_uid is the identity key of the row");
        CountOfLine(list, 0).Should().Be(12, "count is what is left to take");
        CountOf(harness.Connection.Sent[0]).Should().Be(1);
    }

    [Test]
    public async Task Send_OrdersTheLinesOnTheLineId()
    {
        var harness = Build();
        A.CallTo(() => harness.Repository.GetVisibleAsync(Character)).Returns(Task.FromResult(new[]
        {
            Row(id: 30),
            Row(id: 10),
            Row(id: 20)
        }));

        await harness.Service.SendContainerAsync(harness.Client);

        var list = harness.Connection.Sent[1];
        UidOfLine(list, 0).Should().Be(10u);
        UidOfLine(list, 1).Should().Be(20u);
        UidOfLine(list, 2).Should().Be(30u);
    }

    [Test]
    public async Task Send_LeavesTheRowsTheFrameCannotCarryOutOfBothFrames()
    {
        var harness = Build();
        A.CallTo(() => harness.Repository.GetVisibleAsync(Character)).Returns(Task.FromResult(new[]
        {
            Row(id: 1),
            Row(id: 2, code: 0),
            Row(id: 3)
        }));

        await harness.Service.SendContainerAsync(harness.Client);

        var list = harness.Connection.Sent[1];
        CountOf(list).Should().Be(2);
        CountOf(harness.Connection.Sent[0]).Should().Be(2,
            "the counters describe the lines actually sent");
    }

    [Test]
    public async Task Send_PushesAnEmptyPairWhenTheContainerIsEmpty()
    {
        var harness = Build();
        A.CallTo(() => harness.Repository.GetVisibleAsync(Character))
            .Returns(Task.FromResult(Array.Empty<PaidItemEntity>()));

        await harness.Service.SendContainerAsync(harness.Client);

        harness.Connection.Sent.Should().HaveCount(2);
        CountOf(harness.Connection.Sent[0]).Should().Be(0);
        harness.Connection.Sent[1].Length.Should().Be(9);
        CountOf(harness.Connection.Sent[1]).Should().Be(0);
    }

    [Test]
    public async Task Send_CapsTheListAtTheTopOfItsCountField()
    {
        var harness = Build();
        A.CallTo(() => harness.Repository.GetVisibleAsync(Character)).Returns(Task.FromResult(
            Enumerable.Range(1, CommercialStorageRules.MaxEntries + 10)
                .Select(index => Row(id: index))
                .ToArray()));

        await harness.Service.SendContainerAsync(harness.Client);

        var list = harness.Connection.Sent[1];
        list.Length.Should().Be(9 + (10 * CommercialStorageRules.MaxEntries));
        CountOf(list).Should().Be((ushort)CommercialStorageRules.MaxEntries);
        CountOf(harness.Connection.Sent[0]).Should().Be((ushort)CommercialStorageRules.MaxEntries,
            "the counters still describe the lines actually sent");
        UidOfLine(list, CommercialStorageRules.MaxEntries - 1)
            .Should().Be((uint)CommercialStorageRules.MaxEntries);
    }

    [Test]
    public async Task Send_PushesNothingWhenNoCharacterIsInSession()
    {
        var harness = Build(characterName: string.Empty);

        await harness.Service.SendContainerAsync(harness.Client);

        harness.Connection.Sent.Should().BeEmpty();
        A.CallTo(() => harness.Repository.GetVisibleAsync(A<string>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task Takeout_DeliversTheGoodsConsumesTheLineAndRefreshesThePair()
    {
        var harness = Build();
        var row = Row(rest: 5);
        A.CallTo(() => harness.Repository.ResolveAsync(Character, Uid)).Returns(Task.FromResult(row));
        A.CallTo(() => harness.Characters.AddItemAsync(Character, ItemCode, 5))
            .Returns(Task.FromResult(Added()));
        A.CallTo(() => harness.Repository.ConsumeAsync(Character, Uid, 5))
            .Returns(Task.FromResult(CommercialTakeoutResult.Consumed(row, 0)));
        A.CallTo(() => harness.Repository.GetVisibleAsync(Character))
            .Returns(Task.FromResult(Array.Empty<PaidItemEntity>()));

        await Takeout(harness);

        harness.Connection.Sent.Should().HaveCount(3);
        IdOf(harness.Connection.Sent[0]).Should().Be(207,
            "the goods go to the bag through the ordinary inventory frame");
        BinaryPrimitives.ReadUInt32LittleEndian(harness.Connection.Sent[0].AsSpan(9, 4)).Should().Be(77u,
            "the item record of a 207 starts at offset 9 and its handle comes first");

        IdOf(harness.Connection.Sent[1]).Should().Be(10003, "the window is refreshed after the takeout");
        IdOf(harness.Connection.Sent[2]).Should().Be(10004);

        A.CallTo(() => harness.Repository.ConsumeAsync(Character, Uid, 5)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task Takeout_KeepsTheReadAndTheWriteOnTheSessionCharacter()
    {
        var harness = Build();
        var row = Row();
        A.CallTo(() => harness.Repository.ResolveAsync(Character, Uid)).Returns(Task.FromResult(row));
        A.CallTo(() => harness.Characters.AddItemAsync(A<string>._, A<int>._, A<long>._))
            .Returns(Task.FromResult(Added()));
        A.CallTo(() => harness.Repository.ConsumeAsync(A<string>._, A<uint>._, A<ushort>._))
            .Returns(Task.FromResult(CommercialTakeoutResult.Consumed(row, 0)));
        A.CallTo(() => harness.Repository.GetVisibleAsync(A<string>._))
            .Returns(Task.FromResult(Array.Empty<PaidItemEntity>()));

        await Takeout(harness, Uid, 2);

        A.CallTo(() => harness.Repository.ResolveAsync(Character, Uid)).MustHaveHappenedOnceExactly();
        A.CallTo(() => harness.Characters.AddItemAsync(Character, ItemCode, 2)).MustHaveHappenedOnceExactly();
        A.CallTo(() => harness.Repository.ConsumeAsync(Character, Uid, 2)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task Takeout_KeepsTheWidestUidIntact()
    {
        // The worst case of §5.2.2: a uid at the top of the field must reach the repository unchanged, or
        // it would name a different row.
        var harness = Build();
        A.CallTo(() => harness.Repository.ResolveAsync(Character, uint.MaxValue))
            .Returns(Task.FromResult(Row(id: uint.MaxValue)));

        await Takeout(harness, uint.MaxValue, 1);

        A.CallTo(() => harness.Repository.ResolveAsync(Character, uint.MaxValue)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task Takeout_AnswersNothingWhenTheUidNamesNoOwnedRow()
    {
        var harness = Build();
        A.CallTo(() => harness.Repository.ResolveAsync(Character, Uid)).Returns(Task.FromResult<PaidItemEntity>(null));

        await Takeout(harness);

        harness.Connection.Sent.Should().BeEmpty("a refusal has no result packet in this family");
        A.CallTo(() => harness.Characters.AddItemAsync(A<string>._, A<int>._, A<long>._)).MustNotHaveHappened();
        A.CallTo(() => harness.Repository.ConsumeAsync(A<string>._, A<uint>._, A<ushort>._)).MustNotHaveHappened();
    }

    [TestCase(0, TestName = "Takeout_AnswersNothingOnAZeroQuantity")]
    [TestCase(6, TestName = "Takeout_AnswersNothingAboveWhatTheLineHolds")]
    public async Task Takeout_AnswersNothingOutsideTheTakeableBounds(int count)
    {
        var harness = Build();
        A.CallTo(() => harness.Repository.ResolveAsync(Character, Uid))
            .Returns(Task.FromResult(Row(rest: 5)));

        await Takeout(harness, Uid, (ushort)count);

        harness.Connection.Sent.Should().BeEmpty();
        A.CallTo(() => harness.Characters.AddItemAsync(A<string>._, A<int>._, A<long>._)).MustNotHaveHappened();
        A.CallTo(() => harness.Repository.ConsumeAsync(A<string>._, A<uint>._, A<ushort>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task Takeout_AnswersNothingForAnItemCodeTheCatalogueDoesNotKnow()
    {
        var harness = Build(knownCode: false);
        A.CallTo(() => harness.Repository.ResolveAsync(Character, Uid)).Returns(Task.FromResult(Row()));

        await Takeout(harness);

        harness.Connection.Sent.Should().BeEmpty();
        A.CallTo(() => harness.Characters.AddItemAsync(A<string>._, A<int>._, A<long>._)).MustNotHaveHappened();
        A.CallTo(() => harness.Repository.ConsumeAsync(A<string>._, A<uint>._, A<ushort>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task Takeout_LeavesTheLineAloneWhenNothingCouldBeAdded()
    {
        var harness = Build();
        A.CallTo(() => harness.Repository.ResolveAsync(Character, Uid)).Returns(Task.FromResult(Row()));
        A.CallTo(() => harness.Characters.AddItemAsync(A<string>._, A<int>._, A<long>._))
            .Returns(Task.FromResult<ItemEntity>(null));

        await Takeout(harness);

        harness.Connection.Sent.Should().BeEmpty();
        A.CallTo(() => harness.Repository.ConsumeAsync(A<string>._, A<uint>._, A<ushort>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task Takeout_KeepsTheGoodsAndTheRefreshWhenTheConsumptionFails()
    {
        // The assumed order of §5.6 point 5-6: the delivery comes first, so a failure of the consumption
        // cannot lose an item the player paid for. The log is the only trace, and the window still refreshes.
        var harness = Build();
        A.CallTo(() => harness.Repository.ResolveAsync(Character, Uid)).Returns(Task.FromResult(Row()));
        A.CallTo(() => harness.Characters.AddItemAsync(A<string>._, A<int>._, A<long>._))
            .Returns(Task.FromResult(Added()));
        A.CallTo(() => harness.Repository.ConsumeAsync(Character, Uid, 5))
            .Returns(Task.FromResult(CommercialTakeoutResult.Refused(CommercialTakeoutOutcome.UnknownItem)));
        A.CallTo(() => harness.Repository.GetVisibleAsync(Character))
            .Returns(Task.FromResult(Array.Empty<PaidItemEntity>()));

        await Takeout(harness);

        harness.Connection.Sent.Should().HaveCount(3);
        IdOf(harness.Connection.Sent[0]).Should().Be(207);
        IdOf(harness.Connection.Sent[1]).Should().Be(10003);
    }

    [Test]
    public async Task Takeout_ReturnsTheRemainingUnitsToTheRepository()
    {
        var harness = Build();
        var row = Row(rest: 12);
        A.CallTo(() => harness.Repository.ResolveAsync(Character, Uid)).Returns(Task.FromResult(row));
        A.CallTo(() => harness.Characters.AddItemAsync(A<string>._, A<int>._, A<long>._))
            .Returns(Task.FromResult(Added(amount: 7)));
        A.CallTo(() => harness.Repository.ConsumeAsync(Character, Uid, 7))
            .Returns(Task.FromResult(CommercialTakeoutResult.Consumed(row, 5)));
        // The line as it stands once the consumption went through: the refresh must show its new rest.
        A.CallTo(() => harness.Repository.GetVisibleAsync(Character))
            .Returns(Task.FromResult(new[] { Row(rest: 5) }));

        await Takeout(harness, Uid, 7);

        CountOfLine(harness.Connection.Sent[2], 0).Should().Be(5, "the refresh shows what is left");
    }

    [Test]
    public async Task Takeout_PushesNothingWhenNoCharacterIsInSession()
    {
        var harness = Build(characterName: string.Empty);

        await Takeout(harness);

        harness.Connection.Sent.Should().BeEmpty();
        A.CallTo(() => harness.Repository.ResolveAsync(A<string>._, A<uint>._)).MustNotHaveHappened();
    }

    [Test]
    public void OnDataReceived_HandsTheFrameToTheContainerService()
    {
        var service = A.Fake<ICommercialStorageService>();
        var received = false;
        A.CallTo(() => service.HandleTakeoutAsync(A<GameClient>._, A<GameActionPackets.TakeoutCommercialItemRequest>._))
            .Invokes(() => received = true);

        var connection = new StorageTestHarness.FrameConnection(TakeoutFrame(Uid, 250));
        var client = StorageTestHarness.NewGameClient(connection, commercialStorageService: service);

        var receive = () => client.OnDataReceived(TakeoutLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");

        StorageTestHarness.WaitFor(() => received);
        received.Should().BeTrue("the arm hands the frame to the container service");
        A.CallTo(() => service.HandleTakeoutAsync(client,
            new GameActionPackets.TakeoutCommercialItemRequest(Uid, 250))).MustHaveHappenedOnceExactly();
    }

    [Test]
    public void OnDataReceived_HandsTheTakeoutToTheRealContainer()
    {
        // Frame level, through the receive loop: the arm must not reach the throwing switch, the frame must
        // be consumed whole, and the real service must be the one that reads the uid.
        var harness = Build();
        var reached = false;
        A.CallTo(() => harness.Repository.ResolveAsync(Character, Uid))
            .ReturnsLazily(() =>
            {
                reached = true;
                return Task.FromResult(Row());
            });

        var connection = new StorageTestHarness.FrameConnection(TakeoutFrame(Uid, 3));
        var client = StorageTestHarness.NewGameClient(connection, commercialStorageService: harness.Service);
        StorageTestHarness.Session(client).CharacterName = Character;

        var receive = () => client.OnDataReceived(TakeoutLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");

        StorageTestHarness.WaitFor(() => reached);
        reached.Should().BeTrue("the arm hands the frame to the container service");
        A.CallTo(() => harness.Repository.ResolveAsync(Character, Uid)).MustHaveHappenedOnceExactly();
    }

    [TestCase(12, TestName = "OnDataReceived_DropsATruncatedTakeout")]
    [TestCase(14, TestName = "OnDataReceived_DropsAPaddedTakeout")]
    public void OnDataReceived_DropsAnyTakeoutLengthOtherThanThirteen(int length)
    {
        var harness = Build();
        var frame = new byte[length];
        var wellFormed = TakeoutFrame(Uid, 1);
        Array.Copy(wellFormed, frame, Math.Min(length, wellFormed.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)length);
        frame[6] = StorageTestHarness.Checksum(frame);

        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection, commercialStorageService: harness.Service);

        var receive = () => client.OnDataReceived(length);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "a refused frame is still consumed entirely");
        connection.Sent.Should().BeEmpty();

        // Nothing at all is read from the container: no resolution, no answer.
        A.CallTo(() => harness.Repository.ResolveAsync(A<string>._, A<uint>._)).MustNotHaveHappened();
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopOnATakeoutCoalescedWithAnotherFrame()
    {
        var harness = Build();
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[6] = StorageTestHarness.Checksum(keepalive);

        var frame = TakeoutFrame(Uid, 1).Concat(keepalive).ToArray();
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection, commercialStorageService: harness.Service);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "the keepalive behind the takeout was consumed too");
    }
}
