using System.Buffers.Binary;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Pets;

namespace Tests.Game;

/// <summary>
/// What a pet does once out (socle-familier-pet.md §17): it follows its master's destination, goes for its
/// master's loot within its collect range, and is brought back when it falls out of the client view.
/// </summary>
[TestFixture]
public class PetBehaviorTests
{
    private const uint PetHandle = 0x40000010;
    private const uint Cage = 55;

    private IGroundItemService _groundItems = null!;
    private IPetSummonService _petSummon = null!;
    private PetBehavior _behavior = null!;

    [SetUp]
    public void SetUp()
    {
        _groundItems = A.Fake<IGroundItemService>();
        _petSummon = A.Fake<IPetSummonService>();
        _behavior = new PetBehavior(_groundItems, _petSummon);
    }

    private static (GameClient Client, StorageTestHarness.FrameConnection Connection) NewMaster(float collectRange = 0)
    {
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 0x80000001;
        info.X = info.DestinationX = 1000;
        info.Y = info.DestinationY = 1000;
        info.ActivePet = new ActivePet(PetHandle, Cage, new PetWorldEntry { X = 1000, Y = 1000 }, collectRange);
        return (client, connection);
    }

    private static (float X, float Y) MoveTarget(byte[] move) =>
        (BinaryPrimitives.ReadSingleLittleEndian(move.AsSpan(19, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(move.AsSpan(23, 4)));

    private static ushort Id(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));

    [Test]
    public void APetBesideItsMaster_StaysPut()
    {
        var (client, connection) = NewMaster();

        _behavior.Step(client, 1000);

        connection.Sent.Should().BeEmpty();
    }

    [Test]
    public void AMasterWalkingAway_IsFollowedToTwoMetersShortOfItsDestination()
    {
        var (client, connection) = NewMaster();
        StorageTestHarness.Session(client).DestinationX = 1200;

        _behavior.Step(client, 1000);

        var move = connection.Sent.Single();
        Id(move).Should().Be((ushort)GamePackets.TM_SC_MOVE);
        BinaryPrimitives.ReadUInt32LittleEndian(move.AsSpan(11, 4)).Should().Be(PetHandle);
        move[16].Should().Be(PetSummonDefaults.MoveSpeed);
        var (x, y) = MoveTarget(move);
        x.Should().BeApproximately(1200 - PetSummonDefaults.FollowGap, 0.01f, "it trails on its own side");
        y.Should().BeApproximately(1000, 0.01f);
    }

    [Test]
    public void AFollowInFlight_IsNotReissuedUntilTheDestinationDrifts()
    {
        var (client, connection) = NewMaster();
        var info = StorageTestHarness.Session(client);
        info.DestinationX = 1200;
        _behavior.Step(client, 1000);
        connection.Sent.Clear();

        _behavior.Step(client, 1010);
        connection.Sent.Should().BeEmpty("a fresh move every tick would make the pet stutter");

        info.DestinationY = 1300;
        _behavior.Step(client, 1020);
        connection.Sent.Should().ContainSingle("its master turned");
    }

    [Test]
    public void APetOutOfView_IsRecalledBesideItsMaster()
    {
        var (client, connection) = NewMaster();
        StorageTestHarness.Session(client).DestinationX = 1000 + PetSummonDefaults.RecallDistance + 1;

        _behavior.Step(client, 1000);

        A.CallTo(() => _petSummon.Recall(client)).MustHaveHappenedOnceExactly();
        connection.Sent.Should().BeEmpty("the recall sends the frames, not the tick");
    }

    [Test]
    public void ACollectingPet_WalksToTheNearestLootThenTakesIt()
    {
        var (client, connection) = NewMaster(collectRange: 60);
        var spot = new GroundItemSpot(0x40000099, 1030, 1000);
        A.CallTo(() => _groundItems.TryFindNearest(client, 1000, 1000, 0, 60, out spot)).Returns(true)
            .AssignsOutAndRefParameters(spot);

        _behavior.Step(client, 1000);

        MoveTarget(connection.Sent.Single()).Should().Be((1030f, 1000f));
        StorageTestHarness.Session(client).ActivePet!.PickupTarget.Should().Be(spot.Handle);

        _behavior.Step(client, 1001);
        A.CallTo(() => _groundItems.TakeForPetAsync(A<GameClient>._, A<uint>._, A<uint>._)).MustNotHaveHappened();

        _behavior.Step(client, 5000);
        A.CallTo(() => _groundItems.TakeForPetAsync(client, spot.Handle, PetHandle)).MustHaveHappenedOnceExactly();
        StorageTestHarness.Session(client).ActivePet!.PickupTarget.Should().Be(0u);
    }

    [Test]
    public void APetWithoutCollectRange_NeverLooksForLoot()
    {
        var (client, _) = NewMaster(collectRange: 0);

        _behavior.Step(client, 1000);

        GroundItemSpot ignored;
        A.CallTo(() => _groundItems.TryFindNearest(A<GameClient>._, A<float>._, A<float>._, A<byte>._, A<float>._,
            out ignored)).WithAnyArguments().MustNotHaveHappened();
    }

    [Test]
    public void NoPet_NothingHappens()
    {
        var (client, connection) = NewMaster();
        StorageTestHarness.Session(client).ActivePet = null;

        _behavior.Step(client, 1000);

        connection.Sent.Should().BeEmpty();
    }

    [Test]
    public void FollowTarget_StopsShortOnThePetsSide()
    {
        PetSummonRules.FollowTarget(0, 0, 10, 0).Should().BeNull("within 3 m the pet stays");
        var target = PetSummonRules.FollowTarget(0, 0, 100, 0)!.Value;
        target.X.Should().BeApproximately(100 - PetSummonDefaults.FollowGap, 0.001f);
        target.Y.Should().Be(0);
    }
}
