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
    /// <summary>The move speed of a monster without combat stats (a creature's base).</summary>
    private const float FallbackMoveSpeed = 120f;

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
                if (instance.Layer != info.Layer) continue;
                var instanceId = instance.InstanceId;
                var action = MonsterAiRules.Decide(false, true, true, false,
                    mx, my, instance.X, instance.Y, info.X, info.Y,
                    instance.VisibleRange, instance.ChaseRange, Reach(instance));

                if (action == MonsterAiAction.Acquire)
                {
                    _worldState.SetAggro(instanceId, client);
                    // Only the aggro on sight rallies the group, not a retaliation (processFirstAttack).
                    _worldState.RallyGroup(instance, _visible, client);
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
            if (!MonsterAiRules.IsAlive(info.CharacterHp) || instance.Layer != info.Layer)
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

            // Monster skills and Lua triggers are tried before the melee distance gate (official AI).
            if (action is MonsterAiAction.Chase or MonsterAiAction.Attack
                && unchecked((int)(now - nextAttackTick)) >= 0
                && _worldState.HasLineOfSight(mx, my, info.X, info.Y)
                && TryAttackSkill(enemy, instanceId, handle, info, now)) continue;

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

        // The official chase speed: the real move speed (run_speed / 7, states included) times 1.00..1.09
        // (AI_processAttack: XFastRandom / 100 + 1 before GetRealMoveSpeed; NGemity Monster.cpp:803-812).
        var moveSpeed = _combat.GetMonsterStats(instanceId)?.MoveSpeed ?? FallbackMoveSpeed;
        var speed = MonsterMovement.SpeedByte(moveSpeed * (1f + Random.Shared.Next(0, 10) / 100f));

        // Around the obstacles; with no path the monster holds rather than walk through a wall.
        if (_worldState.BeginWalk(instanceId, x, y, speed) is { } order)
        {
            Broadcast(client, instanceId, handle, info, order);
        }
    }

    private void Attack(GameClient client, long instanceId, uint handle, ConnectionInfo info, uint now)
    {
        StopToAttack(client, instanceId, handle, info, now);
        var hit = _combat.RollMonsterHit(instanceId, client, out var intervalTicks);
        var playerHp = MonsterAiRules.PlayerHpAfterDamage(info.CharacterHp, hit.Damage);

        var intervalMs = CombatService.IntervalMs(intervalTicks);
        var monsterHp = _worldState.GetHp(instanceId);
        client.Connection.Send(GameAttackPackets.BuildAttackEvent(handle, info.CharacterHandle,
            intervalMs, intervalMs, GameAttackPackets.ActionAttack, hit.Damage, playerHp,
            monsterHp, (byte)hit.Flags));
        ToOtherWatchers(client, instanceId, true, (_, otherHandle) => GameAttackPackets.BuildAttackEvent(
            otherHandle, info.CharacterHandle, intervalMs, intervalMs, GameAttackPackets.ActionAttack, hit.Damage,
            playerHp, monsterHp, (byte)hit.Flags));
        _combat.DamagePlayer(client, hit.Damage);
        _worldState.SetNextAttack(instanceId, unchecked(now + intervalTicks));
    }

    private void StopToAttack(GameClient client, long instanceId, uint handle, ConnectionInfo info, uint now)
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

    }

    private bool TryAttackSkill(GameClient client, long instanceId, uint handle, ConnectionInfo info, uint now)
    {
        var attackSpeed = _combat.GetMonsterStats(instanceId)?.AttackSpeed ?? 100f;
        var interval = CombatFormulas.AttackIntervalTicks(attackSpeed);
        if (!_worldState.TrySkillOpportunity(instanceId, now, interval)
            || !_skills.TryCast(client, instanceId, handle, now, out var castTicks)) return false;
        StopToAttack(client, instanceId, handle, info, now);
        _worldState.SetNextAttack(instanceId, unchecked(now + Math.Max(castTicks, interval)));
        return true;
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

        // Home at twice the move speed (NGemity Monster.cpp:688, 2 × move speed / 7).
        var moveSpeed = _combat.GetMonsterStats(instanceId)?.MoveSpeed ?? FallbackMoveSpeed;
        var order = _worldState.ReturnHome(instanceId, homeX, homeY, MonsterMovement.SpeedByte(2f * moveSpeed));
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
        return MonsterMovement.Frame(handle, unchecked(order.StartTick + info.ClientClockOffset), info.Layer, order);
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
