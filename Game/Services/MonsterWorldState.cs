using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.Maps.Collision;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Stats;
using Navislamia.Game.Services.MonsterSkills;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// A move the server started: the destination, the speed byte, and the server tick it began at. The
/// caller broadcasts <c>TS_SC_MOVE</c> with this start tick (plus each client's clock offset) and speed
/// so every client interpolates the same path the server does.
/// </summary>
/// <remarks>
/// <see cref="Path"/> holds every waypoint (the destination last) when the move goes around an obstacle;
/// null for a straight move.
/// </remarks>
public readonly record struct MoveOrder(float DestX, float DestY, byte Speed, uint StartTick,
    IReadOnlyList<(float X, float Y)> Path = null);

public partial class MonsterWorldState
{
    private readonly ILogger _logger = Log.ForContext<MonsterWorldState>();
    private readonly IMonsterResourceRepository _repository;
    private readonly IStatResourceRepository _statResources;
    private readonly CollisionMap _collision;
    private readonly MonsterSpawnOptions _options;
    private const float WanderRadiusMin = 75f;
    private const float WanderRadiusMax = 150f;
    private const int FirstMoveMaxMs = 1000;
    private const int MoveIntervalMinMs = 6000;
    private const int MoveIntervalMaxMs = 12000;

    private readonly object _loadLock = new();
    private readonly object _stateLock = new();

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

    // A monster's skill cooldowns (skill id -> ready tick), sparse: only a monster that cast carries any.
    private readonly Dictionary<long, Dictionary<int, uint>> _skillReady = new();
    private readonly Dictionary<long, uint> _skillOpportunity = new();
    private readonly Dictionary<long, uint> _combatStarted = new();
    private readonly Dictionary<long, uint[]> _triggerFlags = new();
    private readonly Dictionary<long, int> _lifeVersions = new();

    public int LifeVersion(long instanceId)
    { lock (_stateLock) return _lifeVersions.GetValueOrDefault(instanceId); }

    public bool CheckTrigger(long instanceId, IReadOnlyList<MonsterTriggerOptions> triggers,
        int index, uint now, ICombatRandom random)
    {
        lock (_stateLock)
        {
            if (!_triggerFlags.TryGetValue(instanceId, out var flags) || flags.Length != triggers.Count)
            {
                flags = new uint[triggers.Count];
                for (var i = 0; i < flags.Length; i++)
                    if (triggers[i].Type is 3 or 4) flags[i] = Math.Max(1u, _combatStarted.GetValueOrDefault(instanceId, now));
                _triggerFlags[instanceId] = flags;
            }
            var hpPercentage = (int)((long)GetHp(instanceId) * 100 / Math.Max(1, MaxHp(instanceId)));
            return MonsterTriggerRules.Check(triggers[index], hpPercentage, now, ref flags[index], random);
        }
    }

    public bool TrySkillOpportunity(long instanceId, uint now, uint interval)
    {
        lock (_stateLock)
        {
            if (_skillOpportunity.TryGetValue(instanceId, out var ready) && unchecked((int)(now - ready)) < 0) return false;
            _skillOpportunity[instanceId] = unchecked(now + Math.Max(1u, interval));
            return true;
        }
    }

    public bool HasLineOfSight(float x, float y, float targetX, float targetY) =>
        _collision.IsEmpty || !_collision.IsWalkBlocked(x, y, targetX, targetY);

    // A monster's single aggro target, sparse like the rest: only a monster in combat carries one.
    private readonly Dictionary<long, AggroTarget> _aggro = new();

    /// <summary>
    /// Every player a monster hates, with how much (<c>StructMonster</c>'s <c>_HATE_TAG</c> list), and the hate
    /// its current target held when it became or last stayed the target (<c>+0x2414</c>).
    /// </summary>
    private readonly Dictionary<long, Dictionary<GameClient, int>> _hate = new();
    private readonly Dictionary<long, int> _targetHate = new();

    /// <summary>
    /// The part of a master's hate a monster owes to the master's summon (<c>StructMonster::addHate</c> on the
    /// summon): the hate list stays keyed by the player, and this says which summon earned how much of it.
    /// </summary>
    private readonly Dictionary<long, Dictionary<GameClient, (uint Handle, int Hate)>> _summonHate = new();

    private SpatialIndex<MonsterInstance> _index;
    private Dictionary<long, MonsterInstance> _byId;
    private readonly Dictionary<long, MonsterInstance> _scriptSpawns = new();
    private long _nextInstanceId;

    public MonsterWorldState(IMonsterResourceRepository repository, IOptions<MonsterSpawnOptions> options,
        IStatResourceRepository statResources = null, IWorldCollision collision = null)
    {
        _repository = repository;
        _statResources = statResources;
        _collision = collision?.Map ?? CollisionMap.Empty;
        _options = options.Value;
        Load();
    }

    public IReadOnlyList<MonsterInstance> WithinRange(float x, float y, float range)
    {
        var staticMonsters = GetIndex()?.WithinRange(x, y, range) ?? Array.Empty<MonsterInstance>();
        lock (_stateLock)
        {
            if (_scriptSpawns.Count == 0) return staticMonsters;
            var result = new List<MonsterInstance>(staticMonsters);
            foreach (var monster in _scriptSpawns.Values)
            {
                var p = CurrentPosition(monster.InstanceId);
                if (CombatRange.Distance(x, y, p.X, p.Y) <= range) result.Add(monster);
            }
            return result;
        }
    }

    /// <summary>Script reinforcements start at the caster, spread out, and inherit its enemy.</summary>
    public IReadOnlyList<(MonsterInstance Instance, MoveOrder Move)> RespawnNearMonster(long sourceId, int monsterId, int count)
    {
        if (count <= 0) return Array.Empty<(MonsterInstance, MoveOrder)>();
        lock (_loadLock)
        {
            var resources = _repository.GetByIds(new[] { monsterId });
            var baseStats = LoadBaseStats(resources);
            var templates = MonsterInstanceFactory.Build(new[] { new MonsterSpawnPoint
                { MonsterId = monsterId, Count = 1 } }, resources,
                id => baseStats.TryGetValue(id, out var stats) ? stats : null,
                useSecondaryRewards: _options.UseSecondaryRewards);
            if (templates.Count == 0) return Array.Empty<(MonsterInstance, MoveOrder)>();
            lock (_stateLock)
            {
                if (!TryGetInstance(sourceId, out var source) || !IsAlive(sourceId) || GetHp(sourceId) <= 0)
                    return Array.Empty<(MonsterInstance, MoveOrder)>();
                var p = CurrentPosition(sourceId);
                if (!_collision.IsEmpty && _collision.IsBlocked(p.X, p.Y)) return Array.Empty<(MonsterInstance, MoveOrder)>();
                var result = new List<(MonsterInstance, MoveOrder)>();
                for (var i = 0; i < count; i++)
                {
                    var x = p.X + Random.Shared.NextSingle() * 120 - 60;
                    var y = p.Y + Random.Shared.NextSingle() * 120 - 60;
                    if (!HasLineOfSight(p.X, p.Y, x, y)) { x = p.X; y = p.Y; }
                    var instance = templates[0] with
                    {
                        InstanceId = _nextInstanceId++,
                        X = p.X,
                        Y = p.Y,
                        Z = source.Z,
                        Layer = source.Layer,
                        IsDungeonRaidMonster = source.IsDungeonRaidMonster,
                        Combat = source.IsDungeonRaidMonster ? templates[0].Combat?.AsRaid() : templates[0].Combat
                    };
                    _scriptSpawns.Add(instance.InstanceId, instance);
                    if (_aggro.TryGetValue(sourceId, out var aggro)) SetAggro(instance.InstanceId, aggro.Enemy);
                    var move = BeginMoveLocked(instance.InstanceId, x, y,
                        MonsterMovement.SpeedByte((instance.Combat?.Plain.MoveSpeed ?? 120) * 3));
                    result.Add((instance, move));
                }
                return result;
            }
        }
    }

    /// <summary>Area skills use current positions, including monsters that moved from another cell.</summary>
    public IReadOnlyList<MonsterInstance> WithinCurrentRange(float x, float y, float range)
    {
        var candidates = new Dictionary<long, MonsterInstance>();
        foreach (var m in WithinRange(x, y, range)) candidates[m.InstanceId] = m;
        lock (_stateLock)
        {
            foreach (var id in _movement.Keys)
                if (TryGetInstance(id, out var moved)) candidates[id] = moved;
            var result = new List<MonsterInstance>();
            foreach (var m in candidates.Values)
            {
                var p = CurrentPosition(m.InstanceId);
                if (!_respawnAt.ContainsKey(m.InstanceId) && GetHp(m.InstanceId) > 0
                    && CombatRange.Distance(x, y, p.X, p.Y) <= range) result.Add(m);
            }
            return result;
        }
    }

    public bool IsAlive(long instanceId)
    {
        lock (_stateLock)
        {
            return TryGetInstance(instanceId, out _) && !_respawnAt.ContainsKey(instanceId);
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

    /// <summary>
    /// <see cref="AddState"/> under the official stacking rule (<see cref="Casting.StateStacking"/>): false when
    /// a stronger or longer state of the same group refuses it; otherwise the states it displaced are in
    /// <paramref name="removed"/> and the applied one, refreshed in place when it was already there, in
    /// <paramref name="applied"/>.
    /// </summary>
    public bool TryAddState(long instanceId, int stateId, int skillId, int stateLevel, uint startTick, uint endTick,
        Casting.StateRule rule, Func<int, Casting.StateRule> rules, out ActiveBuff applied,
        out IReadOnlyList<ActiveBuff> removed)
    {
        lock (_stateLock)
        {
            if (!_states.TryGetValue(instanceId, out var states))
            {
                states = new List<ActiveBuff>();
                _states[instanceId] = states;
            }

            var decision = Casting.StateStacking.Decide(states, stateId, rule, stateLevel, endTick, rules);
            if (decision.Refused)
            {
                applied = default;
                removed = Array.Empty<ActiveBuff>();
                return false;
            }

            var handle = decision.RefreshIndex >= 0 ? states[decision.RefreshIndex].StateHandle : ++_nextStateHandle;
            var dropped = new List<ActiveBuff>();
            var indices = new List<int>(decision.Removed);
            if (decision.RefreshIndex >= 0)
            {
                indices.Add(decision.RefreshIndex);
            }

            indices.Sort();
            for (var i = indices.Count - 1; i >= 0; i--)
            {
                if (indices[i] != decision.RefreshIndex)
                {
                    dropped.Add(states[indices[i]]);
                }

                states.RemoveAt(indices[i]);
            }

            applied = new ActiveBuff(handle, stateId, skillId, decision.Level, startTick, endTick);
            states.Add(applied);
            removed = dropped;
            return true;
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

    /// <summary>
    /// Fills <paramref name="candidates"/> with the monsters of <paramref name="instanceIds"/> that could
    /// acquire a target on sight — aggressive, alive and not already in combat — with their current
    /// position, under one acquisition of the state lock.
    /// </summary>
    /// <remarks>
    /// The AI tick used to ask four questions per visible monster per player, each taking this lock on
    /// its own, every 300 ms, while the movement and combat ticks and the client threads wait on the
    /// same lock.
    /// </remarks>
    public void CollectAcquireCandidates(IReadOnlyList<long> instanceIds,
        List<(MonsterInstance Instance, float X, float Y)> candidates)
    {
        candidates.Clear();
        var byId = _byId;
        if (byId is null)
        {
            return;
        }

        lock (_stateLock)
        {
            foreach (var instanceId in instanceIds)
            {
                if (!TryGetInstance(instanceId, out var instance) || !instance.FirstAttack
                    || _aggro.ContainsKey(instanceId) || _respawnAt.ContainsKey(instanceId))
                {
                    continue;
                }

                var (x, y) = CurrentPosition(instanceId);
                candidates.Add((instance, x, y));
            }
        }
    }

    /// <summary>
    /// Gives <paramref name="enemy"/> to every living, idle monster among <paramref name="instanceIds"/> that
    /// joins <paramref name="leader"/>'s group attack (<see cref="MonsterAiRules.JoinsGroupAttack"/>). The ids
    /// are the monsters the enemy sees: one it does not see could not reach it, and would drop it at once.
    /// </summary>
    public int RallyGroup(MonsterInstance leader, IReadOnlyList<long> instanceIds, GameClient enemy)
    {
        var byId = _byId;
        if (byId is null || !leader.GroupFirstAttack || leader.MonsterGroup == 0)
        {
            return 0;
        }

        var rallied = 0;
        lock (_stateLock)
        {
            var (lx, ly) = CurrentPosition(leader.InstanceId);
            foreach (var instanceId in instanceIds)
            {
                if (!TryGetInstance(instanceId, out var member)
                    || _aggro.ContainsKey(instanceId) || _respawnAt.ContainsKey(instanceId))
                {
                    continue;
                }

                var (mx, my) = CurrentPosition(instanceId);
                if (MonsterAiRules.JoinsGroupAttack(leader, lx, ly, member, mx, my))
                {
                    AddHate(instanceId, enemy, 1);
                    rallied++;
                }
            }
        }

        return rallied;
    }

    public bool TryGetInstance(long instanceId, out MonsterInstance instance)
    {
        lock (_stateLock)
            if (_scriptSpawns.TryGetValue(instanceId, out instance)) return true;
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

        return move.Path is null
            ? MonsterMovement.PositionAt(move.StartX, move.StartY, move.DestX, move.DestY, move.StartTick,
                move.EndTick, ServerClock.Now)
            : MonsterMovement.PositionAlong(move.StartX, move.StartY, move.Path, move.Ends, move.StartTick,
                ServerClock.Now);
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

    private MoveOrder BeginPathLocked(long instanceId, IReadOnlyList<(float X, float Y)> path, byte speed)
    {
        if (path.Count == 1)
        {
            return BeginMoveLocked(instanceId, path[0].X, path[0].Y, speed);
        }

        var (startX, startY) = CurrentPosition(instanceId);
        var start = ServerClock.Now;
        var ends = MonsterMovement.PathEndTicks(startX, startY, path, start, speed);
        var (destX, destY) = path[^1];

        _movement[instanceId] = new Movement(startX, startY, destX, destY, speed, start, ends[^1], path, ends);
        return new MoveOrder(destX, destY, speed, start, path);
    }

    /// <summary>
    /// Walks the monster toward (destX, destY) around the obstacles (docs/packet-specs/
    /// socle-deplacement-monstres.md): straight when the line is free, along a found path otherwise, and
    /// not at all — null — when no path is found, so it never walks through a wall.
    /// </summary>
    public MoveOrder? BeginWalk(long instanceId, float destX, float destY, byte speed)
    {
        lock (_stateLock)
        {
            var path = PlanLocked(instanceId, destX, destY);
            return path is null ? null : BeginPathLocked(instanceId, path, speed);
        }
    }

    /// <summary>The waypoints to (destX, destY) from the current position, or null when blocked.</summary>
    private List<(float X, float Y)> PlanLocked(long instanceId, float destX, float destY)
    {
        var (x, y) = CurrentPosition(instanceId);
        if (_collision.IsEmpty || !_collision.IsWalkBlocked(x, y, destX, destY))
        {
            return new List<(float X, float Y)> { (destX, destY) };
        }

        return PathFinder.Find(_collision, x, y, destX, destY);
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
            // Home is reached whatever happens: around the obstacles when a path exists, straight otherwise
            // (a monster left stranded away from home would keep its aggro state).
            var path = PlanLocked(instanceId, homeX, homeY);
            var order = path is null
                ? BeginMoveLocked(instanceId, homeX, homeY, speed)
                : BeginPathLocked(instanceId, path, speed);
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

            // A HuntaHolic respawn entry with is_wandering = 0 stands where it was spawned.
            if (_standing.Contains(instanceId))
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

            if (!TryGetInstance(instanceId, out var instance))
            {
                return false;
            }

            var angle = Random.Shared.NextDouble() * Math.PI * 2;
            var distance = WanderRadiusMin + Random.Shared.NextDouble() * (WanderRadiusMax - WanderRadiusMin);
            var destX = instance.X + (float)(Math.Cos(angle) * distance);
            var destY = instance.Y + (float)(Math.Sin(angle) * distance);
            _nextMoveAt[instanceId] = now.AddMilliseconds(NextInterval());

            // The official wander refuses a blocked destination (StructMonster::processMove, NGemity
            // Monster::processWalk: IsBlocked); a wander that would cross an obstacle is refused as well
            // rather than routed: the monster simply tries again next time.
            var (fromX, fromY) = CurrentPosition(instanceId);
            if (!_collision.IsEmpty
                && (_collision.IsBlocked(destX, destY) || _collision.IsWalkBlocked(fromX, fromY, destX, destY)))
            {
                return false;
            }

            if (instance.Combat is { } combat)
            {
                speed = MonsterMovement.SpeedByte(combat.Plain.MoveSpeed);
            }

            order = BeginMoveLocked(instanceId, destX, destY, speed);
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

    private readonly Dictionary<long, Dictionary<GameClient, long>> _damageContributions = new();
    private readonly Dictionary<long, (GameClient Player, uint Tick)> _firstDamage = new();

    public MonsterDamageLedger TakeDamageContributions(long instanceId)
    {
        lock (_stateLock)
        {
            if (!_damageContributions.Remove(instanceId, out var damage)) return null;
            var first = _firstDamage[instanceId];
            _firstDamage.Remove(instanceId);
            return new MonsterDamageLedger(damage.Select(d => (d.Key, d.Value)).ToArray(), first.Player, first.Tick);
        }
    }

    public int ApplyDamage(long instanceId, int damage, GameClient contributor = null, uint tick = 0)
    {
        lock (_stateLock)
        {
            if (!_currentHp.TryGetValue(instanceId, out var hp))
            {
                hp = MaxHp(instanceId);
            }

            if (_respawnAt.ContainsKey(instanceId)) return 0;
            var dealt = Math.Min(hp, Math.Max(0, damage));
            if (dealt > 0 && contributor is not null)
            {
                if (!_damageContributions.TryGetValue(instanceId, out var contributions))
                {
                    contributions = new Dictionary<GameClient, long>();
                    _damageContributions.Add(instanceId, contributions);
                    _firstDamage[instanceId] = (contributor, tick);
                }
                contributions[contributor] = CombatRewards.AddProgress(contributions.GetValueOrDefault(contributor), dealt);
            }
            hp -= dealt;
            _currentHp[instanceId] = hp;
            return hp;
        }
    }

    public void Kill(long instanceId, DateTime respawnAt)
    {
        TryKill(instanceId, respawnAt);
    }

    /// <summary>Claims the death once, so simultaneous hits cannot duplicate rewards or quest credit.</summary>
    public bool TryKill(long instanceId, DateTime respawnAt)
    {
        lock (_stateLock)
        {
            if (_respawnAt.ContainsKey(instanceId)) return false;
            _currentHp[instanceId] = 0;
            _respawnAt[instanceId] = respawnAt;
            // A corpse chases nothing and a respawn inherits no target, the same rule as its states.
            _aggro.Remove(instanceId);
            _hate.Remove(instanceId);
            _targetHate.Remove(instanceId);
            _returningHome.Remove(instanceId);
            _skillReady.Remove(instanceId);
            _triggerFlags.Remove(instanceId);
            _lifeVersions[instanceId] = _lifeVersions.GetValueOrDefault(instanceId) + 1;
            _skillOpportunity.Remove(instanceId);
            _combatStarted.Remove(instanceId);
            return true;
        }
    }

    /// <summary>Whether <paramref name="skillId"/> is off cooldown for this monster at <paramref name="now"/>.</summary>
    public bool IsSkillReady(long instanceId, int skillId, uint now)
    {
        lock (_stateLock)
        {
            return !_skillReady.TryGetValue(instanceId, out var ready)
                   || !ready.TryGetValue(skillId, out var tick)
                   || unchecked((int)(now - tick)) >= 0;
        }
    }

    public void SetSkillCooldown(long instanceId, int skillId, uint readyTick)
    {
        lock (_stateLock)
        {
            if (!_skillReady.TryGetValue(instanceId, out var ready))
            {
                ready = new Dictionary<int, uint>();
                _skillReady[instanceId] = ready;
            }

            ready[skillId] = readyTick;
        }
    }

    /// <summary>
    /// Restores up to <paramref name="amount"/> HP to a living monster, capped at its maximum, and returns
    /// what it really gained. A corpse gains nothing.
    /// </summary>
    public int Heal(long instanceId, int amount)
    {
        if (amount <= 0)
        {
            return 0;
        }

        lock (_stateLock)
        {
            var max = MaxHp(instanceId);
            var hp = _currentHp.TryGetValue(instanceId, out var current) ? current : max;
            if (hp <= 0 || _respawnAt.ContainsKey(instanceId))
            {
                return 0;
            }

            var healed = Math.Min(amount, max - hp);
            if (healed > 0)
            {
                _currentHp[instanceId] = hp + healed;
            }

            return Math.Max(0, healed);
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
            _triggerFlags.Remove(instanceId);
            _skillOpportunity.Remove(instanceId);
            _combatStarted[instanceId] = ServerClock.Now;
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
            _skillOpportunity.Remove(instanceId);
            _combatStarted.Remove(instanceId);
            _triggerFlags.Remove(instanceId);
            _hate.Remove(instanceId);
            _targetHate.Remove(instanceId);
            _summonHate.Remove(instanceId);
        }
    }

    /// <summary>
    /// A summon hit the monster: the hate goes to its master's entry (the monster's target is a player's side), and
    /// the summon's share of it is remembered, so the monster turns on the summon while the summon earned more of
    /// that hate than its master did.
    /// </summary>
    public void AddSummonHate(long instanceId, GameClient master, uint summonHandle, int amount)
    {
        if (master is null || summonHandle == 0)
        {
            return;
        }

        lock (_stateLock)
        {
            if (_respawnAt.ContainsKey(instanceId))
            {
                return;
            }

            AddHate(instanceId, master, amount);
            if (!_summonHate.TryGetValue(instanceId, out var shares))
            {
                shares = new Dictionary<GameClient, (uint, int)>();
                _summonHate[instanceId] = shares;
            }

            shares.TryGetValue(master, out var share);
            var hate = share.Handle == summonHandle
                ? (int)Math.Clamp((long)share.Hate + amount, 0L, int.MaxValue)
                : Math.Max(0, amount);
            shares[master] = (summonHandle, hate);
        }
    }

    /// <summary>
    /// Whether the monster's hate toward <paramref name="master"/> is mostly its summon's: the monster then fights the
    /// summon (<paramref name="summonHandle"/>) rather than the player.
    /// </summary>
    public bool TryGetSummonFocus(long instanceId, GameClient master, out uint summonHandle)
    {
        lock (_stateLock)
        {
            summonHandle = 0;
            if (master is null || !_summonHate.TryGetValue(instanceId, out var shares)
                || !shares.TryGetValue(master, out var share) || share.Hate <= 0)
            {
                return false;
            }

            var total = _hate.TryGetValue(instanceId, out var list) ? list.GetValueOrDefault(master) : 0;
            if (share.Hate <= total - share.Hate)
            {
                return false;
            }

            summonHandle = share.Handle;
            return true;
        }
    }

    /// <summary>
    /// A summon left the fight (dead, sent back): its share of the hate is struck off. A master left with no hate of
    /// their own is no one's enemy any more, and a monster that was fighting them moves to its next enemy or home.
    /// </summary>
    public IReadOnlyList<long> ForgetSummon(GameClient master, uint summonHandle)
    {
        lock (_stateLock)
        {
            var dropped = new List<long>();
            foreach (var (instanceId, shares) in _summonHate.ToList())
            {
                if (!shares.TryGetValue(master, out var share) || share.Handle != summonHandle)
                {
                    continue;
                }

                shares.Remove(master);
                if (!_hate.TryGetValue(instanceId, out var list) || !list.TryGetValue(master, out var held))
                {
                    continue;
                }

                var own = held - share.Hate;
                var targeted = _aggro.TryGetValue(instanceId, out var current) && current.Enemy == master;
                if (own > 0)
                {
                    list[master] = own;
                    if (targeted)
                    {
                        _targetHate[instanceId] = own;
                    }

                    continue;
                }

                if (targeted)
                {
                    DropTarget(instanceId);
                    dropped.Add(instanceId);
                }
                else
                {
                    list.Remove(master);
                }
            }

            return dropped;
        }
    }


    /// <summary>
    /// <c>StructMonster::addHate</c> (2012-11 <c>0x1400b6710</c>): <paramref name="amount"/> more hate toward
    /// <paramref name="enemy"/>, never below 0. A monster with no target takes this one; a monster with a target
    /// switches only when this enemy's hate is strictly above what the target held. A dead monster hates nobody.
    /// </summary>
    public void AddHate(long instanceId, GameClient enemy, int amount)
    {
        if (enemy is null)
        {
            return;
        }

        lock (_stateLock)
        {
            if (_respawnAt.ContainsKey(instanceId))
            {
                return;
            }

            if (!_hate.TryGetValue(instanceId, out var list))
            {
                list = new Dictionary<GameClient, int>();
                _hate[instanceId] = list;
            }

            list.TryGetValue(enemy, out var held);
            var hate = (int)Math.Clamp((long)held + amount, 0L, int.MaxValue);
            list[enemy] = hate;

            if (!_aggro.TryGetValue(instanceId, out var current))
            {
                SetAggro(instanceId, enemy);
                _targetHate[instanceId] = hate;
                return;
            }

            if (current.Enemy == enemy)
            {
                _targetHate[instanceId] = hate;
                return;
            }

            if (hate > _targetHate.GetValueOrDefault(instanceId))
            {
                _aggro[instanceId] = current with { Enemy = enemy };
                _targetHate[instanceId] = hate;
            }
        }
    }

    /// <summary>
    /// <c>StructCreature::AddHateToEnemyList</c>: every monster hating <paramref name="helped"/> hates
    /// <paramref name="helper"/> <paramref name="amount"/> more — a heal draws the attention of the healed
    /// player's enemies.
    /// </summary>
    public void AddHateFromHelp(GameClient helped, GameClient helper, int amount, uint helperSummon = 0)
    {
        if (amount == 0 || helped is null)
        {
            return;
        }

        lock (_stateLock)
        {
            List<long> monsters = null;
            foreach (var (instanceId, list) in _hate)
            {
                if (list.ContainsKey(helped))
                {
                    (monsters ??= new List<long>()).Add(instanceId);
                }
            }

            if (monsters is null)
            {
                return;
            }

            foreach (var instanceId in monsters)
            {
                if (helperSummon == 0) AddHate(instanceId, helper, amount);
                else AddSummonHate(instanceId, helper, helperSummon, amount);
            }
        }
    }

    /// <summary>How much <paramref name="instanceId"/> hates <paramref name="enemy"/>; 0 when not at all.</summary>
    public int GetHate(long instanceId, GameClient enemy)
    {
        lock (_stateLock)
        {
            return _hate.TryGetValue(instanceId, out var list) && list.TryGetValue(enemy, out var hate) ? hate : 0;
        }
    }

    /// <summary>
    /// <c>StructMonster::findNextEnemy</c>: the current target is struck off the hate list and the most hated
    /// remaining enemy becomes the target. False, with the whole aggro cleared, when nobody is left.
    /// </summary>
    public bool DropTarget(long instanceId)
    {
        lock (_stateLock)
        {
            if (!_aggro.TryGetValue(instanceId, out var current))
            {
                return false;
            }

            if (_hate.TryGetValue(instanceId, out var list))
            {
                list.Remove(current.Enemy);
                GameClient next = null;
                var best = -1;
                foreach (var (enemy, hate) in list)
                {
                    if (hate > best)
                    {
                        next = enemy;
                        best = hate;
                    }
                }

                if (next is not null)
                {
                    _aggro[instanceId] = current with { Enemy = next };
                    _targetHate[instanceId] = best;
                    return true;
                }
            }

            // Nobody is left to fight: the combat ends as if the aggro had been cleared.
            ClearAggro(instanceId);
            return false;
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

            // The leaving player is no one's enemy any more, nor are their summons.
            foreach (var list in _hate.Values)
            {
                list.Remove(enemy);
            }

            foreach (var shares in _summonHate.Values)
            {
                shares.Remove(enemy);
            }

            if (cleared == null)
            {
                return Array.Empty<long>();
            }

            foreach (var id in cleared)
            {
                ClearAggro(id);
            }

            return cleared;
        }
    }

    private readonly record struct AggroTarget(GameClient Enemy, uint NextAttackTick,
        float HomeX, float HomeY);

    private readonly record struct Movement(
        float StartX, float StartY, float DestX, float DestY, byte Speed, uint StartTick, uint EndTick,
        IReadOnlyList<(float X, float Y)> Path = null, uint[] Ends = null);

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
                _damageContributions.Remove(id);
                _firstDamage.Remove(id);
                _movement.Remove(id);
                _nextMoveAt.Remove(id);
                _returningHome.Remove(id);
                if (_scriptSpawns.Remove(id))
                {
                    _states.Remove(id);
                    _lifeVersions.Remove(id);
                }
            }

            return respawned;
        }
    }

    private static int NextInterval()
    {
        return Random.Shared.Next(MoveIntervalMinMs, MoveIntervalMaxMs);
    }

    /// <summary>
    /// The <c>StatResource</c> rows the loaded monsters name, in one query. A missing row leaves that
    /// monster with level and columns only, and is reported once.
    /// </summary>
    private Dictionary<int, StatBaseStats> LoadBaseStats(IReadOnlyList<MonsterResourceEntity> resources)
    {
        var result = new Dictionary<int, StatBaseStats>();
        if (_statResources is null)
        {
            return result;
        }

        var ids = new HashSet<int>();
        foreach (var resource in resources)
        {
            ids.Add(resource.StatId);
        }

        foreach (var row in _statResources.GetByIds(ids))
        {
            result[(int)row.Id] = new StatBaseStats((int)row.Id, row.Strength, row.Vitality, row.Dexterity,
                row.Agility, row.Intelligence, row.Wisdom, row.Luck);
        }

        var missing = ids.Count - result.Count;
        if (missing > 0)
        {
            _logger.Warning("{missing} of {total} monster stat ids have no StatResource row; those monsters fight on level and columns alone",
                missing, ids.Count);
        }

        return result;
    }

    private int MaxHp(long instanceId)
    {
        return TryGetInstance(instanceId, out var instance) ? instance.Hp : 1;
    }

    private (float X, float Y) Origin(long instanceId)
    {
        return TryGetInstance(instanceId, out var instance)
            ? (instance.X, instance.Y)
            : (0f, 0f);
    }

    /// <summary>A failed startup load is retried at most this often, not on every lookup.</summary>
    private static readonly TimeSpan LoadRetryInterval = TimeSpan.FromSeconds(30);

    private DateTime _nextLoadAttempt;

    private SpatialIndex<MonsterInstance> GetIndex()
    {
        if (_index != null)
        {
            return _index;
        }

        // Every sync of every player reaches here: without a floor between attempts, a database that is
        // down turned each step of each player into a monster query.
        lock (_loadLock)
        {
            if (_index == null && DateTime.UtcNow >= _nextLoadAttempt)
            {
                _nextLoadAttempt = DateTime.UtcNow + LoadRetryInterval;
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
            var baseStats = LoadBaseStats(resources);
            var instances = MonsterInstanceFactory.Build(_options, resources,
                statId => baseStats.TryGetValue(statId, out var stats) ? stats : null,
                _collision.IsEmpty ? null : _collision.IsBlocked);

            var byId = new Dictionary<long, MonsterInstance>(instances.Count);
            foreach (var instance in instances)
            {
                byId[instance.InstanceId] = instance;
            }

            _byId = byId;
            _nextInstanceId = instances.Count;
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
