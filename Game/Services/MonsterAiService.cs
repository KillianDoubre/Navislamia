using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.Network;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.MonsterSkills;
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

    /// <summary>
    /// A chase move is only re-issued when the desired destination has drifted this far from the one
    /// already in flight — otherwise the client would get a fresh move every tick and stutter.
    /// </summary>
    private const float ChaseReissueThreshold = 60f;

    private readonly ILogger _logger = Log.ForContext<MonsterAiService>();
    private readonly MonsterWorldState _worldState;
    private readonly NetworkService _networkService;
    private readonly ICombatService _combat;
    private readonly IMonsterSkillService _skills;

    public MonsterAiService(MonsterWorldState worldState, NetworkService networkService, ICombatService combat,
        IMonsterSkillService skills)
    {
        _worldState = worldState;
        _networkService = networkService;
        _combat = combat;
        _skills = skills;
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

    /// <summary>Reused across ticks: the tick runs on one timer, never concurrently.</summary>
    private readonly List<long> _visible = new();
    private readonly List<(MonsterInstance Instance, float X, float Y)> _candidates = new();

    /// <summary>An aggressive monster with no target takes a player it can see and is close to.</summary>
    private void Acquire(List<GameClient> clients)
    {
        foreach (var client in clients)
        {
            var info = client.ConnectionInfo;

            if (!MonsterAiRules.IsAlive(info.CharacterHp))
            {
                continue;
            }

            _visible.Clear();
            lock (info.MonsterVisibilityLock)
            {
                if (info.SpawnedMonsters.Count == 0)
                {
                    continue;
                }

                _visible.AddRange(info.SpawnedMonsters.Keys);
            }

            _worldState.CollectAcquireCandidates(_visible, _candidates);

            foreach (var (instance, mx, my) in _candidates)
            {
                var instanceId = instance.InstanceId;
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

            // A character at 0 HP is not a target any more: the monster stops swinging and walks home
            // instead of hitting a corpse. Death carries no packet of its own (§3.2), so the value of
            // CharacterHp is the whole of the dead state.
            if (!MonsterAiRules.IsAlive(info.CharacterHp))
            {
                GoHome(enemy, instanceId, handle, info, streamed);
                continue;
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

        Broadcast(client, instanceId, handle, info, _worldState.BeginMove(instanceId, x, y, ChaseSpeed));
    }

    private void Attack(GameClient client, long instanceId, uint handle, ConnectionInfo info, uint now)
    {
        // Stand still to attack: a chase move still in flight would slide the monster through its swing.
        if (_worldState.IsMoving(instanceId))
        {
            _worldState.StopMove(instanceId);
            var startTime = unchecked(now + info.ClientClockOffset);
            client.Connection.Send(GameMovePackets.BuildStopMove(handle, startTime, info.Layer));
            ToOtherWatchers(client, instanceId, false, (other, otherHandle) => GameMovePackets.BuildStopMove(
                otherHandle, unchecked(now + other.ConnectionInfo.ClientClockOffset), other.ConnectionInfo.Layer));
        }

        // A skill, when one comes up, replaces the swing (StructMonster::AI_processAttack). The next
        // opportunity waits for the cast and for the attack interval, whichever is longer.
        if (_skills.TryCast(client, instanceId, handle, now, out var castTicks))
        {
            var attackSpeed = _combat.GetMonsterStats(instanceId)?.AttackSpeed ?? 100f;
            var wait = Math.Max(castTicks, CombatFormulas.AttackIntervalTicks(attackSpeed));
            _worldState.SetNextAttack(instanceId, unchecked(now + wait));
            return;
        }

        var hit = _combat.RollMonsterHit(instanceId, client, out var intervalTicks);
        info.CharacterHp = MonsterAiRules.PlayerHpAfterDamage(info.CharacterHp, hit.Damage);

        var intervalMs = CombatService.IntervalMs(intervalTicks);
        var monsterHp = _worldState.GetHp(instanceId);
        client.Connection.Send(GameAttackPackets.BuildAttackEvent(handle, info.CharacterHandle,
            intervalMs, intervalMs, GameAttackPackets.ActionAttack, hit.Damage, info.CharacterHp,
            monsterHp, (byte)hit.Flags));

        // The players who see both see the swing: the one that brings the player to 0 carries
        // target_hp = 0, which is how they watch the player die (the client has no death packet).
        ToOtherWatchers(client, instanceId, true, (_, otherHandle) => GameAttackPackets.BuildAttackEvent(
            otherHandle, info.CharacterHandle, intervalMs, intervalMs, GameAttackPackets.ActionAttack, hit.Damage,
            info.CharacterHp, monsterHp, (byte)hit.Flags));
        client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", info.CharacterHp));

        _worldState.SetNextAttack(instanceId, unchecked(now + intervalTicks));
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
            Broadcast(client, instanceId, handle, info, order);
        }
        else
        {
            ToOtherWatchers(client, instanceId, false, (other, otherHandle) => MoveFrame(other, otherHandle, order));
        }
    }

    private void Broadcast(GameClient client, long instanceId, uint handle, ConnectionInfo info, MoveOrder order)
    {
        client.Connection.Send(MoveFrame(client, handle, order));
        ToOtherWatchers(client, instanceId, false, (other, otherHandle) => MoveFrame(other, otherHandle, order));
    }

    /// <summary>A monster move for one client: its handle for the monster and its own clock.</summary>
    private static byte[] MoveFrame(GameClient recipient, uint handle, MoveOrder order)
    {
        var info = recipient.ConnectionInfo;
        var startTime = unchecked(order.StartTick + info.ClientClockOffset);
        return GameMovePackets.BuildMove(handle, startTime, info.Layer, order.Speed, order.DestX, order.DestY);
    }

    /// <summary>
    /// A frame about a monster for every other client that has it streamed, under that client's handle for
    /// it (docs/packet-specs/socle-diffusion-combat.md). With <paramref name="mustSeeTarget"/>, only the
    /// clients that also see the target player get it: an attack on a player they do not know is not theirs.
    /// </summary>
    private void ToOtherWatchers(GameClient target, long instanceId, bool mustSeeTarget,
        Func<GameClient, uint, byte[]> build)
    {
        var targetHandle = target.ConnectionInfo.CharacterHandle;
        foreach (var other in _networkService.AuthorizedGameClients.Values)
        {
            if (ReferenceEquals(other, target))
            {
                continue;
            }

            var otherInfo = other.ConnectionInfo;
            var handle = otherInfo.GetMonsterHandle(instanceId);
            if (handle == 0)
            {
                continue;
            }

            if (mustSeeTarget)
            {
                lock (otherInfo.PlayerVisibilityLock)
                {
                    if (!otherInfo.SpawnedPlayers.ContainsKey(targetHandle))
                    {
                        continue;
                    }
                }
            }

            other.Connection.Send(build(other, handle));
        }
    }
}
