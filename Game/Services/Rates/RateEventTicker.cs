using System;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.Network;
using Navislamia.Game.Services.GmCommands;
using Serilog;

namespace Navislamia.Game.Services.Rates;

/// <summary>
/// Announces the end of a rate event, and its reminder, when nobody types a command: a one-second loop
/// asks <see cref="IRateService.Tick"/> for what is due and broadcasts it. It holds
/// <see cref="NetworkService"/> only for the client list, exactly like <c>MonsterAiService</c>; the rate
/// service itself must not, because the combat and drop services it serves are injected into
/// <see cref="NetworkService"/>, which would close a DI cycle that only fails at runtime.
/// </summary>
public class RateEventTicker
{
    private const int TickIntervalMs = 1000;

    private readonly ILogger _logger = Log.ForContext<RateEventTicker>();
    private readonly IRateService _rateService;
    private readonly NetworkService _networkService;

    public RateEventTicker(IRateService rateService, NetworkService networkService)
    {
        _rateService = rateService;
        _networkService = networkService;
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickIntervalMs));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                foreach (var notice in _rateService.Tick())
                {
                    GmCommandService.Broadcast(GmCommandService.SystemSender, notice,
                        _networkService.AuthorizedGameClients.Values);
                }
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "The rate event tick failed");
            }
        }
    }
}
