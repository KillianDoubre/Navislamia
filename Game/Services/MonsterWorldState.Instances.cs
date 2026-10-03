using System;
using System.Collections.Generic;
using Navislamia.Configuration.Options;

namespace Navislamia.Game.Services;

/// <summary>
/// Monsters a game instance owns (the HuntaHolic hunt, socle-huntaholic.md §5): spawned on the instance's layer at a
/// position its caller drew, kept in the same sparse script-spawn table as Lua reinforcements, and removed by the
/// instance when the hunt ends (<c>clearMonsters</c>). A dead one is removed by the ordinary respawn sweep, which
/// never brings a script spawn back: the instance schedules its own respawn.
/// </summary>
public partial class MonsterWorldState
{
    private readonly HashSet<long> _standing = new();

    /// <summary><c>GameContent::IsBlocked</c>: a spawn point inside an obstacle is drawn again.</summary>
    public bool IsBlockedPoint(float x, float y) => !_collision.IsEmpty && _collision.IsBlocked(x, y);

    /// <summary>
    /// <c>respawnMonster(x, y, layer, monster_id, is_wandering, …)</c>: one instance of the resource, or null when the
    /// resource is unknown.
    /// </summary>
    public MonsterInstance? SpawnInstanceMonster(int monsterId, float x, float y, byte layer, bool wandering)
    {
        lock (_loadLock)
        {
            var resources = _repository.GetByIds(new[] { monsterId });
            var baseStats = LoadBaseStats(resources);
            var templates = MonsterInstanceFactory.Build(new[] { new MonsterSpawnPoint
                { MonsterId = monsterId, Count = 1, X = (int)x, Y = (int)y, Layer = layer } }, resources,
                id => baseStats.TryGetValue(id, out var stats) ? stats : null,
                useSecondaryRewards: _options.UseSecondaryRewards);
            if (templates.Count == 0) return null;
            lock (_stateLock)
            {
                var instance = templates[0] with { InstanceId = _nextInstanceId++, X = x, Y = y, Layer = layer };
                _scriptSpawns.Add(instance.InstanceId, instance);
                if (!wandering) _standing.Add(instance.InstanceId);
                return instance;
            }
        }
    }

    /// <summary><c>clearMonsters</c>: the instance's monsters leave the world, dead or alive.</summary>
    public void RemoveInstanceMonsters(IEnumerable<long> ids)
    {
        lock (_stateLock)
        {
            foreach (var id in ids)
            {
                if (!_scriptSpawns.Remove(id)) continue;
                _standing.Remove(id);
                _respawnAt.Remove(id);
                _currentHp.Remove(id);
                _damageContributions.Remove(id);
                _firstDamage.Remove(id);
                _movement.Remove(id);
                _nextMoveAt.Remove(id);
                _returningHome.Remove(id);
                _states.Remove(id);
                _lifeVersions.Remove(id);
                _skillReady.Remove(id);
                _skillOpportunity.Remove(id);
                _aggro.Remove(id);
                _hate.Remove(id);
                _targetHate.Remove(id);
                _summonHate.Remove(id);
            }
        }
    }
}
