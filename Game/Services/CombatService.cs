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
    private const int BaseDamage = 30;
    private const int PerLevelDamage = 5;

    private readonly ILogger _logger = Log.ForContext<CombatService>();
    private readonly MonsterWorldState _worldState;
    private readonly IMonsterSpawnService _spawnService;
    private readonly object _lock = new();
    private readonly Dictionary<GameClient, AttackSession> _sessions = new();
    private readonly Dictionary<long, GameClient> _lastAttacker = new();

    public CombatService(MonsterWorldState worldState, IMonsterSpawnService spawnService)
    {
        _worldState = worldState;
        _spawnService = spawnService;
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

        if (!visible || !_worldState.IsAlive(session.TargetInstanceId))
        {
            StopAttack(client);
            return;
        }

        var damage = BaseDamage + info.CharacterLevel * PerLevelDamage;
        var targetHp = _worldState.ApplyDamage(session.TargetInstanceId, damage);

        client.Connection.Send(GameAttackPackets.BuildAttackEvent(session.AttackerHandle, session.TargetHandle,
            AttackDelayMs, AttackDelayMs, GameAttackPackets.ActionAttack, damage, targetHp, info.CharacterHp));

        if (targetHp <= 0)
        {
            _worldState.Kill(session.TargetInstanceId, now.AddSeconds(RespawnDelaySeconds));

            lock (_lock)
            {
                _lastAttacker[session.TargetInstanceId] = client;
                _sessions.Remove(client);
            }

            lock (info.MonsterVisibilityLock)
            {
                info.SpawnedMonsters.Remove(session.TargetInstanceId);
            }

            client.Connection.Send(GameSpawnPackets.BuildLeave(session.TargetHandle));
            return;
        }

        session.NextSwingAt = now.AddMilliseconds(AttackDelayMs);
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
}
