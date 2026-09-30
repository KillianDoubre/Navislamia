using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Rates;
using Navislamia.Game.Services.Stats;
using Navislamia.Game.Services.Weight;

namespace Tests.Game;

[TestFixture]
public class WeightAndGameTimeTests
{
    private const int Potion = 603002;
    private const int Rock = 700001;

    [TearDown]
    public void TearDown() => WorldClock.OffsetSeconds = 0;

    [Test]
    public void WornItemsWeighNothing()
    {
        var items = new[]
        {
            new ItemEntity { ItemResourceId = Potion, Amount = 10, WearInfo = ItemWearType.None },
            new ItemEntity { ItemResourceId = Rock, Amount = 1, WearInfo = ItemWearType.Armor }
        };

        WeightRules.Carried(items, id => id == Potion ? 0.5f : 30f).Should().Be(5f);
    }

    [TestCase(74f, 100)]
    [TestCase(75f, 50)]
    [TestCase(99f, 50)]
    [TestCase(100f, 10)]
    [TestCase(-1f, 10)]
    public void TheLoadSlowsTheWalkAtThreeQuartersThenAtTheMaximum(float carried, int expected)
    {
        WeightRules.MoveSpeed(100, carried, 100f).Should().Be((byte)expected);
    }

    [Test]
    public void AnUnknownMaximumNeitherSlowsNorRefuses()
    {
        WeightRules.MoveSpeed(100, 1_000f, float.PositiveInfinity).Should().Be(100);
        WeightRules.Fits(1_000f, float.PositiveInfinity, 1_000f).Should().BeTrue();
    }

    [TestCase(90f, 100f, 10f, true)]
    [TestCase(90f, 100f, 11f, false)]
    public void AnAdditionFitsUpToTheMaximumIncluded(float carried, float max, float added, bool fits)
    {
        WeightRules.Fits(carried, max, added).Should().Be(fits);
    }

    [TestCase(50f, 100f, 40f, true)]
    [TestCase(70f, 100f, 40f, false)]
    [TestCase(110f, 100f, 0f, false)]
    public void TheBagComesOffOnlyIfTheLoadStillFitsWithoutIt(float carried, float max, float bag, bool allowed)
    {
        WeightRules.CanTakeOffBag(carried, max, bag).Should().Be(allowed);
    }

    [Test]
    public async Task TheCarriedWeightFollowsEveryAnnouncedChange()
    {
        var (service, client, characters, feed) = Service(maxWeight: 100);
        var info = StorageTestHarness.Session(client);
        service.Seed(info, new[] { new ItemEntity { ItemResourceId = Potion, Amount = 4, WearInfo = ItemWearType.None } });
        info.CarriedWeight.Should().Be(2f);

        A.CallTo(() => characters.GetCarriedItemsAsync("Ana")).Returns(new[]
        {
            new ItemEntity { ItemResourceId = Potion, Amount = 4, WearInfo = ItemWearType.None },
            new ItemEntity { ItemResourceId = Rock, Amount = 2, WearInfo = ItemWearType.None }
        });
        feed.Publish("Ana");
        await service.RefreshAsync(client);

        info.CarriedWeight.Should().Be(62f);
        service.CanCarry(info, Rock, 1).Should().BeTrue();
        service.CanCarry(info, Rock, 2).Should().BeFalse();
        service.MoveSpeed(info, 100).Should().Be(100);
        info.CarriedWeight = 80f;
        service.MoveSpeed(info, 100).Should().Be(50);
    }

    [Test]
    public void TheMoveEchoGoesAtTheSpeedTheLoadAllows()
    {
        var weights = A.Fake<ICarriedWeightService>();
        A.CallTo(() => weights.MoveSpeed(A<ConnectionInfo>._, ConnectionInfo.EchoedMoveSpeed)).Returns((byte)50);
        var frame = MoveRequest(7, (100f, 100f));
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection, carriedWeightService: weights);
        StorageTestHarness.Session(client).CharacterHandle = 7;

        client.OnDataReceived(frame.Length);

        var echo = connection.Sent.Single(sent => BinaryPrimitives.ReadUInt16LittleEndian(sent.AsSpan(4, 2))
                                                  == (ushort)GamePackets.TM_SC_MOVE);
        echo[16].Should().Be(50);
        StorageTestHarness.Session(client).MoveSpeed.Should().Be(50);
    }

    [Test]
    public async Task APickupTheBagCannotCarryIsRefusedTooHeavyAndStaysOnTheGround()
    {
        var (weights, client, characters, _) = Service(maxWeight: 100);
        var players = new PlayerVisibilityService(A.Fake<ILogger<PlayerVisibilityService>>());
        players.Registry.Register(1, client);
        var rates = new RateService(new StaticOptionsMonitor<RatesOptions>(new RatesOptions { EventStatePath = "" }));
        var ground = new GroundItemService(A.Fake<IMonsterDropCatalog>(), characters, A.Fake<IItemGroupCatalog>(),
            rates, players, weights);
        A.CallTo(() => characters.RemoveItemAsync("Ana", 9, A<Func<ItemEntity, long>>._))
            .Returns(new ItemRemoval(new ItemEntity { Id = 9, ItemResourceId = Rock, Amount = 2 }, 2));
        await ground.DropFromInventoryAsync(client, 9, 2);
        var sent = ((StorageTestHarness.FrameConnection)client.Connection).Sent;
        var handle = BinaryPrimitives.ReadUInt32LittleEndian(sent.First(frame =>
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)) == (ushort)GamePackets.TM_SC_ENTER).AsSpan(8, 4));
        StorageTestHarness.Session(client).CarriedWeight = 50f;
        sent.Clear();

        await ground.TakeAsync(client, handle);

        A.CallTo(() => characters.AddItemAsync(A<string>._, A<int>._, A<long>._)).MustNotHaveHappened();
        var result = sent.Single(frame => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2))
                                          == (ushort)GamePackets.TM_SC_RESULT);
        BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(9, 2)).Should().Be((ushort)ResultCode.TooHeavy);
    }

    [Test]
    public void TheGameTimeIsUnixTimeShiftedByTheOffset()
    {
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var before = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        client.SendGameTime();
        WorldClock.OffsetSeconds = 12 * 3600;
        client.SendGameTime();

        var after = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        connection.Sent.Should().HaveCount(2);
        connection.Sent[0].Length.Should().Be(19);
        BinaryPrimitives.ReadUInt16LittleEndian(connection.Sent[0].AsSpan(4, 2)).Should().Be(1101);
        BinaryPrimitives.ReadUInt64LittleEndian(connection.Sent[0].AsSpan(11, 8)).Should().BeInRange(before, after);
        BinaryPrimitives.ReadUInt64LittleEndian(connection.Sent[1].AsSpan(11, 8))
            .Should().BeInRange(before + 12 * 3600, after + 12 * 3600);
    }

    private static (CarriedWeightService Service, GameClient Client, ICharacterService Characters, InventoryChangeFeed Feed)
        Service(float maxWeight)
    {
        var catalog = A.Fake<IItemWeightCatalog>();
        A.CallTo(() => catalog.WeightOf(Potion)).Returns(0.5f);
        A.CallTo(() => catalog.WeightOf(Rock)).Returns(30f);
        var stats = A.Fake<IStatService>();
        A.CallTo(() => stats.Compute(A<ConnectionInfo>._))
            .Returns(new CharacterStatResult(new StatBlock { MaxWeight = maxWeight }, new StatBlock()));
        var characters = A.Fake<ICharacterService>();
        var players = new PlayerVisibilityService(A.Fake<ILogger<PlayerVisibilityService>>());
        var feed = new InventoryChangeFeed();
        var service = new CarriedWeightService(catalog, stats, characters, players, feed);
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()),
            characterService: characters, playerVisibilityService: players);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 1;
        info.CharacterName = "Ana";
        players.Registry.Register(1, client);
        return (service, client, characters, feed);
    }

    private static byte[] MoveRequest(uint handle, params (float X, float Y)[] points)
    {
        var frame = new byte[26 + points.Length * 8];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)frame.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), (ushort)GamePackets.TM_CS_MOVE_REQUEST);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(7, 4), handle);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(24, 2), (ushort)points.Length);
        for (var i = 0; i < points.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(26 + i * 8, 4), points[i].X);
            BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(30 + i * 8, 4), points[i].Y);
        }

        frame[6] = StorageTestHarness.Checksum(frame);
        return frame;
    }
}
