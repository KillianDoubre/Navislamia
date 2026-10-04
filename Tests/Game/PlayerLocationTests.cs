using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.Maps;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Compete;
using Navislamia.Game.Services.Interfaces;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>StructPlayer::ChangeLocation and WorldLocationManager (docs/packet-specs/901-change-location.md).</summary>
[TestFixture]
public class PlayerLocationTests
{
    private const int Town = 10218;   // location_type 1
    private const int Field = 70813;  // location_type 2

    private sealed class Locations : IWorldLocationService
    {
        public readonly Dictionary<int, WorldLocation> Map = new();
        public int Count => Map.Count;
        public IEnumerable<WorldLocation> All => Map.Values;
        public bool TryGet(int locationId, out WorldLocation location) => Map.TryGetValue(locationId, out location);
    }

    private Locations _locations = null!;
    private IMapService _maps = null!;
    private IPkModeService _pk = null!;
    private ICompeteService _compete = null!;
    private PlayerRegistry _registry = null!;
    private DateTime _local = new(2026, 10, 4, 5, 0, 0);

    [SetUp]
    public void SetUp()
    {
        _locations = new Locations();
        var town = new WorldLocation(Town, 1, 1, 2, 60);
        town.TrySetWeatherRatio(0, PlayerLocationRules.TimeDaytime, 100);
        var field = new WorldLocation(Field, 2, 7, 8, 60);
        field.TrySetWeatherRatio(0, PlayerLocationRules.TimeDaytime, 30);
        field.TrySetWeatherRatio(2, PlayerLocationRules.TimeDaytime, 70);
        _locations.Map[Town] = town;
        _locations.Map[Field] = field;
        _maps = A.Fake<IMapService>();
        A.CallTo(() => _maps.GetLocationId(A<float>._, A<float>._)).ReturnsLazily((float x, float _) => x < 1000 ? Town : Field);
        _pk = A.Fake<IPkModeService>();
        _compete = A.Fake<ICompeteService>();
        _registry = new PlayerRegistry();
    }

    private PlayerLocationService Service(int roll = 50)
    {
        var players = A.Fake<IPlayerVisibilityService>();
        A.CallTo(() => players.Registry).Returns(_registry);
        return new PlayerLocationService(_maps, _locations, players, _pk, _compete, new FixedRandom(roll), () => _local,
            runTicks: false);
    }

    private sealed class FixedRandom : Random
    {
        private readonly int _value;
        public FixedRandom(int value) => _value = value;
        public override int Next(int minValue, int maxValue) => _value;
    }

    private (GameClient Client, StorageTestHarness.FrameConnection Connection) Player(uint handle, float x, float y)
    {
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = handle;
        info.CharacterName = "P" + handle;
        info.X = x;
        info.Y = y;
        _registry.Register(handle, client);
        return (client, connection);
    }

    private static ushort Id(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));

    private static (int Previous, int Current) ChangeOf(byte[] packet) => (
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(7, 4)), BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(11, 4)));

    private static (uint Region, ushort Weather) WeatherOf(byte[] packet) => (
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)), BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(11, 2)));

    [Test]
    public void ChangeLocation_IsFifteenBytes()
    {
        var packet = GameWeatherPackets.BuildChangeLocation(Town, Field);

        packet.Should().HaveCount(15);
        Id(packet).Should().Be((ushort)GamePackets.TM_SC_CHANGE_LOCATION);
        ChangeOf(packet).Should().Be((Town, Field));
        packet[6].Should().Be((byte)(packet[0] + packet[1] + packet[2] + packet[3] + packet[4] + packet[5]));
    }

    [TestCase(2, 29, PlayerLocationRules.TimeNight)]
    [TestCase(2, 30, PlayerLocationRules.TimeDawn)]
    [TestCase(3, 59, PlayerLocationRules.TimeDawn)]
    [TestCase(4, 0, PlayerLocationRules.TimeDaytime)]
    [TestCase(8, 29, PlayerLocationRules.TimeDaytime)]
    [TestCase(8, 30, PlayerLocationRules.TimeEvening)]
    [TestCase(9, 59, PlayerLocationRules.TimeEvening)]
    [TestCase(10, 0, PlayerLocationRules.TimeNight)]
    [TestCase(16, 0, PlayerLocationRules.TimeDaytime)]
    [TestCase(23, 0, PlayerLocationRules.TimeNight)]
    public void TimeIndex_IsGetCurrentTimeIdx(int hour, int minute, int expected) =>
        PlayerLocationRules.TimeIndex(new DateTime(2026, 10, 4, hour, minute, 0)).Should().Be(expected);

    [TestCase(0, 0)]
    [TestCase(29, 0)]
    [TestCase(30, 2)]
    [TestCase(99, 2)]
    public void Roll_WalksTheRatiosOfTheTimeSlot(int roll, int expected) =>
        PlayerLocationRules.Roll(_locations.Map[Field], PlayerLocationRules.TimeDaytime, roll, 5).Should().Be((ushort)expected);

    [Test]
    public void Roll_KeepsTheWeatherWhenTheSlotSumsBelowTheRoll() =>
        PlayerLocationRules.Roll(_locations.Map[Field], PlayerLocationRules.TimeNight, 10, 4).Should().Be(4);

    [Test]
    public void EnteringALocation_SendsTheChangeThenItsWeather_AndTheSameOneOnlyTheChange()
    {
        _locations.Map[Field].CurrentWeather = 2;
        var service = Service();
        var (client, connection) = Player(1, 5000, 5000);

        service.Refresh(client);
        service.Refresh(client);

        connection.Sent.Select(Id).Should().Equal((ushort)GamePackets.TM_SC_CHANGE_LOCATION,
            (ushort)GamePackets.TM_SC_WEATHER_INFO, (ushort)GamePackets.TM_SC_CHANGE_LOCATION);
        ChangeOf(connection.Sent[0]).Should().Be((0, Field));
        WeatherOf(connection.Sent[1]).Should().Be(((uint)Field, (ushort)2));
        ChangeOf(connection.Sent[2]).Should().Be((Field, Field), "the 901 goes out changed or not");
        StorageTestHarness.Session(client).LocationId.Should().Be(Field);
    }

    [Test]
    public void ARequestFarFromTheServersEstimate_UsesTheEstimate()
    {
        var service = Service();
        var (client, connection) = Player(1, 5000, 5000);

        service.ChangeByRequest(client, 500, 500);
        ChangeOf(connection.Sent[0]).Should().Be((0, Field), "500 is far beyond the 120-unit error range");

        connection.Sent.Clear();
        StorageTestHarness.Session(client).X = 1050;
        service.ChangeByRequest(client, 990, 5000);
        ChangeOf(connection.Sent[0]).Should().Be((Field, Town), "60 units off is within the error range");
    }

    [Test]
    public void EnteringATown_TurnsThePkModeOffAndEndsTheDuel_AFieldDoesNot()
    {
        var service = Service();
        var (client, _) = Player(1, 5000, 5000);

        service.Refresh(client);
        A.CallTo(() => _pk.LeavePkField(A<GameClient>._, A<bool>._)).MustNotHaveHappened();
        A.CallTo(() => _compete.Leave(A<GameClient>._, A<CompeteEndType>._)).MustNotHaveHappened();

        StorageTestHarness.Session(client).X = 100;
        service.Refresh(client);
        A.CallTo(() => _pk.LeavePkField(client, false)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _compete.Leave(client, CompeteEndType.LeftField)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public void TheWeatherIsRolledWhenDue_AndOnlyItsPlayersHearOfTheChange()
    {
        var service = Service(roll: 50);
        var (inField, fieldConnection) = Player(1, 5000, 5000);
        var (inTown, townConnection) = Player(2, 100, 100);
        service.Refresh(inField);
        service.Refresh(inTown);
        fieldConnection.Sent.Clear();
        townConnection.Sent.Clear();

        service.Tick(1000);

        _locations.Map[Field].CurrentWeather.Should().Be(2, "50 falls in the 70 % share of weather 2");
        fieldConnection.Sent.Select(WeatherOf).Should().Equal(((uint)Field, (ushort)2));
        townConnection.Sent.Should().BeEmpty("the town rolled clear again: no change, no broadcast");

        fieldConnection.Sent.Clear();
        service.Tick(1000 + 60 * 6000 - 1);
        _locations.Map[Field].LastWeatherChange.Should().Be(1000u, "60 minutes have not passed");
        service.Tick(1000 + 60 * 6000 + 1);
        _locations.Map[Field].LastWeatherChange.Should().Be(1000u + 60 * 6000 + 1);
    }

    [Test]
    public void ThePlayersLocationIsCheckedAgainEveryFiveMinutes()
    {
        var service = Service();
        var (client, connection) = Player(1, 5000, 5000);
        service.Refresh(client);
        var checkedAt = StorageTestHarness.Session(client).LastLocationCheck;
        connection.Sent.Clear();

        service.Tick(checkedAt + 1000);
        connection.Sent.Where(p => Id(p) == (ushort)GamePackets.TM_SC_CHANGE_LOCATION).Should().BeEmpty();

        service.Tick(checkedAt + PlayerLocationRules.PeriodicCheckTicks + 1);
        connection.Sent.Where(p => Id(p) == (ushort)GamePackets.TM_SC_CHANGE_LOCATION).Should().ContainSingle();
    }

    [Test]
    public void LeavingAPkField_SchedulesTheSwitchOff_OrCancelsAPendingSwitchOn()
    {
        var fields = A.Fake<IPkFieldService>();
        A.CallTo(() => fields.IsPkField(A<ConnectionInfo>._)).Returns(false);
        using var pk = new PkModeService(fields, A.Fake<ICombatService>(), runTicks: false);
        var (client, _) = Player(1, 100, 100);
        var info = StorageTestHarness.Session(client);

        pk.LeavePkField(client, immediate: false);
        info.TurnOffPkAt.Should().Be(0u, "the mode was off: nothing to do");

        info.PkMode = true;
        pk.LeavePkField(client, immediate: false);
        info.TurnOffPkAt.Should().NotBe(0u);
        unchecked((int)(info.TurnOffPkAt - ServerClock.Now)).Should().BeGreaterThan(2900, "30 s");

        info.TurnOffPkAt = 0;
        pk.LeavePkField(client, immediate: true);
        unchecked((int)(info.TurnOffPkAt - ServerClock.Now)).Should().BeLessThanOrEqualTo(0, "a deathmatch or arena: at once");

        info.PkMode = false;
        info.TurnOffPkAt = 0;
        info.TurnOnPkAt = ServerClock.Now + 500;
        pk.LeavePkField(client, immediate: false);
        info.TurnOnPkAt.Should().Be(0u, "TurnOffPkMode cancels a pending switch-on");
        info.TurnOffPkAt.Should().Be(0u);
    }
}
