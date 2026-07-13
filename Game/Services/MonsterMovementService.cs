using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

public class MonsterMovementService
{
    private const int TickIntervalMs = 500;
    private const byte WalkSpeed = 25;

    private readonly ILogger _logger = Log.ForContext<MonsterMovementService>();
    private readonly MonsterWorldState _worldState;
    private readonly NetworkService _networkService;

    public MonsterMovementService(MonsterWorldState worldState, NetworkService networkService)
    {
        _worldState = worldState;
        _networkService = networkService;
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickIntervalMs));

        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                Tick(DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Monster movement tick failed");
            }
        }
    }

    private void Tick(DateTime now)
    {
        var clients = new List<GameClient>(_networkService.AuthorizedGameClients.Values);
        if (clients.Count == 0)
        {
            return;
        }

        var activeIds = CollectVisibleMonsters(clients);
        if (activeIds.Count == 0)
        {
            return;
        }

        List<(long Id, float X, float Y)> moves = null;
        foreach (var id in activeIds)
        {
            if (_worldState.TryBeginWander(id, now, out var destination))
            {
                (moves ??= new List<(long, float, float)>()).Add((id, destination.X, destination.Y));
            }
        }

        if (moves != null)
        {
            Broadcast(clients, moves);
        }
    }

    private static HashSet<long> CollectVisibleMonsters(List<GameClient> clients)
    {
        var activeIds = new HashSet<long>();

        foreach (var client in clients)
        {
            var info = client.ConnectionInfo;
            lock (info.MonsterVisibilityLock)
            {
                foreach (var id in info.SpawnedMonsters.Keys)
                {
                    activeIds.Add(id);
                }
            }
        }

        return activeIds;
    }

    private static void Broadcast(List<GameClient> clients, List<(long Id, float X, float Y)> moves)
    {
        foreach (var client in clients)
        {
            var info = client.ConnectionInfo;
            var startTime = unchecked((uint)Environment.TickCount + info.ClientClockOffset);

            lock (info.MonsterVisibilityLock)
            {
                foreach (var move in moves)
                {
                    if (info.SpawnedMonsters.TryGetValue(move.Id, out var handle))
                    {
                        client.Connection.Send(GameMovePackets.BuildMove(handle, startTime, info.Layer, WalkSpeed, move.X, move.Y));
                    }
                }
            }
        }
    }
}
