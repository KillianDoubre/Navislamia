using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Trade;

namespace Tests.Game;

[TestFixture]
public class PlayerTradeTests
{
    private PlayerVisibilityService _visibility;
    private ICharacterService _characters;
    private PlayerTradeService _trade;
    private GameClient _ana;
    private GameClient _bo;

    [SetUp]
    public void SetUp()
    {
        _visibility = new PlayerVisibilityService(A.Fake<ILogger<PlayerVisibilityService>>());
        _characters = A.Fake<ICharacterService>();
        _trade = new PlayerTradeService(_characters, _visibility);
        _ana = Player(1, "Ana", 100);
        _bo = Player(2, "Bo", 150);
    }

    [Test]
    public void TheFrameIs97BytesWithTheTradedCountInTheRecord()
    {
        var item = new ItemEntity { Id = 77, ItemResourceId = 603002, Amount = 9 };

        var frame = PlayerTradePackets.Build(5, TradeMode.AddItem, item, 3);

        frame.Length.Should().Be(97);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)).Should().Be(280);
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(7, 4)).Should().Be(5u);
        frame[11].Should().Be(5);
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(12, 4)).Should().Be(77u);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(16, 4)).Should().Be(603002);
        BinaryPrimitives.ReadInt64LittleEndian(frame.AsSpan(20, 8)).Should().Be(77);
        BinaryPrimitives.ReadInt64LittleEndian(frame.AsSpan(28, 8)).Should().Be(3);

        PlayerTradePackets.TryRead(frame, out var read).Should().BeTrue();
        read.Should().Be(new PlayerTradePackets.TradeRequest(5, TradeMode.AddItem, 77, 3));
        PlayerTradePackets.TryRead(frame.AsSpan(0, 96), out _).Should().BeFalse();
    }

    [Test]
    public async Task ARequestReachesTheTargetNamingTheRequester()
    {
        await Send(_ana, 2, TradeMode.Request);

        Trades(_bo).Should().ContainSingle().Which.Should().Be((1u, TradeMode.Request, 0L));
    }

    [Test]
    public async Task ARequestToAPlayerTooFarAnswersTooFar()
    {
        Info(_bo).X = 100 + WorldVisibility.RegionSize + 1;

        await Send(_ana, 2, TradeMode.Request);

        Results(_ana).Should().Equal(ResultCode.TooFar);
        Trades(_bo).Should().BeEmpty();
    }

    [Test]
    public async Task OnlyAnAcceptAnsweringARequestBeginsATrade()
    {
        await Send(_bo, 1, TradeMode.Accept);
        Results(_bo).Should().Equal(ResultCode.AccessDenied);
        Trades(_ana).Should().BeEmpty();

        await Send(_ana, 2, TradeMode.Request);
        await Send(_bo, 1, TradeMode.Accept);

        Trades(_ana).Should().Equal((2u, TradeMode.Begin, 0L));
        Trades(_bo).Should().Contain((1u, TradeMode.Begin, 0L));
    }

    [Test]
    public async Task AnOfferIsEchoedToBothWithTheCountOffered()
    {
        await Open();
        Item(_ana, 10, amount: 5);

        await Send(_ana, 2, TradeMode.AddItem, uid: 10, count: 3);

        Trades(_ana).Should().Equal((1u, TradeMode.AddItem, 3L));
        Trades(_bo).Should().Equal((1u, TradeMode.AddItem, 3L));

        Clear();
        await Send(_ana, 2, TradeMode.AddItem, uid: 10, count: 3);
        Results(_ana).Should().Equal(ResultCode.NotExist);

        await Send(_ana, 2, TradeMode.ModifyCount, uid: 10, count: 5);
        Trades(_bo).Should().Equal((1u, TradeMode.ModifyCount, 5L));
    }

    [Test]
    public async Task AnOfferBeyondTheStackOrOfAWornItemIsRefused()
    {
        await Open();
        Item(_ana, 10, amount: 5);
        Item(_ana, 11, amount: 1, worn: true);

        await Send(_ana, 2, TradeMode.AddItem, uid: 10, count: 6);
        await Send(_ana, 2, TradeMode.AddItem, uid: 11, count: 1);

        Results(_ana).Should().Equal(ResultCode.NotExist, ResultCode.NotActable);
        Trades(_bo).Should().BeEmpty();
    }

    [Test]
    public async Task GoldAboveTheBalanceIsRefused()
    {
        await Open();

        await Send(_ana, 2, TradeMode.AddGold, count: 1001);
        Results(_ana).Should().Equal(ResultCode.NotExist);

        await Send(_ana, 2, TradeMode.AddGold, count: 400);
        Trades(_bo).Should().Equal((1u, TradeMode.AddGold, 400L));
    }

    [Test]
    public async Task ConfirmingBeforeBothFreezeCancelsTheTrade()
    {
        await Open();

        await Send(_ana, 2, TradeMode.Confirm);

        Trades(_ana).Should().Equal((2u, TradeMode.Cancel, 0L));
        Trades(_bo).Should().Equal((1u, TradeMode.Cancel, 0L));
    }

    [Test]
    public async Task TheSecondConfirmationCarriesTheTradeOut()
    {
        await Open();
        Item(_ana, 10, amount: 5);
        await Send(_ana, 2, TradeMode.AddItem, uid: 10, count: 2);
        await Send(_bo, 1, TradeMode.AddGold, count: 300);
        await Send(_ana, 2, TradeMode.Freeze);
        await Send(_bo, 1, TradeMode.Freeze);

        ItemExchange exchange = null;
        var received = new ItemEntity { Id = 99, ItemResourceId = 603002, Amount = 2 };
        A.CallTo(() => _characters.ExchangeItemsAsync(A<ItemExchange>._))
            .Invokes((ItemExchange e) => exchange = e)
            .Returns(new ItemExchangeResult(ItemTransferOutcome.Success,
                Array.Empty<ItemTransferred>(), new[] { new ItemTransferred(10, 3, received) }));

        await Send(_ana, 2, TradeMode.Confirm);
        A.CallTo(() => _characters.ExchangeItemsAsync(A<ItemExchange>._)).MustNotHaveHappened();
        Clear();
        await Send(_bo, 1, TradeMode.Confirm);

        // Bo confirms last, so Bo is the exchange's first side and Ana its second.
        exchange.Should().NotBeNull();
        exchange.FirstGives.Should().BeEmpty();
        exchange.SecondGives.Should().Equal(new ItemTransferLine(10, 2));
        Info(_ana).CharacterGold.Should().Be(1300);
        Info(_bo).CharacterGold.Should().Be(700);
        exchange.FirstGold.Should().Be(700);
        exchange.SecondGold.Should().Be(1300);
        Trades(_ana).Should().Contain((1u, TradeMode.Process, 0L));
        Trades(_bo).Should().Contain((2u, TradeMode.Process, 0L));
        Ids(_ana).Should().Contain((ushort)GamePackets.TM_SC_UPDATE_ITEM_COUNT);
        Ids(_bo).Should().Contain((ushort)GamePackets.TM_SC_INVENTORY);

        Clear();
        await Send(_ana, 2, TradeMode.AddGold, count: 1);
        Trades(_bo).Should().BeEmpty();
    }

    [Test]
    public async Task ATradeTheDatabaseRefusesGivesTheGoldBack()
    {
        await Open();
        await Send(_ana, 2, TradeMode.AddGold, count: 250);
        await Send(_ana, 2, TradeMode.Freeze);
        await Send(_bo, 1, TradeMode.Freeze);
        A.CallTo(() => _characters.ExchangeItemsAsync(A<ItemExchange>._))
            .Returns(ItemExchangeResult.Failed(ItemTransferOutcome.ItemMissing));

        await Send(_ana, 2, TradeMode.Confirm);
        await Send(_bo, 1, TradeMode.Confirm);

        Info(_ana).CharacterGold.Should().Be(1000);
        Info(_bo).CharacterGold.Should().Be(1000);
        Results(_ana).Should().Contain(ResultCode.AccessDenied);
        Results(_bo).Should().Contain(ResultCode.AccessDenied);
    }

    [Test]
    public async Task LeavingTheWorldClosesThePartnersWindow()
    {
        await Open();

        _trade.CancelFor(_ana);

        Trades(_bo).Should().Equal((1u, TradeMode.Cancel, 0L));
    }

    private async Task Open()
    {
        await Send(_ana, 2, TradeMode.Request);
        await Send(_bo, 1, TradeMode.Accept);
        Clear();
    }

    private Task Send(GameClient from, uint target, TradeMode mode, long uid = 0, long count = 0) =>
        _trade.HandleAsync(from, new PlayerTradePackets.TradeRequest(target, mode, uid, count));

    private void Item(GameClient owner, long id, long amount, bool worn = false)
    {
        var item = new ItemEntity
        {
            Id = id, ItemResourceId = 603002, Amount = amount,
            WearInfo = worn ? ItemWearType.Weapon : ItemWearType.None
        };
        A.CallTo(() => _characters.GetItemByHandleAsync(Info(owner).CharacterName, (uint)id))
            .Returns(Task.FromResult(item));
    }

    private GameClient Player(uint handle, string name, float x)
    {
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()),
            playerVisibilityService: _visibility);
        var info = Info(client);
        info.CharacterHandle = handle;
        info.CharacterName = name;
        info.CharacterGold = 1000;
        info.X = x;
        info.Y = 100;
        _visibility.Registry.Register(handle, client);
        return client;
    }

    private static ConnectionInfo Info(GameClient client) => StorageTestHarness.Session(client);

    private static List<byte[]> Sent(GameClient client) => ((StorageTestHarness.FrameConnection)client.Connection).Sent;

    private void Clear()
    {
        Sent(_ana).Clear();
        Sent(_bo).Clear();
    }

    private static List<ushort> Ids(GameClient client) =>
        Sent(client).Select(frame => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2))).ToList();

    private static List<(uint Target, TradeMode Mode, long Count)> Trades(GameClient client) =>
        Sent(client)
            .Where(frame => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)) == (ushort)GamePackets.TM_TRADE)
            .Select(frame => (BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(7, 4)), (TradeMode)(sbyte)frame[11],
                BinaryPrimitives.ReadInt64LittleEndian(frame.AsSpan(28, 8))))
            .ToList();

    private static List<ResultCode> Results(GameClient client) =>
        Sent(client)
            .Where(frame => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)) == (ushort)GamePackets.TM_SC_RESULT
                            && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(7, 2)) == (ushort)GamePackets.TM_TRADE)
            .Select(frame => (ResultCode)BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(9, 2)))
            .ToList();
}
