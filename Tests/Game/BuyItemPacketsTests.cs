using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;

namespace Tests.Game;

/// <summary>
/// <c>TM_CS_BUY_ITEM</c> (251): the shop window sends one fixed 13-byte frame per catalogue line the
/// player validated, and the branch that reads it must keep it away from the throwing switch.
/// See docs/packet-specs/251-buy-item.md.
/// </summary>
[TestFixture]
public class BuyItemPacketTests
{
    private const int PacketLength = GameTradePackets.BuyItemSize;
    private const int HeaderSize = 7;

    /// <summary>
    /// The frame as the 7.3 client writes it: the length in hard at 0, the id at 4, the header checksum
    /// at 6, then the two payload fields (<c>SFrame.exe</c> <c>0x48f380</c>).
    /// </summary>
    private static byte[] ClientFrame(int itemCode, ushort buyCount)
    {
        var packet = new byte[PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_BUY_ITEM);
        packet[6] = StorageTestHarness.Checksum(packet);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(HeaderSize, 4), itemCode);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(HeaderSize + 4, 2), buyCount);
        return packet;
    }

    /// <summary>A frame that claims the given length and holds no more than the header.</summary>
    private static byte[] MalformedFrame(int length)
    {
        var packet = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_BUY_ITEM);
        packet[6] = StorageTestHarness.Checksum(packet);
        return packet;
    }

    private static ushort Id(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));

    [Test]
    public void Ids_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_CS_BUY_ITEM).Should().Be(251);

        // rzu remaps 251 to 1251 from EPIC_9_6_3 on (TS_CS_BUY_ITEM.h:14-15), above EPIC_7_3: the 9.6.3
        // id must not be declared, and 251 must be defined or OnDataReceived drops the frame as
        // "Undefined packet ID" before any dispatch.
        Enum.IsDefined(typeof(GamePackets), (ushort)1251).Should().BeFalse();
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_BUY_ITEM).Should().BeTrue();
    }

    [Test]
    public void ClientPacket_UsesTheThirteenByteLayout()
    {
        // item_code is laid down by hand — 04 03 02 01 — so a big-endian read would give 0x04030201
        // (67305985) instead of the little-endian 0x01020304 (16909060), and buy_count — 02 01 — would
        // give 0x0201 (513) instead of 0x0102 (258).
        var packet = ClientFrame(0x01020304, 0x0102);

        packet.Length.Should().Be(13, "7 bytes of header plus a 4-byte code and a 2-byte count");
        Marshal.SizeOf<Header>().Should().Be(HeaderSize, "the frame's header is 7 bytes, no more");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(13,
            "the client writes the length in hard");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(251);
        packet[6].Should().Be(StorageTestHarness.Checksum(packet),
            "the checksum is the sum of the first six header bytes");
        packet[7].Should().Be(0x04);
        packet[8].Should().Be(0x03);
        packet[9].Should().Be(0x02);
        packet[10].Should().Be(0x01);
        packet[11].Should().Be(0x02);
        packet[12].Should().Be(0x01);
    }

    [Test]
    public void TryReadBuyItem_ReadsTheCodeAndTheCountAtTheirOffsets()
    {
        GameTradePackets.TryReadBuyItem(ClientFrame(0x01020304, 0x0102), out var itemCode, out var buyCount)
            .Should().BeTrue();

        itemCode.Should().Be(0x01020304, "item_code is a little endian int32 at offset 7");
        itemCode.Should().NotBe(0x04030201, "rzu writes the scalar int32 as it stands on x86");
        buyCount.Should().Be(0x0102, "buy_count is a little endian uint16 at offset 11");
        buyCount.Should().NotBe((ushort)0x0201, "a byte swap would read the two count bytes backwards");
    }

    [Test]
    public void TryReadBuyItem_ReadsRealisticValuesFromTheCatalog()
    {
        GameTradePackets.TryReadBuyItem(ClientFrame(603002, 3), out var itemCode, out var buyCount)
            .Should().BeTrue();

        itemCode.Should().Be(603002);
        buyCount.Should().Be(3);
    }

    [Test]
    public void TheFrameHasNoRoomForAFourthField()
    {
        // A shifted read is what this guards: an int32 read two bytes past the code field would fold the
        // count into the code, which is exactly how a layout that moved by one field would look.
        var packet = ClientFrame(0x00010002, 3);

        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(HeaderSize + 2, 4))
            .Should().NotBe(0x00010002, "the code cannot be read two bytes late");

        // 7 + 4 + 2 is the whole frame: no byte is left for a padded field, and the count's two bytes are
        // the last two of the frame.
        GameTradePackets.BuyItemSize.Should().Be(13);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(11, 2)).Should().Be(3);
    }

    [TestCase(0, TestName = "TryReadBuyItem_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadBuyItem_RejectsAHeaderOnlyFrame")]
    [TestCase(12, TestName = "TryReadBuyItem_RejectsATruncatedFrame")]
    [TestCase(14, TestName = "TryReadBuyItem_RejectsAPaddedFrame")]
    [TestCase(36, TestName = "TryReadBuyItem_RejectsAFrameTheSizeOfTheEcho")]
    public void TryReadBuyItem_RefusesAnyLengthOtherThanThirteen(int length)
    {
        var packet = new byte[length];
        if (length >= PacketLength)
        {
            ClientFrame(603002, 3).CopyTo(packet, 0);
        }

        GameTradePackets.TryReadBuyItem(packet, out var itemCode, out var buyCount).Should().BeFalse();
        itemCode.Should().Be(0);
        buyCount.Should().Be(0);
    }

    [Test]
    public void BuyItem_IsDispatchedBeforeTheUnknownPacketThrow()
    {
        // GameClient's dispatch is a chain of ifs, so a member added to the enum without a branch reaches
        // the final switch and its `throw` kills the receive loop. Nothing smaller than a source scan can
        // check that without a live socket.
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "Game", "Network", "Clients", "GameClient.cs"));

        var branch = source.IndexOf($"GamePackets.{GamePackets.TM_CS_BUY_ITEM}", StringComparison.Ordinal);
        var finalSwitch = source.IndexOf("throw new Exception($\"Unknown Packet Type", StringComparison.Ordinal);

        branch.Should().BeGreaterThan(-1, "TM_CS_BUY_ITEM needs a branch of its own in OnDataReceived");
        finalSwitch.Should().BeGreaterThan(-1, "the final switch is the guard this test is about");
        branch.Should().BeLessThan(finalSwitch, "251 must be handled before the final switch throws");
    }

    [Test]
    public void OnDataReceived_ConsumesTheFrameWithoutThrowing()
    {
        var connection = new StorageTestHarness.FrameConnection(ClientFrame(603002, 1));
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(PacketLength);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[6] = StorageTestHarness.Checksum(keepalive);

        var frames = ClientFrame(603002, 1).Concat(keepalive).ToArray();
        var connection = new StorageTestHarness.FrameConnection(frames);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(frames.Length);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
    }

    [Test]
    public void OnDataReceived_RefusesAFrameThatIsNotThirteenBytesLong()
    {
        // A frame whose declared length is not the established one is consumed whole and answered with
        // 28 (InvalidArgument), never partially read.
        var frame = MalformedFrame(9);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        client.OnDataReceived(frame.Length);

        StorageTestHarness.WaitFor(() => connection.Sent.Count > 0);
        connection.BytesAvailable.Should().Be(0);
        connection.Sent.Should().ContainSingle().Which.Length.Should().Be(15);
        var result = connection.Sent[0];
        Id(result).Should().Be((ushort)GamePackets.TM_SC_RESULT);
        BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(7, 2)).Should().Be(251);
        BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(9, 2)).Should().Be((ushort)ResultCode.InvalidArgument);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Navislamia.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the repository root is needed to check the dispatch chain");

        return directory!.FullName;
    }
}

/// <summary>
/// The transaction side of the open window: what a purchase costs, what it sends back, in which order,
/// and what it answers when it cannot be made. The reference is
/// <c>WorldSession.cpp:733-814</c>; see docs/packet-specs/251-buy-item.md §5.2 and §6.
/// </summary>
[TestFixture]
public class MarketTradeTests
{
    private const int ItemCode = 603002;
    private const uint MerchantHandle = 0x80000123;
    private const string MarketName = "deva_weapon";

    /// <summary>The merchant entry of an NPC dialog, as the client sends it back on selection.</summary>
    private const string Trigger = "open_market(deva_weapon)";

    private ICharacterService _characters = null!;

    [SetUp]
    public void SetUp()
    {
        _characters = A.Fake<ICharacterService>();
        A.CallTo(() => _characters.AddItemAsync(A<string>._, A<int>._, A<long>._))
            .Returns(new ItemEntity { Id = 12, ItemResourceId = ItemCode, Amount = 1 });
    }

    private static MarketResourceRow Row(int code, long price, int huntaholicPoint = 0, string name = MarketName) =>
        new()
        {
            Name = name,
            SortId = 1,
            Code = code,
            Price = price,
            HuntaholicPoint = huntaholicPoint
        };

    /// <summary>A client whose only usable part is its connection and the session behind it.</summary>
    private static (GameClient Client, StorageTestHarness.FrameConnection Connection, ConnectionInfo Info)
        NewBuyer(IMarketTradeService service, long gold = 20_000, int chaos = 7, uint dialogHandle = MerchantHandle,
            string marketName = MarketName)
    {
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection, marketTradeService: service);
        var info = StorageTestHarness.Session(client);
        info.CharacterName = "Buyer";
        info.CharacterGold = gold;
        info.CharacterChaos = chaos;
        info.NpcDialogHandle = dialogHandle;
        info.OpenMarketName = marketName;
        return (client, connection, info);
    }

    private MarketTradeService Buyer(long gold = 20_000, int chaos = 7, uint dialogHandle = MerchantHandle,
        string marketName = MarketName, long price = 5_000L, int huntaholicPoint = 0)
    {
        var service = new MarketTradeService(
            new MarketCatalog(new MarketCatalogOptions
            {
                Markets = new List<MarketResourceRow> { Row(ItemCode, price, huntaholicPoint) }
            }), _characters);

        _buyer = NewBuyer(service, gold, chaos, dialogHandle, marketName);
        return service;
    }

    private (GameClient Client, StorageTestHarness.FrameConnection Connection, ConnectionInfo Info) _buyer;

    private static ushort Id(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));

    /// <summary>The 7.3 selection frame: 7-byte header, a length-prefixed trigger at 7, ASCII at 9.</summary>
    private static byte[] SelectionFrame(string trigger)
    {
        var packet = new byte[9 + trigger.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_DIALOG);
        packet[6] = StorageTestHarness.Checksum(packet);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(7, 2), (ushort)trigger.Length);
        Encoding.ASCII.GetBytes(trigger).CopyTo(packet, 9);
        return packet;
    }

    /// <summary>
    /// The dialog service with the real catalogue under it: the merchant trigger has to reach the market
    /// service for the memory to be written, so only the warp and storage sides are faked.
    /// </summary>
    private static NpcDialogService DialogService() =>
        new(Options.Create(new NpcDialogOptions()), A.Fake<IWarpService>(), A.Fake<IStorageService>(),
            new MarketService(new MarketCatalog(new MarketCatalogOptions
            {
                Markets = new List<MarketResourceRow> { Row(ItemCode, 5_000L, 7) }
            })));

    [Test]
    public async Task Buy_DebitsTheWholePriceAndEchoesTheTransaction()
    {
        var service = Buyer(gold: 20_000, price: 5_000L, huntaholicPoint: 4);
        var (client, connection, info) = _buyer;

        await service.BuyAsync(client, ItemCode, 2);

        // The gold goes out first, then the acknowledgement naming the item, then the transaction echo.
        connection.Sent.Select(Id).Should().Equal(
            new[]
            {
                (ushort)GamePackets.TM_SC_GOLD_UPDATE,
                (ushort)GamePackets.TM_SC_RESULT,
                (ushort)GamePackets.TM_SC_NPC_TRADE_INFO
            });

        info.CharacterGold.Should().Be(10_000, "two units at 5000 gold each");

        var gold = connection.Sent[0];
        gold.Length.Should().Be(19, "7 bytes of header plus a 8-byte gold and a 4-byte chaos");
        BinaryPrimitives.ReadUInt64LittleEndian(gold.AsSpan(7, 8)).Should().Be(10_000UL);
        BinaryPrimitives.ReadUInt32LittleEndian(gold.AsSpan(15, 4)).Should().Be(7U, "chaos is echoed as it stands");

        var result = connection.Sent[1];
        result.Length.Should().Be(15, "7 bytes of header plus request_msg_id, result and value");
        BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(7, 2)).Should().Be(251,
            "the request is the one being answered");
        BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(9, 2)).Should().Be((ushort)ResultCode.Success);
        BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(11, 4)).Should().Be(ItemCode);

        var echo = connection.Sent[2];
        echo.Length.Should().Be(36);
        echo[7].Should().Be(0, "is_sell is 0 on a purchase");
        BinaryPrimitives.ReadInt32LittleEndian(echo.AsSpan(8, 4)).Should().Be(ItemCode);
        BinaryPrimitives.ReadInt64LittleEndian(echo.AsSpan(12, 8)).Should().Be(2L, "the count the client asked for");
        BinaryPrimitives.ReadInt64LittleEndian(echo.AsSpan(20, 8)).Should().Be(10_000L,
            "price is the whole amount, not the unit price");
        BinaryPrimitives.ReadInt32LittleEndian(echo.AsSpan(28, 4)).Should().Be(4,
            "the huntaholic point is the one the open window announced for that line");
        BinaryPrimitives.ReadUInt32LittleEndian(echo.AsSpan(32, 4)).Should().Be(MerchantHandle);

        A.CallTo(() => _characters.AddItemAsync("Buyer", ItemCode, 2)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task Buy_ReachesTheLineThroughTheOpenDialogsMarket()
    {
        // The market a purchase reads is the one the window remembered, so a line the catalogue carries is
        // bought and the item is added under the character's name.
        var service = Buyer();
        var (client, connection, _) = _buyer;

        await service.BuyAsync(client, ItemCode, 1);

        A.CallTo(() => _characters.AddItemAsync("Buyer", ItemCode, 1)).MustHaveHappenedOnceExactly();
        connection.Sent.Should().HaveCount(3);
    }

    [Test]
    public async Task Buy_TakesTheDebitBeforeItWaitsOnTheStore()
    {
        // Two 251 frames coalesced in one buffer are handled one after the other on the same receive
        // thread: the second one must not be able to pass the gold check on gold the first one already
        // spent. The debit is taken synchronously, before the add is awaited — the adds are held open here
        // to prove exactly that.
        var pending = new TaskCompletionSource<ItemEntity>(TaskCreationOptions.RunContinuationsAsynchronously);
        A.CallTo(() => _characters.AddItemAsync("Buyer", ItemCode, 1)).ReturnsLazily(() => pending.Task);
        var service = Buyer(gold: 10_000, price: 5_000L);
        var (client, connection, info) = _buyer;

        var first = service.BuyAsync(client, ItemCode, 1);
        var second = service.BuyAsync(client, ItemCode, 1);

        info.CharacterGold.Should().Be(0, "both debits are already taken while both adds are still waiting");

        var third = service.BuyAsync(client, ItemCode, 1);

        connection.Sent.Select(Id).Should().Equal(
            new[]
            {
                (ushort)GamePackets.TM_SC_GOLD_UPDATE,
                (ushort)GamePackets.TM_SC_GOLD_UPDATE,
                (ushort)GamePackets.TM_SC_RESULT
            },
            "the third purchase is refused before it buys");
        BinaryPrimitives.ReadUInt16LittleEndian(connection.Sent[2].AsSpan(9, 2))
            .Should().Be((ushort)ResultCode.NotEnoughMoney);

        pending.SetResult(new ItemEntity { Id = 12, ItemResourceId = ItemCode, Amount = 1 });
        await Task.WhenAll(first, second, third);

        info.CharacterGold.Should().Be(0);
        A.CallTo(() => _characters.AddItemAsync("Buyer", ItemCode, 1)).MustHaveHappenedTwiceExactly();
    }

    [Test]
    public async Task Buy_RefusesAHandlelessDialog()
    {
        // A dialog that carries no NPC handle cannot be tied to a counter, whatever the session remembers.
        var service = Buyer(dialogHandle: 0);
        var (client, connection, info) = _buyer;

        await service.BuyAsync(client, ItemCode, 1);

        connection.Sent.Should().ContainSingle();
        var result = connection.Sent[0];
        Id(result).Should().Be((ushort)GamePackets.TM_SC_RESULT);
        BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(7, 2)).Should().Be(251);
        BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(9, 2)).Should().Be((ushort)ResultCode.Unknown);
        BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(11, 4)).Should().Be(0);
        info.CharacterGold.Should().Be(20_000, "nothing was bought");
        A.CallTo(() => _characters.AddItemAsync(A<string>._, A<int>._, A<long>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task Buy_RefusesAnEmptyMarketName()
    {
        var service = Buyer(marketName: string.Empty);
        var (client, connection, info) = _buyer;

        await service.BuyAsync(client, ItemCode, 1);

        connection.Sent.Should().ContainSingle("the reference answers 7 when no market resolves");
        BinaryPrimitives.ReadUInt16LittleEndian(connection.Sent[0].AsSpan(9, 2)).Should().Be((ushort)ResultCode.Unknown);
        info.CharacterGold.Should().Be(20_000);
    }

    [Test]
    public async Task Buy_RefusesAMarketTheCatalogDoesNotCarry()
    {
        var service = Buyer(marketName: "deva_armor");
        var (client, connection, info) = _buyer;

        await service.BuyAsync(client, ItemCode, 1);

        connection.Sent.Should().ContainSingle("the reference answers 7 for an unknown market (WorldSession.cpp:740-744)");
        BinaryPrimitives.ReadUInt16LittleEndian(connection.Sent[0].AsSpan(9, 2)).Should().Be((ushort)ResultCode.Unknown);
        info.CharacterGold.Should().Be(20_000);
    }

    [Test]
    public async Task Buy_RefusesAZeroCountWithSeven()
    {
        var service = Buyer();
        var (client, connection, info) = _buyer;

        await service.BuyAsync(client, ItemCode, 0);

        connection.Sent.Should().ContainSingle("WorldSession.cpp:735-738 refuses a zero count with 7");
        var result = connection.Sent[0];
        Id(result).Should().Be((ushort)GamePackets.TM_SC_RESULT);
        BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(9, 2)).Should().Be((ushort)ResultCode.Unknown);
        BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(11, 4)).Should().Be(0);
        info.CharacterGold.Should().Be(20_000);
        A.CallTo(() => _characters.AddItemAsync(A<string>._, A<int>._, A<long>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task Buy_SaysNothingAboutALineTheMarketDoesNotCarry()
    {
        var service = Buyer();
        var (client, connection, info) = _buyer;

        await service.BuyAsync(client, 999_999, 1);

        connection.Sent.Should().BeEmpty("the reference's loop ends with nothing sent (WorldSession.cpp:749-814)");
        info.CharacterGold.Should().Be(20_000);
        A.CallTo(() => _characters.AddItemAsync(A<string>._, A<int>._, A<long>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task Buy_RefusesWhatThePurseCannotPay()
    {
        var service = Buyer(gold: 9_999, price: 5_000L);
        var (client, connection, info) = _buyer;

        await service.BuyAsync(client, ItemCode, 2);

        connection.Sent.Should().ContainSingle("10 is the reference's code for a total that cannot be paid");
        var result = connection.Sent[0];
        Id(result).Should().Be((ushort)GamePackets.TM_SC_RESULT);
        BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(9, 2)).Should().Be((ushort)ResultCode.NotEnoughMoney);
        BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(11, 4)).Should().Be(0);
        info.CharacterGold.Should().Be(9_999, "a refused purchase does not move the gold");
    }

    [Test]
    public async Task Buy_AcceptsAPriceThePurseCoversExactly()
    {
        var service = Buyer(gold: 10_000, price: 5_000L);
        var (client, connection, info) = _buyer;

        await service.BuyAsync(client, ItemCode, 2);

        info.CharacterGold.Should().Be(0, "the reference refuses only strictly unaffordable totals");
        connection.Sent.Select(Id).Should().Contain((ushort)GamePackets.TM_SC_NPC_TRADE_INFO);
    }

    [Test]
    public async Task Buy_CreditsTheGoldBackWhenTheStoreRefusesTheItem()
    {
        A.CallTo(() => _characters.AddItemAsync("Buyer", ItemCode, 2))
            .ThrowsAsync(new InvalidOperationException("the store is down"));
        var service = Buyer(gold: 20_000, price: 5_000L);
        var (client, connection, info) = _buyer;

        await service.BuyAsync(client, ItemCode, 2);

        info.CharacterGold.Should().Be(20_000, "nothing was bought, so nothing is paid");
        connection.Sent.Select(Id).Should().Equal(
            new[]
            {
                (ushort)GamePackets.TM_SC_GOLD_UPDATE,
                (ushort)GamePackets.TM_SC_GOLD_UPDATE,
                (ushort)GamePackets.TM_SC_RESULT
            });
        BinaryPrimitives.ReadUInt64LittleEndian(connection.Sent[1].AsSpan(7, 8)).Should().Be(20_000UL,
            "the second update carries the gold back");
        BinaryPrimitives.ReadUInt16LittleEndian(connection.Sent[2].AsSpan(9, 2)).Should().Be((ushort)ResultCode.DBError);
        connection.Sent.Select(Id).Should().NotContain((ushort)GamePackets.TM_SC_NPC_TRADE_INFO,
            "a purchase that did not happen is not echoed");
    }

    [Test]
    public async Task Buy_CreditsTheGoldBackWhenTheCharacterIsGone()
    {
        A.CallTo(() => _characters.AddItemAsync("Buyer", ItemCode, 1)).Returns((ItemEntity)null);
        var service = Buyer();
        var (client, connection, info) = _buyer;

        await service.BuyAsync(client, ItemCode, 1);

        info.CharacterGold.Should().Be(20_000);
        BinaryPrimitives.ReadUInt16LittleEndian(connection.Sent.Last().AsSpan(9, 2))
            .Should().Be((ushort)ResultCode.DBError);
    }

    [Test]
    public void Open_ReportsWhetherTheWindowWasOpened()
    {
        var catalog = new MarketCatalog(new MarketCatalogOptions
        {
            Markets = new List<MarketResourceRow> { Row(ItemCode, 5_000L) }
        });
        var service = new MarketService(catalog);
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);

        service.Open(client, MerchantHandle, MarketName).Should().BeTrue();
        connection.Sent.Should().ContainSingle("the window opened");

        service.Open(client, MerchantHandle, "deva_armor").Should().BeFalse();
        service.Open(client, 0u, MarketName).Should().BeFalse();
        connection.Sent.Should().ContainSingle("a market this server cannot resolve is never announced");
    }

    [Test]
    public void SelectingAMerchantTrigger_RemembersTheMarketForTheOpenDialog()
    {
        // The memory the whole lot hangs on: 250 carries no market name back to the server, so the
        // selection that opened it is what a later 251 is judged against.
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.NpcDialogHandle = MerchantHandle;
        info.NpcDialogTriggers.Add(Trigger);

        DialogService().Select(client, SelectionFrame(Trigger));

        connection.Sent.Should().ContainSingle();
        Id(connection.Sent[0]).Should().Be((ushort)GamePackets.TM_SC_MARKET);
        info.OpenMarketName.Should().Be(MarketName);
    }

    [Test]
    public void SelectingAMarketTheCatalogueDoesNotKnow_RemembersNothing()
    {
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.NpcDialogHandle = MerchantHandle;
        info.NpcDialogTriggers.Add("open_market(deva_armor)");

        DialogService().Select(client, SelectionFrame("open_market(deva_armor)"));

        connection.Sent.Should().BeEmpty();
        info.OpenMarketName.Should().BeEmpty("a window that was refused is not open");
    }

    [Test]
    public async Task AClosedDialog_ForgetsTheMarketAndRefusesThePurchase()
    {
        var service = Buyer();
        var (client, connection, info) = _buyer;

        info.ClearNpcDialog();

        info.OpenMarketName.Should().BeEmpty("the window is bound to the dialog that opened it");

        await service.BuyAsync(client, ItemCode, 1);

        connection.Sent.Should().ContainSingle();
        BinaryPrimitives.ReadUInt16LittleEndian(connection.Sent[0].AsSpan(9, 2)).Should().Be((ushort)ResultCode.Unknown);
        A.CallTo(() => _characters.AddItemAsync(A<string>._, A<int>._, A<long>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task TheReceiveLoop_BuysOnTheWire()
    {
        // The whole path, frame included: the shop window's 13 bytes go in and the three answers come out.
        var service = Buyer(gold: 20_000, price: 5_000L, huntaholicPoint: 4);
        var connection = new StorageTestHarness.FrameConnection(BuyerFrame(ItemCode, 2));
        var client = StorageTestHarness.NewGameClient(connection, marketTradeService: service);
        var info = StorageTestHarness.Session(client);
        info.CharacterName = "Buyer";
        info.CharacterGold = 20_000;
        info.CharacterChaos = 7;
        info.NpcDialogHandle = MerchantHandle;
        info.OpenMarketName = MarketName;

        client.OnDataReceived(GameTradePackets.BuyItemSize);

        StorageTestHarness.WaitFor(() => connection.Sent.Count >= 3);
        connection.Sent.Select(Id).Should().Equal(
            new[]
            {
                (ushort)GamePackets.TM_SC_GOLD_UPDATE,
                (ushort)GamePackets.TM_SC_RESULT,
                (ushort)GamePackets.TM_SC_NPC_TRADE_INFO
            });
        BinaryPrimitives.ReadUInt16LittleEndian(connection.Sent[1].AsSpan(9, 2)).Should().Be((ushort)ResultCode.Success);
        BinaryPrimitives.ReadInt64LittleEndian(connection.Sent[2].AsSpan(20, 8)).Should().Be(10_000L);
        info.CharacterGold.Should().Be(10_000);
        connection.BytesAvailable.Should().Be(0, "the frame was consumed whole");
    }

    private static byte[] BuyerFrame(int itemCode, ushort buyCount)
    {
        var packet = new byte[GameTradePackets.BuyItemSize];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_BUY_ITEM);
        packet[6] = StorageTestHarness.Checksum(packet);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(7, 4), itemCode);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(11, 2), buyCount);
        return packet;
    }
}
