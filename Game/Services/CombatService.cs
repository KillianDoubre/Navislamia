using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

public class CombatService : ICombatService
{
    private const int TickIntervalMs = 100;
    private const ushort AttackDelayMs = 1200;

    /// <summary>How soon an out-of-reach swing re-checks range while the client walks the player in.</summary>
    private const int RangeRetryMs = 200;
    private const int RespawnDelaySeconds = 10;
    private const int DamageHpDivisor = 3;
    private const int DeathAnimationSeconds = 6;
    private const uint MonsterDeadStatus = 1 << 8;

    private readonly ILogger _logger = Log.ForContext<CombatService>();
    private readonly MonsterWorldState _worldState;
    private readonly IMonsterSpawnService _spawnService;
    private readonly ILevelingService _levelingService;
    private readonly IGroundItemService _groundItemService;
    private readonly object _lock = new();
    private readonly Dictionary<GameClient, AttackSession> _sessions = new();
    private readonly Dictionary<long, GameClient> _lastAttacker = new();
    private readonly List<PendingLeave> _pendingLeaves = new();

    public CombatService(MonsterWorldState worldState, IMonsterSpawnService spawnService,
        ILevelingService levelingService, IGroundItemService groundItemService)
    {
        _worldState = worldState;
        _spawnService = spawnService;
        _levelingService = levelingService;
        _groundItemService = groundItemService;
        _ = RunAsync();
    }

    public void StartAttack(GameClient client, uint targetHandle)
    {
        var info = client.ConnectionInfo;
        if (!info.TryResolveMonster(targetHandle, out var targetInstanceId)
            || !_worldState.IsAlive(targetInstanceId))
        {
            return;
        }

        lock (_lock)
        {
            _sessions[client] = new AttackSession
            {
                Client = client,
                TargetInstanceId = targetInstanceId,
                TargetHandle = targetHandle,
                AttackerHandle = info.CharacterHandle,
                NextSwingAt = DateTime.UtcNow
            };
        }
    }

    public void StopAttack(GameClient client)
    {
        AttackSession session;

        lock (_lock)
        {
            if (!_sessions.TryGetValue(client, out session))
            {
                return;
            }

            _sessions.Remove(client);
        }

        client.Connection.Send(GameAttackPackets.BuildEndAttack(session.AttackerHandle, session.TargetHandle));
    }

    public void DropAggro(GameClient client)
    {
        _worldState.ClearAggroFor(client);
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
                _logger.Error(ex, "Combat tick failed");
            }
        }
    }

    private void Tick(DateTime now)
    {
        List<AttackSession> due = null;

        lock (_lock)
        {
            foreach (var session in _sessions.Values)
            {
                if (session.NextSwingAt <= now)
                {
                    (due ??= new List<AttackSession>()).Add(session);
                }
            }
        }

        if (due != null)
        {
            foreach (var session in due)
            {
                ProcessSwing(session, now);
            }
        }

        ProcessPendingLeaves(now);
        ProcessRespawns(now);
    }

    private void ProcessSwing(AttackSession session, DateTime now)
    {
        var client = session.Client;
        var info = client.ConnectionInfo;

        bool visible;
        lock (info.MonsterVisibilityLock)
        {
            visible = info.SpawnedMonsters.TryGetValue(session.TargetInstanceId, out var handle)
                && handle == session.TargetHandle;
        }

        if (!visible || !_worldState.IsAlive(session.TargetInstanceId)
            || !_worldState.TryGetInstance(session.TargetInstanceId, out var instance))
        {
            StopAttack(client);
            return;
        }

        // Gate the swing on the same real reach the monster attacks at, so a player cannot hit from
        // across the view while the monster cannot hit back. Out of reach, hold and re-check soon — the
        // client is walking the player in — rather than land a hit or stop the attack.
        var (monsterX, monsterY) = _worldState.GetPosition(session.TargetInstanceId);
        var reach = CombatRange.MeleeReach(instance.AttackRange, instance.Size, instance.Scale);
        if (!CombatRange.InReach(info.X, info.Y, monsterX, monsterY, reach))
        {
            session.NextSwingAt = now.AddMilliseconds(RangeRetryMs);
            return;
        }

        var damage = GetHitDamage(session.TargetInstanceId);
        var targetHp = ApplyDamage(client, session.TargetInstanceId, session.TargetHandle, damage);

        // Plant the player during the swing, the same rule the monster follows: a unit stands still to
        // attack. Only ever sent in reach, where the client has already stopped the player at the
        // target, so it reinforces rather than fights the client.
        client.Connection.Send(GameMovePackets.BuildStopMove(session.AttackerHandle,
            unchecked(ServerClock.Now + info.ClientClockOffset), info.Layer));

        client.Connection.Send(GameAttackPackets.BuildAttackEvent(session.AttackerHandle, session.TargetHandle,
            AttackDelayMs, AttackDelayMs, GameAttackPackets.ActionAttack, damage, targetHp, info.CharacterHp));

        if (targetHp <= 0)
        {
            return;
        }

        session.NextSwingAt = now.AddMilliseconds(AttackDelayMs);
    }

    public int GetHitDamage(long instanceId)
    {
        return _worldState.TryGetInstance(instanceId, out var instance)
            ? Math.Max(1, instance.Hp / DamageHpDivisor)
            : 0;
    }

    public int ApplyDamage(GameClient client, long instanceId, uint targetHandle, int damage)
    {
        if (!_worldState.TryGetInstance(instanceId, out var instance))
        {
            return 0;
        }

        var info = client.ConnectionInfo;
        var targetHp = _worldState.ApplyDamage(instanceId, damage);
        if (targetHp > 0)
        {
            // The monster fights back. Every monster retaliates, aggressive or not; the AI service
            // takes it from here (Kill clears the aggro on death below).
            _worldState.SetAggro(instanceId, client);
            return targetHp;
        }

        var now = DateTime.UtcNow;
        _worldState.Kill(instanceId, now.AddSeconds(RespawnDelaySeconds));

        // A corpse keeps no debuff, and a respawn must not inherit one either.
        foreach (var state in _worldState.ClearStates(instanceId))
        {
            client.Connection.Send(GameSkillPackets.BuildStateRemoval(targetHandle, state.StateHandle,
                (uint)state.StateId));
        }

        client.Connection.Send(GameMovePackets.BuildStopMove(targetHandle,
            unchecked(ServerClock.Now + info.ClientClockOffset), info.Layer));
        client.Connection.Send(GameCharacterPackets.BuildStatusChange(targetHandle, MonsterDeadStatus));

        lock (_lock)
        {
            _lastAttacker[instanceId] = client;
            _sessions.Remove(client);
            _pendingLeaves.Add(new PendingLeave
            {
                Client = client,
                InstanceId = instanceId,
                Handle = targetHandle,
                LeaveAt = now.AddSeconds(DeathAnimationSeconds)
            });
        }

        var (dropX, dropY) = _worldState.GetPosition(instanceId);
        _groundItemService.DropForMonster(client, instance.MonsterId, dropX, dropY, instance.Z);
        AwardKill(client, info, instance.Level);
        return targetHp;
    }

    private void AwardKill(GameClient client, ConnectionInfo info, int monsterLevel)
    {
        var (exp, jp, gold) = CombatRewards.Compute(monsterLevel);
        info.CharacterExp += exp;
        info.CharacterJp += jp;
        info.CharacterGold += gold;

        client.Connection.Send(GameCharacterPackets.BuildExpUpdate(info.CharacterHandle, info.CharacterExp, info.CharacterJp));
        client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(info.CharacterGold, info.CharacterChaos));
        _levelingService.ApplyExperience(client);
    }

    private void ProcessPendingLeaves(DateTime now)
    {
        List<PendingLeave> due = null;

        lock (_lock)
        {
            for (var i = _pendingLeaves.Count - 1; i >= 0; i--)
            {
                if (_pendingLeaves[i].LeaveAt <= now)
                {
                    (due ??= new List<PendingLeave>()).Add(_pendingLeaves[i]);
                    _pendingLeaves.RemoveAt(i);
                }
            }
        }

        if (due == null)
        {
            return;
        }

        foreach (var leave in due)
        {
            var info = leave.Client.ConnectionInfo;
            lock (info.MonsterVisibilityLock)
            {
                if (info.SpawnedMonsters.TryGetValue(leave.InstanceId, out var handle) && handle == leave.Handle)
                {
                    leave.Client.Connection.Send(GameSpawnPackets.BuildLeave(leave.Handle));
                    info.SpawnedMonsters.Remove(leave.InstanceId);
                }
            }
        }
    }

    private void ProcessRespawns(DateTime now)
    {
        var respawned = _worldState.CollectRespawns(now);

        foreach (var instanceId in respawned)
        {
            GameClient attacker;

            lock (_lock)
            {
                _lastAttacker.TryGetValue(instanceId, out attacker);
                _lastAttacker.Remove(instanceId);
            }

            if (attacker != null)
            {
                _spawnService.Sync(attacker);
            }
        }
    }

    private sealed class AttackSession
    {
        public GameClient Client;
        public long TargetInstanceId;
        public uint TargetHandle;
        public uint AttackerHandle;
        public DateTime NextSwingAt;
    }

    private sealed class PendingLeave
    {
        public GameClient Client;
        public long InstanceId;
        public uint Handle;
        public DateTime LeaveAt;
    }
}
