using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Casting;
using Navislamia.Game.Services.Creatures;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services;

public partial class SkillCastService
{
    private sealed class SummonCast
    {
        public GameClient Master;
        public SummonPresence Actor;
        public CreatureCard Card;
        public PendingCast Cast;
        public DamageTarget DamageTarget;
        public int TargetLife;
        public bool Started;
        public bool GroundCreated;
        public float GroundX, GroundY, GroundMagic;
        public uint GroundStart;
    }

    private readonly object _summonCastGate = new();
    private readonly Dictionary<(GameClient, uint), SummonCast> _summonCasts = new();

    private bool SummonCasterAlive(SummonCast cast)
    {
        if (cast.Master.ConnectionInfo.CharacterHandle == 0 || cast.Master.ConnectionInfo.CharacterHp <= 0
            || cast.Actor.Hp <= 0 || cast.Master.ConnectionInfo.RideHandle == cast.Actor.Handle
            || cast.Actor.Layer != cast.Master.ConnectionInfo.Layer
            || !cast.Master.ConnectionInfo.Summons.Any(s => ReferenceEquals(s, cast.Actor))) return false;
        lock (_lock) return _clients.Contains(cast.Master);
    }

    private bool SummonTargetAllowed(GameClient master, SummonPresence actor, SupportTarget target, CastableBuffFields fields)
    {
        if (target.Hp <= 0 || target.Layer != actor.Layer || _combatService.ArePlayerEnemies(master, target.Owner)) return false;
        if (target.Summon is null ? !fields.UseOnCharacter : !fields.UseOnSummon) return false;
        return target.Handle == actor.Handle ? fields.UseOnSelf
            : SameParty(master, target.Owner) ? fields.UseOnParty : fields.UseOnNeutral;
    }

    private bool SummonCanCast(SummonPresence actor)
    {
        lock (actor.BuffLock)
            return !actor.ActiveBuffs.Any(s =>
            {
                var rule = _stateCatalog.GetRule(s.StateId);
                return CastRules.InterruptsCasting(s.StateId, rule.EffectType, rule.Values);
            });
    }

    private SupportTarget SummonAnchor(GameClient master, SummonPresence actor, uint handle) =>
        handle == 0 || handle == actor.Handle ? new SupportTarget(master, actor) : SupportAnchor(master, handle);

    private bool SummonRange(SummonCast cast, uint now)
    {
        var f = cast.Cast.Fields;
        var p = cast.Actor.PositionAt(now);
        var size = _creatures?.TryGetSummonTarget(cast.Master, cast.Actor.Handle, out var summon) == true
            ? CombatRange.UnitSize(summon.Size, summon.Scale) : CombatRange.PlayerUnitSize;
        if (f.RequiredTarget is 0 or 2 && SkillAreaRules.IsArea(f.EffectType))
            return f.RequiredTarget == 0 || SkillCastRangeRules.GroundInRange(f.CastRange, cast.Actor.Stats.AttackRange,
                CombatRange.Distance(p.X, p.Y, cast.Cast.Request.X, cast.Cast.Request.Y));
        var target = cast.DamageTarget;
        var q = target is not null ? DamagePosition(target, now)
            : SummonAnchor(cast.Master, cast.Actor, cast.Cast.Request.Target)?.Position(now) ?? p;
        var targetSize = target?.Monster is { } monster ? CombatRange.UnitSize(monster.Size, monster.Scale) : CombatRange.PlayerUnitSize;
        return SkillCastRangeRules.InRange(f.CastRange, cast.Actor.Stats.AttackRange, CombatRange.Distance(p.X, p.Y, q.X, q.Y),
            size, targetSize, target?.Monster is not null && _monsterState.IsMoving(target.Id));
    }

    private void CastSummon(GameClient master, GameActionPackets.SkillRequest request)
    {
        var info = master.ConnectionInfo; var now = ServerClock.Now;
        var actor = Array.Find(info.Summons, s => s.Handle == request.Caster);
        void Fail(ResultCode code) => master.Connection.Send(GameSkillPackets.BuildSkill(request.SkillId, request.SkillLevel,
            request.Caster, request.Target, request.X, request.Y, request.Z, (byte)request.Layer, SkillPacketType.Casting,
            0, 0, actor?.Hp ?? 0, actor?.Mp ?? 0, errorCode: (ushort)code));
        // onSkill: a dead master's request is dropped; a caster that is not one of its summons does not exist.
        if (info.CharacterHp <= 0) return;
        if (actor is null) { Fail(ResultCode.NotExist); return; }
        CreatureCard card;
        lock (info.SummonLock) card = info.CreatureCards.Values.FirstOrDefault(c => c.SummonHandle == actor.Handle);
        if (card is null || !_catalog.TryGet(request.SkillId, out var fields)) { Fail(ResultCode.AccessDenied); return; }
        byte learned;
        lock (info.SummonLock) learned = card.Skills.GetValueOrDefault(request.SkillId);
        if (learned == 0) { Fail(ResultCode.AccessDenied); return; }
        // onSkill: a level outside 1..learned is brought back to the learned one.
        var level = request.SkillLevel >= 1 && request.SkillLevel <= learned ? request.SkillLevel : learned;
        request = request with { SkillLevel = level };
        if (card.Level < fields.RequiredLevel) { Fail(ResultCode.NotActable); return; }
        if (request.Layer != actor.Layer || !float.IsFinite(request.X) || !float.IsFinite(request.Y) || !float.IsFinite(request.Z))
        { Fail(ResultCode.InvalidArgument); return; }
        if (fields.Kind is SkillCastKind.Summon or SkillCastKind.Unsummon or SkillCastKind.Taming or SkillCastKind.ActivateProp
            or SkillCastKind.PropHeal)
        { Fail(ResultCode.AccessDenied); return; }
        if (fields.Target == 101) request = request with { Target = info.CharacterHandle };
        if (fields.Target == 102) request = request with { Target = actor.Handle };
        var pending = new SummonCast
        {
            Master = master,
            Actor = actor,
            Card = card,
            Cast = new PendingCast(request, fields, level, -1, now, unchecked(now + BuffCurve.CastDelayTicks(fields, level)))
        };
        if (!SummonCasterAlive(pending) || !SummonCanCast(actor)) { Fail(ResultCode.NotActable); return; }
        var offensive = fields.Kind is SkillCastKind.PhysicalAttack or SkillCastKind.MagicAttack or SkillCastKind.Debuff;
        if (offensive && !(SkillAreaRules.IsArea(fields.EffectType) && fields.RequiredTarget is 0 or 2))
        {
            if (info.TryResolveMonster(request.Target, out var id) && _monsterState.TryGetInstance(id, out var monster))
            {
                if (monster.Layer != actor.Layer || !_monsterState.IsAlive(id) || _monsterState.GetHp(id) <= 0)
                { Fail(ResultCode.NotActable); return; }
                pending.DamageTarget = new DamageTarget(id, monster); pending.TargetLife = _monsterState.LifeVersion(id);
            }
            else if (TryHostilePlayer(master, request.Target, fields, out var player, out var error))
                pending.DamageTarget = new DamageTarget(PlayerTargetId(request.Target), Player: player);
            else { Fail(error); return; }
        }
        else if (!offensive)
        {
            if (fields.Kind == SkillCastKind.Resurrection)
            {
                if (!TryResolveDeadPlayer(info, request.Target, out _, out var error)) { Fail(error); return; }
                var target = SupportAnchor(master, request.Target);
                if (target is null || target.Layer != actor.Layer || !fields.UseOnCharacter
                    || _combatService.ArePlayerEnemies(master, target.Owner)
                    || !(SameParty(master, target.Owner) ? fields.UseOnParty : fields.UseOnNeutral))
                { Fail(ResultCode.NotActable); return; }
            }
            else
            {
                var anchor = SummonAnchor(master, actor, request.Target);
                if (anchor is null || (fields.Target is 21 or 32 or 45 or 51 or 102
                    ? anchor.Hp <= 0 || anchor.Layer != actor.Layer || _combatService.ArePlayerEnemies(master, anchor.Owner)
                    : !SummonTargetAllowed(master, actor, anchor, fields))) { Fail(ResultCode.NotActable); return; }
                if (fields.Target is 21 or 32 or 51 && !SameParty(master, anchor.Owner)) { Fail(ResultCode.NotOwn); return; }
            }
        }
        if (!SummonRange(pending, now)) { Fail(ResultCode.TooFar); return; }
        lock (_summonCastGate)
        {
            if (_summonCasts.ContainsKey((master, actor.Handle))) { Fail(ResultCode.NotActable); return; }
            lock (info.SummonLock)
                lock (actor.BuffLock)
                {
                    if (card.SkillCooldowns.TryGetValue(request.SkillId, out var ready) && unchecked((int)(now - ready)) < 0
                        || card.CommonSkillReady != 0 && unchecked((int)(now - card.CommonSkillReady)) < 0)
                    { Fail(ResultCode.CoolTime); return; }
                    var cost = BuffCurve.MpCost(fields, level);
                    if (actor.Mp < cost) { Fail(ResultCode.NotEnoughMP); return; }
                    actor.Mp -= cost;
                    card.SkillCooldowns[request.SkillId] = unchecked(now + BuffCurve.CooldownTicks(fields, level));
                    card.SkillCooldownDurations[request.SkillId] = BuffCurve.CooldownTicks(fields, level);
                    card.CommonSkillReady = unchecked(now + BuffCurve.CommonDelayTicks(fields));
                }
            _summonCasts.Add((master, actor.Handle), pending);
        }
        _creatures?.SummonAttack(master, actor.Handle, 0);
        SaveSummonCastVitals(pending);
        SendSummonSkill(pending, SkillPacketType.Casting, mpCost: BuffCurve.MpCost(fields, level),
            delay: BuffCurve.CastDelayTicks(fields, level));
        if (pending.Cast.FireTick == now) FireSummon(pending, now);
    }

    private void ProcessSummonCasts(uint now)
    {
        SummonCast[] casts; lock (_summonCastGate) casts = _summonCasts.Values.ToArray();
        foreach (var cast in casts)
        {
            if (!SummonCasterAlive(cast)) { CancelSummonCast(cast.Master, cast.Actor.Handle, true); continue; }
            if (!cast.Started && unchecked((int)(now - cast.Cast.FireTick)) >= 0)
                try { FireSummon(cast, now); }
                catch (Exception exception)
                {
                    _logger.Error(exception, "Summon skill {skillId} failed for {clientTag}", cast.Cast.Request.SkillId, cast.Master.ClientTag);
                    CancelSummonCast(cast.Master, cast.Actor.Handle, true);
                }
        }
    }

    private void FireSummon(SummonCast cast, uint now)
    {
        lock (_summonCastGate)
        {
            if (cast.Started || !_summonCasts.TryGetValue((cast.Master, cast.Actor.Handle), out var active) || !ReferenceEquals(active, cast)) return;
            cast.Started = true;
        }
        var fields = cast.Cast.Fields;
        if (!SummonCasterAlive(cast) || !SummonCanCast(cast.Actor) || !SummonRange(cast, now))
        { CancelSummonCast(cast.Master, cast.Actor.Handle, true); return; }
        if (fields.Kind is SkillCastKind.PhysicalAttack or SkillCastKind.MagicAttack)
        {
            var count = SkillAreaRules.FireCount(fields, cast.Cast.SkillLevel);
            if (fields.EffectType == 271)
            {
                var p = cast.Actor.PositionAt(now);
                var q = cast.DamageTarget is { } target ? DamagePosition(target, now)
                    : fields.RequiredTarget == 0 ? p : (cast.Cast.Request.X, cast.Cast.Request.Y);
                cast.GroundX = q.Item1; cast.GroundY = q.Item2; cast.GroundMagic = cast.Actor.Stats.MagicPoint;
            }
            _effects.Schedule(now, SkillAreaRules.Interval(fields),
                SkillAreaRules.IsSequential(fields.EffectType) || fields.EffectType == 271 ? count : 1,
                tick => FireSummonDamage(cast, tick, count), () => CompleteSummonCast(cast));
            _effects.Tick(now); return;
        }
        var hits = new List<(long Id, SkillHit Hit)>();
        if (fields.Kind is SkillCastKind.Heal or SkillCastKind.Buff or SkillCastKind.Aura)
        {
            var anchor = SummonAnchor(cast.Master, cast.Actor, cast.Cast.Request.Target);
            if (anchor is null || anchor.Hp <= 0 || anchor.Layer != cast.Actor.Layer
                || _combatService.ArePlayerEnemies(cast.Master, anchor.Owner)
                || fields.Target is 21 or 32 or 51 && !SameParty(cast.Master, anchor.Owner))
            { CancelSummonCast(cast.Master, cast.Actor.Handle, true); return; }
        }
        if (fields.Kind == SkillCastKind.Debuff)
        {
            if (cast.DamageTarget is null || !DamageTargetAlive(cast.Master, fields, cast.DamageTarget)
                || cast.DamageTarget.Monster is not null && _monsterState.LifeVersion(cast.DamageTarget.Id) != cast.TargetLife)
            { CancelSummonCast(cast.Master, cast.Actor.Handle, true); return; }
            ApplySummonDebuff(cast, now);
        }
        else if (fields.Kind == SkillCastKind.Resurrection)
        {
            var hit = ResurrectPlayer(fields, cast.Cast.SkillLevel, cast.Cast.Request.Target);
            if (hit is null) { CancelSummonCast(cast.Master, cast.Actor.Handle, true); return; }
            hits.Add((-1, hit.Value));
        }
        else if (fields.Kind == SkillCastKind.Aura) ToggleSummonAura(cast, now);
        else
        {
            foreach (var target in SummonSupportTargets(cast, now))
            {
                if (fields.Kind == SkillCastKind.Heal)
                    hits.Add((-1, HealSupport(cast.Master, target, fields, cast.Cast.SkillLevel,
                        cast.Actor.Stats.MagicPoint, cast.Actor.Handle)));
                else if (ApplySupportState(target, fields.StateId, fields.SkillId,
                    BuffCurve.StateLevel(fields, cast.Cast.SkillLevel), now,
                    unchecked(now + BuffCurve.DurationTicks(fields, cast.Cast.SkillLevel)), cast.Actor.Handle)) RefreshSupportStats(target);
            }
        }
        SendSummonSkill(cast, SkillPacketType.Fire, hits);
        CompleteSummonCast(cast);
    }

    private SupportTarget[] SummonSupportTargets(SummonCast cast, uint now)
    {
        var f = cast.Cast.Fields; var anchor = SummonAnchor(cast.Master, cast.Actor, cast.Cast.Request.Target);
        if (anchor is null) return Array.Empty<SupportTarget>();
        var all = SupportUnits(cast.Master);
        var area = IsRegionHeal(f) || f.EffectType == 302 || f.Target is 2 or 3 or 4 or 6 or 21 or 32 or 45 or 51;
        all = f.Target switch
        {
            21 => all.Where(t => t.Summon is null && SameParty(cast.Master, t.Owner)),
            32 => all.Where(t => t.Summon is not null && SameParty(cast.Master, t.Owner)),
            51 => all.Where(t => SameParty(cast.Master, t.Owner)),
            45 => all.Where(t => ReferenceEquals(t.Owner, cast.Master)),
            101 => new[] { new SupportTarget(cast.Master) },
            102 => all.Where(t => t.Handle == cast.Actor.Handle || t.Handle == cast.Master.ConnectionInfo.CharacterHandle),
            _ => area ? all : new[] { anchor }
        };
        var center = f.Target is 21 or 32 or 45 or 51 ? cast.Actor.PositionAt(now) : anchor.Position(now);
        if (area) all = all.Where(t => { var p = t.Position(now); return CombatRange.Distance(center.X, center.Y, p.X, p.Y) <= SupportRadius(f); });
        if (f.Target is 3 or 6) all = all.Where(t => t.Handle != cast.Actor.Handle);
        if (IsRegionHeal(f))
        {
            if (SupportVar(f, 11) == 2) all = all.Where(t => SameParty(cast.Master, t.Owner));
            if (SupportVar(f, 11) == 3) return Array.Empty<SupportTarget>();
            if (f.EffectType == 508 && SupportVar(f, 12) == 1) all = all.Where(t => t.Summon is null);
            if (f.EffectType == 508 && SupportVar(f, 12) == 2) all = all.Where(t => t.Summon is not null);
        }
        return all.Where(t => SummonTargetAllowed(cast.Master, cast.Actor, t, f)).DistinctBy(t => t.Handle).ToArray();
    }

    private bool FireSummonDamage(SummonCast cast, uint now, int count)
    {
        lock (_summonCastGate)
            if (!cast.GroundCreated && (!_summonCasts.TryGetValue((cast.Master, cast.Actor.Handle), out var active)
                || !ReferenceEquals(active, cast))) return false;
        if (!SummonCasterAlive(cast)) return false;
        var f = cast.Cast.Fields; var primary = cast.DamageTarget;
        if (cast.GroundCreated && unchecked((int)(now - cast.GroundStart)) > SkillAreaRules.Duration(f, cast.Cast.SkillLevel)) return false;
        if (f.EffectType != 271 && primary is not null && (!DamageTargetAlive(cast.Master, f, primary)
            || primary.Monster is not null && _monsterState.LifeVersion(primary.Id) != cast.TargetLife)) return false;
        var p = cast.Actor.PositionAt(now);
        var q = f.EffectType == 271 ? (X: cast.GroundX, Y: cast.GroundY) : primary is not null ? DamagePosition(primary, now)
            : f.RequiredTarget == 0 ? p : (cast.Cast.Request.X, cast.Cast.Request.Y);
        if (f.EffectType == 271 && !cast.GroundCreated)
        {
            cast.GroundCreated = true;
            cast.GroundStart = now;
            _effects.Track(new GroundSkillProp(cast.GroundX, cast.GroundY, cast.Cast.Request.Z, cast.Actor.Layer,
                now, SkillAreaRules.Duration(f, cast.Cast.SkillLevel), f.SkillId,
                () => _players?.Registry.Clients ?? new[] { cast.Master },
                recipient => ReferenceEquals(recipient, cast.Master) || SeesPlayer(recipient, cast.Master) ? cast.Actor.Handle : 0,
                () => SummonCasterAlive(cast)), now);
            SendSummonSkill(cast, SkillPacketType.Fire);
            CompleteSummonCast(cast);
        }
        var stats = cast.Actor.Stats;
        var damage = SkillAreaRules.Damage(f, cast.Cast.SkillLevel, stats.AttackPointRight,
            f.EffectType == 271 ? cast.GroundMagic : stats.MagicPoint);
        var targets = primary is null ? new List<DamageTarget>() : new List<DamageTarget> { primary };
        if (SkillAreaRules.IsArea(f.EffectType))
        {
            var area = SkillAreaRules.Area(f); var center = f.EffectType == 271 || area.TargetOrigin ? q : p;
            var candidates = _monsterState.WithinCurrentRange(center.X, center.Y, area.Radius)
                .Where(m => m.Layer == cast.Actor.Layer).Select(m => new DamageTarget(m.InstanceId, m))
                .Concat(HostileDamagePlayers(cast.Master, f).Where(t => t.Player.ConnectionInfo.Layer == cast.Actor.Layer));
            targets = SkillAreaRules.Select(candidates, area, p.X, p.Y, q.X, q.Y,
                t => DamagePosition(t, now), ref damage, _random);
        }
        var hits = new List<(long, SkillHit)>();
        foreach (var target in targets.Take(255))
            for (var i = 0; i < (SkillAreaRules.IsAtOnceMultiple(f.EffectType) ? count : 1); i++)
            {
                if (!DamageTargetAlive(cast.Master, f, target)) break;
                var defender = target.Player is { } player ? _statService.Compute(player.ConnectionInfo).Total
                    : _combatService.GetMonsterStats(target.Id) ?? new StatBlock();
                var targetLevel = target.Player?.ConnectionInfo.CharacterLevel ?? target.Monster.Value.Level;
                var kind = f.Kind == SkillCastKind.MagicAttack ? DamageKind.Magical : DamageKind.Physical;
                var hit = target.Player is { } opponent
                    ? _combatService.RollSummonHitOnPlayer(cast.Master, opponent, stats, cast.Card.Level, damage, kind,
                        SkillDamageCurve.HitBonus(f, cast.Card.Level, targetLevel), SkillDamageCurve.CriticalBonus(f, cast.Cast.SkillLevel), f.ElementalType)
                    : CombatFormulas.Resolve(Combatant.From(stats, cast.Card.Level), Combatant.From(defender, targetLevel),
                        damage, kind, SkillDamageCurve.HitBonus(f, cast.Card.Level, targetLevel),
                        SkillDamageCurve.CriticalBonus(f, cast.Cast.SkillLevel), _random, f.ElementalType);
                var hp = target.Player is { } victim ? _combatService.DamagePlayerBySummon(cast.Master, victim, cast.Actor.Handle,
                    hit.Damage, kind == DamageKind.Magical)
                    : _combatService.ApplyDamage(cast.Master, target.Id, cast.Master.ConnectionInfo.GetMonsterHandle(target.Id), hit.Damage, 0);
                if (target.Monster is not null && hp > 0) _monsterState.AddSummonHate(target.Id, cast.Master, cast.Actor.Handle,
                    HateRules.SkillHate(f.HateMod, f.HateBasic, f.HatePerSkl, cast.Cast.SkillLevel, hit.Damage));
                _ethereal?.Hit(cast.Master, true, hit.Damage, EtherealHit.Skill, cast.Card);
                hits.Add((target.Id, new SkillHit(kind == DamageKind.Magical ? SkillHitType.MagicDamage : SkillHitType.Damage,
                    DamageHandle(cast.Master, target.Id), hp, hit.Damage, (byte)hit.Flags, ElementalType: (byte)f.ElementalType)));
                if (hp <= 0) break;
            }
        SendSummonSkill(cast, f.EffectType == 271 ? SkillPacketType.RegionFire : SkillPacketType.Fire, hits,
            fireCount: (byte)(SkillAreaRules.IsAtOnceMultiple(f.EffectType) ? count : 1));
        return true;
    }

    private void ApplySummonDebuff(SummonCast cast, uint now)
    {
        var f = cast.Cast.Fields; var target = cast.DamageTarget;
        var defender = target.Player is { } player ? _statService.Compute(player.ConnectionInfo).Total : _combatService.GetMonsterStats(target.Id);
        var chance = CastRules.StateLandingChance(f.EffectType, cast.Actor.Stats.MagicAccuracy, defender?.MagicAvoid ?? 0,
            SkillDamageCurve.HitBonus(f, cast.Card.Level, target.Player?.ConnectionInfo.CharacterLevel ?? target.Monster.Value.Level),
            f.ProbabilityOnHit, f.ProbabilityIncBySlv, cast.Cast.SkillLevel);
        if (!CastRules.StateLands(chance, _random.Next(100))) return;
        var level = BuffCurve.StateLevel(f, cast.Cast.SkillLevel); var end = unchecked(now + BuffCurve.DurationTicks(f, cast.Cast.SkillLevel));
        if (target.Player is { } victim)
        {
            if (ApplySupportState(new SupportTarget(victim), f.StateId, f.SkillId, level, now, end, cast.Actor.Handle))
                RefreshSupportStats(new SupportTarget(victim));
            return;
        }
        _monsterState.AddSummonHate(target.Id, cast.Master, cast.Actor.Handle, HateRules.SkillHate(f.HateMod, f.HateBasic, f.HatePerSkl, cast.Cast.SkillLevel, 0));
        if (!_monsterState.TryAddState(target.Id, f.StateId, f.SkillId, level, now, end,
            _stateCatalog.GetRule(f.StateId), _stateCatalog.GetRule, out var state, out var removed, cast.Actor.Handle,
            NewStatePulse(f.StateId, now, cast.Actor.Handle, cast.Master))) return;
        foreach (var observer in SupportPlayers(cast.Master))
        {
            var handle = observer.ConnectionInfo.GetMonsterHandle(target.Id); if (handle == 0) continue;
            foreach (var old in removed) observer.Connection.Send(GameSkillPackets.BuildStateRemoval(handle, old.StateHandle, (uint)old.StateId));
            observer.Connection.Send(GameSkillPackets.BuildState(handle, state.StateHandle, (uint)state.StateId,
                (ushort)state.StateLevel, end, now));
        }
    }

    private void CompleteSummonCast(SummonCast cast)
    {
        lock (_summonCastGate)
        {
            if (!_summonCasts.TryGetValue((cast.Master, cast.Actor.Handle), out var active) || !ReferenceEquals(active, cast)) return;
            _summonCasts.Remove((cast.Master, cast.Actor.Handle));
        }
        if (SummonCasterAlive(cast)) SendSummonSkill(cast, SkillPacketType.Complete);
        uint cooldown; uint total; byte learned;
        lock (cast.Master.ConnectionInfo.SummonLock)
        {
            cooldown = (uint)Math.Max(0, unchecked((int)(cast.Card.SkillCooldowns.GetValueOrDefault(cast.Cast.Request.SkillId) - ServerClock.Now)));
            total = cast.Card.SkillCooldownDurations.GetValueOrDefault(cast.Cast.Request.SkillId);
            learned = cast.Card.Skills.GetValueOrDefault(cast.Cast.Request.SkillId);
        }
        cast.Master.Connection.Send(GameCharacterPackets.BuildSkillList(cast.Actor.Handle,
            new[] { new SkillListEntry(cast.Cast.Request.SkillId, learned, total, cooldown) }));
    }

    private void SaveSummonCastVitals(SummonCast cast) =>
        _creatures?.SetSummonVitals(cast.Master, cast.Card, cast.Actor.Hp, cast.Actor.Mp);

    private void SendSummonSkill(SummonCast cast, SkillPacketType type, IReadOnlyList<(long Id, SkillHit Hit)> hits = null,
        int mpCost = 0, uint delay = 0, byte fireCount = 1)
    {
        var request = cast.Cast.Request; var f = cast.Cast.Fields;
        byte[] Frame(GameClient observer)
        {
            var mapped = (hits ?? Array.Empty<(long, SkillHit)>()).Select(h => h.Id == -1 ? h.Hit
                : h.Hit with { TargetHandle = DamageHandle(observer, h.Id) }).Where(h => h.TargetHandle != 0).ToArray();
            var target = cast.DamageTarget is { } t ? DamageHandle(observer, t.Id) : request.Target == 0 ? cast.Actor.Handle : request.Target;
            return GameSkillPackets.BuildSkill(request.SkillId, request.SkillLevel, cast.Actor.Handle, target,
                cast.GroundCreated ? cast.GroundX : request.X, cast.GroundCreated ? cast.GroundY : request.Y,
                request.Z, (byte)request.Layer, type, 0, mpCost, cast.Actor.Hp, cast.Actor.Mp,
                castDelayTicks: delay, hits: mapped, multiple: SkillAreaRules.IsSequential(f.EffectType), range: SkillAreaRules.IsArea(f.EffectType)
                    ? SkillAreaRules.Area(f).Radius : SupportRadius(f), fireCount: fireCount);
        }
        cast.Master.Connection.Send(Frame(cast.Master));
        foreach (var observer in _players?.Observers(cast.Master) ?? Array.Empty<GameClient>())
            if (observer.ConnectionInfo.Layer == cast.Actor.Layer) observer.Connection.Send(Frame(observer));
    }

    public bool CancelSummonCast(GameClient client, uint handle, bool force = false)
    {
        SummonCast cast;
        lock (_summonCastGate)
        {
            if (!_summonCasts.TryGetValue((client, handle), out cast) || !force && !cast.Cast.Fields.Cancelable) return false;
            _summonCasts.Remove((client, handle));
        }
        SendSummonSkill(cast, SkillPacketType.Cancel); return true;
    }

    private void CancelAllSummonCasts(GameClient client)
    {
        uint[] handles; lock (_summonCastGate) handles = _summonCasts.Keys.Where(k => ReferenceEquals(k.Item1, client)).Select(k => k.Item2).ToArray();
        foreach (var handle in handles) CancelSummonCast(client, handle, true);
        foreach (var summon in client.ConnectionInfo.Summons) ForgetSummonCaster(client, summon.Handle);
    }

    public void OnSummonCasterDamaged(GameClient client, uint handle, int damage)
    {
        lock (_summonCastGate)
        {
            if (!_summonCasts.TryGetValue((client, handle), out var cast) || cast.Started) return;
            var disturbance = CastRules.DamageDisturbance(cast.Cast.Fields.CastingType, cast.Cast.Fields.CastingLevel,
                damage, (int)cast.Actor.Stats.MaxHp, (int)cast.Actor.Stats.CastingSpeed);
            if (disturbance <= 0) return;
            if (cast.Cast.Fields.CastingType == CastRules.Breakable)
            { if (_random.Next(100) < disturbance) CancelSummonCast(client, handle, true); }
            else
            {
                cast.Cast.FireTick = unchecked(cast.Cast.FireTick + (uint)disturbance);
                var request = cast.Cast.Request;
                client.Connection.Send(GameSkillPackets.BuildSkill(request.SkillId, request.SkillLevel, handle, request.Target,
                    request.X, request.Y, request.Z, (byte)request.Layer, SkillPacketType.CastingUpdate, 0, 0,
                    cast.Actor.Hp, cast.Actor.Mp, unchecked(cast.Cast.FireTick - cast.Cast.StartTick), 0));
            }
        }
    }
}
