using System;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Services.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// The PK mode switch of the official server (<c>onTurnOnPkMode</c>/<c>onTurnOffPkMode</c>,
/// <c>StructPlayer::TurnOnPkMode</c>/<c>TurnOffPkMode</c> and the deadlines its <c>onProcess</c> applies): the
/// mode goes on <see cref="OnDelayTicks"/> after a request made in a PK field and off <see cref="OffDelayTicks"/>
/// after its request; asking again during the wait cancels it (docs/packet-specs/socle-pk-compte-a-rebours.md).
/// </summary>
public interface IPkModeService
{
    /// <summary>800: the answer for the request's <c>TS_SC_RESULT</c>.</summary>
    ResultCode RequestOn(GameClient client);

    /// <summary>801: the answer for the request's <c>TS_SC_RESULT</c>.</summary>
    ResultCode RequestOff(GameClient client);

    /// <summary>The deadlines reached at <paramref name="now"/> take effect.</summary>
    void Process(GameClient client, uint now);

    /// <summary>
    /// <c>StructPlayer::ChangeLocation</c>: entering a place without PK turns the mode off — at once in a
    /// deathmatch or an arena, after the usual 30 s elsewhere (<c>TurnOffPkMode</c>).
    /// </summary>
    void LeavePkField(GameClient client, bool immediate) { }
}

public static class PkModeRules
{
    /// <summary><c>GameRule::PK_ON_TIME</c>: 10 seconds.</summary>
    public const uint OnDelayTicks = 1000;

    /// <summary><c>GameRule::PK_OFF_TIME</c>: 30 seconds.</summary>
    public const uint OffDelayTicks = 3000;

    /// <summary>
    /// <c>StructPlayer::TurnOnPkMode</c>: a pending switch-off is cancelled (true), a pending switch-on is cancelled
    /// (false), otherwise the switch-on is set <see cref="OnDelayTicks"/> ahead (true).
    /// </summary>
    public static bool TurnOn(ref uint turnOnAt, ref uint turnOffAt, uint now)
    {
        if (turnOffAt != 0)
        {
            turnOffAt = 0;
            return true;
        }

        if (turnOnAt != 0)
        {
            turnOnAt = 0;
            return false;
        }

        turnOnAt = Deadline(now, OnDelayTicks);
        return true;
    }

    /// <summary>
    /// <c>StructPlayer::TurnOffPkMode</c>: a pending switch-on is cancelled (true), a pending switch-off stays
    /// (false), otherwise the switch-off is set <see cref="OffDelayTicks"/> ahead (true).
    /// </summary>
    public static bool TurnOff(ref uint turnOnAt, ref uint turnOffAt, uint now)
    {
        if (turnOnAt != 0)
        {
            turnOnAt = 0;
            return true;
        }

        if (turnOffAt != 0)
        {
            return false;
        }

        turnOffAt = Deadline(now, OffDelayTicks);
        return true;
    }

    /// <summary>A deadline is never 0, which means "none".</summary>
    private static uint Deadline(uint now, uint delay)
    {
        var at = unchecked(now + delay);
        return at == 0 ? 1 : at;
    }

    public static bool Reached(uint deadline, uint now) => deadline != 0 && unchecked((int)(now - deadline)) >= 0;
}

public sealed class PkModeService : IPkModeService, IDisposable
{
    private readonly ILogger _logger = Log.ForContext<PkModeService>();
    private readonly IPkFieldService _fields;
    private readonly ICombatService _combat;
    private readonly IPlayerVisibilityService _players;
    private readonly CancellationTokenSource _stop = new();

    public PkModeService(IPkFieldService fields, ICombatService combat, IPlayerVisibilityService players = null,
        bool runTicks = true)
    {
        _fields = fields;
        _combat = combat;
        _players = players;
        if (runTicks && players is not null)
        {
            _ = RunAsync();
        }
    }

    public ResultCode RequestOn(GameClient client)
    {
        var info = client.ConnectionInfo;
        if (!_fields.IsPkField(info))
        {
            return ResultCode.NotActable;
        }

        lock (info.PkModeLock)
        {
            var on = info.TurnOnPkAt;
            var off = info.TurnOffPkAt;
            var accepted = PkModeRules.TurnOn(ref on, ref off, ServerClock.Now);
            info.TurnOnPkAt = on;
            info.TurnOffPkAt = off;
            if (!accepted)
            {
                // onTurnOnPkMode: a second 800 during the countdown cancels it, and the status goes again (a Bloody
                // name would otherwise turn white on the client).
                client.SendActorStatus();
            }

            return accepted ? ResultCode.Success : ResultCode.NotActable;
        }
    }

    public ResultCode RequestOff(GameClient client)
    {
        var info = client.ConnectionInfo;
        lock (info.PkModeLock)
        {
            var on = info.TurnOnPkAt;
            var off = info.TurnOffPkAt;
            var accepted = PkModeRules.TurnOff(ref on, ref off, ServerClock.Now);
            info.TurnOnPkAt = on;
            info.TurnOffPkAt = off;
            return accepted ? ResultCode.Success : ResultCode.NotActable;
        }
    }

    public void LeavePkField(GameClient client, bool immediate)
    {
        var info = client.ConnectionInfo;
        // GameRules:PkFieldsEverywhere (a debugging option) makes every place a PK field.
        if (_fields.IsPkField(info))
        {
            return;
        }

        lock (info.PkModeLock)
        {
            // ( IsPKOning() || ( IsPKOn() && !IsPKOffing() ) ) && !IsInPKField()
            if (info.TurnOnPkAt == 0 && !(info.PkMode && info.TurnOffPkAt == 0))
            {
                return;
            }

            var now = ServerClock.Now;
            if (immediate && info.TurnOnPkAt == 0)
            {
                info.TurnOffPkAt = now == 0 ? 1u : now;
                return;
            }

            // TurnOffPkMode( false ): a pending switch-on is cancelled, otherwise the switch-off is 30 s ahead.
            var on = info.TurnOnPkAt;
            var off = info.TurnOffPkAt;
            PkModeRules.TurnOff(ref on, ref off, now);
            info.TurnOnPkAt = on;
            info.TurnOffPkAt = off;
        }
    }

    public void Process(GameClient client, uint now)
    {
        var info = client.ConnectionInfo;
        bool switchOff, switchOn;
        lock (info.PkModeLock)
        {
            switchOff = PkModeRules.Reached(info.TurnOffPkAt, now);
            if (switchOff)
            {
                info.TurnOffPkAt = 0;
            }

            switchOn = PkModeRules.Reached(info.TurnOnPkAt, now);
            if (switchOn)
            {
                info.TurnOnPkAt = 0;
            }
        }

        if (switchOff)
        {
            info.PkMode = false;
            client.SendActorStatus();
        }

        if (switchOn)
        {
            // The 5 points of immorality (0 on a PK server) are paid when the mode really goes on.
            if (!info.PkMode)
            {
                _combat.OnPkEnabled(client);
            }

            info.PkMode = true;
            client.SendActorStatus();
        }
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                try
                {
                    var now = ServerClock.Now;
                    foreach (var client in _players.Registry?.Clients ?? Array.Empty<GameClient>())
                    {
                        if (client.ConnectionInfo.TurnOnPkAt != 0 || client.ConnectionInfo.TurnOffPkAt != 0)
                        {
                            Process(client, now);
                        }
                    }
                }
                catch (Exception exception)
                {
                    _logger.Error(exception, "The PK mode tick failed");
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
