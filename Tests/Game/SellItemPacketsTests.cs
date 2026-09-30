using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// <c>TM_CS_SELL_ITEM</c> (252): the trade window the merchant opened sends one fixed 13-byte frame per
/// sold line, and the branch that reads it must keep it away from the throwing switch.
/// See docs/packet-specs/252-sell-item.md.
/// </summary>
[TestFixture]
public class SellItemPacketTests
{
    private const int PacketLength = GameTradePackets.SellItemSize;
    private const int HeaderSize = 7;

    /// <summary>
    /// The frame as the 7.3 client writes it: the length in hard at 0, the id at 4, the header checksum
    /// at 6, then the two payload fields (<c>SFrame.exe</c> <c>0x48f410</c>).
    /// </summary>
    private static byte[] ClientFrame(uint itemHandle, ushort sellCount)
    {
        var packet = new byte[PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_SELL_ITEM);
        packet[6] = StorageTestHarness.Checksum(packet);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), itemHandle);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(HeaderSize + 4, 2), sellCount);
        return packet;
    }

    /// <summary>A frame that claims the given length and holds no more than the header.</summary>
    private static byte[] MalformedFrame(int length)
    {
        var packet = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_SELL_ITEM);
        packet[6] = StorageTestHarness.Checksum(packet);
        return packet;
    }

    private static ushort Id(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));

    [Test]
    public void Ids_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_CS_SELL_ITEM).Should().Be(252);

        // rzu remaps 252 to 1252 from EPIC_9_6_3 on, above EPIC_7_3: the 9.6.3 id must not be declared,
        // and 252 must be defined or OnDataReceived drops the frame as "Undefined packet ID" before any
        // dispatch.
        Enum.IsDefined(typeof(GamePackets), (ushort)1252).Should().BeFalse();
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_SELL_ITEM).Should().BeTrue();
    }

    [Test]
    public void ClientPacket_UsesTheThirteenByteLayout()
    {
        // handle is laid down by hand — 04 03 02 01 — so a big-endian read would give 0x04030201
        // (67305985) instead of the little-endian 0x01020304 (16909060), and sell_count — 02 01 — would
        // give 0x0201 (513) instead of the little-endian 0x0102 (258).
        var packet = ClientFrame(0x01020304, 0x0102);

        packet.Length.Should().Be(13, "7 bytes of header plus a 4-byte handle and a 2-byte count");
        Marshal.SizeOf<Header>().Should().Be(HeaderSize, "the frame's header is 7 bytes, no more");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(13,
            "the client writes the length in hard");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(252);
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
    public void TryReadSellItem_ReadsTheHandleAndTheCountAtTheirOffsets()
    {
        GameTradePackets.TryReadSellItem(ClientFrame(0x01020304, 0x0102), out var itemHandle, out var sellCount)
            .Should().BeTrue();

        itemHandle.Should().Be(0x01020304, "handle is a little endian uint32 at offset 7");
        itemHandle.Should().NotBe(0x04030201, "rzu writes the scalar uint32 as it stands on x86");
        sellCount.Should().Be(0x0102, "sell_count is a little endian uint16 at offset 11");
        sellCount.Should().NotBe((ushort)0x0201, "a byte swap would read the two count bytes backwards");
    }

    [Test]
    public void TryReadSellItem_ReadsRealisticValues()
    {
        GameTradePackets.TryReadSellItem(ClientFrame(0x80000123, 7), out var itemHandle, out var sellCount)
            .Should().BeTrue();

        itemHandle.Should().Be(0x80000123);
        sellCount.Should().Be(7);
    }

    [Test]
    public void TheFrameHasNoRoomForAFourthField()
    {
        // A shifted read is what this guards: a uint32 read two bytes past the handle field would fold the
        // count into the handle, which is exactly how a layout that moved by one field would look.
        var packet = ClientFrame(0x00010002, 3);

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(HeaderSize + 2, 4))
            .Should().NotBe(0x00010002, "the handle cannot be read two bytes late");

        // 7 + 4 + 2 is the whole frame: no byte is left for a padded field, and the count's two bytes are
        // the last two of the frame.
        GameTradePackets.SellItemSize.Should().Be(13);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(11, 2)).Should().Be(3);
    }

    [TestCase(0, TestName = "TryReadSellItem_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadSellItem_RejectsAHeaderOnlyFrame")]
    [TestCase(12, TestName = "TryReadSellItem_RejectsATruncatedFrame")]
    [TestCase(14, TestName = "TryReadSellItem_RejectsAPaddedFrame")]
    [TestCase(36, TestName = "TryReadSellItem_RejectsAFrameTheSizeOfTheEcho")]
    public void TryReadSellItem_RefusesAnyLengthOtherThanThirteen(int length)
    {
        var packet = new byte[length];
        if (length >= PacketLength)
        {
            ClientFrame(0x80000123, 3).CopyTo(packet, 0);
        }

        GameTradePackets.TryReadSellItem(packet, out var itemHandle, out var sellCount).Should().BeFalse();
        itemHandle.Should().Be(0);
        sellCount.Should().Be(0);
    }

    [Test]
    public void SellItem_IsDispatchedBeforeTheUnknownPacketThrow()
    {
        // GameClient's dispatch is a chain of ifs, so a member added to the enum without a branch reaches
        // the final switch and its `throw` kills the receive loop. Nothing smaller than a source scan can
        // check that without a live socket.
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "Game", "Network", "Clients", "GameClient.cs"));

        var branch = source.IndexOf($"GamePackets.{GamePackets.TM_CS_SELL_ITEM}", StringComparison.Ordinal);
        var finalSwitch = source.IndexOf("throw new Exception($\"Unknown Packet Type", StringComparison.Ordinal);

        branch.Should().BeGreaterThan(-1, "TM_CS_SELL_ITEM needs a branch of its own in OnDataReceived");
        finalSwitch.Should().BeGreaterThan(-1, "the final switch is the guard this test is about");
        branch.Should().BeLessThan(finalSwitch, "252 must be handled before the final switch throws");
    }

    [Test]
    public void OnDataReceived_ConsumesTheFrameWithoutThrowing()
    {
        var frame = ClientFrame(0x80000123, 1);
        var connection = new StorageTestHarness.FrameConnection(frame);
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

        var frames = ClientFrame(0x80000123, 1).Concat(keepalive).ToArray();
        var connection = new StorageTestHarness.FrameConnection(frames);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(frames.Length);

        receive.Should().NotThrow();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
    }

    [Test]
    public void OnDataReceived_HandsTheDecodedHandleAndCountToTheSellService()
    {
        // The wiring the offsets are worth nothing without: what the reader decoded is what the gesture is
        // asked to sell.
        var service = A.Fake<IMarketSellService>();
        var seenHandle = 0u;
        var seenCount = (ushort)0;
        A.CallTo(() => service.SellAsync(A<GameClient>._, A<uint>._, A<ushort>._))
            .Invokes(call =>
            {
                seenHandle = call.GetArgument<uint>(1);
                seenCount = call.GetArgument<ushort>(2);
            });

        var frame = ClientFrame(0x01020304, 0x0102);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection, marketSellService: service);

        client.OnDataReceived(frame.Length);

        StorageTestHarness.WaitFor(() => seenCount != 0);
        seenHandle.Should().Be(0x01020304, "the handle is read at offset 7");
        seenCount.Should().Be(0x0102, "the count is read at offset 11");
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
        BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(7, 2)).Should().Be(252);
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
/// The transaction side of the open window: what a sale pays, what it sends back, in which order, and
/// what it answers when it cannot be made. The reference is <c>WorldSession.cpp:1021-1072</c>; see
/// docs/packet-specs/252-sell-item.md §5.2.
/// </summary>
[TestFixture]
public class MarketSellTests
{
    private const int ItemCode = 603001;
    private const int BandCode = 602700;
    private const int OutOfScaleCode = 603099;
    private const uint ItemHandle = 0x80000234;
    private const uint MerchantHandle = 0x80000123;

    private IItemResourceRepository _items = null!;
    private ICharacterService _characters = null!;

    private (GameClient Client, StorageTestHarness.FrameConnection Connection, ConnectionInfo Info) _seller;

    [SetUp]
    public void SetUp()
    {
        _items = A.Fake<IItemResourceRepository>();
        A.CallTo(() => _items.GetSellPriceFields()).Returns(new List<ItemSellFields>
        {
            new(ItemCode, 2, 5_000),
            new(BandCode, 0, 1_000),
            new(OutOfScaleCode, 9, 1_000)
        });

        _characters = A.Fake<ICharacterService>();
        A.CallTo(() => _characters.ConsumeItemAsync(A<string>._, A<uint>._, A<long>._)).Returns(3L);
    }

    /// <summary>The item the merchant is about to buy back, as the store hands it over.</summary>
    private void Item(int code, long amount = 5, uint level = 1, ItemWearType wear = ItemWearType.None,
        int? storageId = null, int? summonedBy = null)
    {
        A.CallTo(() => _characters.GetItemByHandleAsync("Seller", ItemHandle)).Returns(new ItemEntity
        {
            Id = 3,
            ItemResourceId = code,
            Amount = amount,
            Level = level,
            WearInfo = wear,
            StorageId = storageId,
            EquippedBySummonId = summonedBy
        });
    }

    /// <summary>A client whose only usable part is its connection and the session behind it.</summary>
    private MarketSellService Seller(long gold = 1_000, int chaos = 7, uint dialogHandle = MerchantHandle)
    {
        var service = new MarketSellService(_characters, new ItemSellCatalog(_items));
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection, marketSellService: service);
        var info = StorageTestHarness.Session(client);
        info.CharacterName = "Seller";
        info.CharacterGold = gold;
        info.CharacterChaos = chaos;
        info.NpcDialogHandle = dialogHandle;
        _seller = (client, connection, info);
        return service;
    }

    private static ushort Id(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));

    private static ushort ResultOf(byte[] packet) =>
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(9, 2));

    [Test]
    public async Task Sell_PaysTheUnitPriceTimesTheSoldQuantityAndAnswersInOrder()
    {
        Item(ItemCode, amount: 5, level: 1);
        var service = Seller(gold: 1_000);
        var (client, connection, info) = _seller;

        await service.SellAsync(client, ItemHandle, 2);

        // The units leave the bag first (the erase is what makes the client drop them), then the gold, then
        // the acknowledgement, then the transaction echo.
        connection.Sent.Select(Id).Should().Equal(
            new[]
            {
                (ushort)GamePackets.TM_SC_UPDATE_ITEM_COUNT,
                (ushort)GamePackets.TM_SC_GOLD_UPDATE,
                (ushort)GamePackets.TM_SC_RESULT,
                (ushort)GamePackets.TM_SC_NPC_TRADE_INFO
            });

        var stack = connection.Sent[0];
        stack.Length.Should().Be(19, "7 bytes of header plus a 4-byte handle and an 8-byte count");
        BinaryPrimitives.ReadUInt32LittleEndian(stack.AsSpan(7, 4)).Should().Be(ItemHandle);
        BinaryPrimitives.ReadInt64LittleEndian(stack.AsSpan(11, 8)).Should().Be(3L, "5 units minus the 2 sold");

        // 5000 gold of rank 2 at level 1 is worth 1250 (a quarter of its price), so two units pay 2500.
        info.CharacterGold.Should().Be(3_500);
        var gold = connection.Sent[1];
        gold.Length.Should().Be(19);
        BinaryPrimitives.ReadUInt64LittleEndian(gold.AsSpan(7, 8)).Should().Be(3_500UL,
            "the price is the whole amount, not the unit price");
        BinaryPrimitives.ReadUInt32LittleEndian(gold.AsSpan(15, 4)).Should().Be(7U, "chaos is echoed as it stands");

        var result = connection.Sent[2];
        result.Length.Should().Be(15, "7 bytes of header plus request_msg_id, result and value");
        BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(7, 2)).Should().Be(252,
            "the request is the one being answered");
        ResultOf(result).Should().Be((ushort)ResultCode.Success);
        BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(11, 4)).Should().Be(unchecked((int)ItemHandle));

        var echo = connection.Sent[3];
        echo.Length.Should().Be(36);
        echo[7].Should().Be(1, "is_sell is 1 on a sale");
        BinaryPrimitives.ReadInt32LittleEndian(echo.AsSpan(8, 4)).Should().Be(ItemCode);
        BinaryPrimitives.ReadInt64LittleEndian(echo.AsSpan(12, 8)).Should().Be(2L, "count is the sold quantity");
        BinaryPrimitives.ReadInt64LittleEndian(echo.AsSpan(20, 8)).Should().Be(2_500L, "price is the total");
        BinaryPrimitives.ReadInt32LittleEndian(echo.AsSpan(28, 4)).Should().Be(0,
            "the reference never fills huntaholic_point on a sale");
        BinaryPrimitives.ReadUInt32LittleEndian(echo.AsSpan(32, 4)).Should().Be(MerchantHandle,
            "target is the merchant the window belongs to");

        A.CallTo(() => _characters.ConsumeItemAsync("Seller", ItemHandle, 2)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task Sell_PaysTheWholePriceInsideTheBuyingPriceBand()
    {
        Item(BandCode, amount: 1);
        A.CallTo(() => _characters.ConsumeItemAsync("Seller", ItemHandle, 1)).Returns(0L);
        var service = Seller(gold: 0);
        var (client, connection, info) = _seller;

        await service.SellAsync(client, ItemHandle, 1);

        info.CharacterGold.Should().Be(1_000, "602700 is inside 602700-602799 and pays its full price");
        var echo = connection.Sent[^1];
        BinaryPrimitives.ReadInt64LittleEndian(echo.AsSpan(20, 8)).Should().Be(1_000L);
        BinaryPrimitives.ReadUInt32LittleEndian(echo.AsSpan(32, 4)).Should().Be(MerchantHandle);
    }

    [Test]
    public async Task Sell_DestroysTheStackWhenItsLastUnitGoes()
    {
        Item(ItemCode, amount: 2);
        A.CallTo(() => _characters.ConsumeItemAsync("Seller", ItemHandle, 2)).Returns(0L);
        var service = Seller();
        var (client, connection, _) = _seller;

        await service.SellAsync(client, ItemHandle, 2);

        connection.Sent.Select(Id).Should().Equal(
            new[]
            {
                (ushort)GamePackets.TM_SC_DESTROY_ITEM,
                (ushort)GamePackets.TM_SC_GOLD_UPDATE,
                (ushort)GamePackets.TM_SC_RESULT,
                (ushort)GamePackets.TM_SC_NPC_TRADE_INFO
            },
            "an emptied stack is destroyed rather than left at zero");
        connection.Sent[0].Length.Should().Be(11, "7 bytes of header plus the handle");
        BinaryPrimitives.ReadUInt32LittleEndian(connection.Sent[0].AsSpan(7, 4)).Should().Be(ItemHandle);
    }

    [Test]
    public async Task Sell_EchoesTheMerchantTheSessionRemembers()
    {
        Item(ItemCode, amount: 3);
        var service = Seller(dialogHandle: 0xDEADBEEF);
        var (client, connection, _) = _seller;

        await service.SellAsync(client, ItemHandle, 1);

        BinaryPrimitives.ReadUInt32LittleEndian(connection.Sent[^1].AsSpan(32, 4)).Should().Be(0xDEADBEEF);
    }

    [Test]
    public async Task Sell_RefusesACountOfZero()
    {
        Item(ItemCode);
        var service = Seller();
        var (client, connection, info) = _seller;

        await service.SellAsync(client, ItemHandle, 0);

        connection.Sent.Should().ContainSingle("the reference answers Unknown (7) when sell_count is 0");
        ResultOf(connection.Sent[0]).Should().Be((ushort)ResultCode.Unknown);
        BinaryPrimitives.ReadInt32LittleEndian(connection.Sent[0].AsSpan(11, 4)).Should().Be(0);
        info.CharacterGold.Should().Be(1_000);
        A.CallTo(() => _characters.ConsumeItemAsync(A<string>._, A<uint>._, A<long>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task Sell_RefusesAHandleThatResolvesToNoItem()
    {
        A.CallTo(() => _characters.GetItemByHandleAsync("Seller", ItemHandle)).Returns((ItemEntity)null!);
        var service = Seller();
        var (client, connection, info) = _seller;

        await service.SellAsync(client, ItemHandle, 1);

        connection.Sent.Should().ContainSingle("the reference answers NotExist (1) with value 0");
        ResultOf(connection.Sent[0]).Should().Be((ushort)ResultCode.NotExist);
        BinaryPrimitives.ReadInt32LittleEndian(connection.Sent[0].AsSpan(11, 4)).Should().Be(0);
        info.CharacterGold.Should().Be(1_000);
        A.CallTo(() => _characters.ConsumeItemAsync(A<string>._, A<uint>._, A<long>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task Sell_RefusesAnItemTheCatalogCannotPrice()
    {
        Item(603999, amount: 1);
        var service = Seller();
        var (client, connection, _) = _seller;

        await service.SellAsync(client, ItemHandle, 1);

        connection.Sent.Should().ContainSingle("no item resource means no price, refused with value 0");
        ResultOf(connection.Sent[0]).Should().Be((ushort)ResultCode.NotExist);
        BinaryPrimitives.ReadInt32LittleEndian(connection.Sent[0].AsSpan(11, 4)).Should().Be(0);
        A.CallTo(() => _characters.ConsumeItemAsync(A<string>._, A<uint>._, A<long>._)).MustNotHaveHappened();
    }

    [TestCase("worn", TestName = "Sell_RefusesAWornItem")]
    [TestCase("stored", TestName = "Sell_RefusesAStoredItem")]
    [TestCase("summon", TestName = "Sell_RefusesAnItemEquippedByASummon")]
    public async Task Sell_RefusesAnItemThatIsNotInTheBag(string kind)
    {
        // The demonstrable subset of Player::IsSellable (Player.cpp:3158-3166, itself IsErasable :3136).
        Item(ItemCode, amount: 1,
            wear: kind == "worn" ? ItemWearType.Weapon : ItemWearType.None,
            storageId: kind == "stored" ? 1 : null,
            summonedBy: kind == "summon" ? 4 : null);
        var service = Seller();
        var (client, connection, info) = _seller;

        await service.SellAsync(client, ItemHandle, 1);

        connection.Sent.Should().ContainSingle("the reference answers NotExist (1) with the item code");
        ResultOf(connection.Sent[0]).Should().Be((ushort)ResultCode.NotExist);
        BinaryPrimitives.ReadInt32LittleEndian(connection.Sent[0].AsSpan(11, 4)).Should().Be(ItemCode);
        info.CharacterGold.Should().Be(1_000);
        A.CallTo(() => _characters.ConsumeItemAsync(A<string>._, A<uint>._, A<long>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task Sell_RefusesMoreUnitsThanTheStackHolds()
    {
        Item(ItemCode, amount: 1);
        var service = Seller();
        var (client, connection, _) = _seller;

        await service.SellAsync(client, ItemHandle, 2);

        connection.Sent.Should().ContainSingle("the reference refuses a negative remainder with the item code");
        ResultOf(connection.Sent[0]).Should().Be((ushort)ResultCode.NotExist);
        BinaryPrimitives.ReadInt32LittleEndian(connection.Sent[0].AsSpan(11, 4)).Should().Be(ItemCode);
        A.CallTo(() => _characters.ConsumeItemAsync(A<string>._, A<uint>._, A<long>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task Sell_RefusesAnItemWhoseRankIsOutsideThePriceScale()
    {
        // The reference indexes its own scale out of bounds for such a rank (GameContent.cpp:200, :214): no
        // price can be reproduced, so the sale is refused with the code a non-sellable item gets. The
        // conduct is reserved (docs/packet-specs/252-sell-item.md A VERIFIER 6).
        Item(OutOfScaleCode, amount: 1);
        var service = Seller();
        var (client, connection, info) = _seller;

        await service.SellAsync(client, ItemHandle, 1);

        connection.Sent.Should().ContainSingle();
        ResultOf(connection.Sent[0]).Should().Be((ushort)ResultCode.NotExist);
        BinaryPrimitives.ReadInt32LittleEndian(connection.Sent[0].AsSpan(11, 4)).Should().Be(OutOfScaleCode);
        info.CharacterGold.Should().Be(1_000);
        A.CallTo(() => _characters.ConsumeItemAsync(A<string>._, A<uint>._, A<long>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task Sell_RefusesWhenTheEraseFails()
    {
        Item(ItemCode, amount: 5);
        A.CallTo(() => _characters.ConsumeItemAsync("Seller", ItemHandle, 2)).Returns((long?)null);
        var service = Seller();
        var (client, connection, info) = _seller;

        await service.SellAsync(client, ItemHandle, 2);

        connection.Sent.Should().ContainSingle("a failed erase pays nothing");
        ResultOf(connection.Sent[0]).Should().Be((ushort)ResultCode.NotActable);
        BinaryPrimitives.ReadInt32LittleEndian(connection.Sent[0].AsSpan(11, 4))
            .Should().Be(unchecked((int)ItemHandle), "the reference answers the handle it could not erase");
        info.CharacterGold.Should().Be(1_000);
    }

    [Test]
    public async Task Sell_AnswersADeadStoreWithTheDepotCode()
    {
        A.CallTo(() => _characters.GetItemByHandleAsync("Seller", ItemHandle))
            .Throws(new InvalidOperationException("no store"));
        var service = Seller();
        var (client, connection, info) = _seller;

        await service.SellAsync(client, ItemHandle, 1);

        connection.Sent.Should().ContainSingle();
        ResultOf(connection.Sent[0]).Should().Be((ushort)ResultCode.DBError);
        info.CharacterGold.Should().Be(1_000);
    }

    [Test]
    public async Task Sell_RefusesAPurseThatCannotTakeTheGold()
    {
        // Ported from the reference's own overflow guard (:1050-1053): on this repository's int64 gold it
        // only fires on an overflow, and it refuses with NotActable (5) and the item code.
        Item(ItemCode, amount: 1);
        var service = Seller(gold: long.MaxValue);
        var (client, connection, info) = _seller;

        await service.SellAsync(client, ItemHandle, 1);

        connection.Sent.Should().ContainSingle();
        ResultOf(connection.Sent[0]).Should().Be((ushort)ResultCode.NotActable);
        BinaryPrimitives.ReadInt32LittleEndian(connection.Sent[0].AsSpan(11, 4)).Should().Be(ItemCode);
        info.CharacterGold.Should().Be(long.MaxValue, "nothing was paid");
        A.CallTo(() => _characters.ConsumeItemAsync(A<string>._, A<uint>._, A<long>._)).MustNotHaveHappened();
    }
}

/// <summary>
/// The price rule itself, without a client or a store: the scale of
/// <c>GameContent::GetItemSellPrice</c> (<c>GameContent.cpp:195-220</c>) and the truncation it applies on
/// every turn of its loop. docs/packet-specs/252-sell-item.md §5.3.
/// </summary>
[TestFixture]
public class MarketSellPriceTests
{
    [Test]
    public void SellPrice_TruncatesEveryIncrementLikeTheReference()
    {
        // The emulation of §5.3: for price 2, rank 2 and level 5, each of the four turns adds
        // (2 * 0.4f * 0.01f) * 100f, which is 0.80000001 and truncates to 0, so the total stays 2 and a
        // quarter of it truncates to 0. Adding the four turns up before truncating — the other reading of
        // the reference, and the reason this rule is still reserved — would give 5.2 and 1 here.
        MarketSellPrice.TryComputeUnitPrice(2, 2, 5, false, out var unitPrice).Should().BeTrue();
        unitPrice.Should().Be(0);

        MarketSellPrice.TryComputeUnitPrice(2, 2, 5, true, out var bought).Should().BeTrue();
        bought.Should().Be(2, "the buying band pays the whole total, whose increments are still 0");
    }

    [Test]
    public void SellPrice_LeavesALevelOneItemAtItsOwnPrice()
    {
        // lv = 1 runs the loop zero times, whatever the rank: the price is the base, a quarter of it sold.
        MarketSellPrice.TryComputeUnitPrice(1_000, 0, 1, false, out var low).Should().BeTrue();
        MarketSellPrice.TryComputeUnitPrice(1_000, 8, 1, false, out var high).Should().BeTrue();

        low.Should().Be(250);
        high.Should().Be(250);
    }

    [TestCase(0, 3_625, TestName = "SellPrice_Rank0_KeepsTheHighestFactor")]
    [TestCase(1, 3_625, TestName = "SellPrice_Rank1_SharesTheRank0Factor")]
    [TestCase(2, 1_250, TestName = "SellPrice_Rank2_UsesFourTenths")]
    [TestCase(3, 750, TestName = "SellPrice_Rank3_UsesTwoTenths")]
    [TestCase(4, 575, TestName = "SellPrice_Rank4_UsesThirteenHundredths")]
    [TestCase(5, 500, TestName = "SellPrice_Rank5_UsesOneTenth")]
    [TestCase(6, 500, TestName = "SellPrice_Rank6_UsesOneTenth")]
    [TestCase(7, 500, TestName = "SellPrice_Rank7_UsesOneTenth")]
    [TestCase(8, 500, TestName = "SellPrice_Rank8_UsesOneTenth")]
    public void SellPrice_KeepsTheReferenceScale(int rank, long expected)
    {
        // Price 1000 at level 11: the price plus ten increments (the loop runs from level 2 to 11), then a
        // quarter of the total. The scale is f[rank - 1], and rank 0 and rank 1 both read f[0] = 1.35.
        MarketSellPrice.TryComputeUnitPrice(1_000, rank, 11, false, out var unitPrice).Should().BeTrue();

        unitPrice.Should().Be(expected);
    }

    [TestCase(-1)]
    [TestCase(9)]
    [TestCase(1_000)]
    public void SellPrice_RefusesARankTheScaleDoesNotCover(int rank)
    {
        MarketSellPrice.TryComputeUnitPrice(1_000, rank, 11, false, out var unitPrice).Should().BeFalse();
        unitPrice.Should().Be(0);
    }

    [TestCase(602699, false, TestName = "SellPrice_CodeBelowTheBand_IsSoldAtAQuarter")]
    [TestCase(602700, true, TestName = "SellPrice_FirstCodeOfTheBand_IsSoldAtFullPrice")]
    [TestCase(602799, true, TestName = "SellPrice_LastCodeOfTheBand_IsSoldAtFullPrice")]
    [TestCase(602800, false, TestName = "SellPrice_CodeAboveTheBand_IsSoldAtAQuarter")]
    public void SellPrice_KnowsTheBuyingPriceBand(int code, bool expected)
    {
        MarketSellPrice.IsSamePriceForBuying(code).Should().Be(expected);
    }
}
