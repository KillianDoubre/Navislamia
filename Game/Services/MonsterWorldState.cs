using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

public class MonsterWorldState
{
    private readonly ILogger _logger = Log.ForContext<MonsterWorldState>();
    private readonly IMonsterResourceRepository _repository;
    private readonly MonsterSpawnOptions _options;
    private readonly object _loadLock = new();
    private readonly object _stateLock = new();
    private static readonly IReadOnlySet<long> EmptyDead = new HashSet<long>();

    private readonly Dictionary<long, int> _currentHp = new();
    private readonly Dictionary<long, DateTime> _respawnAt = new();

    private SpatialIndex<MonsterInstance> _index;
    private Dictionary<long, int> _maxHp;

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
            }

            return respawned;
        }
    }

    private int MaxHp(long instanceId)
    {
        return _maxHp != null && _maxHp.TryGetValue(instanceId, out var hp) ? hp : 1;
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

            var maxHp = new Dictionary<long, int>(instances.Count);
            foreach (var instance in instances)
            {
                maxHp[instance.InstanceId] = instance.Hp;
            }

            _maxHp = maxHp;
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
