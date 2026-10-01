using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Rates;
using Navislamia.Game.Services.Stats;
using Serilog;

namespace Navislamia.Game.Services;

public class CombatService : ICombatService
{
    private const int TickIntervalMs = 100;

    /// <summary>How soon an out-of-reach swing re-checks range while the client walks the player in.</summary>
    private const int RangeRetryMs = 200;
    private const int DeathAnimationSeconds = 6;

    private readonly ILogger _logger = Log.ForContext<CombatService>();
    private readonly MonsterWorldState _worldState;
    private readonly IMonsterSpawnService _spawnService;
    private readonly ILevelingService _levelingService;
    private readonly IGroundItemService _groundItemService;
    private readonly IRateService _rates;
    private readonly IStatService _stats;
    private readonly IStateCatalog _states;
    private readonly ICombatRandom _random;
    private readonly object _lock = new();
    private readonly Dictionary<GameClient, AttackSession> _sessions = new();
    private readonly Dictionary<long, GameClient> _lastAttacker = new();
    private readonly List<PendingLeave> _pendingLeaves = new();

    public CombatService(MonsterWorldState worldState, IMonsterSpawnService spawnService,
        ILevelingService levelingService, IGroundItemService groundItemService, IRateService rates,
        IStatService stats, IStateCatalog states, ICombatRandom random = null)
    {
        _rates = rates;
        _stats = stats;
        _states = states;
        _random = random ?? CombatRandom.Shared;
        _worldState = worldState;
        _spawnService = spawnService;
        _levelingService = levelingService;
        _groundItemService = groundItemService;
        _ = RunAsync();
    }

    public void StartAttack(GameClient client, uint targetHandle)
    {
        var info = client.ConnectionInfo;

        // A character at 0 HP is dead (this version has no death packet, the hp value is the whole state):
        // it must not start swinging, exactly as SkillCastService refuses a cast at 0 HP.
        if (!MonsterAiRules.IsAlive(info.CharacterHp)
            || !info.TryResolveMonster(targetHandle, out var targetInstanceId)
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

        // The attack session outlives the player's death, so a swing already scheduled when the killing
        // blow landed would keep hitting from a corpse: dead attackers stop here.
        if (!visible || !MonsterAiRules.IsAlive(info.CharacterHp)
            || !_worldState.IsAlive(session.TargetInstanceId)
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

        var stats = _stats.Compute(info).Total;
        var hit = CombatFormulas.Resolve(Combatant.From(stats, info.CharacterLevel),
            MonsterCombatant(session.TargetInstanceId, instance), stats.AttackPointRight, DamageKind.Physical,
            0, 0, _random);

        // A miss still lands as an attack: the monster turns on the player either way.
        var targetHp = ApplyDamage(client, session.TargetInstanceId, session.TargetHandle, hit.Damage);
        var intervalMs = IntervalMs(CombatFormulas.AttackIntervalTicks(stats.AttackSpeed));

        // Plant the player during the swing, the same rule the monster follows: a unit stands still to
        // attack. Only ever sent in reach, where the client has already stopped the player at the
        // target, so it reinforces rather than fights the client.
        client.Connection.Send(GameMovePackets.BuildStopMove(session.AttackerHandle,
            unchecked(ServerClock.Now + info.ClientClockOffset), info.Layer));

        client.Connection.Send(GameAttackPackets.BuildAttackEvent(session.AttackerHandle, session.TargetHandle,
            intervalMs, intervalMs, GameAttackPackets.ActionAttack, hit.Damage, targetHp, info.CharacterHp,
            (byte)hit.Flags));

        if (targetHp <= 0)
        {
            return;
        }

        session.NextSwingAt = now.AddMilliseconds(intervalMs);
    }

    public HitResult RollHit(GameClient client, long instanceId, float baseDamage, DamageKind kind,
        int accuracyBonus, int criticalBonus)
    {
        if (!_worldState.TryGetInstance(instanceId, out var instance))
        {
            return new HitResult(0, HitFlags.Miss);
        }

        var info = client.ConnectionInfo;
        var stats = _stats.Compute(info).Total;
        return CombatFormulas.Resolve(Combatant.From(stats, info.CharacterLevel),
            MonsterCombatant(instanceId, instance), baseDamage, kind, accuracyBonus, criticalBonus, _random);
    }

    public HitResult RollMonsterHit(long instanceId, GameClient target, out uint intervalTicks)
    {
        intervalTicks = CombatFormulas.AttackIntervalTicks(100f);
        if (!_worldState.TryGetInstance(instanceId, out var instance))
        {
            return new HitResult(0, HitFlags.Miss);
        }

        var monster = MonsterStats(instanceId, instance);
        intervalTicks = CombatFormulas.AttackIntervalTicks(monster.AttackSpeed);
        return RollMonsterHit(instance, monster, target, monster.AttackPointRight, DamageKind.Physical, 0, 0);
    }

    public HitResult RollMonsterHit(long instanceId, GameClient target, float baseDamage, DamageKind kind,
        int accuracyBonus, int criticalBonus)
    {
        if (!_worldState.TryGetInstance(instanceId, out var instance))
        {
            return new HitResult(0, HitFlags.Miss);
        }

        return RollMonsterHit(instance, MonsterStats(instanceId, instance), target, baseDamage, kind,
            accuracyBonus, criticalBonus);
    }

    public StatBlock GetMonsterStats(long instanceId) =>
        _worldState.TryGetInstance(instanceId, out var instance) ? MonsterStats(instanceId, instance) : null;

    private HitResult RollMonsterHit(MonsterInstance instance, StatBlock monster, GameClient target,
        float baseDamage, DamageKind kind, int accuracyBonus, int criticalBonus)
    {
        var info = target.ConnectionInfo;
        var player = _stats.Compute(info).Total;
        var hit = CombatFormulas.Resolve(Combatant.From(monster, instance.Level),
            Combatant.From(player, info.CharacterLevel), baseDamage, kind, accuracyBonus, criticalBonus, _random);

        // /immortal: the monster still swings and the dice still roll, but nothing is lost.
        return info.IsImmortal ? hit with { Damage = 0 } : hit;
    }

    /// <summary>The swing interval in milliseconds, what <c>attack_speed</c>/<c>attack_delay</c> carry.</summary>
    internal static ushort IntervalMs(uint intervalTicks) =>
        (ushort)Math.Min(ushort.MaxValue, intervalTicks * (1000 / ServerClock.TicksPerSecond));

    private Combatant MonsterCombatant(long instanceId, MonsterInstance instance) =>
        Combatant.From(MonsterStats(instanceId, instance), instance.Level);

    /// <summary>
    /// The monster's stats with its active states folded in: a debuff now moves what it says it moves.
    /// </summary>
    private StatBlock MonsterStats(long instanceId, MonsterInstance instance)
    {
        if (instance.Combat is null)
        {
            var bare = new StatBlock();
            StatCalculator.SeedFromLevel(Math.Max(1, instance.Level), bare);
            return bare;
        }

        var states = _worldState.GetStates(instanceId);
        if (states.Count == 0)
        {
            return instance.Combat.Plain;
        }

        var effects = new List<StatEffect>();
        foreach (var state in states)
        {
            effects.AddRange(_states.Resolve(state.StateId, state.StateLevel));
        }

        return instance.Combat.Compute(effects);
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
        _worldState.Kill(instanceId, now + _rates.MonsterRespawnDelay);

        // A corpse keeps no debuff, and a respawn must not inherit one either.
        foreach (var state in _worldState.ClearStates(instanceId))
        {
            client.Connection.Send(GameSkillPackets.BuildStateRemoval(targetHandle, state.StateHandle,
                (uint)state.StateId));
        }

        client.Connection.Send(GameMovePackets.BuildStopMove(targetHandle,
            unchecked(ServerClock.Now + info.ClientClockOffset), info.Layer));
        client.Connection.Send(GameCharacterPackets.BuildStatusChange(targetHandle, ActorStatus.ForMonster(true)));

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
        // The rates apply to the reward, rounded at random so a fractional rate is exact on average.
        var (baseExp, baseJp, baseGold) = CombatRewards.Compute(monsterLevel);
        var exp = _rates.Scale(baseExp, RateType.Exp);
        var jp = _rates.Scale(baseJp, RateType.Jp);
        var gold = _rates.Scale(baseGold, RateType.Gold);
        info.CharacterExp += exp;
        info.CharacterJp += jp;
        info.AddGold(gold);

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
