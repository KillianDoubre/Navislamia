using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services.Buffs;
using Serilog;

namespace Navislamia.Game.Services;

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
    private readonly Dictionary<long, (float X, float Y)> _position = new();
    private readonly Dictionary<long, DateTime> _nextMoveAt = new();

    // Sparse like every other map here: almost no monster ever carries a state.
    private readonly Dictionary<long, List<ActiveBuff>> _states = new();
    private static readonly IReadOnlyList<ActiveBuff> EmptyStates = Array.Empty<ActiveBuff>();
    private ushort _nextStateHandle;

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
            if (_position.TryGetValue(instanceId, out var position))
            {
                return position;
            }
        }

        return Origin(instanceId);
    }

    public bool TryBeginWander(long instanceId, DateTime now, out (float X, float Y) destination)
    {
        destination = default;

        lock (_stateLock)
        {
            if (_respawnAt.ContainsKey(instanceId))
            {
                return false;
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
            destination = (
                instance.X + (float)(Math.Cos(angle) * distance),
                instance.Y + (float)(Math.Sin(angle) * distance));

            _position[instanceId] = destination;
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
        }
    }

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
                _position.Remove(id);
                _nextMoveAt.Remove(id);
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
