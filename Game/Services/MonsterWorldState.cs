using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services.Buffs;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// A move the server started: the destination, the speed byte, and the server tick it began at. The
/// caller broadcasts <c>TS_SC_MOVE</c> with this start tick (plus each client's clock offset) and speed
/// so every client interpolates the same path the server does.
/// </summary>
public readonly record struct MoveOrder(float DestX, float DestY, byte Speed, uint StartTick);

public class MonsterWorldState
{
    private readonly ILogger _logger = Log.ForContext<MonsterWorldState>();
    private readonly IMonsterResourceRepository _repository;
    private readonly MonsterSpawnOptions _options;
    private const float WanderRadiusMin = 75f;
    private const float WanderRadiusMax = 150f;
    private const int FirstMoveMaxMs = 1000;
    private const int MoveIntervalMinMs = 6000;
    private const int MoveIntervalMaxMs = 12000;

    private readonly object _loadLock = new();
    private readonly object _stateLock = new();
    private static readonly IReadOnlySet<long> EmptyDead = new HashSet<long>();

    private readonly Dictionary<long, int> _currentHp = new();
    private readonly Dictionary<long, DateTime> _respawnAt = new();

    // A monster's active move, interpolated over time exactly as the client does, so the server's
    // notion of where a monster is matches the animation the client is playing. Replaces the old
    // snap-to-destination, which jumped the server position ahead of the walk and read as jitter.
    private readonly Dictionary<long, Movement> _movement = new();
    private readonly Dictionary<long, DateTime> _nextMoveAt = new();

    // Monsters walking home after dropping aggro: idle wander must leave them alone until they arrive,
    // otherwise a fresh wander destination hijacks the return the moment the target is dropped.
    private readonly HashSet<long> _returningHome = new();

    // Sparse like every other map here: almost no monster ever carries a state.
    private readonly Dictionary<long, List<ActiveBuff>> _states = new();
    private static readonly IReadOnlyList<ActiveBuff> EmptyStates = Array.Empty<ActiveBuff>();
    private ushort _nextStateHandle;

    // A monster's single aggro target, sparse like the rest: only a monster in combat carries one.
    private readonly Dictionary<long, AggroTarget> _aggro = new();

    private SpatialIndex<MonsterInstance> _index;
    private Dictionary<long, MonsterInstance> _byId;

    public MonsterWorldState(IMonsterResourceRepository repository, IOptions<MonsterSpawnOptions> options)
    {
        _repository = repository;
        _options = options.Value;
        Load();
    }

    public IReadOnlyList<MonsterInstance> WithinRange(float x, float y, float range)
    {
        return GetIndex()?.WithinRange(x, y, range) ?? Array.Empty<MonsterInstance>();
    }

    public bool IsAlive(long instanceId)
    {
        lock (_stateLock)
        {
            return !_respawnAt.ContainsKey(instanceId);
        }
    }

    public IReadOnlySet<long> GetDeadInstances()
    {
        lock (_stateLock)
        {
            return _respawnAt.Count == 0 ? EmptyDead : new HashSet<long>(_respawnAt.Keys);
        }
    }

    /// <summary>
    /// Applies a state to a monster, replacing any active instance of the same state and reusing its
    /// handle. Returns the applied buff so the caller can put its handle on the wire.
    /// </summary>
    public ActiveBuff AddState(long instanceId, int stateId, int skillId, int stateLevel, uint startTick,
        uint endTick)
    {
        lock (_stateLock)
        {
            if (!_states.TryGetValue(instanceId, out var states))
            {
                states = new List<ActiveBuff>();
                _states[instanceId] = states;
            }

            var existing = states.FindIndex(state => state.StateId == stateId);
            ushort handle;
            if (existing >= 0)
            {
                handle = states[existing].StateHandle;
                states.RemoveAt(existing);
            }
            else
            {
                handle = ++_nextStateHandle;
            }

            var buff = new ActiveBuff(handle, stateId, skillId, stateLevel, startTick, endTick);
            states.Add(buff);
            return buff;
        }
    }

    public IReadOnlyList<ActiveBuff> GetStates(long instanceId)
    {
        lock (_stateLock)
        {
            return _states.TryGetValue(instanceId, out var states) && states.Count > 0
                ? states.ToArray()
                : EmptyStates;
        }
    }

    /// <summary>Removes every state whose deadline has passed, across all monsters.</summary>
    public IReadOnlyList<(long InstanceId, ActiveBuff State)> RemoveExpiredStates(uint now)
    {
        lock (_stateLock)
        {
            if (_states.Count == 0)
            {
                return Array.Empty<(long, ActiveBuff)>();
            }

            List<(long, ActiveBuff)> expired = null;
            List<long> emptied = null;
            foreach (var (instanceId, states) in _states)
            {
                for (var i = states.Count - 1; i >= 0; i--)
                {
                    if (unchecked((int)(now - states[i].EndTick)) < 0)
                    {
                        continue;
                    }

                    expired ??= new List<(long, ActiveBuff)>();
                    expired.Add((instanceId, states[i]));
                    states.RemoveAt(i);
                }

                if (states.Count == 0)
                {
                    // Keep the map sparse: a monster that once had a state must not keep an empty list.
                    emptied ??= new List<long>();
                    emptied.Add(instanceId);
                }
            }

            if (emptied is not null)
            {
                foreach (var instanceId in emptied)
                {
                    _states.Remove(instanceId);
                }
            }

            return (IReadOnlyList<(long, ActiveBuff)>)expired ?? Array.Empty<(long, ActiveBuff)>();
        }
    }

    /// <summary>Drops every state of one monster: a corpse keeps no debuff.</summary>
    public IReadOnlyList<ActiveBuff> ClearStates(long instanceId)
    {
        lock (_stateLock)
        {
            if (!_states.Remove(instanceId, out var states) || states.Count == 0)
            {
                return EmptyStates;
            }

            return states.ToArray();
        }
    }

    public bool TryGetInstance(long instanceId, out MonsterInstance instance)
    {
        if (_byId != null && _byId.TryGetValue(instanceId, out instance))
        {
            return true;
        }

        instance = default;
        return false;
    }

    public (float X, float Y) GetPosition(long instanceId)
    {
        lock (_stateLock)
        {
            return CurrentPosition(instanceId);
        }
    }

    /// <summary>The current position, interpolated from the active move; assumes the state lock is held.</summary>
    private (float X, float Y) CurrentPosition(long instanceId)
    {
        if (!_movement.TryGetValue(instanceId, out var move))
        {
            return Origin(instanceId);
        }

        return MonsterMovement.PositionAt(move.StartX, move.StartY, move.DestX, move.DestY,
            move.StartTick, move.EndTick, ServerClock.Now);
    }

    /// <summary>
    /// Starts a move from the monster's current position toward a destination, interpolated over time
    /// like the client. Returns the order so the caller can broadcast the matching <c>TS_SC_MOVE</c>
    /// with the same start tick and speed.
    /// </summary>
    public MoveOrder BeginMove(long instanceId, float destX, float destY, byte speed)
    {
        lock (_stateLock)
        {
            return BeginMoveLocked(instanceId, destX, destY, speed);
        }
    }

    private MoveOrder BeginMoveLocked(long instanceId, float destX, float destY, byte speed)
    {
        var (startX, startY) = CurrentPosition(instanceId);
        var length = CombatRange.Distance(startX, startY, destX, destY);
        var start = ServerClock.Now;
        var end = MonsterMovement.EndTick(start, length, speed);

        _movement[instanceId] = new Movement(startX, startY, destX, destY, speed, start, end);
        return new MoveOrder(destX, destY, speed, start);
    }

    public bool IsMoving(long instanceId)
    {
        lock (_stateLock)
        {
            return IsMovingLocked(instanceId);
        }
    }

    private bool IsMovingLocked(long instanceId)
    {
        return _movement.TryGetValue(instanceId, out var move)
            && unchecked((int)(ServerClock.Now - move.EndTick)) < 0;
    }

    /// <summary>
    /// Freezes the monster at its current position, so it stands still to attack rather than sliding
    /// through the swing on a chase move that is still playing. Returns where it stopped.
    /// </summary>
    public (float X, float Y) StopMove(long instanceId)
    {
        lock (_stateLock)
        {
            var (x, y) = CurrentPosition(instanceId);
            var now = ServerClock.Now;
            _movement[instanceId] = new Movement(x, y, x, y, 0, now, now);
            return (x, y);
        }
    }

    /// <summary>
    /// Walks the monster back to a position and suppresses idle wander until it arrives, so the return
    /// is one uninterrupted walk rather than being hijacked by a fresh wander destination.
    /// </summary>
    public MoveOrder ReturnHome(long instanceId, float homeX, float homeY, byte speed)
    {
        lock (_stateLock)
        {
            var order = BeginMoveLocked(instanceId, homeX, homeY, speed);
            _returningHome.Add(instanceId);
            return order;
        }
    }

    public bool TryGetMoveDestination(long instanceId, out float x, out float y)
    {
        lock (_stateLock)
        {
            if (_movement.TryGetValue(instanceId, out var move))
            {
                x = move.DestX;
                y = move.DestY;
                return true;
            }
        }

        x = 0f;
        y = 0f;
        return false;
    }

    public bool TryBeginWander(long instanceId, DateTime now, byte speed, out MoveOrder order)
    {
        order = default;

        lock (_stateLock)
        {
            if (_respawnAt.ContainsKey(instanceId))
            {
                return false;
            }

            // A monster in combat is driven by the AI service, not the idle wander.
            if (_aggro.ContainsKey(instanceId))
            {
                return false;
            }

            // A monster still walking home after a drop is left alone until it arrives.
            if (_returningHome.Contains(instanceId))
            {
                if (IsMovingLocked(instanceId))
                {
                    return false;
                }

                _returningHome.Remove(instanceId);
            }

            if (!_nextMoveAt.TryGetValue(instanceId, out var next))
            {
                _nextMoveAt[instanceId] = now.AddMilliseconds(Random.Shared.Next(0, FirstMoveMaxMs));
                return false;
            }

            if (next > now)
            {
                return false;
            }

            if (_byId == null || !_byId.TryGetValue(instanceId, out var instance))
            {
                return false;
            }

            var angle = Random.Shared.NextDouble() * Math.PI * 2;
            var distance = WanderRadiusMin + Random.Shared.NextDouble() * (WanderRadiusMax - WanderRadiusMin);
            var destX = instance.X + (float)(Math.Cos(angle) * distance);
            var destY = instance.Y + (float)(Math.Sin(angle) * distance);

            order = BeginMoveLocked(instanceId, destX, destY, speed);
            _nextMoveAt[instanceId] = now.AddMilliseconds(NextInterval());
            return true;
        }
    }

    public int GetHp(long instanceId)
    {
        lock (_stateLock)
        {
            if (_currentHp.TryGetValue(instanceId, out var hp))
            {
                return hp;
            }
        }

        return MaxHp(instanceId);
    }

    public int ApplyDamage(long instanceId, int damage)
    {
        lock (_stateLock)
        {
            if (!_currentHp.TryGetValue(instanceId, out var hp))
            {
                hp = MaxHp(instanceId);
            }

            hp = Math.Max(0, hp - damage);
            _currentHp[instanceId] = hp;
            return hp;
        }
    }

    public void Kill(long instanceId, DateTime respawnAt)
    {
        lock (_stateLock)
        {
            _currentHp[instanceId] = 0;
            _respawnAt[instanceId] = respawnAt;
            // A corpse chases nothing and a respawn inherits no target, the same rule as its states.
            _aggro.Remove(instanceId);
            _returningHome.Remove(instanceId);
        }
    }

    /// <summary>
    /// Points a monster at a player. An existing target on the same client keeps its attack cooldown
    /// so retaliation cannot reset the swing timer; a new target may strike at once. The position the
    /// monster held when it first acquired is remembered, so on drop it walks back to exactly where it
    /// was rather than to its spawn origin.
    /// </summary>
    public void SetAggro(long instanceId, GameClient enemy)
    {
        lock (_stateLock)
        {
            if (_aggro.TryGetValue(instanceId, out var current) && current.Enemy == enemy)
            {
                return;
            }

            var (homeX, homeY) = CurrentPosition(instanceId);
            _aggro[instanceId] = new AggroTarget(enemy, 0, homeX, homeY);
            // Re-acquiring cancels any in-progress return home.
            _returningHome.Remove(instanceId);
        }
    }

    /// <summary>The position a monster held before it aggroed, to return to when it drops the target.</summary>
    public bool TryGetAggroHome(long instanceId, out float x, out float y)
    {
        lock (_stateLock)
        {
            if (_aggro.TryGetValue(instanceId, out var target))
            {
                x = target.HomeX;
                y = target.HomeY;
                return true;
            }
        }

        x = 0f;
        y = 0f;
        return false;
    }

    public bool TryGetAggro(long instanceId, out GameClient enemy, out uint nextAttackTick)
    {
        lock (_stateLock)
        {
            if (_aggro.TryGetValue(instanceId, out var target))
            {
                enemy = target.Enemy;
                nextAttackTick = target.NextAttackTick;
                return true;
            }
        }

        enemy = null;
        nextAttackTick = 0;
        return false;
    }

    /// <summary>A snapshot of every monster currently in combat, for the AI tick to act on.</summary>
    public IReadOnlyList<(long InstanceId, GameClient Enemy, uint NextAttackTick)> SnapshotAggro()
    {
        lock (_stateLock)
        {
            if (_aggro.Count == 0)
            {
                return Array.Empty<(long, GameClient, uint)>();
            }

            var snapshot = new List<(long, GameClient, uint)>(_aggro.Count);
            foreach (var pair in _aggro)
            {
                snapshot.Add((pair.Key, pair.Value.Enemy, pair.Value.NextAttackTick));
            }

            return snapshot;
        }
    }

    public void SetNextAttack(long instanceId, uint nextAttackTick)
    {
        lock (_stateLock)
        {
            if (_aggro.TryGetValue(instanceId, out var target))
            {
                _aggro[instanceId] = target with { NextAttackTick = nextAttackTick };
            }
        }
    }

    public void ClearAggro(long instanceId)
    {
        lock (_stateLock)
        {
            _aggro.Remove(instanceId);
        }
    }

    /// <summary>
    /// Drops every target pointing at a leaving client and returns the affected monsters, so a
    /// disconnected or warped player leaves nothing chasing a ghost.
    /// </summary>
    public IReadOnlyList<long> ClearAggroFor(GameClient enemy)
    {
        lock (_stateLock)
        {
            List<long> cleared = null;
            foreach (var pair in _aggro)
            {
                if (pair.Value.Enemy == enemy)
                {
                    (cleared ??= new List<long>()).Add(pair.Key);
                }
            }

            if (cleared == null)
            {
                return Array.Empty<long>();
            }

            foreach (var id in cleared)
            {
                _aggro.Remove(id);
            }

            return cleared;
        }
    }

    private readonly record struct AggroTarget(GameClient Enemy, uint NextAttackTick,
        float HomeX, float HomeY);

    private readonly record struct Movement(
        float StartX, float StartY, float DestX, float DestY, byte Speed, uint StartTick, uint EndTick);

    public IReadOnlyList<long> CollectRespawns(DateTime now)
    {
        lock (_stateLock)
        {
            if (_respawnAt.Count == 0)
            {
                return Array.Empty<long>();
            }

            List<long> respawned = null;
            foreach (var pair in _respawnAt)
            {
                if (pair.Value <= now)
                {
                    (respawned ??= new List<long>()).Add(pair.Key);
                }
            }

            if (respawned == null)
            {
                return Array.Empty<long>();
            }

            foreach (var id in respawned)
            {
                _respawnAt.Remove(id);
                _currentHp.Remove(id);
                _movement.Remove(id);
                _nextMoveAt.Remove(id);
                _returningHome.Remove(id);
            }

            return respawned;
        }
    }

    private static int NextInterval()
    {
        return Random.Shared.Next(MoveIntervalMinMs, MoveIntervalMaxMs);
    }

    private int MaxHp(long instanceId)
    {
        return _byId != null && _byId.TryGetValue(instanceId, out var instance) ? instance.Hp : 1;
    }

    private (float X, float Y) Origin(long instanceId)
    {
        return _byId != null && _byId.TryGetValue(instanceId, out var instance)
            ? (instance.X, instance.Y)
            : (0f, 0f);
    }

    private SpatialIndex<MonsterInstance> GetIndex()
    {
        if (_index != null)
        {
            return _index;
        }

        lock (_loadLock)
        {
            if (_index == null)
            {
                Load();
            }
        }

        return _index;
    }

    private void Load()
    {
        try
        {
            var resourceIds = MonsterInstanceFactory.GetRequiredResourceIds(_options);
            var resources = _repository.GetByIds(resourceIds);
            var instances = MonsterInstanceFactory.Build(_options, resources);

            var byId = new Dictionary<long, MonsterInstance>(instances.Count);
            foreach (var instance in instances)
            {
                byId[instance.InstanceId] = instance;
            }

            _byId = byId;
            _index = new SpatialIndex<MonsterInstance>(instances, monster => monster.X, monster => monster.Y,
                WorldVisibility.ViewRange);

            _logger.Information("Loaded {instances} monster instances from {points} spawn points and {areas} official areas ({resources} monster resources)",
                _index.Count, _options.Spawns.Count, _options.Areas.Count, resources.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load monsters at startup; will retry on first access");
        }
    }
}
