using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Navislamia.Game.Services.Dungeons;
using Serilog;

namespace DevConsole;

public sealed class DungeonMaintenanceService : BackgroundService
{
    private readonly IDungeonService _dungeons;
    private readonly Navislamia.Game.Services.Guilds.IGuildService _guilds;
    public DungeonMaintenanceService(IDungeonService dungeons, Navislamia.Game.Services.Guilds.IGuildService guilds)
    { _dungeons = dungeons; _guilds = guilds; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        await _guilds.TickAsync();
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await _guilds.TickAsync(); await _dungeons.SweepAsync(); }
            catch (Exception error) { Log.Error(error, "Dungeon membership sweep failed"); }
        }
    }
}
