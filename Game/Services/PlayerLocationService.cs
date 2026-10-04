using System;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.Maps;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// The location a player stands in and the weather of each location (docs/packet-specs/901-change-location.md):
/// <c>StructPlayer::ChangeLocation</c> and <c>WorldLocationManager</c> of the official server.
/// </summary>
public interface IPlayerLocationService
{
    /// <summary>
    /// <c>TM_CS_CHANGE_LOCATION</c> (900): the client's position is kept when it is within
    /// <see cref="PlayerLocationRules.ErrorRange"/> of the server's estimate, the estimate otherwise.
    /// </summary>
    void ChangeByRequest(GameClient client, float x, float y);

    /// <summary>World entry, warp end, periodic check: the location at the session's own position.</summary>
    void Refresh(GameClient client);
}

/// <summary>The pure rules of the location and weather cycle.</summary>
public static class PlayerLocationRules
{
    /// <summary><c>GameRule::CHANGE_LOCATION_ERROR_RANGE</c> = 10 × <c>DEFAULT_UNIT_SIZE</c> (12).</summary>
    public const float ErrorRange = 120f;

    /// <summary>The location is checked again with each save, every 30 000 ticks (<c>StructPlayer::onProcess</c>).</summary>
    public const uint PeriodicCheckTicks = 30000;

    public const int TimeDawn = 0, TimeDaytime = 1, TimeEvening = 2, TimeNight = 3;

    /// <summary>
    /// <c>GetCurrentTimeIdx</c>: a 12-hour cycle of local time — 2:30 dawn, 4:00 day, 8:30 evening, 10:00 night.
    /// </summary>
    public static int TimeIndex(DateTime local)
    {
        var hour = local.Hour >= 12 ? local.Hour - 12 : local.Hour;
        var minute = local.Minute;
        if (hour >= 4 && (hour < 8 || (hour == 8 && minute < 30))) return TimeDaytime;
        if (hour < 4 && (hour > 2 || (hour == 2 && minute >= 30))) return TimeDawn;
        if (hour < 10 && (hour > 8 || (hour == 8 && minute >= 30))) return TimeEvening;
        return TimeNight;
    }

    /// <summary>
    /// <c>WorldLocationManager::onProcess</c>: a roll of 0..99 walks the ratios of the time slot; the weather whose
    /// share contains it wins, and a slot whose ratios sum under the roll keeps <paramref name="current"/>.
    /// </summary>
    public static ushort Roll(WorldLocation location, int timeIndex, int roll, ushort current)
    {
        for (var weather = 0; weather < WorldLocationService.WeatherCount; weather++)
        {
            var ratio = location.GetWeatherRatio(weather, timeIndex);
            if (roll < ratio)
            {
                return (ushort)weather;
            }

            roll -= ratio;
        }

        return current;
    }

    /// <summary>The roll is due when it never ran or <c>last_changed_time + weather_change_time &lt; t</c>.</summary>
    public static bool WeatherDue(WorldLocation location, uint now) =>
        location.LastWeatherChange == 0 || unchecked((int)(location.LastWeatherChange + location.WeatherChangeTicks - now)) < 0;

    /// <summary><c>IsInField() || IsInBattleField()</c>: a duel may go on only in a field (2) or a battle field (5).</summary>
    public static bool KeepsCompete(short locationType) => locationType is 2 or 5;

    /// <summary>A deathmatch (11) or battle arena (15) turns the PK mode off at once, any other place after 30 s.</summary>
    public static bool ImmediatePkOff(short locationType) => locationType is 11 or 15;
}

public sealed class PlayerLocationService : IPlayerLocationService, IDisposable
{
    private readonly ILogger _logger = Log.ForContext<PlayerLocationService>();
    private readonly IMapService _maps;
    private readonly IWorldLocationService _locations;
    private readonly IPlayerVisibilityService _players;
    private readonly IPkModeService _pkMode;
    private readonly Compete.ICompeteService _compete;
    private readonly Random _random;
    private readonly Func<DateTime> _localTime;
    private readonly CancellationTokenSource _stop = new();

    public PlayerLocationService(IMapService maps, IWorldLocationService locations, IPlayerVisibilityService players,
        IPkModeService pkMode = null, Compete.ICompeteService compete = null)
        : this(maps, locations, players, pkMode, compete, null, null, runTicks: true)
    {
    }

    public PlayerLocationService(IMapService maps, IWorldLocationService locations, IPlayerVisibilityService players,
        IPkModeService pkMode, Compete.ICompeteService compete, Random random, Func<DateTime> localTime, bool runTicks)
    {
        _maps = maps;
        _locations = locations;
        _players = players;
        _pkMode = pkMode;
        _compete = compete;
        _random = random ?? Random.Shared;
        // The weather follows the world's clock, /gametime shift included, in the server's time zone like the
        // official localtime_s.
        _localTime = localTime ?? (() => DateTimeOffset.FromUnixTimeSeconds((long)WorldClock.GameTimeNow).LocalDateTime);
        if (runTicks)
        {
            _ = RunAsync();
        }
    }

    public void ChangeByRequest(GameClient client, float x, float y)
    {
        var info = client.ConnectionInfo;
        if (info.CharacterHandle == 0)
        {
            return;
        }

        var (serverX, serverY) = Buffs.SkillCastRangeRules.PlayerPosition(info, ServerClock.Now);
        var dx = x - serverX;
        var dy = y - serverY;
        var trusted = dx * dx + dy * dy < PlayerLocationRules.ErrorRange * PlayerLocationRules.ErrorRange;
        Change(client, trusted ? x : serverX, trusted ? y : serverY);
    }

    public void Refresh(GameClient client)
    {
        var info = client.ConnectionInfo;
        if (info.CharacterHandle == 0)
        {
            return;
        }

        var (x, y) = Buffs.SkillCastRangeRules.PlayerPosition(info, ServerClock.Now);
        Change(client, x, y);
    }

    /// <summary><c>StructPlayer::ChangeLocation</c>.</summary>
    public void Change(GameClient client, float x, float y)
    {
        var info = client.ConnectionInfo;
        var locationId = _maps.GetLocationId(x, y);
        int previous;
        lock (info.LocationLock)
        {
            previous = info.LocationId;
            info.LocationId = locationId;
            info.LastLocationCheck = ServerClock.Now;
        }

        // Sent every time, changed or not.
        client.Connection.Send(GameWeatherPackets.BuildChangeLocation(previous, locationId));
        if (previous == locationId || locationId == 0 || !_locations.TryGet(locationId, out var location))
        {
            return;
        }

        // AddToLocation: the new place's weather.
        client.Connection.Send(GameWeatherPackets.BuildWeatherInfo((uint)locationId, location.CurrentWeather));

        if (!PkFieldService.AllowsPk(locationId, location.LocationType))
        {
            _pkMode?.LeavePkField(client, PlayerLocationRules.ImmediatePkOff(location.LocationType));
        }

        if (!PlayerLocationRules.KeepsCompete(location.LocationType))
        {
            // COMPETE_END_BY_ENTERING_SAFETY_ZONE.
            _compete?.Leave(client, Compete.CompeteEndType.LeftField);
        }
    }

    /// <summary>One pass of <c>WorldLocationManager::onProcess</c> and of the players' periodic location check.</summary>
    public void Tick(uint now)
    {
        var timeIndex = PlayerLocationRules.TimeIndex(_localTime());
        foreach (var location in _locations.All)
        {
            if (!PlayerLocationRules.WeatherDue(location, now))
            {
                continue;
            }

            location.LastWeatherChange = now == 0 ? 1u : now;
            var before = location.CurrentWeather;
            var after = PlayerLocationRules.Roll(location, timeIndex, _random.Next(0, 100), before);
            if (after == before)
            {
                continue;
            }

            location.CurrentWeather = after;
            var frame = GameWeatherPackets.BuildWeatherInfo((uint)location.Id, after);
            foreach (var client in _players.Registry?.Clients ?? Array.Empty<GameClient>())
            {
                if (client.ConnectionInfo.LocationId == location.Id)
                {
                    client.Connection.Send(frame);
                }
            }
        }

        foreach (var client in _players.Registry?.Clients ?? Array.Empty<GameClient>())
        {
            var info = client.ConnectionInfo;
            if (info.CharacterHandle != 0
                && unchecked((int)(now - info.LastLocationCheck)) > (int)PlayerLocationRules.PeriodicCheckTicks)
            {
                Refresh(client);
            }
        }
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                try
                {
                    Tick(ServerClock.Now);
                }
                catch (Exception exception)
                {
                    _logger.Error(exception, "The location tick failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
