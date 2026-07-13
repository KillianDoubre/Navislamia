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
    private const int RespawnDelaySeconds = 10;
    private const int DamageHpDivisor = 3;
    private const int DeathAnimationSeconds = 6;
    private const uint MonsterDeadStatus = 1 << 8;

    private readonly ILogger _logger = Log.ForContext<CombatService>();
    private readonly MonsterWorldState _worldState;
    private readonly IMonsterSpawnService _spawnService;
    private readonly ILevelingService _levelingService;
    private readonly object _lock = new();
    private readonly Dictionary<GameClient, AttackSession> _sessions = new();
    private readonly Dictionary<long, GameClient> _lastAttacker = new();
    private readonly List<PendingLeave> _pendingLeaves = new();

    public CombatService(MonsterWorldState worldState, IMonsterSpawnService spawnService,
        ILevelingService levelingService)
    {
        _worldState = worldState;
        _spawnService = spawnService;
        _levelingService = levelingService;
        _ = RunAsync();
    }

    public void StartAttack(GameClient client, uint targetHandle)
    {
        var info = client.ConnectionInfo;
        long targetInstanceId = -1;

        lock (info.MonsterVisibilityLock)
        {
            foreach (var pair in info.SpawnedMonsters)
            {
                if (pair.Value == targetHandle)
                {
                    targetInstanceId = pair.Key;
                    break;
                }
            }
        }

        if (targetInstanceId < 0 || !_worldState.IsAlive(targetInstanceId))
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

        var damage = Math.Max(1, instance.Hp / DamageHpDivisor);
        var targetHp = _worldState.ApplyDamage(session.TargetInstanceId, damage);

        client.Connection.Send(GameAttackPackets.BuildAttackEvent(session.AttackerHandle, session.TargetHandle,
            AttackDelayMs, AttackDelayMs, GameAttackPackets.ActionAttack, damage, targetHp, info.CharacterHp));

        if (targetHp <= 0)
        {
            _worldState.Kill(session.TargetInstanceId, now.AddSeconds(RespawnDelaySeconds));
            client.Connection.Send(GameCharacterPackets.BuildStatusChange(session.TargetHandle, MonsterDeadStatus));

            lock (_lock)
            {
                _lastAttacker[session.TargetInstanceId] = client;
                _sessions.Remove(client);
                _pendingLeaves.Add(new PendingLeave
                {
                    Client = client,
                    InstanceId = session.TargetInstanceId,
                    Handle = session.TargetHandle,
                    LeaveAt = now.AddSeconds(DeathAnimationSeconds)
                });
            }

            AwardKill(client, info, instance.Level);
            return;
        }

        session.NextSwingAt = now.AddMilliseconds(AttackDelayMs);
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
