using System.Collections.Generic;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// The fold of the Arcadia <c>WorldLocation</c> table: one row per (id, weather_id, time_id), one in-memory
/// location per id, exactly as NGemity's <c>WorldLocationManager::RegisterWorldLocation</c> does it.
/// </summary>
[TestFixture]
public class WorldLocationServiceTests
{
    private static WorldLocationEntity Row(int id, int weatherId, int timeId, short weatherRatio,
        short locationType = 0, short weatherChangeTime = 0, int x = 0, int y = 0) => new()
    {
        Id = id,
        WeatherId = weatherId,
        TimeId = timeId,
        WeatherRatio = weatherRatio,
        LocationType = locationType,
        WeatherChangeTime = weatherChangeTime,
        X = x,
        Y = y
    };

    private static WorldLocationService BuildService(params WorldLocationEntity[] rows)
    {
        var repository = A.Fake<IWorldLocationRepository>();
        A.CallTo(() => repository.GetAll()).Returns(new List<WorldLocationEntity>(rows));

        return new WorldLocationService(repository);
    }

    [Test]
    public void RowsOfTheSameLocationFoldIntoASingleEntry()
    {
        var service = BuildService(
            Row(10, 0, 0, 100),
            Row(10, 0, 1, 60),
            Row(10, 1, 0, 40));

        service.Count.Should().Be(1);
        service.TryGet(10, out var location).Should().BeTrue();
        location.Id.Should().Be(10);
    }

    [Test]
    public void FirstRowOfALocationFixesLocationTypeAndWeatherChangeTime()
    {
        var service = BuildService(
            Row(10, 0, 0, 100, locationType: 3, weatherChangeTime: 60),
            Row(10, 0, 1, 60, locationType: 9, weatherChangeTime: 99));

        service.TryGet(10, out var location).Should().BeTrue();
        location.LocationType.Should().Be(3);
        location.WeatherChangeTime.Should().Be(60);
    }

    [Test]
    public void EveryRowFeedsTheWeatherRatioMatrixAtItsWeatherAndTime()
    {
        var service = BuildService(
            Row(10, 0, 0, 100),
            Row(10, 0, 3, 7),
            Row(10, 6, 2, 42));

        service.TryGet(10, out var location).Should().BeTrue();
        location.GetWeatherRatio(0, 0).Should().Be(100);
        location.GetWeatherRatio(0, 3).Should().Be(7);
        location.GetWeatherRatio(6, 2).Should().Be(42);
        location.GetWeatherRatio(0, 1).Should().Be(0);
    }

    [Test]
    public void WeatherRatioMatrixKeepsAllSevenWeathersAndFourTimes()
    {
        var rows = new List<WorldLocationEntity>();
        for (var weatherId = 0; weatherId < WorldLocationService.WeatherCount; weatherId++)
        {
            for (var timeId = 0; timeId < WorldLocationService.TimeCount; timeId++)
            {
                rows.Add(Row(10, weatherId, timeId, (short)(weatherId * 10 + timeId)));
            }
        }

        var service = BuildService(rows.ToArray());

        service.Count.Should().Be(1);
        service.TryGet(10, out var location).Should().BeTrue();
        for (var weatherId = 0; weatherId < WorldLocationService.WeatherCount; weatherId++)
        {
            for (var timeId = 0; timeId < WorldLocationService.TimeCount; timeId++)
            {
                location.GetWeatherRatio(weatherId, timeId).Should().Be((short)(weatherId * 10 + timeId));
            }
        }
    }

    [Test]
    public void RowOutsideTheMatrixIsIgnoredInsteadOfOverrunningIt()
    {
        var service = BuildService(
            Row(10, 0, 0, 100),
            Row(10, WorldLocationService.WeatherCount, 0, 55),
            Row(10, 0, WorldLocationService.TimeCount, 66));

        service.TryGet(10, out var location).Should().BeTrue();
        location.GetWeatherRatio(0, 0).Should().Be(100);
        location.GetWeatherRatio(WorldLocationService.WeatherCount - 1, 0).Should().Be(0);
        location.GetWeatherRatio(0, WorldLocationService.TimeCount - 1).Should().Be(0);
    }

    [Test]
    public void LocationsKeepTheirOwnRows()
    {
        var service = BuildService(
            Row(10, 0, 0, 100, locationType: 1, weatherChangeTime: 60, x: 3, y: 6),
            Row(11, 0, 0, 30, locationType: 2, weatherChangeTime: 90, x: 7, y: 8));

        service.Count.Should().Be(2);

        service.TryGet(10, out var first).Should().BeTrue();
        first.LocationType.Should().Be(1);
        first.WeatherChangeTime.Should().Be(60);
        first.X.Should().Be(3);
        first.Y.Should().Be(6);
        first.GetWeatherRatio(0, 0).Should().Be(100);

        service.TryGet(11, out var second).Should().BeTrue();
        second.LocationType.Should().Be(2);
        second.WeatherChangeTime.Should().Be(90);
        second.X.Should().Be(7);
        second.Y.Should().Be(8);
        second.GetWeatherRatio(0, 0).Should().Be(30);
    }

    [Test]
    public void UnknownLocationIsNotFound()
    {
        var service = BuildService(Row(10, 0, 0, 100));

        service.TryGet(11, out var location).Should().BeFalse();
        location.Should().BeNull();
    }

    [Test]
    public void EmptyTableYieldsNoLocation()
    {
        var service = BuildService();

        service.Count.Should().Be(0);
        service.TryGet(0, out var location).Should().BeFalse();
        location.Should().BeNull();
    }

    [Test]
    public void KnownLocationReportsWeatherZero()
    {
        // No reference ever assigns current_weather: NGemity only declares it and sends it, so a folded
        // location reports the same 0 rzu puts in the 902 every time.
        var service = BuildService(Row(10, 0, 0, 100));

        service.TryGet(10, out var location).Should().BeTrue();
        location.CurrentWeather.Should().Be(0);
    }
}
