using System.Buffers.Binary;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Pets;
using Navislamia.Game.Services.Rates;

namespace Tests.Game;

/// <summary>
/// A pet takes its master's loot through the manual pickup (socle-familier-pet.md §17): the item is claimed
/// and added, <c>TS_SC_TAKE_ITEM_RESULT</c> names the pet as the actor to animate, and no
/// <c>TS_SC_RESULT</c> answers a <c>TM_CS_TAKE_ITEM</c> that was never sent.
/// </summary>
[TestFixture]
public class PetPickupTests
{
    private const uint PetHandle = 0x40000010;

    private ICharacterService _characters = null!;
    private IItemUseCatalog _itemTypes = null!;
    private GroundItemService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _characters = A.Fake<ICharacterService>();
        _itemTypes = A.Fake<IItemUseCatalog>();
        var consumable = new ItemUseFields(603002, 0, 0, ItemBaseType.Supply);
        A.CallTo(() => _itemTypes.TryGetUseFields(603002, out consumable)).Returns(true)
            .AssignsOutAndRefParameters(consumable);
        var rates = new RateService(new StaticOptionsMonitor<RatesOptions>(new RatesOptions { EventStatePath = "" }));
        _service = new GroundItemService(A.Fake<IMonsterDropCatalog>(), _characters, A.Fake<IItemGroupCatalog>(),
            rates, A.Fake<Navislamia.Game.Services.Interfaces.IPlayerVisibilityService>(), itemTypes: _itemTypes);
    }

    private (GameClient Client, StorageTestHarness.FrameConnection Connection) NewMaster(string name,
        uint handle = 0x80000001)
    {
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = handle;
        info.CharacterName = name;
        info.X = 1000;
        info.Y = 1000;
        return (client, connection);
    }

    /// <summary>Puts one item on the ground at the master's feet, the way the player drops one (203).</summary>
    private async Task<uint> DropOne(GameClient client, StorageTestHarness.FrameConnection connection)
    {
        A.CallTo(() => _characters.RemoveItemAsync(A<string>._, A<uint>._, A<Func<ItemEntity, long>>._))
            .Returns(new ItemRemoval(new ItemEntity { Id = 7, ItemResourceId = 603002, Amount = 1 }, 1));
        await _service.DropFromInventoryAsync(client, 7, 1);

        var enter = connection.Sent.First(packet =>
            BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)) == (ushort)GamePackets.TM_SC_ENTER);
        connection.Sent.Clear();
        return BinaryPrimitives.ReadUInt32LittleEndian(enter.AsSpan(8, 4));
    }

    private static ushort Id(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));

    [Test]
    public async Task TryFindNearest_SeesOnlyTheMastersLootWithinRangeOnItsLayer()
    {
        var (master, masterConnection) = NewMaster("Master");
        // Two players never share a handle: the entry's hPlayer[0] is what names the killer on the wire, so
        // a distinct handle is what makes "somebody else" somebody else.
        var (other, _) = NewMaster("Other", 0x80000002);
        var handle = await DropOne(master, masterConnection);

        _service.TryFindNearest(master, 1030, 1000, 0, 60, out var spot).Should().BeTrue();
        spot.Handle.Should().Be(handle);

        _service.TryFindNearest(master, 1100, 1000, 0, 60, out _).Should().BeFalse("100 units is beyond 5 m");
        _service.TryFindNearest(master, 1000, 1000, 1, 60, out _).Should().BeFalse("another layer");
        _service.TryFindNearest(other, 1000, 1000, 0, 60, out _).Should().BeTrue(
            "an object a player drops has an empty pick_up_order (only monsters set one): open to every pet");
    }

    /// <summary>
    /// Only a monster's drop gets a pick_up_order (<c>StructMonster::SetPickupOrder</c>): what a player drops
    /// goes out with an empty order, which the client shows open to all and anybody takes at once.
    /// </summary>
    [Test]
    public async Task A_dropped_object_has_an_empty_order_and_anybody_takes_it_at_once()
    {
        var (master, masterConnection) = NewMaster("Master");
        A.CallTo(() => _characters.RemoveItemAsync(A<string>._, A<uint>._, A<Func<ItemEntity, long>>._))
            .Returns(new ItemRemoval(new ItemEntity { Id = 7, ItemResourceId = 603002, Amount = 1 }, 1));
        await _service.DropFromInventoryAsync(master, 7, 1);
        var enter = masterConnection.Sent.First(packet => Id(packet) == (ushort)GamePackets.TM_SC_ENTER);
        BinaryPrimitives.ReadUInt32LittleEndian(enter.AsSpan(46, 4)).Should().Be(0u, "hPlayer[0]: no order");
        var handle = BinaryPrimitives.ReadUInt32LittleEndian(enter.AsSpan(8, 4));

        var (other, otherConnection) = NewMaster("Other", 0x80000002);
        A.CallTo(() => _characters.AddItemAsync("Other", 603002, 1))
            .Returns(new ItemEntity { Id = 9, ItemResourceId = 603002, Amount = 1 });
        await _service.TakeAsync(other, handle);

        otherConnection.Sent.Should().Contain(packet => Id(packet) == (ushort)GamePackets.TM_SC_TAKE_ITEM_RESULT);
    }

    /// <summary>The client's gather (SFrame.exe @0x473ea3): each bit is one item type, 0x3f is "All".</summary>
    [TestCase(PetPickupFilter.Consumable, ItemBaseType.Supply, true)]
    [TestCase(PetPickupFilter.Soulstone, ItemBaseType.Soulstone, true)]
    [TestCase(PetPickupFilter.Cube, ItemBaseType.Cube, true)]
    [TestCase(PetPickupFilter.Card, ItemBaseType.Card, true)]
    [TestCase(PetPickupFilter.Gear, ItemBaseType.Armor, true)]
    [TestCase(PetPickupFilter.Etc, ItemBaseType.Etc, true)]
    [TestCase(PetPickupFilter.Gear, ItemBaseType.Supply, false)]
    [TestCase(PetPickupFilter.Default, ItemBaseType.Etc, false)]
    [TestCase(PetPickupFilter.Default, ItemBaseType.Charm, false)]
    [TestCase(0x3eu, ItemBaseType.Charm, false)]
    [TestCase(PetPickupFilter.All, ItemBaseType.Charm, true)]
    [TestCase(0u, ItemBaseType.Use, true)]
    public void PetPickupFilter_FollowsTheClientGather(uint filter, ItemBaseType type, bool collected) =>
        PetPickupFilter.Collects(filter, type).Should().Be(collected);

    [Test]
    public void PetPickupFilter_CollectsGoldWhateverTheFilter() =>
        PetPickupFilter.Collects(0, null).Should().BeTrue("gold has no client record (NON ÉTABLI)");

    [Test]
    public async Task TryFindNearest_SkipsTheTypesTheFilterExcludes()
    {
        var (master, masterConnection) = NewMaster("Master");
        await DropOne(master, masterConnection);
        StorageTestHarness.Session(master).PetPickupFilter.Should().Be(PetPickupFilter.Default);

        StorageTestHarness.Session(master).PetPickupFilter = PetPickupFilter.Gear | PetPickupFilter.Card;
        _service.TryFindNearest(master, 1000, 1000, 0, 60, out _).Should().BeFalse("a consumable, filtered out");

        StorageTestHarness.Session(master).PetPickupFilter = PetPickupFilter.Consumable;
        _service.TryFindNearest(master, 1000, 1000, 0, 60, out _).Should().BeTrue();
    }

    /// <summary>
    /// <c>onTakeItem</c>: a 204 naming the summoned collecting pet is the pet's take, judged from where the pet
    /// stands and animated on it; the client's own gather sends exactly that.
    /// </summary>
    [Test]
    public async Task TakeItem_NamingTheCollectingPet_IsJudgedFromThePetAndAnimatesIt()
    {
        var (master, connection) = NewMaster("Master");
        var handle = await DropOne(master, connection);
        var info = StorageTestHarness.Session(master);
        info.X = 1500;
        A.CallTo(() => _characters.AddItemAsync("Master", 603002, 1))
            .Returns(new ItemEntity { Id = 8, ItemResourceId = 603002, Amount = 1 });

        await _service.TakeAsync(master, info.CharacterHandle, handle);
        Code(connection.Sent.Last()).Should().Be((ushort)ResultCode.TooFar, "the player stands 500 units away");

        info.ActivePet = new ActivePet(PetHandle, 1, new PetWorldEntry { X = 1010, Y = 1000 }, collectRange: 60);
        connection.Sent.Clear();
        await _service.TakeAsync(master, PetHandle, handle);

        var result = connection.Sent.First(packet => Id(packet) == (ushort)GamePackets.TM_SC_TAKE_ITEM_RESULT);
        BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(11, 4)).Should().Be(PetHandle);
        Code(connection.Sent.Last()).Should().Be((ushort)ResultCode.Success);
    }

    [Test]
    public async Task TakeItem_NamingAPetThatDoesNotCollect_IsThePlayersTake()
    {
        var (master, connection) = NewMaster("Master");
        var handle = await DropOne(master, connection);
        var info = StorageTestHarness.Session(master);
        info.X = 1500;
        info.ActivePet = new ActivePet(PetHandle, 1, new PetWorldEntry { X = 1010, Y = 1000 });

        await _service.TakeAsync(master, PetHandle, handle);

        Code(connection.Sent.Last()).Should().Be((ushort)ResultCode.TooFar);
    }

    private static ushort Code(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(9, 2));

    [Test]
    public async Task TakeForPet_AnimatesThePetAndAnswersNoRequest()
    {
        var (master, connection) = NewMaster("Master");
        var handle = await DropOne(master, connection);
        A.CallTo(() => _characters.AddItemAsync("Master", 603002, 1))
            .Returns(new ItemEntity { Id = 8, ItemResourceId = 603002, Amount = 1 });

        (await _service.TakeForPetAsync(master, handle, PetHandle)).Should().BeTrue();

        connection.Sent.Select(Id).Should().StartWith(new[]
        {
            (ushort)GamePackets.TM_SC_TAKE_ITEM_RESULT, (ushort)GamePackets.TM_SC_LEAVE
        });
        connection.Sent.Select(Id).Should().NotContain((ushort)GamePackets.TM_SC_RESULT,
            "no TM_CS_TAKE_ITEM was sent, so nothing is tagged with it");
        var result = connection.Sent[0];
        BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(7, 4)).Should().Be(handle);
        BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(11, 4)).Should().Be(PetHandle,
            "item_taker tells the client which actor plays the pick-up animation");

        (await _service.TakeForPetAsync(master, handle, PetHandle)).Should().BeFalse("the item is gone");
    }
}
