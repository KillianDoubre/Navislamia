using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// The booth trade between two players who see each other (docs/packet-specs/705-buy-from-booth.md §5):
/// the real visibility and watch services, the database faked at <see cref="ICharacterService"/>.
/// </summary>
[TestFixture]
public class BoothTradeServiceTests
{
    private const uint OwnerHandle = 0x40000001;
    private const uint CustomerHandle = 0x40000002;
    private const uint OfferedHandle = 0x0A000001;

    private ICharacterService _characters = null!;
    private PlayerVisibilityService _visibility = null!;
    private BoothTradeService _service = null!;
    private GameClient _owner = null!;
    private GameClient _customer = null!;

    [SetUp]
    public void SetUp()
    {
        _characters = A.Fake<ICharacterService>();
        _visibility = new PlayerVisibilityService(
            A.Fake<Microsoft.Extensions.Logging.ILogger<PlayerVisibilityService>>());
        _service = new BoothTradeService(_characters, _visibility, new BoothWatchService(_characters));

        _owner = Player(OwnerHandle, "Owner", 0f, gold: 1000);
        _customer = Player(CustomerHandle, "Customer", 100f, gold: 1000);
        _visibility.EnterWorld(_owner);
        _visibility.EnterWorld(_customer);
        Sent(_owner).Clear();
        Sent(_customer).Clear();
    }

    [Test]
    public void OpeningABooth_PublishesItsBitToTheOwnerAndToWhoeverSeesIt()
    {
        Info(_owner).OpenBooth(SellBooth(3, 100));

        _service.PublishBoothStatus(_owner);

        StatusOf(Sent(_owner).Single()).Should().Be(CreatureStatus.PlayerSellBooth);
        StatusOf(Sent(_customer).Single()).Should().Be(CreatureStatus.PlayerSellBooth);
    }

    [Test]
    public void BoothNames_AreGivenForOpenBoothsOnly()
    {
        Info(_owner).OpenBooth(SellBooth(3, 100));
        var ask = new byte[11 + 8];
        BinaryPrimitives.WriteInt32LittleEndian(ask.AsSpan(7, 4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(ask.AsSpan(11, 4), OwnerHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(ask.AsSpan(15, 4), CustomerHandle);

        _service.HandleGetBoothsName(_customer, ask);

        var answer = Sent(_customer).Single();
        Id(answer).Should().Be(708);
        BinaryPrimitives.ReadUInt32LittleEndian(answer.AsSpan(7, 4)).Should().Be(1);
        BinaryPrimitives.ReadUInt32LittleEndian(answer.AsSpan(11, 4)).Should().Be(OwnerHandle);
        answer.AsSpan(15, 8).ToArray().Should().Equal("Boutique"u8.ToArray());
    }

    [Test]
    public async Task Buying_MovesGoldAndItemsAndTellsBothSides()
    {
        Info(_owner).OpenBooth(SellBooth(3, 100));
        Info(_customer).BeginWatchingBooth(OwnerHandle);
        var offered = OwnerItem(amount: 5);
        var received = new ItemEntity { Id = 77, ItemResourceId = 240100, Amount = 2, WearInfo = ItemWearType.None };
        A.CallTo(() => _characters.TransferItemsAsync(A<ItemTransfer>._)).Returns(new ItemTransferResult(
            ItemTransferOutcome.Success, new[] { new ItemTransferred(OfferedHandle, 3, received) }));

        await _service.HandleBuyAsync(_customer, BoothTradePacketsTests.BuildBuy(OwnerHandle, (OfferedHandle, 240100, 2)));

        Info(_customer).CharacterGold.Should().Be(800);
        Info(_owner).CharacterGold.Should().Be(1200);
        A.CallTo(() => _characters.TransferItemsAsync(A<ItemTransfer>.That.Matches(t =>
                t.GiverName == "Owner" && t.ReceiverName == "Customer" && t.GiverGold == 1200 && t.ReceiverGold == 800
                && t.Lines.Single() == new ItemTransferLine(OfferedHandle, 2))))
            .MustHaveHappenedOnceExactly();
        Info(_owner).Booth.Items[0].Count.Should().Be(1, "two of the three units were sold");

        var customerIds = Sent(_customer).Select(Id).ToList();
        customerIds.Should().Contain(new ushort[] { 1001, 207, 703 });
        ResultOf(Sent(_customer).Last(frame => Id(frame) == 0)).Should().Be((705, ResultCode.Success));

        var ownerFrames = Sent(_owner);
        ownerFrames.Select(Id).Should().Contain(new ushort[] { 1001, 255, 710 });
        var tradeInfo = ownerFrames.Single(frame => Id(frame) == 710);
        BinaryPrimitives.ReadUInt32LittleEndian(tradeInfo.AsSpan(7, 4)).Should().Be(CustomerHandle);
        tradeInfo[11].Should().Be(1, "a purchase from a sell booth");
        BinaryPrimitives.ReadInt64LittleEndian(tradeInfo.AsSpan(14 + 75, 8)).Should().Be(100, "the unit price");
        _ = offered;
    }

    [Test]
    public async Task BuyingTheLastUnits_ClosesTheBoothForEveryone()
    {
        Info(_owner).OpenBooth(SellBooth(2, 100));
        Info(_customer).BeginWatchingBooth(OwnerHandle);
        OwnerItem(amount: 2);
        A.CallTo(() => _characters.TransferItemsAsync(A<ItemTransfer>._)).Returns(new ItemTransferResult(
            ItemTransferOutcome.Success,
            new[] { new ItemTransferred(OfferedHandle, 0, new ItemEntity { Id = 77, ItemResourceId = 240100, Amount = 2, WearInfo = ItemWearType.None }) }));

        await _service.HandleBuyAsync(_customer, BoothTradePacketsTests.BuildBuy(OwnerHandle, (OfferedHandle, 240100, 2)));

        Info(_owner).IsBoothOpen.Should().BeFalse();
        Sent(_customer).Select(Id).Should().Contain((ushort)709);
        Info(_customer).WatchedBoothHandle.Should().BeNull();
        StatusOf(Sent(_customer).Last(frame => Id(frame) == 500)).Should().Be(0u, "the booth bit is cleared");
    }

    [Test]
    public async Task Buying_WithoutTheGold_IsRefusedAndTheUnitsStayOnOffer()
    {
        Info(_owner).OpenBooth(SellBooth(3, 600));
        Info(_customer).BeginWatchingBooth(OwnerHandle);
        OwnerItem(amount: 5);

        await _service.HandleBuyAsync(_customer, BoothTradePacketsTests.BuildBuy(OwnerHandle, (OfferedHandle, 240100, 2)));

        ResultOf(Sent(_customer).Single()).Should().Be((705, ResultCode.NotEnoughMoney));
        Info(_customer).CharacterGold.Should().Be(1000);
        Info(_owner).CharacterGold.Should().Be(1000);
        Info(_owner).Booth.Items[0].Count.Should().Be(3);
        A.CallTo(() => _characters.TransferItemsAsync(A<ItemTransfer>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task AFailedTransfer_GivesTheGoldAndTheUnitsBack()
    {
        Info(_owner).OpenBooth(SellBooth(3, 100));
        Info(_customer).BeginWatchingBooth(OwnerHandle);
        OwnerItem(amount: 5);
        A.CallTo(() => _characters.TransferItemsAsync(A<ItemTransfer>._))
            .Returns(ItemTransferResult.Failed(ItemTransferOutcome.ItemMissing));

        await _service.HandleBuyAsync(_customer, BoothTradePacketsTests.BuildBuy(OwnerHandle, (OfferedHandle, 240100, 2)));

        ResultOf(Sent(_customer).Single()).Should().Be((705, ResultCode.NotExist));
        Info(_customer).CharacterGold.Should().Be(1000);
        Info(_owner).CharacterGold.Should().Be(1000);
        Info(_owner).Booth.Items[0].Count.Should().Be(3);
    }

    [Test]
    public async Task Buying_WithoutWatchingTheBooth_OrFromOneself_IsNotActable()
    {
        Info(_owner).OpenBooth(SellBooth(3, 100));
        OwnerItem(amount: 5);

        await _service.HandleBuyAsync(_customer, BoothTradePacketsTests.BuildBuy(OwnerHandle, (OfferedHandle, 240100, 1)));
        ResultOf(Sent(_customer).Single()).Should().Be((705, ResultCode.NotActable));

        await _service.HandleBuyAsync(_owner, BoothTradePacketsTests.BuildBuy(OwnerHandle, (OfferedHandle, 240100, 1)));
        ResultOf(Sent(_owner).Single()).Should().Be((705, ResultCode.NotActable));
    }

    [Test]
    public async Task Selling_ToABuyBooth_PaysTheSellerAndHandsTheItemToTheOwner()
    {
        const uint sellerItem = 0x0A000055;
        Info(_owner).OpenBooth(new StartBoothRequest(2, "Boutique"u8.ToArray(),
            new[] { new BoothOpenItem(OfferedHandle, 4, 50) }));
        Info(_customer).BeginWatchingBooth(OwnerHandle);
        OwnerItem(amount: 1);
        A.CallTo(() => _characters.GetItemByHandleAsync("Customer", sellerItem))
            .Returns(new ItemEntity { Id = sellerItem, ItemResourceId = 240100, Amount = 3, WearInfo = ItemWearType.None });
        var received = new ItemEntity { Id = sellerItem, ItemResourceId = 240100, Amount = 3, WearInfo = ItemWearType.None };
        A.CallTo(() => _characters.TransferItemsAsync(A<ItemTransfer>._)).Returns(new ItemTransferResult(
            ItemTransferOutcome.Success, new[] { new ItemTransferred(sellerItem, 0, received) }));

        var sale = new byte[19];
        BinaryPrimitives.WriteUInt32LittleEndian(sale.AsSpan(7, 4), OwnerHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(sale.AsSpan(11, 4), sellerItem);
        BinaryPrimitives.WriteInt32LittleEndian(sale.AsSpan(15, 4), 3);

        await _service.HandleSellAsync(_customer, sale);

        Info(_customer).CharacterGold.Should().Be(1150);
        Info(_owner).CharacterGold.Should().Be(850);
        A.CallTo(() => _characters.TransferItemsAsync(A<ItemTransfer>.That.Matches(t =>
                t.GiverName == "Customer" && t.ReceiverName == "Owner")))
            .MustHaveHappenedOnceExactly();
        Info(_owner).Booth.Items[0].Count.Should().Be(1);
        ResultOf(Sent(_customer).Last(frame => Id(frame) == 0)).Should().Be((706, ResultCode.Success));
        Sent(_customer).Select(Id).Should().Contain((ushort)254, "the whole stack left the seller's bag");
        Sent(_owner).Select(Id).Should().Contain(new ushort[] { 207, 710 });
        Sent(_owner).Single(frame => Id(frame) == 710)[11].Should().Be(0, "a sale to a buy booth");
    }

    [TestCase(GamePackets.TM_CS_BUY_FROM_BOOTH, 13, (ushort)0)]
    [TestCase(GamePackets.TM_CS_SELL_TO_BOOTH, 19, (ushort)0)]
    [TestCase(GamePackets.TM_CS_GET_BOOTHS_NAME, 11, (ushort)708)]
    [TestCase(GamePackets.TM_SC_GET_BOOTHS_NAME, 11, null)]
    [TestCase(GamePackets.TM_SC_BOOTH_CLOSED, 11, null)]
    [TestCase(GamePackets.TM_SC_BOOTH_TRADE_INFO, 14, null)]
    public void ReceiveLoop_RoutesEveryIdOfTheFamily(GamePackets id, int length, ushort? answer)
    {
        // Every declared id has its arm: one without would reach the throwing switch and end the loop.
        // A 705 of 13 bytes carries no item (refused), a 706 names no watched booth (refused), a 707 of
        // no handle is answered with an empty 708, and the three server ids are dropped.
        var frame = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), (ushort)id);
        frame[6] = StorageTestHarness.Checksum(frame);

        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection, characterService: _characters);
        Info(client).CharacterHandle = CustomerHandle;
        Info(client).CharacterName = "Customer";

        client.OnDataReceived(connection.BytesAvailable);
        StorageTestHarness.WaitFor(() => answer is null || connection.Sent.Count > 0);

        connection.BytesAvailable.Should().Be(0, "the frame was consumed");
        if (answer is null)
        {
            connection.Sent.Should().BeEmpty();
        }
        else
        {
            connection.Sent.Select(Id).Should().Contain(answer.Value);
        }
    }

    private ItemEntity OwnerItem(long amount)
    {
        var item = new ItemEntity { Id = OfferedHandle, ItemResourceId = 240100, Amount = amount, WearInfo = ItemWearType.None };
        A.CallTo(() => _characters.GetItemByHandleAsync("Owner", OfferedHandle)).Returns(item);
        return item;
    }

    private static StartBoothRequest SellBooth(int count, long unitPrice) =>
        new(1, "Boutique"u8.ToArray(), new[] { new BoothOpenItem(OfferedHandle, count, unitPrice) });

    private static GameClient Player(uint handle, string name, float x, long gold)
    {
        var connection = new StorageTestHarness.FrameConnection(System.Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = handle;
        info.CharacterName = name;
        info.CharacterGold = gold;
        info.X = x;
        info.Y = 0f;
        info.Appearance = new PlayerAppearance();
        return client;
    }

    private static ConnectionInfo Info(GameClient client) => StorageTestHarness.Session(client);

    private static List<byte[]> Sent(GameClient client) => ((StorageTestHarness.FrameConnection)client.Connection).Sent;

    private static ushort Id(byte[] frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2));

    private static uint StatusOf(byte[] frame)
    {
        Id(frame).Should().Be((ushort)GamePackets.TM_SC_STATUS_CHANGE);
        return BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(11, 4));
    }

    private static (ushort Request, ResultCode Result) ResultOf(byte[] frame)
    {
        Id(frame).Should().Be((ushort)GamePackets.TM_SC_RESULT);
        return (BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(7, 2)),
            (ResultCode)BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(9, 2)));
    }
}
