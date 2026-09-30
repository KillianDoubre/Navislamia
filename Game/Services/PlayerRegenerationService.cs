using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;

namespace Navislamia.Game.Services;

public sealed class PlayerRegenerationService
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly IPlayerVisibilityService _players;
    private readonly IStatService _stats;
    private readonly ILogger<PlayerRegenerationService> _logger;

    public PlayerRegenerationService(IPlayerVisibilityService players, IStatService stats,
        ILogger<PlayerRegenerationService> logger)
    {
        _players = players;
        _stats = stats;
        _logger = logger;
    }

    public void Start() => _ = ExecuteAsync(_stopping.Token);

    public void Stop() => _stopping.Cancel();

    private async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                Tick();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public void Tick()
    {
        foreach (var client in _players.Registry.Clients)
        {
            try
            {
                var info = client.ConnectionInfo;
                if (info.CharacterHandle == 0 || info.CharacterHp <= 0) continue;

                var total = _stats.Compute(info).Total;
                var maxHp = Math.Max(1, (int)total.MaxHp);
                var maxMp = Math.Max(0, (int)total.MaxMp);
                var sitFactor = info.IsSitting ? 2 : 1;
                var hpGain = Gain(maxHp, total.HpRegenPoint, total.HpRegenPercentage, sitFactor);
                var mpGain = Gain(maxMp, total.MpRegenPoint, total.MpRegenPercentage, sitFactor);

                var hp = Math.Min(maxHp, info.CharacterHp + hpGain);
                var mp = Math.Min(maxMp, info.CharacterMp + mpGain);
                var actualHpGain = hp - info.CharacterHp;
                var actualMpGain = mp - info.CharacterMp;
                if (actualHpGain != 0 || actualMpGain != 0)
                {
                    info.CharacterHp = hp;
                    info.CharacterMp = mp;
                    _players.SendToObservers(client,
                        GameStatPackets.BuildRegenHpMp(info.CharacterHandle, actualHpGain, actualMpGain, hp, mp),
                        includeSelf: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Player regeneration tick failed");
            }
        }
    }

    public static int Gain(int maximum, float points, float percent, int sitFactor) =>
        Math.Max(1, (int)((maximum * percent / 100f + points) * 3f / 60f * sitFactor));
}
