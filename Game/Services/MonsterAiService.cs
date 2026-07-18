using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// Drives monster aggression: aggressive monsters acquire a player on sight, every monster chases and
/// attacks its aggro target, and combat damage lands on the player. The player-facing half of combat;
/// damage to the monster, death and respawn stay in <see cref="CombatService"/>.
/// </summary>
/// <remarks>
/// Structured like <see cref="MonsterMovementService"/>: a periodic tick over the authorized clients,
/// reading player position from <c>ConnectionInfo</c> and monster position from
/// <c>MonsterWorldState</c>. It holds <c>NetworkService</c> only for the client list, the same way the
/// movement service does, and never reaches back into it for anything else.
/// </remarks>
public class MonsterAiService
{
    private const int TickIntervalMs = 300;
    private const byte ChaseSpeed = 40;

    /// <summary>Dropped monsters walk home at twice the chase speed.</summary>
    private const byte ReturnSpeed = ChaseSpeed * 2;

    private const ushort AttackSpeedMs = 1200;
    private const uint AttackIntervalTicks = 120;

    /// <summary>
    /// A chase move is only re-issued when the desired destination has drifted this far from the one
    /// already in flight — otherwise the client would get a fresh move every tick and stutter.
    /// </summary>
    private const float ChaseReissueThreshold = 60f;

    private readonly ILogger _logger = Log.ForContext<MonsterAiService>();
    private readonly MonsterWorldState _worldState;
    private readonly NetworkService _networkService;

    public MonsterAiService(MonsterWorldState worldState, NetworkService networkService)
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
                _logger.Error(ex, "Monster AI tick failed");
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

        Acquire(clients);
        Act();
    }

    /// <summary>An aggressive monster with no target takes a player it can see and is close to.</summary>
    private void Acquire(List<GameClient> clients)
    {
        foreach (var client in clients)
        {
            var info = client.ConnectionInfo;

            List<long> visible;
            lock (info.MonsterVisibilityLock)
            {
                if (info.SpawnedMonsters.Count == 0)
                {
                    continue;
                }

                visible = new List<long>(info.SpawnedMonsters.Keys);
            }

            foreach (var instanceId in visible)
            {
                if (_worldState.TryGetAggro(instanceId, out _, out _)
                    || !_worldState.IsAlive(instanceId)
                    || !_worldState.TryGetInstance(instanceId, out var instance)
                    || !instance.FirstAttack)
                {
                    continue;
                }

                var (mx, my) = _worldState.GetPosition(instanceId);
                var action = MonsterAiRules.Decide(false, true, true, false,
                    mx, my, instance.X, instance.Y, info.X, info.Y,
                    instance.VisibleRange, instance.ChaseRange, Reach(instance));

                if (action == MonsterAiAction.Acquire)
                {
                    _worldState.SetAggro(instanceId, client);
                }
            }
        }
    }

    /// <summary>Chases, attacks or drops every monster currently in combat.</summary>
    private void Act()
    {
        foreach (var (instanceId, enemy, nextAttackTick) in _worldState.SnapshotAggro())
        {
            if (enemy is null || !_worldState.IsAlive(instanceId)
                || !_worldState.TryGetInstance(instanceId, out var instance))
            {
                _worldState.ClearAggro(instanceId);
                continue;
            }

            var info = enemy.ConnectionInfo;

            uint handle;
            bool streamed;
            lock (info.MonsterVisibilityLock)
            {
                streamed = info.SpawnedMonsters.TryGetValue(instanceId, out handle);
            }

            var (mx, my) = _worldState.GetPosition(instanceId);
            var now = ServerClock.Now;
            var reach = Reach(instance);

            var action = MonsterAiRules.Decide(true, instance.FirstAttack, streamed,
                unchecked((int)(now - nextAttackTick)) >= 0,
                mx, my, instance.X, instance.Y, info.X, info.Y,
                instance.VisibleRange, instance.ChaseRange, reach);

            switch (action)
            {
                case MonsterAiAction.Chase:
                    Chase(enemy, instanceId, handle, mx, my, info, reach);
                    break;
                case MonsterAiAction.Attack:
                    Attack(enemy, instanceId, handle, info, now);
                    break;
                case MonsterAiAction.Drop:
                    GoHome(enemy, instanceId, handle, info, streamed);
                    break;
            }
        }
    }

    private static float Reach(MonsterInstance instance) =>
        CombatRange.MeleeReach(instance.AttackRange, instance.Size, instance.Scale);

    private void Chase(GameClient client, long instanceId, uint handle, float mx, float my,
        ConnectionInfo info, float reach)
    {
        var (x, y) = MonsterAiRules.ChaseStep(mx, my, info.X, info.Y, reach);

        // Let a move already heading to about the same spot play out rather than restarting the
        // client animation every tick.
        if (_worldState.IsMoving(instanceId)
            && _worldState.TryGetMoveDestination(instanceId, out var cx, out var cy)
            && CombatRange.Distance(x, y, cx, cy) < ChaseReissueThreshold)
        {
            return;
        }

        Broadcast(client, handle, info, _worldState.BeginMove(instanceId, x, y, ChaseSpeed));
    }

    private void Attack(GameClient client, long instanceId, uint handle, ConnectionInfo info, uint now)
    {
        // Stand still to attack: a chase move still in flight would slide the monster through its swing.
        if (_worldState.IsMoving(instanceId))
        {
            _worldState.StopMove(instanceId);
            var startTime = unchecked(now + info.ClientClockOffset);
            client.Connection.Send(GameMovePackets.BuildStopMove(handle, startTime, info.Layer));
        }

        var damage = MonsterAiRules.PlayerDamage(info.CharacterMaxHp);
        info.CharacterHp = Math.Max(1, info.CharacterHp - damage);

        client.Connection.Send(GameAttackPackets.BuildAttackEvent(handle, info.CharacterHandle,
            AttackSpeedMs, AttackSpeedMs, GameAttackPackets.ActionAttack, damage, info.CharacterHp,
            _worldState.GetHp(instanceId)));
        client.Connection.Send(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", info.CharacterHp));

        _worldState.SetNextAttack(instanceId, unchecked(now + AttackIntervalTicks));
    }

    private void GoHome(GameClient client, long instanceId, uint handle, ConnectionInfo info, bool streamed)
    {
        // Read the pre-aggro position before clearing the target, then walk back to it at double speed.
        var hasHome = _worldState.TryGetAggroHome(instanceId, out var homeX, out var homeY);
        _worldState.ClearAggro(instanceId);

        if (!hasHome)
        {
            return;
        }

        var order = _worldState.ReturnHome(instanceId, homeX, homeY, ReturnSpeed);
        if (streamed)
        {
            Broadcast(client, handle, info, order);
        }
    }

    private static void Broadcast(GameClient client, uint handle, ConnectionInfo info, MoveOrder order)
    {
        var startTime = unchecked(order.StartTick + info.ClientClockOffset);
        client.Connection.Send(GameMovePackets.BuildMove(handle, startTime, info.Layer, order.Speed,
            order.DestX, order.DestY));
    }
}
