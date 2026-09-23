using System.Buffers.Binary;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
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
    private GroundItemService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _characters = A.Fake<ICharacterService>();
        var rates = new RateService(new StaticOptionsMonitor<RatesOptions>(new RatesOptions { EventStatePath = "" }));
        _service = new GroundItemService(A.Fake<IMonsterDropCatalog>(), _characters, A.Fake<IItemGroupCatalog>(),
            rates);
    }

    private (GameClient Client, StorageTestHarness.FrameConnection Connection) NewMaster(string name)
    {
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 0x80000001;
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
        var (other, _) = NewMaster("Other");
        var handle = await DropOne(master, masterConnection);

        _service.TryFindNearest(master, 1030, 1000, 0, 60, out var spot).Should().BeTrue();
        spot.Handle.Should().Be(handle);

        _service.TryFindNearest(master, 1100, 1000, 0, 60, out _).Should().BeFalse("100 units is beyond 5 m");
        _service.TryFindNearest(master, 1000, 1000, 1, 60, out _).Should().BeFalse("another layer");
        _service.TryFindNearest(other, 1000, 1000, 0, 60, out _).Should().BeFalse("the loot is its killer's alone");
    }

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
