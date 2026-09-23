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
        if (_networkService.AuthorizedGameClients.IsEmpty)
        {
            return;
        }

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

        Dictionary<long, MoveOrder> moves = null;
        foreach (var id in activeIds)
        {
            if (_worldState.TryBeginWander(id, now, WalkSpeed, out var order))
            {
                (moves ??= new Dictionary<long, MoveOrder>())[id] = order;
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

    /// <summary>
    /// Sends each client the moves of the monsters it sees. Each client walks whichever of its visible set
    /// and the world's moves is smaller: every client used to scan every move of the world, so the cost
    /// grew with players times moving monsters even for players far apart.
    /// </summary>
    private static void Broadcast(List<GameClient> clients, Dictionary<long, MoveOrder> moves)
    {
        foreach (var client in clients)
        {
            var info = client.ConnectionInfo;

            lock (info.MonsterVisibilityLock)
            {
                var spawned = info.SpawnedMonsters;
                if (spawned.Count <= moves.Count)
                {
                    foreach (var (id, handle) in spawned)
                    {
                        if (moves.TryGetValue(id, out var order))
                        {
                            SendMove(client, info, handle, order);
                        }
                    }
                }
                else
                {
                    foreach (var (id, order) in moves)
                    {
                        if (spawned.TryGetValue(id, out var handle))
                        {
                            SendMove(client, info, handle, order);
                        }
                    }
                }
            }
        }
    }

    private static void SendMove(GameClient client, ConnectionInfo info, uint handle, MoveOrder order)
    {
        var startTime = unchecked(order.StartTick + info.ClientClockOffset);
        client.Connection.Send(GameMovePackets.BuildMove(handle, startTime, info.Layer, order.Speed,
            order.DestX, order.DestY));
    }
}
