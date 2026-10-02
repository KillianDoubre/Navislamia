using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Scripting;
using Serilog;

namespace Navislamia.Game.Services.MonsterSkills;

public interface IMonsterSkillService
{
    /// <summary>
    /// At the moment a monster may strike <paramref name="target"/>, rolls its skills and casts the first
    /// one that comes up. True when a skill was cast — it replaces the swing — with the time the monster
    /// spent casting in <paramref name="castTicks"/>.
    /// </summary>
    bool TryCast(GameClient target, long instanceId, uint monsterHandle, uint now, out uint castTicks);
}

/// <summary>
/// Monster skills (docs/packet-specs/socle-competences-monstres.md): the official pick of
/// <see cref="MonsterSkillRules.Pick"/>, then the same <c>TS_SC_SKILL</c> sequence a player cast sends —
/// <c>ST_Casting</c>, <c>ST_Fire</c> with the hit, <c>ST_Complete</c> — with the monster as caster.
/// </summary>
/// <remarks>
/// A damage skill rolls through <see cref="ICombatService.RollMonsterHit(long, GameClient, float, DamageKind, int, int)"/>,
/// so the hit, block, critical and defence rules are the swing's; a harmful state lands on the player through
/// <see cref="ISkillCastService.ApplyState"/> (icon, countdown, expiry and stat refresh of a buff); a self
/// state lands in <see cref="MonsterWorldState"/>, where the combat rule reads it; a heal restores the
/// monster's own HP. Must not inject <c>NetworkService</c>, the DI cycle that only fails at runtime.
/// </remarks>
public class MonsterSkillService : IMonsterSkillService
{
    private readonly ILogger _logger = Log.ForContext<MonsterSkillService>();
    private readonly IMonsterSkillCatalog _catalog;
    private readonly MonsterWorldState _world;
    private readonly ICombatService _combat;
    private readonly ISkillCastService _skillCast;
    private readonly ICombatRandom _random;
    private readonly IPlayerVisibilityService _players;
    private readonly IScriptService _scripts;
    private readonly SkillEffectScheduler _effects;
    private readonly ICharacterService _characters;
    private readonly IMonsterSpawnService _spawns;
    private readonly IQuestService _quests;
    private readonly object _gate = new();
    private readonly HashSet<(long Id, int Life)> _casting = new();

    public MonsterSkillService(IMonsterSkillCatalog catalog, MonsterWorldState world, ICombatService combat,
        ISkillCastService skillCast, ICombatRandom random = null, IPlayerVisibilityService players = null,
        IScriptService scripts = null, SkillEffectScheduler effects = null,
        ICharacterService characters = null, IMonsterSpawnService spawns = null, IQuestService quests = null)
    {
        _players = players;
        _quests = quests;
        _scripts = scripts;
        _effects = effects ?? new SkillEffectScheduler();
        _characters = characters;
        _spawns = spawns ?? new MonsterSpawnService(world);
        _catalog = catalog;
        _world = world;
        _combat = combat;
        _skillCast = skillCast;
        _random = random ?? CombatRandom.Shared;
    }

    public bool TryCast(GameClient target, long instanceId, uint monsterHandle, uint now, out uint castTicks)
    {
        castTicks = 0;
        if (!_world.TryGetInstance(instanceId, out var instance) || !_world.IsAlive(instanceId)
            || _world.GetHp(instanceId) <= 0 || target.ConnectionInfo.CharacterHp <= 0
            || target.ConnectionInfo.Layer != instance.Layer)
        {
            return false;
        }

        var skills = _catalog.Get(instance.SkillLinkId);
        lock (_gate)
            if (_casting.Contains((instanceId, _world.LifeVersion(instanceId)))) { castTicks = 5; return true; }

        var triggers = _catalog.GetTriggers(instance.SkillLinkId);
        if (_scripts is not null)
        for (var i = 0; i < triggers.Count; i++)
        {
            if (!_world.CheckTrigger(instanceId, triggers, i, now, _random)) continue;
            var (x, y) = _world.GetPosition(instanceId);
            var attempted = false;
            var casted = false;
            uint wait = 0;
            var ran = _scripts.RunMonsterTrigger(triggers[i].Function, new MonsterScriptContext
            {
                MonsterHandle = monsterHandle, TargetHandle = target.ConnectionInfo.CharacterHandle,
                MonsterId = instance.MonsterId, TriggerIndex = i, X = x, Y = y, Layer = instance.Layer,
                IsDungeonRaidMonster = instance.IsDungeonRaidMonster,
                PlayerContext = _quests?.CreateScriptContext(target),
                RespawnNearMonster = (monsterId, count) =>
                {
                    var spawned = _world.RespawnNearMonster(instanceId, monsterId, count);
                    if (spawned.Count == 0) return;
                    foreach (var recipient in _players?.Registry.Clients ?? new[] { target })
                    {
                        _spawns.Sync(recipient);
                        foreach (var (reinforcement, move) in spawned)
                        {
                            var h = recipient.ConnectionInfo.GetMonsterHandle(reinforcement.InstanceId);
                            if (h != 0) recipient.Connection.Send(GameMovePackets.BuildMove(h,
                                unchecked(move.StartTick + recipient.ConnectionInfo.ClientClockOffset),
                                reinforcement.Layer, move.Speed, move.DestX, move.DestY));
                        }
                    }
                },
                SetAutoUser = (value, handle) =>
                {
                    var recipient = target;
                    if (handle != target.ConnectionInfo.CharacterHandle
                        && (_players is null || !_players.Registry.TryResolve(handle, out recipient))) return 0;
                    if (_characters is null) throw new InvalidOperationException("Character persistence is required for set_auto_user");
                    var info = recipient.ConnectionInfo;
                    if (!_characters.SetAutoUsedAsync(info.AccountName, info.CharacterName, value).GetAwaiter().GetResult()) return 0;
                    info.AutoUsed = value;
                    if (!value)
                    {
                        if (_players is not null) foreach (var peer in _players.Registry.Clients)
                            if (peer.ConnectionInfo.AccountName == info.AccountName) peer.ConnectionInfo.AutoUsed = false;
                        _skillCast.RemoveState(recipient, 5997);
                    }
                    _logger.Information("Monster Lua set_auto_user: {Character}, account {Account}, flag {Value}",
                        info.CharacterName, info.AccountName, value);
                    return value ? 1 : 0;
                },
                CastSkill = (index, requestedTarget, groundX, groundY) =>
                {
                    attempted = true;
                    if (casted || index < 0 || index >= skills.Count) return false;
                    var candidate = skills[index];
                    if (candidate.Effect == MonsterSkillEffect.Unsupported
                        || !_world.IsSkillReady(instanceId, candidate.Fields.SkillId, now)) return false;
                    var recipient = target;
                    if (!candidate.OnSelf && requestedTarget != target.ConnectionInfo.CharacterHandle
                        && !(groundX.HasValue && groundY.HasValue))
                    {
                        if (_players is null || !_players.Registry.TryResolve(requestedTarget, out recipient)
                            || recipient.ConnectionInfo.GetMonsterHandle(instanceId) == 0) return false;
                    }
                    if (candidate.OnSelf && requestedTarget != 0 && requestedTarget != monsterHandle) return false;
                    if (recipient.ConnectionInfo.CharacterHp <= 0 || recipient.ConnectionInfo.Layer != target.ConnectionInfo.Layer) return false;
                    if (!CanReach(instanceId, instance, recipient, candidate, now, groundX, groundY)) return false;
                    wait = BeginCast(recipient, instanceId, instance,
                        ReferenceEquals(recipient, target) ? monsterHandle : recipient.ConnectionInfo.GetMonsterHandle(instanceId),
                        candidate, now, groundX, groundY);
                    casted = true;
                    return true;
                },
                AddState = (stateId, level, duration, handle) =>
                {
                    if (handle == monsterHandle)
                    {
                        var state = _world.AddState(instanceId, stateId, 0, level, now, unchecked(now + duration));
                        void SendState(GameClient recipient)
                        {
                            var h = recipient.ConnectionInfo.GetMonsterHandle(instanceId);
                            if (ReferenceEquals(recipient, target)) h = monsterHandle;
                            if (h != 0) recipient.Connection.Send(GameSkillPackets.BuildState(h, state.StateHandle,
                                (uint)stateId, (ushort)level, state.EndTick, now));
                        }
                        SendState(target);
                        if (_players is not null) foreach (var observer in _players.Observers(target)) SendState(observer);
                    }
                    else if (handle == target.ConnectionInfo.CharacterHandle && !target.ConnectionInfo.IsImmortal)
                        _skillCast.ApplyState(target, stateId, level, duration);
                }
            });
            if (casted || (ran && !attempted)) { castTicks = wait; return true; }
        }
        if (skills.Count == 0)
        {
            return false;
        }

        var skill = MonsterSkillRules.Pick(skills,
            candidate => _world.IsSkillReady(instanceId, candidate.Fields.SkillId, now)
                && CanReach(instanceId, instance, target, candidate, now), _random);
        if (skill is null)
        {
            return false;
        }

        castTicks = BeginCast(target, instanceId, instance, monsterHandle, skill, now);
        return true;
    }

    private bool CanReach(long id, MonsterInstance instance, GameClient target, MonsterSkill skill, uint now,
        float? groundX = null, float? groundY = null)
    {
        var fields = skill.Fields;
        if (!SkillCastRangeRules.AppliesTo(fields.EffectType) || skill.OnSelf || fields.RequiredTarget == 0) return true;
        var p = _world.GetPosition(id);
        var attackRange = _combat.GetMonsterStats(id)?.AttackRange ?? instance.AttackRange;
        if (groundX.HasValue && groundY.HasValue)
            return float.IsFinite(groundX.Value) && float.IsFinite(groundY.Value)
                && SkillCastRangeRules.GroundInRange(fields.CastRange, attackRange,
                    CombatRange.Distance(p.X, p.Y, groundX.Value, groundY.Value));
        var q = SkillCastRangeRules.PlayerPosition(target.ConnectionInfo, now);
        return SkillCastRangeRules.InRange(fields.CastRange, attackRange,
            CombatRange.Distance(p.X, p.Y, q.X, q.Y), CombatRange.UnitSize(instance.Size, instance.Scale),
            CombatRange.PlayerUnitSize, SkillCastRangeRules.IsPlayerMoving(target.ConnectionInfo, now));
    }

    private uint BeginCast(GameClient target, long instanceId, MonsterInstance instance, uint monsterHandle,
        MonsterSkill skill, uint now, float? groundX = null, float? groundY = null)
    {
        var castTicks = BuffCurve.CastDelayTicks(skill.Fields, skill.Level);
        var cooldown = BuffCurve.CooldownTicks(skill.Fields, skill.Level);
        if (cooldown > 0)
        {
            _world.SetSkillCooldown(instanceId, skill.Fields.SkillId, unchecked(now + cooldown));
        }

        if (skill.Fields.Kind is SkillCastKind.PhysicalAttack or SkillCastKind.MagicAttack
            && skill.Fields.EffectType is not (0 or 101 or 201 or 231 or 30001))
            CastDamageSequence(target, instanceId, instance, monsterHandle, skill, now, castTicks, groundX, groundY);
        else Cast(target, instanceId, instance, monsterHandle, skill, now, castTicks);
        return castTicks;
    }

    private void CastDamageSequence(GameClient primary, long id, MonsterInstance instance, uint primaryHandle,
        MonsterSkill skill, uint now, uint delay, float? groundX, float? groundY)
    {
        var fields = skill.Fields;
        var area = SkillAreaRules.Area(fields);
        var ground = fields.EffectType == 271;
        var count = SkillAreaRules.FireCount(fields, skill.Level);
        var info = primary.ConnectionInfo;
        var layer = info.Layer;
        var targetHandle = info.CharacterHandle;
        var initialPosition = SkillCastRangeRules.PlayerPosition(info, now);
        var (fixedX, fixedY) = (groundX ?? initialPosition.X, groundY ?? initialPosition.Y);
        var life = _world.LifeVersion(id);
        uint start = 0;
        var created = false;
        float groundMagic = 0;
        void Send(SkillPacketType type, IReadOnlyList<SkillHit> hits = null)
        {
            var recipients = _players?.Registry.Clients ?? new[] { primary };
            foreach (var recipient in recipients)
            {
                var handle = _players is null && ReferenceEquals(recipient, primary)
                    ? primaryHandle : recipient.ConnectionInfo.GetMonsterHandle(id);
                if (recipient.ConnectionInfo.Layer != layer) continue;
                var known = (hits ?? Array.Empty<SkillHit>()).Where(h =>
                {
                    if (h.TargetHandle == recipient.ConnectionInfo.CharacterHandle) return true;
                    lock (recipient.ConnectionInfo.PlayerVisibilityLock)
                        return recipient.ConnectionInfo.SpawnedPlayers.ContainsKey(h.TargetHandle);
                }).ToArray();
                if (handle == 0 && known.Length == 0) continue;
                recipient.Connection.Send(GameSkillPackets.BuildSkill((ushort)fields.SkillId, (byte)skill.Level,
                    handle, fields.RequiredTarget == 0 ? handle : targetHandle, fixedX, fixedY, info.Z, (byte)layer, type,
                    0, 0, _world.GetHp(id), 0, type == SkillPacketType.Casting ? delay : 0,
                    hits: known, multiple: SkillAreaRules.IsSequential(fields.EffectType), range: area.Radius,
                    fireCount: SkillAreaRules.IsAtOnceMultiple(fields.EffectType) ? (byte)count : (byte)1));
            }
        }
        Send(SkillPacketType.Casting);
        lock (_gate) _casting.Add((id, life));
        bool Fire(uint tick)
        {
            if (!_world.IsAlive(id) || _world.LifeVersion(id) != life) return false;
            if (!ground && (info.CharacterHp <= 0 || info.CharacterHandle != targetHandle || info.Layer != layer)) return false;
            if (!ground && _players is not null && (!_players.Registry.TryResolve(targetHandle, out var current)
                || !ReferenceEquals(current, primary))) return false;
            if (ground)
            {
                if (!created)
                {
                    created = true; start = tick;
                    groundMagic = _combat.GetMonsterStats(id)?.MagicPoint ?? 0;
                    _effects.Track(new GroundSkillProp(fixedX, fixedY, info.Z, layer, tick,
                        SkillAreaRules.Duration(fields, skill.Level), fields.SkillId,
                        () => _players?.Registry.Clients ?? new[] { primary },
                        recipient => _players is null && ReferenceEquals(recipient, primary)
                            ? primaryHandle : recipient.ConnectionInfo.GetMonsterHandle(id),
                        () => _world.IsAlive(id) && _world.LifeVersion(id) == life), tick);
                    Send(SkillPacketType.Fire); Send(SkillPacketType.Complete);
                    lock (_gate) _casting.Remove((id, life));
                }
                if (unchecked((int)(tick - start)) > SkillAreaRules.Duration(fields, skill.Level)) return false;
            }
            var (cx, cy) = _world.GetPosition(id);
            var (tx, ty) = ground || groundX.HasValue ? (fixedX, fixedY) : fields.RequiredTarget == 0 ? (cx, cy)
                : SkillCastRangeRules.PlayerPosition(info, tick);
            var stats = _combat.GetMonsterStats(id);
            var damage = SkillAreaRules.Damage(fields, skill.Level, stats?.AttackPointRight ?? 0,
                ground ? groundMagic : stats?.MagicPoint ?? 0);
            var targets = new List<GameClient> { primary };
            if (SkillAreaRules.IsArea(fields.EffectType))
            {
                var candidates = _players?.Registry.Clients ?? new[] { primary };
                targets = SkillAreaRules.Select(candidates.Where(c => c.ConnectionInfo.CharacterHp > 0
                    && c.ConnectionInfo.Layer == layer),
                    area, cx, cy, tx, ty, c => SkillCastRangeRules.PlayerPosition(c.ConnectionInfo, tick), ref damage, _random);
            }
            var hits = new List<SkillHit>();
            foreach (var target in targets.Take(255))
            {
                var repetitions = SkillAreaRules.IsAtOnceMultiple(fields.EffectType) ? count : 1;
                for (var i = 0; i < repetitions && target.ConnectionInfo.CharacterHp > 0; i++)
                {
                    var hit = _combat.RollElementalMonsterHit(id, target, damage,
                        fields.Kind == SkillCastKind.MagicAttack ? DamageKind.Magical : DamageKind.Physical,
                        SkillDamageCurve.HitBonus(fields, instance.Level, target.ConnectionInfo.CharacterLevel),
                        SkillDamageCurve.CriticalBonus(fields, skill.Level), fields.ElementalType);
                    // The same landing as a swing: mana shield, reflections and the duel it interrupts.
                    var hp = _combat.DamagePlayer(target, hit.Damage, id, fields.Kind == SkillCastKind.MagicAttack);
                    hits.Add(new SkillHit(fields.Kind == SkillCastKind.MagicAttack ? SkillHitType.MagicDamage : SkillHitType.Damage,
                        target.ConnectionInfo.CharacterHandle, hp, hit.Damage, (byte)hit.Flags, ElementalType: (byte)fields.ElementalType));
                    if (hp <= 0) break;
                }
            }
            Send(ground ? SkillPacketType.RegionFire : SkillPacketType.Fire, hits);
            return true;
        }
        void Finish()
        {
            if (!ground || !created) lock (_gate) _casting.Remove((id, life));
            if (_world.IsAlive(id) && _world.LifeVersion(id) == life && (!ground || !created)) Send(SkillPacketType.Complete);
        }
        _effects.Schedule(unchecked(now + delay), SkillAreaRules.Interval(fields),
            SkillAreaRules.IsSequential(fields.EffectType) || ground ? count : 1, Fire, Finish);
        if (delay == 0) _effects.Tick(now);
    }

    private void Cast(GameClient client, long instanceId, MonsterInstance instance, uint monsterHandle,
        MonsterSkill skill, uint now, uint castTicks)
    {
        var info = client.ConnectionInfo;
        var (mx, my) = _world.GetPosition(instanceId);
        var (x, y, z) = skill.OnSelf ? (mx, my, instance.Z) : (info.X, info.Y, info.Z);

        // One frame per recipient: the monster's handle is that client's, and so is the target's when the
        // monster casts on itself. The players who see the target watch the cast too.
        byte[] Frame(uint handle, SkillPacketType type, uint delay, SkillHit? hit)
        {
            var target = skill.OnSelf ? handle : info.CharacterHandle;
            var rehit = hit is { } h && skill.OnSelf ? h with { TargetHandle = handle } : hit;
            return GameSkillPackets.BuildSkill((ushort)skill.Fields.SkillId,
                (byte)Math.Clamp(skill.Level, 0, byte.MaxValue), handle, target, x, y, z, (byte)info.Layer, type, 0,
                0, _world.GetHp(instanceId), 0, delay, 0, rehit);
        }

        void Send(SkillPacketType type, uint delay = 0, SkillHit? hit = null)
        {
            client.Connection.Send(Frame(monsterHandle, type, delay, hit));
            ObserverFrames.SendMonsterFrame(_players, client, instanceId,
                (_, handle) => Frame(handle, type, delay, hit));
        }

        Send(SkillPacketType.Casting, castTicks);
        var fire = Apply(client, instanceId, instance, monsterHandle, skill, now);
        Send(SkillPacketType.Fire, 0, fire);
        Send(SkillPacketType.Complete);

        _logger.Debug("Monster {instanceId} cast {effect} {skillId} level {level} on {target}", instanceId,
            skill.Effect, skill.Fields.SkillId, skill.Level, skill.OnSelf ? "itself" : client.ClientTag);
    }

    private SkillHit? Apply(GameClient client, long instanceId, MonsterInstance instance, uint monsterHandle,
        MonsterSkill skill, uint now)
    {
        var info = client.ConnectionInfo;
        var fields = skill.Fields;

        switch (skill.Effect)
        {
            case MonsterSkillEffect.State when skill.OnSelf:
            {
                var duration = BuffCurve.DurationTicks(fields, skill.Level);
                if (duration == 0)
                {
                    return null;
                }

                var stateLevel = BuffCurve.StateLevel(fields, skill.Level);
                var state = _world.AddState(instanceId, fields.StateId, fields.SkillId, stateLevel, now,
                    unchecked(now + duration));
                client.Connection.Send(GameSkillPackets.BuildState(monsterHandle, state.StateHandle,
                    (uint)state.StateId, (ushort)stateLevel, state.EndTick, now));
                ObserverFrames.SendMonsterFrame(_players, client, instanceId, (_, watcherHandle) =>
                    GameSkillPackets.BuildState(watcherHandle, state.StateHandle, (uint)state.StateId,
                        (ushort)stateLevel, state.EndTick, now));
                return null;
            }

            case MonsterSkillEffect.State:
            {
                var duration = BuffCurve.DurationTicks(fields, skill.Level);
                var chance = Casting.CastRules.StateLandingChance(fields.EffectType,
                    _combat.GetMonsterStats(instanceId)?.MagicAccuracy ?? 0f,
                    _combat.GetPlayerStats(client)?.MagicAvoid ?? 0f,
                    SkillDamageCurve.HitBonus(fields, instance.Level, info.CharacterLevel), fields.ProbabilityOnHit,
                    fields.ProbabilityIncBySlv, skill.Level);
                if (duration > 0 && !info.IsImmortal && Casting.CastRules.StateLands(chance, _random.Next(100)))
                {
                    _skillCast.ApplyState(client, fields.StateId, BuffCurve.StateLevel(fields, skill.Level),
                        duration);
                }

                return null;
            }

            case MonsterSkillEffect.Heal:
            {
                var stats = _combat.GetMonsterStats(instanceId);
                var amount = HealCurve.Amount(fields.Vars, skill.Level, stats?.MagicPoint ?? 0f, instance.Hp);
                var healed = _world.Heal(instanceId, amount);
                return new SkillHit(SkillHitType.AddHp, monsterHandle, _world.GetHp(instanceId), healed);
            }

            default:
            {
                var stats = _combat.GetMonsterStats(instanceId);
                var magical = fields.Kind == SkillCastKind.MagicAttack;
                var baseDamage = MonsterSkillRules.BaseDamage(skill, stats?.AttackPointRight ?? 0f,
                    stats?.MagicPoint ?? 0f);
                var hit = _combat.RollElementalMonsterHit(instanceId, client, baseDamage,
                    magical ? DamageKind.Magical : DamageKind.Physical,
                    SkillDamageCurve.HitBonus(fields, instance.Level, info.CharacterLevel),
                    SkillDamageCurve.CriticalBonus(fields, skill.Level), fields.ElementalType);

                _combat.DamagePlayer(client, hit.Damage, instanceId, magical);
                return new SkillHit(magical ? SkillHitType.MagicDamage : SkillHitType.Damage, info.CharacterHandle,
                    info.CharacterHp, hit.Damage, (byte)hit.Flags, ElementalType: (byte)fields.ElementalType);
            }
        }
    }
}
