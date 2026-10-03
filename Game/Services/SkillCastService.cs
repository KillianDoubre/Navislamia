using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Casting;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Props;
using Navislamia.Game.Services.Stats;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// Casting skills on players, their parties and summons, or monsters: states, auras, healing and damage.
/// <c>SkillService</c> is the one that <em>learns</em> them.
/// </summary>
/// <remarks>
/// The cast sequence mirrors the reference server's <c>Skill::ProcSkill</c> and is the same for every
/// kind: <c>ST_Casting</c> carrying the mp cost and the cast delay, then the effect and <c>ST_Fire</c>,
/// then <c>ST_Complete</c>, then the skill list so the client learns the cooldown. A failure answers a
/// single <c>ST_Casting</c> with an error code, which is what <c>SendSkillCastFailMessage</c> does. Only
/// the kind-specific effect differs, which is why <c>BuffCatalog</c> resolves the kind once at startup.
/// <para>
/// Damage goes through <see cref="ICombatService"/> so that an auto-attack and an attack skill share one
/// damage rule and one death path; this service must never reimplement either.
/// </para>
/// <para>
/// Like <c>GroundItemService</c>, this must not depend on <c>NetworkService</c>: that is a DI cycle which
/// builds fine and only throws at runtime.
/// </para>
/// </remarks>
public partial class SkillCastService : ISkillCastService
{
    private const int TickIntervalMs = 500;

    /// <summary>How often the casts in progress are looked at: a cast fires at most this late.</summary>
    private const int CastTickIntervalMs = 50;

    /// <summary>An aura never expires; it stays until the player toggles it off.</summary>
    private const uint NeverExpires = uint.MaxValue;

    private readonly ILogger _logger = Log.ForContext<SkillCastService>();
    private readonly IBuffCatalog _catalog;
    private readonly IStatService _statService;
    private readonly IStateCatalog _stateCatalog;
    private readonly MonsterWorldState _monsterState;
    private readonly ICombatService _combatService;
    private readonly IFieldPropCatalog _fieldPropCatalog;
    private readonly IWarpService _warpService;
    private readonly IPlayerVisibilityService _players;
    private readonly SkillEffectScheduler _effects;
    private readonly HashSet<GameClient> _casting = new();
    private readonly ICombatRandom _random;
    private readonly ILevelingService _leveling;

    /// <summary>Taming (4003), summoning (4001) and sending back (4002).</summary>
    private readonly Creatures.ICreatureService _creatures;
    private readonly IBuffPersistence _buffPersistence;
    private readonly object _lock = new();
    private readonly List<GameClient> _clients = new();

    public SkillCastService(IBuffCatalog catalog, IStatService statService, IStateCatalog stateCatalog,
        MonsterWorldState monsterState,
        ICombatService combatService, IFieldPropCatalog fieldPropCatalog, IWarpService warpService,
        IPlayerVisibilityService players = null, SkillEffectScheduler effects = null,
        CastInterrupts interrupts = null, ICombatRandom random = null, bool runTicks = true,
        ILevelingService leveling = null, IBuffPersistence buffPersistence = null,
        Creatures.ICreatureService creatures = null)
    {
        _creatures = creatures;
        _leveling = leveling;
        _buffPersistence = buffPersistence;
        _players = players;
        _effects = effects ?? new SkillEffectScheduler(runTicks);
        _random = random ?? CombatRandom.Shared;
        interrupts?.Attach(this);
        _catalog = catalog;
        _statService = statService;
        _stateCatalog = stateCatalog;
        _monsterState = monsterState;
        _combatService = combatService;
        _fieldPropCatalog = fieldPropCatalog;
        _warpService = warpService;

        if (_catalog.Count == 0)
        {
            _logger.Warning("The skill catalog is empty; casting will be unavailable");
        }

        if (runTicks)
        {
            _ = RunAsync();
            _ = RunCastsAsync();
        }
    }

    public void Register(GameClient client)
    {
        lock (_lock)
        {
            if (!_clients.Contains(client))
            {
                _clients.Add(client);
            }
        }
    }

    public void Unregister(GameClient client)
    {
        CancelAllSummonCasts(client);
        RemoveAllAuraProjections(client);
        lock (_lock)
        {
            _clients.Remove(client);
            _casting.Remove(client);
        }

        lock (client.ConnectionInfo.CastLock)
        {
            client.ConnectionInfo.PendingCast = null;
        }
    }

    public void Cast(GameClient client, GameActionPackets.SkillRequest request)
    {
        var info = client.ConnectionInfo;
        if (request.Caster != info.CharacterHandle && request.Caster != 0)
        { CastSummon(client, request); return; }
        var now = ServerClock.Now;

        lock (_lock)
            if (_casting.Contains(client))
            { SendCastFailed(client, request, ResultCode.NotActable); return; }

        if (!TryValidate(client, request, now, out var fields, out var targetInstanceId, out var skillLevel,
                out var error))
        {
            SendCastFailed(client, request, error);
            return;
        }

        if (!InCastRange(info, fields, targetInstanceId, now))
        {
            SendCastFailed(client, request, ResultCode.TooFar);
            return;
        }

        var castDelay = BuffCurve.CastDelayTicks(fields, skillLevel);
        request = request with { SkillLevel = skillLevel };
        var pending = new PendingCast(request, fields, skillLevel, targetInstanceId, now, unchecked(now + castDelay))
        { PlayerTarget = ResolvePlayerTarget(targetInstanceId) };

        // One cast at a time: a second request while one is being cast is refused, never queued.
        lock (info.CastLock)
        {
            if (info.PendingCast is not null)
            {
                SendCastFailed(client, request, ResultCode.NotActable);
                return;
            }

            if (castDelay > 0)
            {
                info.PendingCast = pending;
            }
        }

        var mpCost = BuffCurve.MpCost(fields, skillLevel);
        info.CharacterMp -= mpCost;
        var cooldown = BuffCurve.CooldownTicks(fields, skillLevel);
        if (cooldown > 0)
        {
            info.SkillCooldowns[request.SkillId] = unchecked(now + cooldown);
        }

        SendSkill(client, request, fields.Kind, targetInstanceId, SkillPacketType.Casting, mpCost, castDelay);

        if (castDelay == 0)
        {
            Fire(client, pending);
        }
    }

    /// <summary>
    /// Fires every cast whose time has come. The tick calls it; the tests call it with their own clock.
    /// </summary>
    public void ProcessCasts(uint now)
    {
        ProcessSummonCasts(now);
        GameClient[] clients;
        lock (_lock)
        {
            if (_clients.Count == 0)
            {
                return;
            }

            clients = _clients.ToArray();
        }

        foreach (var client in clients)
        {
            PendingCast due;
            lock (client.ConnectionInfo.CastLock)
            {
                due = client.ConnectionInfo.PendingCast;
                if (due is null || unchecked((int)(now - due.FireTick)) < 0)
                {
                    continue;
                }

                client.ConnectionInfo.PendingCast = null;
            }

            try
            {
                Fire(client, due);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Skill {skillId} of {clientTag} failed to fire", due.Request.SkillId,
                    client.ClientTag);
            }
        }
    }

    public bool CancelCast(GameClient client)
    {
        var info = client.ConnectionInfo;
        PendingCast cancelled;
        lock (info.CastLock)
        {
            cancelled = info.PendingCast;

            // StructSkill::Cancel refuses a skill that is not flagged cancellable.
            if (cancelled is null || !cancelled.Fields.Cancelable)
            {
                return false;
            }

            info.PendingCast = null;
        }

        SendSkill(client, cancelled.Request, cancelled.Fields.Kind, cancelled.TargetInstanceId,
            SkillPacketType.Cancel, 0, 0);
        _logger.Debug("{clientTag} cast of {skillId} was cancelled", client.ClientTag, cancelled.Request.SkillId);
        return true;
    }

    public void OnCasterDamaged(GameClient client, int damage)
    {
        var info = client.ConnectionInfo;
        PendingCast pending;
        lock (info.CastLock)
        {
            pending = info.PendingCast;
        }

        if (pending is null)
        {
            return;
        }

        var castingSpeed = (int)_statService.Compute(info).Total.CastingSpeed;
        var disturbance = CastRules.DamageDisturbance(pending.Fields.CastingType, pending.Fields.CastingLevel,
            damage, info.CharacterMaxHp, castingSpeed);
        if (disturbance <= 0)
        {
            return;
        }

        if (pending.Fields.CastingType == CastRules.Breakable)
        {
            if (_random.Next(100) < disturbance)
            {
                CancelCast(client);
            }

            return;
        }

        uint total;
        lock (info.CastLock)
        {
            if (!ReferenceEquals(info.PendingCast, pending))
            {
                return;
            }

            pending.FireTick = unchecked(pending.FireTick + (uint)disturbance);
            total = unchecked(pending.FireTick - pending.StartTick);
        }

        // ST_CastingUpdate goes to the caster alone (PendMessage in StructSkill::onDamage).
        client.Connection.Send(GameSkillPackets.BuildSkill((ushort)pending.Request.SkillId,
            pending.Request.SkillLevel, info.CharacterHandle, pending.Request.Target, pending.Request.X,
            pending.Request.Y, pending.Request.Z, (byte)pending.Request.Layer, SkillPacketType.CastingUpdate, 0, 0,
            info.CharacterHp, info.CharacterMp, total, 0));
    }

    /// <summary>
    /// The effect, <c>ST_Fire</c>, <c>ST_Complete</c> and the cooldown in the skill list. A caster who died, or
    /// a monster target that died while the spell was being cast, drops it instead.
    /// </summary>
    private void Fire(GameClient client, PendingCast cast)
    {
        var info = client.ConnectionInfo;
        var request = cast.Request;
        var fields = cast.Fields;
        var skillLevel = cast.SkillLevel;
        var targetInstanceId = cast.TargetInstanceId;
        var now = ServerClock.Now;

        if (info.CharacterHp <= 0)
        {
            return;
        }

        if (IsPlayerTarget(targetInstanceId) && (!TryHostilePlayer(client, PlayerTargetHandle(targetInstanceId), fields, out var currentTarget, out _)
            || !ReferenceEquals(currentTarget, cast.PlayerTarget) || !PlayerCastInRange(info, fields, currentTarget, now)))
        {
            SendSkill(client, request, fields.Kind, targetInstanceId, SkillPacketType.Cancel, 0, 0);
            return;
        }

        // A ground area has no monster target (-1): only a cast at a monster is dropped with it.
        if (TargetsAMonster(fields.Kind) && targetInstanceId >= 0 && !_monsterState.IsAlive(targetInstanceId))
        {
            SendSkill(client, request, fields.Kind, targetInstanceId, SkillPacketType.Cancel, 0, 0);
            return;
        }

        // The area and multi-hit families run their own fires once the cast delay is over
        // (docs/packet-specs/socle-competences-zone-multi-coups.md): the delay, the cancel and the pushback
        // above are the same as every other skill's.
        if (IsDamageSequence(fields))
        {
            CastDamageSequence(client, request, fields, targetInstanceId, skillLevel, now, 0, cast.PlayerTarget);
            return;
        }

        if (IsSupport(fields)) { FireSupport(client, cast, now); return; }

        SkillHit? hit = null;
        switch (fields.Kind)
        {
            case SkillCastKind.Buff:
                ApplyBuff(client, fields, skillLevel, now);
                break;
            case SkillCastKind.Aura:
                ToggleAura(client, fields, skillLevel, now);
                break;
            case SkillCastKind.Heal:
                hit = ApplyHeal(client, fields, skillLevel);
                break;
            case SkillCastKind.Debuff:
                ApplyDebuff(client, fields, skillLevel, now, targetInstanceId);
                break;
            case SkillCastKind.PhysicalAttack:
            case SkillCastKind.MagicAttack:
                hit = ApplyAttack(client, fields, skillLevel, request.Target, targetInstanceId);
                break;
            case SkillCastKind.ActivateProp:
                ActivateProp(client, targetInstanceId);
                break;
            case SkillCastKind.Taming:
                if (_creatures?.StartTaming(client, targetInstanceId, skillLevel) != true)
                {
                    SendSkill(client, request, fields.Kind, targetInstanceId, SkillPacketType.Cancel, 0, 0);
                    return;
                }

                break;
            case SkillCastKind.Summon:
            case SkillCastKind.Unsummon:
                var done = fields.Kind == SkillCastKind.Summon
                    ? _creatures?.Summon(client, (uint)targetInstanceId)
                    : _creatures?.Unsummon(client, (uint)targetInstanceId);
                if (done != true)
                {
                    SendSkill(client, request, fields.Kind, targetInstanceId, SkillPacketType.Cancel, 0, 0);
                    return;
                }

                break;
            case SkillCastKind.Resurrection:
                hit = ResurrectPlayer(fields, skillLevel, (uint)targetInstanceId);
                if (hit is null)
                {
                    // The target came back, or left, while the spell was being cast.
                    SendSkill(client, request, fields.Kind, targetInstanceId, SkillPacketType.Cancel, 0, 0);
                    return;
                }

                break;
            default:
                // Every kind is handled above; a new one must not silently behave like a buff.
                _logger.Error("Skill {skillId} has unhandled kind {kind}", request.SkillId, fields.Kind);
                return;
        }

        SendSkill(client, request, fields.Kind, targetInstanceId, SkillPacketType.Fire, 0, 0, hit);

        // Only a buff or an aura moves the caster's stat block. A heal changes HP, which travels as a
        // property; a debuff and an attack land on a monster.
        if (fields.Kind is SkillCastKind.Buff or SkillCastKind.Aura)
        {
            SendStatRefresh(client, info);
        }

        SendSkill(client, request, fields.Kind, targetInstanceId, SkillPacketType.Complete, 0, 0);

        var cooldown = BuffCurve.CooldownTicks(fields, skillLevel);
        client.Connection.Send(GameCharacterPackets.BuildSkillList(info.CharacterHandle,
            new[] { new SkillListEntry(request.SkillId, skillLevel, cooldown, cooldown) }));

        _logger.Debug("{clientTag} cast {kind} {skillId} level {level}", client.ClientTag, fields.Kind,
            request.SkillId, skillLevel);
    }

    /// <summary>
    /// <c>StructCreature::CastSkill</c>'s range: only a cast on a monster is measured, from body to body,
    /// against <c>cast_range</c> (<see cref="CastRules.InRange"/>). A prop's activation keeps its own checks.
    /// </summary>
    private bool InCastRange(ConnectionInfo info, CastableBuffFields fields, long targetInstanceId, uint now)
    {
        if (IsPlayerTarget(targetInstanceId))
            return ResolvePlayerTarget(targetInstanceId) is { } player && PlayerCastInRange(info, fields, player, now);
        if (fields.Kind == SkillCastKind.Resurrection)
        {
            if (_players is null || !_players.Registry.TryResolve((uint)targetInstanceId, out var target))
            {
                return false;
            }

            var (px, py) = info.PositionAt(now);
            var (tx2, ty2) = target.ConnectionInfo.PositionAt(now);
            return CastRules.InRange(fields.CastRange, _statService.Compute(info).Total.AttackRange, px, py,
                CombatRange.PlayerUnitSize, tx2, ty2, CombatRange.PlayerUnitSize, false);
        }

        if (!TargetsAMonster(fields.Kind) || SkillCastRangeRules.AppliesTo(fields.EffectType)
            || !_monsterState.TryGetInstance(targetInstanceId, out var instance))
        {
            return true;
        }

        var (cx, cy) = info.PositionAt(now);
        var (tx, ty) = _monsterState.GetPosition(targetInstanceId);
        var attackRange = _statService.Compute(info).Total.AttackRange;
        return CastRules.InRange(fields.CastRange, attackRange, cx, cy, CombatRange.PlayerUnitSize, tx, ty,
            CombatRange.UnitSize(instance.Size, instance.Scale), _monsterState.IsMoving(targetInstanceId));
    }

    /// <summary>
    /// Cancels one of the player's own states on the client's request. The notification is the same
    /// <c>TM_SC_STATE</c> removal the expiry tick already sends, and an aura is switched off as an aura:
    /// the toggle group is dropped and <c>TM_SC_AURA</c> goes out at <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Refusals answer a <c>TS_SC_RESULT</c> tagged 408 and change nothing: no packet pairs with this
    /// request, and nothing establishes how the client 7.3 renders such a result.
    /// </remarks>
    public void RemoveState(GameClient client, GameActionPackets.RemoveStateRequest request)
    {
        const ushort requestId = (ushort)GamePackets.TM_CS_REQUEST_REMOVE_STATE;
        var info = client.ConnectionInfo;
        var handle = info.CharacterHandle;

        ActiveBuff removed;
        int? toggleGroup;
        ResultCode error;

        // Resolve and mutate under one lock: the expiry tick walks the same list, and the index is only
        // valid while it is held.
        lock (info.BuffLock)
        {
            if (!StateRemoval.TryResolve(request.Target, handle, request.StateCode, info.ActiveBuffs,
                    info.ActiveAuras, _stateCatalog.IsEraseOnRequest(request.StateCode), out var plan,
                    out error))
            {
                removed = default;
                toggleGroup = null;
            }
            else
            {
                removed = plan.Buff;
                toggleGroup = plan.ToggleGroup;
                info.ActiveBuffs.RemoveAt(plan.Index);

                // One aura per group: cancelling an aura-based state must undo the toggle, not only the icon.
                if (toggleGroup is { } group)
                {
                    info.ActiveAuras.Remove(group);
                }
            }
        }

        if (error != ResultCode.Success)
        {
            _logger.Debug("{clientTag} could not cancel state {stateCode}: {error}", client.ClientTag,
                request.StateCode, error);
            client.SendResult(requestId, (ushort)error);
            return;
        }

        if (toggleGroup is not null)
        {
            SendToSelfAndWatchers(client, GameSkillPackets.BuildAura(handle, (ushort)removed.SkillId, false));
        }

        SendToSelfAndWatchers(client, GameSkillPackets.BuildStateRemoval(handle, removed.StateHandle,
            (uint)removed.StateId));
        SendStatRefresh(client, info);
        client.SendResult(requestId, (ushort)ResultCode.Success);

        _logger.Debug("{clientTag} cancelled state {stateCode} through the state window", client.ClientTag,
            request.StateCode);
    }

    /// <summary>
    /// Carries out a prop's script. Validation has already resolved the prop and proven the action is
    /// supported, so this only has to move the player.
    /// </summary>
    private void ActivateProp(GameClient client, long instanceId)
    {
        if (!TryGetPropTemplate(instanceId, out var template))
        {
            return;
        }

        var action = template.Action;

        switch (action.Kind)
        {
            case PropActionKind.CommonWarpGate:
            case PropActionKind.RunTeleport:
                // The reference scatters arrivals so a crowd does not stack on one point.
                _warpService.Warp(client,
                    action.X + Random.Shared.Next(0, 11),
                    action.Y + Random.Shared.Next(0, 11));
                break;

            case PropActionKind.EnterDungeon:
            case PropActionKind.ExitDungeon:
                if (_fieldPropCatalog.TryGetDungeonStart(action.DungeonId, out var x, out var y))
                {
                    _warpService.Warp(client, x, y);
                }

                break;
        }
    }

    private bool TryValidate(GameClient client, GameActionPackets.SkillRequest request, uint now,
        out CastableBuffFields fields, out long targetInstanceId, out byte skillLevel, out ResultCode error)
    {
        var info = client.ConnectionInfo;
        fields = default;
        targetInstanceId = -1;
        skillLevel = 0;

        if (request.Caster != info.CharacterHandle || request.Caster == 0)
        {
            error = ResultCode.NotOwn;
            return false;
        }

        if (info.CharacterHp <= 0)
        {
            error = ResultCode.NotActable;
            return false;
        }

        if (!_catalog.TryGet(request.SkillId, out fields))
        {
            error = ResultCode.AccessDenied;
            return false;
        }

        // A prop's activate skill is never learned: the client casts it because the prop advertises
        // it, so the learned list is the wrong gate and the prop itself is the authorisation.
        if (fields.Kind == SkillCastKind.ActivateProp)
        {
            skillLevel = 1;

            if (!TryValidateProp(info, request, out targetInstanceId, out error))
            {
                return false;
            }
        }
        else
        {
            if (!info.LearnedSkills.TryGetValue(request.SkillId, out skillLevel) || skillLevel == 0)
            {
                error = ResultCode.AccessDenied;
                return false;
            }

            if (IsDamageSequence(fields) && info.Layer != 0)
            { error = ResultCode.NotActable; return false; }
            if (SkillCastRangeRules.AppliesTo(fields.EffectType) && request.Layer != info.Layer)
            { error = ResultCode.InvalidArgument; return false; }

            if (fields.Kind is SkillCastKind.PhysicalAttack or SkillCastKind.MagicAttack
                && SkillAreaRules.IsArea(fields.EffectType) && fields.RequiredTarget is 0 or 2)
            {
                if (!float.IsFinite(request.X) || !float.IsFinite(request.Y) || !float.IsFinite(request.Z)
                    || request.Layer != info.Layer)
                { error = ResultCode.InvalidArgument; return false; }
                if (fields.RequiredTarget == 2)
                {
                    var p = SkillCastRangeRules.PlayerPosition(info, now);
                    if (!SkillCastRangeRules.GroundInRange(fields.CastRange, _statService.Compute(info).Total.AttackRange,
                        CombatRange.Distance(p.X, p.Y, request.X, request.Y)))
                    { error = ResultCode.TooFar; return false; }
                }
            }
            else if (fields.Kind is SkillCastKind.Summon or SkillCastKind.Unsummon)
            {
                // The target of 4001/4002 is the creature card (PrepareSummon), not a creature.
                targetInstanceId = request.Target;
                error = _creatures?.CheckSummon(info, request.Target, fields.Kind == SkillCastKind.Summon)
                        ?? ResultCode.NotActable;
                if (error != ResultCode.Success)
                {
                    return false;
                }
            }
            else if (IsSupport(fields))
            {
                if (!ValidateSupport(client, request, fields, now, out error)) return false;
            }
            else if (!TryValidateTarget(client, request, fields, out targetInstanceId, out error))
            {
                return false;
            }

            if (SkillCastRangeRules.AppliesTo(fields.EffectType) && targetInstanceId >= 0)
            {
                if (!_monsterState.TryGetInstance(targetInstanceId, out var target) || target.Layer != info.Layer)
                { error = ResultCode.NotActable; return false; }
                var p = SkillCastRangeRules.PlayerPosition(info, now);
                var q = _monsterState.GetPosition(targetInstanceId);
                if (!SkillCastRangeRules.InRange(fields.CastRange, _statService.Compute(info).Total.AttackRange,
                    CombatRange.Distance(p.X, p.Y, q.X, q.Y), CombatRange.PlayerUnitSize,
                    CombatRange.UnitSize(target.Size, target.Scale), _monsterState.IsMoving(targetInstanceId)))
                { error = ResultCode.TooFar; return false; }
            }

            // StructSkill's taming checks, in their order and with their own codes (NOT_TAMABLE 90,
            // TARGET_ALREADY_BEING_TAMED 91, NOT_ENOUGH_TARGET_HP 92, NOT_ENOUGH_SUMMON_CARD 93, ALREADY_TAMING 70).
            if (fields.Kind == SkillCastKind.Taming)
            {
                error = _creatures?.CheckTaming(info, targetInstanceId) ?? ResultCode.NotActable;
                if (error != ResultCode.Success)
                {
                    return false;
                }
            }
        }

        if (info.SkillCooldowns.TryGetValue(request.SkillId, out var readyAt)
            && unchecked((int)(now - readyAt)) < 0)
        {
            error = ResultCode.CoolTime;
            return false;
        }

        if (info.CharacterMp < BuffCurve.MpCost(fields, skillLevel))
        {
            error = ResultCode.NotEnoughMP;
            return false;
        }

        error = ResultCode.Success;
        return true;
    }

    /// <summary>
    /// Resolves the cast target for every kind but a prop: a visible, living monster for the kinds
    /// that need one, and the caster for the rest.
    /// </summary>
    private bool TryValidateTarget(GameClient client, GameActionPackets.SkillRequest request,
        CastableBuffFields fields, out long targetInstanceId, out ResultCode error)
    {
        var info = client.ConnectionInfo;
        var kind = fields.Kind;
        targetInstanceId = -1;

        if (kind == SkillCastKind.Resurrection)
        {
            return TryResolveDeadPlayer(info, request.Target, out targetInstanceId, out error);
        }

        if (TargetsAMonster(kind))
        {
            if (!info.TryResolveMonster(request.Target, out targetInstanceId))
            {
                if (kind is SkillCastKind.PhysicalAttack or SkillCastKind.MagicAttack or SkillCastKind.Debuff)
                {
                    if (!TryHostilePlayer(client, request.Target, fields, out var player, out error)) return false;
                    targetInstanceId = PlayerTargetId(player.ConnectionInfo.CharacterHandle);
                    return true;
                }
                error = ResultCode.NotExist;
                return false;
            }

            if (!_monsterState.IsAlive(targetInstanceId))
            {
                error = ResultCode.NotActable;
                return false;
            }
        }
        else if (request.Target != 0 && request.Target != info.CharacterHandle)
        {
            // Summons and other players are not modelled; everything else lands on the caster.
            error = ResultCode.NotExist;
            return false;
        }

        error = ResultCode.Success;
        return true;
    }

    /// <summary>
    /// The target of a resurrection: another player, dead, whom the caster sees. A living target is
    /// <c>NotActable</c>, an unknown or unseen one <c>NotExist</c>.
    /// </summary>
    private bool TryResolveDeadPlayer(ConnectionInfo info, uint handle, out long targetInstanceId,
        out ResultCode error)
    {
        targetInstanceId = -1;
        bool seen;
        lock (info.PlayerVisibilityLock)
        {
            seen = info.SpawnedPlayers.ContainsKey(handle);
        }

        if (handle == 0 || handle == info.CharacterHandle || !seen || _players is null
            || !_players.Registry.TryResolve(handle, out var target))
        {
            error = ResultCode.NotExist;
            return false;
        }

        if (target.ConnectionInfo.CharacterHp > 0)
        {
            error = ResultCode.NotActable;
            return false;
        }

        targetInstanceId = handle;
        error = ResultCode.Success;
        return true;
    }

    /// <summary>
    /// <c>SKILL_RESURRECTION</c> / <c>SKILL_RESURRECTION_WITH_RECOVER</c> on a dead player: HP and MP from the
    /// skill's variables (<see cref="ResurrectionRules.VitalsBySkill"/>), a share of the death's experience back
    /// (<see cref="ResurrectionRules.SkillExpRatio"/>), where they fell. The hit is <c>SHT_REBIRTH</c>. Null when
    /// the target is no longer there or no longer dead.
    /// </summary>
    private SkillHit? ResurrectPlayer(CastableBuffFields fields, int skillLevel, uint handle)
    {
        if (_players is null || !_players.Registry.TryResolve(handle, out var target)
            || target.ConnectionInfo.CharacterHp > 0)
        {
            return null;
        }

        var info = target.ConnectionInfo;
        var stats = _statService.Compute(info).Total;
        var effect = (Navislamia.Game.DataAccess.Entities.Enums.SkillEffectType)fields.EffectType;
        var previousMp = info.CharacterMp;
        var (hp, mp) = ResurrectionRules.VitalsBySkill(effect, fields.Vars, skillLevel, stats.MaxHp, stats.MaxMp,
            info.CharacterMp);

        info.CharacterHp = hp;
        info.CharacterMp = mp;
        target.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", hp));
        target.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "mp", mp));
        var exp = _leveling?.RestoreDeathExperience(target, ResurrectionRules.SkillExpRatio(effect, fields.Vars,
            skillLevel)) ?? 0;

        _logger.Debug("{clientTag} was resurrected by skill {skillId} with {hp} hp and {exp} exp back",
            target.ClientTag, fields.SkillId, hp, exp);
        return new SkillHit(SkillHitType.Rebirth, handle, hp, hp, IncMp: mp - previousMp,
            RecoveryExp: (int)Math.Min(exp, int.MaxValue), TargetMp: mp);
    }

    public ResultCode CheckItemSkillTarget(GameClient client, int skillId, uint targetHandle)
    {
        if (_catalog.TryGet(skillId, out var support) && IsSupport(support))
        {
            var request = new GameActionPackets.SkillRequest((ushort)skillId, client.ConnectionInfo.CharacterHandle,
                targetHandle, 0, 0, 0, unchecked((sbyte)client.ConnectionInfo.Layer), 1);
            return ValidateSupport(client, request, support, ServerClock.Now, out var refusal)
                ? ResultCode.Success : refusal;
        }
        if (!_catalog.TryGet(skillId, out var fields) || fields.Kind != SkillCastKind.Resurrection)
        {
            return ResultCode.Success;
        }

        return TryResolveDeadPlayer(client.ConnectionInfo, targetHandle, out _, out var error)
            ? ResultCode.Success
            : error;
    }

    public bool ApplyItemSkill(GameClient client, int skillId, int skillLevel, uint targetHandle)
    {
        if (_catalog.TryGet(skillId, out var support) && IsSupport(support))
            return ApplySupportItem(client, support, skillLevel, targetHandle);
        if (!_catalog.TryGet(skillId, out var fields) || fields.Kind != SkillCastKind.Resurrection)
        {
            return ApplyItemSkill(client, skillId, skillLevel);
        }

        var level = Math.Max(1, skillLevel);
        var hit = ResurrectPlayer(fields, level, targetHandle);
        if (hit is null)
        {
            return false;
        }

        var info = client.ConnectionInfo;
        var request = new GameActionPackets.SkillRequest((ushort)skillId, info.CharacterHandle, targetHandle,
            info.X, info.Y, info.Z, (sbyte)info.Layer, (byte)level);
        SendSkill(client, request, fields.Kind, targetHandle, SkillPacketType.Fire, 0, 0, hit);
        SendSkill(client, request, fields.Kind, targetHandle, SkillPacketType.Complete, 0, 0);
        return true;
    }

    /// <summary>
    /// Resolves the cast target to a prop this client can see, and applies the reference server's
    /// FieldProp::IsUsable checks.
    /// </summary>
    private bool TryValidateProp(ConnectionInfo info, GameActionPackets.SkillRequest request,
        out long instanceId, out ResultCode error)
    {
        if (!info.TryResolveProp(request.Target, out instanceId)
            || !TryGetPropTemplate(instanceId, out var template))
        {
            error = ResultCode.NotExist;
            return false;
        }

        if (template.ActivateSkillId != request.SkillId
            || !FieldPropUsage.IsUsable(template, info)
            || !FieldPropUsage.CanAct(template, _fieldPropCatalog))
        {
            error = ResultCode.NotActable;
            return false;
        }

        error = ResultCode.Success;
        return true;
    }

    private bool TryGetPropTemplate(long instanceId, out FieldPropTemplate template)
    {
        if (_fieldPropCatalog.TryGetInstance(instanceId, out var instance))
        {
            return _fieldPropCatalog.TryGetTemplate(instance.PropId, out template);
        }

        template = default;
        return false;
    }

    /// <summary>
    /// The kinds whose target is a monster rather than the caster. Taming is one of them since étape 0 of
    /// the socle (docs/packet-specs/socle-apprivoisement-invocation.md §11): the creature spell 4003 is
    /// cast at a monster, so an unresolvable handle must answer <c>NotExist</c> rather than land on the
    /// caster.
    /// </summary>
    private static bool TargetsAMonster(SkillCastKind kind)
    {
        return kind is SkillCastKind.Debuff or SkillCastKind.PhysicalAttack or SkillCastKind.MagicAttack
            or SkillCastKind.Taming;
    }

    /// <summary>The area and multi-hit damage families, which <see cref="CastDamageSequence"/> resolves.</summary>
    private static bool IsDamageSequence(CastableBuffFields fields) =>
        fields.Kind is SkillCastKind.PhysicalAttack or SkillCastKind.MagicAttack
        && fields.EffectType is not (0 or 231 or 30001);

    public void ApplyState(GameClient client, int stateId, int stateLevel, uint durationTicks)
    {
        var now = ServerClock.Now;
        ApplyState(client, stateId, 0, stateLevel, now, unchecked(now + durationTicks));
        SendStatRefresh(client, client.ConnectionInfo);
    }

    public bool ApplyItemSkill(GameClient client, int skillId, int skillLevel)
    {
        if (client.ConnectionInfo.CharacterHp <= 0 || !_catalog.TryGet(skillId, out var fields))
            return false;

        var level = Math.Max(1, skillLevel);
        if (IsSupport(fields)) return ApplySupportItem(client, fields, level);
        switch (fields.Kind)
        {
            case SkillCastKind.Buff:
                ApplyBuff(client, fields, level, ServerClock.Now);
                SendStatRefresh(client, client.ConnectionInfo);
                return true;
            case SkillCastKind.Heal:
                ApplyHeal(client, fields, level);
                return true;
            default:
                return false;
        }
    }

    public bool RemoveState(GameClient client, int stateId)
    {
        var info = client.ConnectionInfo;
        ActiveBuff removed;
        lock (info.BuffLock)
        {
            var index = info.ActiveBuffs.FindIndex(buff => buff.StateId == stateId);
            if (index < 0)
            {
                return false;
            }

            removed = info.ActiveBuffs[index];
            info.ActiveBuffs.RemoveAt(index);
        }

        SendToSelfAndWatchers(client, GameSkillPackets.BuildStateRemoval(info.CharacterHandle, removed.StateHandle,
            (uint)removed.StateId));
        SendStatRefresh(client, info);
        return true;
    }

    private void ApplyBuff(GameClient client, CastableBuffFields fields, int skillLevel, uint now)
    {
        var duration = BuffCurve.DurationTicks(fields, skillLevel);
        var stateLevel = BuffCurve.StateLevel(fields, skillLevel);
        ApplyState(client, fields.StateId, fields.SkillId, stateLevel, now, unchecked(now + duration));
    }

    private void ToggleAura(GameClient client, CastableBuffFields fields, int skillLevel, uint now)
    {
        var info = client.ConnectionInfo;
        int activeSkillId;
        lock (info.BuffLock)
        {
            info.ActiveAuras.TryGetValue(fields.ToggleGroup, out activeSkillId);
        }

        var action = AuraToggle.Resolve(activeSkillId, fields.SkillId);
        if (action is AuraAction.TurnOff or AuraAction.Swap)
        {
            RemoveAura(client, activeSkillId, fields.ToggleGroup);
        }

        if (action == AuraAction.TurnOff)
        {
            return;
        }

        var stateLevel = BuffCurve.StateLevel(fields, skillLevel);
        lock (info.BuffLock)
        {
            info.ActiveAuras[fields.ToggleGroup] = fields.SkillId;
        }

        SendToSelfAndWatchers(client, GameSkillPackets.BuildAura(info.CharacterHandle, (ushort)fields.SkillId,
            true));
        if (SelectSupportTargets(client, info.CharacterHandle, fields, now)
            .Any(t => t.Summon is null && ReferenceEquals(t.Owner, client)))
            ApplyState(client, fields.StateId, fields.SkillId, stateLevel, now, NeverExpires);
        PulseSupportAura(client, fields, now);
    }

    private void RemoveAura(GameClient client, int skillId, int toggleGroup)
    {
        RemoveAuraProjections(client, skillId);
        var info = client.ConnectionInfo;
        ActiveBuff? state = null;

        lock (info.BuffLock)
        {
            info.ActiveAuras.Remove(toggleGroup);

            var index = info.ActiveBuffs.FindIndex(buff => buff.SkillId == skillId);
            if (index >= 0)
            {
                state = info.ActiveBuffs[index];
                info.ActiveBuffs.RemoveAt(index);
            }
        }

        SendToSelfAndWatchers(client, GameSkillPackets.BuildAura(info.CharacterHandle, (ushort)skillId, false));
        if (state.HasValue)
        {
            SendToSelfAndWatchers(client, GameSkillPackets.BuildStateRemoval(info.CharacterHandle,
                state.Value.StateHandle, (uint)state.Value.StateId));
        }
    }

    private SkillHit ApplyHeal(GameClient client, CastableBuffFields fields, int skillLevel)
    {
        var info = client.ConnectionInfo;
        var stats = _statService.Compute(info).Total;
        var maxHp = (int)stats.MaxHp;

        var heal = HealCurve.Amount(fields.Vars, skillLevel, stats.MagicPoint, maxHp);
        var healed = Math.Min(heal, Math.Max(0, maxHp - info.CharacterHp));
        info.CharacterHp += healed;

        // AddHateToEnemyList: the healed player's enemies now hate the healer too.
        _monsterState.AddHateFromHelp(client, client,
            HateRules.SkillHate(fields.HateMod, fields.HateBasic, fields.HatePerSkl, skillLevel, healed));

        client.SendVitalProperty(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", info.CharacterHp));
        return new SkillHit(SkillHitType.AddHp, info.CharacterHandle, info.CharacterHp, healed);
    }

    /// <summary>
    /// An offensive skill computes its own base damage and bonuses (<see cref="SkillDamageCurve"/>), then
    /// rolls through the same <see cref="ICombatService.RollHit"/> as a swing and lands through
    /// <see cref="ICombatService.ApplyDamage"/>, so death, drops, reward and respawn stay in one place.
    /// </summary>
    private SkillHit ApplyAttack(GameClient client, CastableBuffFields fields, int skillLevel,
        uint targetHandle, long instanceId)
    {
        var info = client.ConnectionInfo;
        var stats = _statService.Compute(info).Total;
        var magical = fields.Kind == SkillCastKind.MagicAttack;
        var targetLevel = _monsterState.TryGetInstance(instanceId, out var instance) ? instance.Level : 0;

        var baseDamage = SkillDamageCurve.BaseDamage(fields.Kind, fields.Vars, skillLevel,
            stats.AttackPointRight, stats.MagicPoint);
        if (ResolvePlayerTarget(instanceId) is { } player)
        {
            var playerHit = _combatService.RollPlayerHit(client, player, baseDamage,
                magical ? DamageKind.Magical : DamageKind.Physical,
                SkillDamageCurve.HitBonus(fields, info.CharacterLevel, player.ConnectionInfo.CharacterLevel),
                SkillDamageCurve.CriticalBonus(fields, skillLevel), fields.ElementalType);
            var hp = _combatService.DamagePlayerByPlayer(client, player, playerHit.Damage, magical);
            return new SkillHit(magical ? SkillHitType.MagicDamage : SkillHitType.Damage, targetHandle, hp,
                playerHit.Damage, (byte)playerHit.Flags, ElementalType: (byte)fields.ElementalType);
        }
        var hit = _combatService.RollElementalHit(client, instanceId, baseDamage,
            magical ? DamageKind.Magical : DamageKind.Physical,
            SkillDamageCurve.HitBonus(fields, info.CharacterLevel, targetLevel),
            SkillDamageCurve.CriticalBonus(fields, skillLevel), fields.ElementalType);

        var hate = HateRules.SkillHate(fields.HateMod, fields.HateBasic, fields.HatePerSkl, skillLevel, hit.Damage);
        var targetHp = _combatService.ApplyDamage(client, instanceId, targetHandle, hit.Damage, hate);
        var type = magical ? SkillHitType.MagicDamage : SkillHitType.Damage;

        return new SkillHit(type, targetHandle, targetHp, hit.Damage, (byte)hit.Flags, ElementalType: (byte)fields.ElementalType);
    }

    private void CastDamageSequence(GameClient client, GameActionPackets.SkillRequest request,
        CastableBuffFields fields, long targetId, int level, uint now, uint castDelay, GameClient primaryPlayer = null)
    {
        var info = client.ConnectionInfo;
        var layer = info.Layer;
        var caster = info.CharacterHandle;
        var area = SkillAreaRules.Area(fields);
        var count = SkillAreaRules.FireCount(fields, level);
        var ground = fields.EffectType == 271;
        var created = false;
        var targetLife = _monsterState.LifeVersion(targetId);
        uint startedAt = 0;
        float groundMagic = 0;
        var (fixedX, fixedY) = fields.RequiredTarget == 0 ? SkillCastRangeRules.PlayerPosition(info, now)
            : primaryPlayer is not null ? SkillCastRangeRules.PlayerPosition(primaryPlayer.ConnectionInfo, now)
            : targetId >= 0 ? _monsterState.GetPosition(targetId) : (request.X, request.Y);
        request = request with { X = fixedX, Y = fixedY, Target = fields.RequiredTarget == 0 ? caster : request.Target };
        lock (_lock) _casting.Add(client);
        void Finish()
        {
            if (!ground || !created) lock (_lock) _casting.Remove(client);
            if (info.CharacterHandle == caster && (!ground || !created))
                SendDamageSkill(client, request, fields, targetId, SkillPacketType.Complete, Array.Empty<(long, SkillHit)>());
        }
        bool Fire(uint tick)
        {
            if (info.CharacterHandle != caster || info.CharacterHp <= 0 || info.Layer != layer) return false;
            if (_players is not null && (!_players.Registry.TryResolve(caster, out var current) || !ReferenceEquals(current, client))) return false;
            if (!ground && primaryPlayer is not null && (!TryHostilePlayer(client, PlayerTargetHandle(targetId), fields, out var active, out _)
                || !ReferenceEquals(active, primaryPlayer) || !PlayerCastInRange(info, fields, primaryPlayer, tick))) return false;
            if (!ground && targetId >= 0 && (!_monsterState.IsAlive(targetId) || _monsterState.GetHp(targetId) <= 0
                || _monsterState.LifeVersion(targetId) != targetLife)) return false;
            if (ground)
            {
                if (!created)
                {
                    created = true;
                    startedAt = tick;
                    groundMagic = _statService.Compute(info).Total.MagicPoint;
                    _effects.Track(new GroundSkillProp(fixedX, fixedY, request.Z, layer, tick,
                        SkillAreaRules.Duration(fields, level), fields.SkillId,
                        () => _players?.Registry.Clients ?? new[] { client },
                        recipient =>
                        {
                            if (ReferenceEquals(recipient, client)) return caster;
                            lock (recipient.ConnectionInfo.PlayerVisibilityLock)
                                return recipient.ConnectionInfo.SpawnedPlayers.ContainsKey(caster) ? caster : 0;
                        },
                        () => info.CharacterHandle == caster && info.CharacterHp > 0 && info.Layer == layer
                            && (_players is null || (_players.Registry.TryResolve(caster, out var owner) && ReferenceEquals(owner, client)))), tick);
                    SendDamageSkill(client, request, fields, targetId, SkillPacketType.Fire, Array.Empty<(long, SkillHit)>());
                    lock (_lock) _casting.Remove(client);
                    SendDamageSkill(client, request, fields, targetId, SkillPacketType.Complete, Array.Empty<(long, SkillHit)>());
                }
                if (unchecked((int)(tick - startedAt)) > SkillAreaRules.Duration(fields, level)) return false;
            }
            var (tx, ty) = ground ? (fixedX, fixedY)
                : primaryPlayer is not null ? SkillCastRangeRules.PlayerPosition(primaryPlayer.ConnectionInfo, tick)
                : targetId < 0 ? (fixedX, fixedY) : _monsterState.GetPosition(targetId);
            var stats = _statService.Compute(info).Total;
            var damage = SkillAreaRules.Damage(fields, level, stats.AttackPointRight, ground ? groundMagic : stats.MagicPoint);
            var targets = new List<DamageTarget>();
            if (SkillAreaRules.IsArea(fields.EffectType))
            {
                var (cx, cy) = SkillCastRangeRules.PlayerPosition(info, tick);
                var (ox, oy) = area.TargetOrigin ? (tx, ty) : (cx, cy);
                var candidates = _monsterState.WithinCurrentRange(ox, oy, area.Radius)
                    .Where(m => m.Layer == layer).Select(m => new DamageTarget(m.InstanceId, m))
                    .Concat(HostileDamagePlayers(client, fields));
                targets = SkillAreaRules.Select(candidates, area,
                    cx, cy, tx, ty, t => DamagePosition(t, tick), ref damage, _random);
            }
            else if (primaryPlayer is not null) targets.Add(new DamageTarget(targetId, Player: primaryPlayer));
            else if (_monsterState.TryGetInstance(targetId, out var single)) targets.Add(new DamageTarget(targetId, single));
            var hits = new List<(long, SkillHit)>();
            foreach (var target in targets.Take(255))
            {
                var handle = DamageHandle(client, target.Id);
                if (!DamageTargetAlive(client, fields, target)) continue;
                var repetitions = SkillAreaRules.IsAtOnceMultiple(fields.EffectType) ? count : 1;
                for (var i = 0; i < repetitions && DamageTargetAlive(client, fields, target); i++)
                {
                    var magical = fields.Kind == SkillCastKind.MagicAttack;
                    var kind = magical ? DamageKind.Magical : DamageKind.Physical;
                    var accuracy = SkillDamageCurve.HitBonus(fields, info.CharacterLevel, target.Level);
                    var critical = SkillDamageCurve.CriticalBonus(fields, level);
                    var hit = target.Player is { } player
                        ? _combatService.RollPlayerHit(client, player, damage, kind, accuracy, critical, fields.ElementalType)
                        : _combatService.RollElementalHit(client, target.Id, damage, kind, accuracy, critical, fields.ElementalType);
                    var hp = target.Player is { } victim
                        ? _combatService.DamagePlayerByPlayer(client, victim, hit.Damage, magical)
                        : _combatService.ApplyDamage(client, target.Id, handle, hit.Damage,
                            HateRules.SkillHate(fields.HateMod, fields.HateBasic, fields.HatePerSkl, level, hit.Damage));
                    hits.Add((target.Id, new SkillHit(magical ? SkillHitType.MagicDamage : SkillHitType.Damage,
                        handle, hp, hit.Damage, (byte)hit.Flags, ElementalType: (byte)fields.ElementalType)));
                    if (hp <= 0) break;
                }
            }
            SendDamageSkill(client, request, fields, targetId, ground ? SkillPacketType.RegionFire : SkillPacketType.Fire,
                hits, SkillAreaRules.IsAtOnceMultiple(fields.EffectType) ? (byte)count : (byte)1);
            return true;
        }
        var sequential = SkillAreaRules.IsSequential(fields.EffectType) || ground;
        _effects.Schedule(unchecked(now + castDelay), SkillAreaRules.Interval(fields), sequential ? count : 1, Fire, Finish);
        var cooldown = BuffCurve.CooldownTicks(fields, level);
        client.Connection.Send(GameCharacterPackets.BuildSkillList(info.CharacterHandle,
            new[] { new SkillListEntry(fields.SkillId, (byte)level, cooldown, cooldown) }));
        if (castDelay == 0) _effects.Tick(now);
    }

    private void SendDamageSkill(GameClient client, GameActionPackets.SkillRequest request, CastableBuffFields fields,
        long targetId, SkillPacketType type, IReadOnlyList<(long InstanceId, SkillHit Hit)> hits, byte fireCount = 1)
    {
        byte[] Frame(GameClient recipient)
        {
            var mapped = hits.Select(h => (h.Hit, Handle: DamageHandle(recipient, h.InstanceId)))
                .Where(h => h.Handle != 0).Select(h => h.Hit with { TargetHandle = h.Handle }).ToArray();
            var target = targetId != -1 ? DamageHandle(recipient, targetId) : request.Target;
            return GameSkillPackets.BuildSkill(request.SkillId, request.SkillLevel, client.ConnectionInfo.CharacterHandle,
                target, request.X, request.Y, request.Z, (byte)request.Layer, type, 0, 0,
                client.ConnectionInfo.CharacterHp, client.ConnectionInfo.CharacterMp, hits: mapped,
                multiple: SkillAreaRules.IsSequential(fields.EffectType), range: SkillAreaRules.Area(fields).Radius, fireCount: fireCount);
        }
        client.Connection.Send(Frame(client));
        if (_players is not null)
        {
            var recipients = new HashSet<GameClient>(_players.Observers(client));
            foreach (var other in _players.Registry.Clients)
                if (hits.Any(h => DamageHandle(other, h.InstanceId) != 0)) recipients.Add(other);
            recipients.Remove(client);
            foreach (var other in recipients)
                if (other.ConnectionInfo.Layer == client.ConnectionInfo.Layer) other.Connection.Send(Frame(other));
        }
    }

    private void ApplyDebuff(GameClient client, CastableBuffFields fields, int skillLevel, uint now,
        long instanceId)
    {
        if (IsPlayerTarget(instanceId))
        {
            if (ResolvePlayerTarget(instanceId) is { } player) ApplyPlayerDebuff(client, player, fields, skillLevel, now);
            return;
        }
        var info = client.ConnectionInfo;

        // STATE_SKILL_FUNCTOR::onCreature: a harmful state has to land first (CastRules.StateLandingChance).
        var targetLevel = _monsterState.TryGetInstance(instanceId, out var instance) ? instance.Level : 0;
        var chance = CastRules.StateLandingChance(fields.EffectType, _statService.Compute(info).Total.MagicAccuracy,
            _combatService.GetMonsterStats(instanceId)?.MagicAvoid ?? 0f,
            SkillDamageCurve.HitBonus(fields, info.CharacterLevel, targetLevel), fields.ProbabilityOnHit,
            fields.ProbabilityIncBySlv, skillLevel);
        if (!CastRules.StateLands(chance, _random.Next(100)))
        {
            _logger.Debug("{clientTag} debuff {skillId} was resisted (chance {chance})", client.ClientTag,
                fields.SkillId, chance);
            return;
        }

        // The state skill draws the monster's attention whether or not the state then stacks.
        _monsterState.AddHate(instanceId, client,
            HateRules.SkillHate(fields.HateMod, fields.HateBasic, fields.HatePerSkl, skillLevel, 0));

        var duration = BuffCurve.DurationTicks(fields, skillLevel);
        var stateLevel = BuffCurve.StateLevel(fields, skillLevel);
        if (!_monsterState.TryAddState(instanceId, fields.StateId, fields.SkillId, stateLevel, now,
                unchecked(now + duration), _stateCatalog.GetRule(fields.StateId), _stateCatalog.GetRule,
                out var state, out var displaced))
        {
            return;
        }

        stateLevel = state.StateLevel;
        var handle = info.GetMonsterHandle(instanceId);
        if (handle != 0)
        {
            foreach (var old in displaced)
            {
                client.Connection.Send(GameSkillPackets.BuildStateRemoval(handle, old.StateHandle, (uint)old.StateId));
            }

            client.Connection.Send(GameSkillPackets.BuildState(handle, state.StateHandle,
                (uint)state.StateId, (ushort)stateLevel, state.EndTick, now));
        }

        // The players watching see the debuff on the monster too, under their own handle for it.
        foreach (var old in displaced)
        {
            ObserverFrames.SendMonsterFrame(_players, client, instanceId, (_, watcherHandle) =>
                GameSkillPackets.BuildStateRemoval(watcherHandle, old.StateHandle, (uint)old.StateId));
        }

        ObserverFrames.SendMonsterFrame(_players, client, instanceId, (_, watcherHandle) =>
            GameSkillPackets.BuildState(watcherHandle, state.StateHandle, (uint)state.StateId, (ushort)stateLevel,
                state.EndTick, now));
    }

    /// <summary>
    /// Puts a state on the player under the official stacking rule (<see cref="StateStacking"/>, result 9
    /// on a refusal): the states it displaces are taken off on the wire, the same state is refreshed in place,
    /// and a stun-like state breaks the cast in progress (<see cref="CastRules.InterruptsCasting"/>).
    /// </summary>
    private bool ApplyState(GameClient client, int stateId, int skillId, int stateLevel, uint now,
        uint endTick, uint sourceHandle = 0, bool projection = false)
    {
        var info = client.ConnectionInfo;
        var rule = _stateCatalog.GetRule(stateId);

        ushort stateHandle;
        int level;
        var displaced = new List<ActiveBuff>();
        lock (info.BuffLock)
        {
            var decision = StateStacking.Decide(info.ActiveBuffs, stateId, rule, stateLevel, endTick,
                _stateCatalog.GetRule, force: endTick == NeverExpires);
            if (decision.Refused)
            {
                return false;
            }

            level = decision.Level;
            stateHandle = decision.RefreshIndex >= 0
                ? info.ActiveBuffs[decision.RefreshIndex].StateHandle
                : ++info.NextStateHandle;

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
                    displaced.Add(info.ActiveBuffs[indices[i]]);
                }

                info.ActiveBuffs.RemoveAt(indices[i]);
            }

            info.ActiveBuffs.Add(new ActiveBuff(stateHandle, stateId, skillId, level, now, endTick, sourceHandle, projection));
        }

        foreach (var state in displaced)
        {
            SendToSelfAndWatchers(client, GameSkillPackets.BuildStateRemoval(info.CharacterHandle, state.StateHandle,
                (uint)state.StateId));
        }

        // An aura has no deadline: the wire wants -1, which is what uint.MaxValue writes.
        SendToSelfAndWatchers(client, GameSkillPackets.BuildState(info.CharacterHandle, stateHandle,
            (uint)stateId, (ushort)level, endTick, now));

        if (CastRules.InterruptsCasting(stateId, rule.EffectType, rule.Values))
        {
            CancelCast(client);
        }

        return true;
    }

    /// <summary>
    /// A frame about the player's own states (505) or auras (407), for the player and every player who sees
    /// them (docs/packet-specs/socle-diffusion-combat.md, icons): NGemity broadcasts them to the region. Always
    /// called outside <c>BuffLock</c>: the visibility takes an observer's lock and then that lock.
    /// </summary>
    private void SendToSelfAndWatchers(GameClient client, byte[] frame)
    {
        if (_players is null)
        {
            client.Connection.Send(frame);
            return;
        }

        _players.SendToObservers(client, frame, includeSelf: true);
    }

    /// <summary>
    /// One step of a cast, to the caster and to the players who see them
    /// (docs/packet-specs/socle-diffusion-combat.md): a cast on the caster goes out as is, a cast on a monster
    /// is rebuilt with each observer's handle for it, and a prop activation stays the caster's (a prop's
    /// handle is per client too, and nothing tracks which observer streams which prop).
    /// </summary>
    private void SendSkill(GameClient client, GameActionPackets.SkillRequest request, SkillCastKind kind,
        long targetInstanceId, SkillPacketType type, int mpCost, uint castDelay, SkillHit? hit = null)
    {
        var info = client.ConnectionInfo;
        byte[] Frame(uint target, SkillHit? frameHit) => GameSkillPackets.BuildSkill((ushort)request.SkillId,
            request.SkillLevel, info.CharacterHandle, target, request.X, request.Y, request.Z, (byte)request.Layer,
            type, 0, mpCost, info.CharacterHp, info.CharacterMp, castDelay, 0, frameHit);

        client.Connection.Send(Frame(request.Target, hit));

        if (IsPlayerTarget(targetInstanceId))
        {
            var recipients = _players is null ? new HashSet<GameClient>() : new HashSet<GameClient>(_players.Observers(client));
            if (ResolvePlayerTarget(targetInstanceId) is { } player) recipients.Add(player);
            recipients.Remove(client);
            foreach (var recipient in recipients)
                if (recipient.ConnectionInfo.Layer == info.Layer)
                    recipient.Connection.Send(Frame(DamageHandle(recipient, targetInstanceId), hit));
            return;
        }

        switch (kind)
        {
            case SkillCastKind.PhysicalAttack or SkillCastKind.MagicAttack or SkillCastKind.Debuff
                or SkillCastKind.Taming:
                ObserverFrames.SendMonsterFrame(_players, client, targetInstanceId, (_, handle) =>
                    Frame(handle, hit is { } monsterHit ? monsterHit with { TargetHandle = handle } : null));
                break;
            case SkillCastKind.Buff or SkillCastKind.Aura or SkillCastKind.Heal or SkillCastKind.Resurrection
                or SkillCastKind.Summon or SkillCastKind.Unsummon:
                _players?.SendToObservers(client, Frame(request.Target, hit));
                break;
        }
    }

    private static void SendCastFailed(GameClient client, GameActionPackets.SkillRequest request,
        ResultCode error)
    {
        var info = client.ConnectionInfo;
        client.Connection.Send(GameSkillPackets.BuildSkill((ushort)request.SkillId, request.SkillLevel,
            request.Caster, request.Target, request.X, request.Y, request.Z, (byte)request.Layer,
            SkillPacketType.Casting, 0, 0, info.CharacterHp, info.CharacterMp, 0, (ushort)error));
    }

    private void SendStatRefresh(GameClient client, ConnectionInfo info)
    {
        _statService.RefreshBuffs(info);
        var stats = _statService.Compute(info);
        var handle = info.CharacterHandle;

        lock (info.ProgressLock)
        {
            info.CharacterMaxHp = SupportAmount(stats.Total.MaxHp);
            info.CharacterHp = Math.Min(info.CharacterHp, info.CharacterMaxHp);
            info.CharacterMp = Math.Min(info.CharacterMp, SupportAmount(stats.Total.MaxMp));
        }

        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, stats.Total, StatInfoType.Total));
        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, stats.ByItem, StatInfoType.ByItem));
        SendToSelfAndWatchers(client, GameStatPackets.BuildProperty(handle, "max_hp", info.CharacterMaxHp));
        SendToSelfAndWatchers(client, GameStatPackets.BuildProperty(handle, "max_mp", SupportAmount(stats.Total.MaxMp)));
        SendToSelfAndWatchers(client, GameStatPackets.BuildProperty(handle, "hp", info.CharacterHp));
        SendToSelfAndWatchers(client, GameStatPackets.BuildProperty(handle, "mp", info.CharacterMp));
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickIntervalMs));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                var now = ServerClock.Now;
                ProcessBuffs(now);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "The buff expiry tick failed");
            }
        }
    }

    private async Task RunCastsAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(CastTickIntervalMs));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                ProcessCasts(ServerClock.Now);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "The cast tick failed");
            }
        }
    }

    private void ExpirePlayerBuffs(uint now)
    {
        GameClient[] clients;
        lock (_lock)
        {
            if (_clients.Count == 0)
            {
                return;
            }

            clients = _clients.ToArray();
        }

        foreach (var client in clients)
        {
            var info = client.ConnectionInfo;
            List<ActiveBuff> expired = null;

            lock (info.BuffLock)
            {
                for (var i = info.ActiveBuffs.Count - 1; i >= 0; i--)
                {
                    var buff = info.ActiveBuffs[i];
                    if (buff.EndTick == NeverExpires || unchecked((int)(now - buff.EndTick)) < 0)
                    {
                        continue;
                    }

                    expired ??= new List<ActiveBuff>();
                    expired.Add(buff);
                    info.ActiveBuffs.RemoveAt(i);
                }
            }

            if (expired is null)
            {
                continue;
            }

            foreach (var buff in expired)
            {
                SendToSelfAndWatchers(client, GameSkillPackets.BuildStateRemoval(info.CharacterHandle,
                    buff.StateHandle, (uint)buff.StateId));
            }

            SendStatRefresh(client, info);
        }
    }

    private void ExpireMonsterStates(uint now)
    {
        var expired = _monsterState.RemoveExpiredStates(now);
        if (expired.Count == 0)
        {
            return;
        }

        GameClient[] clients;
        lock (_lock)
        {
            clients = _clients.ToArray();
        }

        foreach (var (instanceId, state) in expired)
        {
            foreach (var client in clients)
            {
                var handle = client.ConnectionInfo.GetMonsterHandle(instanceId);
                if (handle != 0)
                {
                    SendToSelfAndWatchers(client, GameSkillPackets.BuildStateRemoval(handle, state.StateHandle,
                        (uint)state.StateId));
                }
            }
        }
    }
}
