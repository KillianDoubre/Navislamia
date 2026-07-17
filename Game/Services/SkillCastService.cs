using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Props;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// Casting every skill a player can use on themselves or on one monster: buffs, toggle auras, heals,
/// debuffs and single-target attacks. <c>SkillService</c> is the one that <em>learns</em> them.
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
public class SkillCastService : ISkillCastService
{
    private const int TickIntervalMs = 500;

    /// <summary>An aura never expires; it stays until the player toggles it off.</summary>
    private const uint NeverExpires = uint.MaxValue;

    private readonly ILogger _logger = Log.ForContext<SkillCastService>();
    private readonly IBuffCatalog _catalog;
    private readonly IStatService _statService;
    private readonly MonsterWorldState _monsterState;
    private readonly ICombatService _combatService;
    private readonly IFieldPropCatalog _fieldPropCatalog;
    private readonly IWarpService _warpService;
    private readonly object _lock = new();
    private readonly List<GameClient> _clients = new();

    public SkillCastService(IBuffCatalog catalog, IStatService statService, MonsterWorldState monsterState,
        ICombatService combatService, IFieldPropCatalog fieldPropCatalog, IWarpService warpService)
    {
        _catalog = catalog;
        _statService = statService;
        _monsterState = monsterState;
        _combatService = combatService;
        _fieldPropCatalog = fieldPropCatalog;
        _warpService = warpService;

        if (_catalog.Count == 0)
        {
            _logger.Warning("The skill catalog is empty; casting will be unavailable");
        }

        _ = RunAsync();
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
        lock (_lock)
        {
            _clients.Remove(client);
        }
    }

    public void Cast(GameClient client, GameActionPackets.SkillRequest request)
    {
        var info = client.ConnectionInfo;
        var now = ServerClock.Now;

        if (!TryValidate(info, request, now, out var fields, out var targetInstanceId, out var skillLevel,
                out var error))
        {
            SendCastFailed(client, request, error);
            return;
        }

        var mpCost = BuffCurve.MpCost(fields, skillLevel);
        var castDelay = BuffCurve.CastDelayTicks(fields, skillLevel);

        info.CharacterMp -= mpCost;
        var cooldown = BuffCurve.CooldownTicks(fields, skillLevel);
        if (cooldown > 0)
        {
            info.SkillCooldowns[request.SkillId] = unchecked(now + cooldown);
        }

        SendSkill(client, request, SkillPacketType.Casting, mpCost, castDelay);

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
                hit = ApplyAttack(client, fields, request.Target, targetInstanceId);
                break;
            case SkillCastKind.ActivateProp:
                ActivateProp(client, targetInstanceId);
                break;
            default:
                // Every kind is handled above; a new one must not silently behave like a buff.
                _logger.Error("Skill {skillId} has unhandled kind {kind}", request.SkillId, fields.Kind);
                return;
        }

        SendSkill(client, request, SkillPacketType.Fire, 0, 0, hit);

        // Only a buff or an aura moves the caster's stat block. A heal changes HP, which travels as a
        // property; a debuff and an attack land on a monster.
        if (fields.Kind is SkillCastKind.Buff or SkillCastKind.Aura)
        {
            SendStatRefresh(client, info);
        }

        SendSkill(client, request, SkillPacketType.Complete, 0, 0);

        client.Connection.Send(GameCharacterPackets.BuildSkillList(info.CharacterHandle,
            new[] { new SkillListEntry(request.SkillId, skillLevel, cooldown, cooldown) }));

        _logger.Debug("{clientTag} cast {kind} {skillId} level {level}", client.ClientTag, fields.Kind,
            request.SkillId, skillLevel);
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

    private bool TryValidate(ConnectionInfo info, GameActionPackets.SkillRequest request, uint now,
        out CastableBuffFields fields, out long targetInstanceId, out byte skillLevel, out ResultCode error)
    {
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

            if (!TryValidateTarget(info, request, fields.Kind, out targetInstanceId, out error))
            {
                return false;
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
    private bool TryValidateTarget(ConnectionInfo info, GameActionPackets.SkillRequest request,
        SkillCastKind kind, out long targetInstanceId, out ResultCode error)
    {
        targetInstanceId = -1;

        if (TargetsAMonster(kind))
        {
            if (!info.TryResolveMonster(request.Target, out targetInstanceId))
            {
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

    private static bool TargetsAMonster(SkillCastKind kind)
    {
        return kind is SkillCastKind.Debuff or SkillCastKind.PhysicalAttack or SkillCastKind.MagicAttack;
    }

    private static void ApplyBuff(GameClient client, CastableBuffFields fields, int skillLevel, uint now)
    {
        var duration = BuffCurve.DurationTicks(fields, skillLevel);
        var stateLevel = BuffCurve.StateLevel(fields, skillLevel);
        ApplyState(client, fields.StateId, fields.SkillId, stateLevel, now, unchecked(now + duration));
    }

    private static void ToggleAura(GameClient client, CastableBuffFields fields, int skillLevel, uint now)
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

        client.Connection.Send(GameSkillPackets.BuildAura(info.CharacterHandle, (ushort)fields.SkillId,
            true));
        ApplyState(client, fields.StateId, fields.SkillId, stateLevel, now, NeverExpires);
    }

    private static void RemoveAura(GameClient client, int skillId, int toggleGroup)
    {
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

        client.Connection.Send(GameSkillPackets.BuildAura(info.CharacterHandle, (ushort)skillId, false));
        if (state.HasValue)
        {
            client.Connection.Send(GameSkillPackets.BuildStateRemoval(info.CharacterHandle,
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

        client.Connection.Send(GameStatPackets.BuildProperty(info.CharacterHandle, "hp", info.CharacterHp));
        return new SkillHit(SkillHitType.AddHp, info.CharacterHandle, info.CharacterHp, healed);
    }

    /// <summary>
    /// An offensive skill deals the same damage an auto-attack swing does, and goes through
    /// <see cref="ICombatService.ApplyDamage"/> so death, drops, reward and respawn stay in one place.
    /// </summary>
    private SkillHit ApplyAttack(GameClient client, CastableBuffFields fields, uint targetHandle,
        long instanceId)
    {
        var damage = _combatService.GetHitDamage(instanceId);
        var targetHp = _combatService.ApplyDamage(client, instanceId, targetHandle, damage);
        var type = fields.Kind == SkillCastKind.MagicAttack
            ? SkillHitType.MagicDamage
            : SkillHitType.Damage;

        return new SkillHit(type, targetHandle, targetHp, damage);
    }

    private void ApplyDebuff(GameClient client, CastableBuffFields fields, int skillLevel, uint now,
        long instanceId)
    {
        var info = client.ConnectionInfo;
        var duration = BuffCurve.DurationTicks(fields, skillLevel);
        var stateLevel = BuffCurve.StateLevel(fields, skillLevel);
        var state = _monsterState.AddState(instanceId, fields.StateId, fields.SkillId, stateLevel, now,
            unchecked(now + duration));

        var handle = info.GetMonsterHandle(instanceId);
        if (handle != 0)
        {
            client.Connection.Send(GameSkillPackets.BuildState(handle, state.StateHandle,
                (uint)state.StateId, (ushort)stateLevel, state.EndTick, now));
        }
    }

    private static void ApplyState(GameClient client, int stateId, int skillId, int stateLevel, uint now,
        uint endTick)
    {
        var info = client.ConnectionInfo;

        ushort stateHandle;
        lock (info.BuffLock)
        {
            var existing = info.ActiveBuffs.FindIndex(buff => buff.StateId == stateId);
            if (existing >= 0)
            {
                stateHandle = info.ActiveBuffs[existing].StateHandle;
                info.ActiveBuffs.RemoveAt(existing);
            }
            else
            {
                stateHandle = ++info.NextStateHandle;
            }

            info.ActiveBuffs.Add(new ActiveBuff(stateHandle, stateId, skillId, stateLevel, now, endTick));
        }

        // An aura has no deadline: the wire wants -1, which is what uint.MaxValue writes.
        client.Connection.Send(GameSkillPackets.BuildState(info.CharacterHandle, stateHandle,
            (uint)stateId, (ushort)stateLevel, endTick, now));
    }

    private static void SendSkill(GameClient client, GameActionPackets.SkillRequest request,
        SkillPacketType type, int mpCost, uint castDelay, SkillHit? hit = null)
    {
        var info = client.ConnectionInfo;
        client.Connection.Send(GameSkillPackets.BuildSkill((ushort)request.SkillId, request.SkillLevel,
            info.CharacterHandle, request.Target, request.X, request.Y, request.Z, (byte)request.Layer,
            type, 0, mpCost, info.CharacterHp, info.CharacterMp, castDelay, 0, hit));
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

        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, stats.Total, StatInfoType.Total));
        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, stats.ByItem, StatInfoType.ByItem));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_hp", (int)stats.Total.MaxHp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_mp", (int)stats.Total.MaxMp));
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickIntervalMs));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                var now = ServerClock.Now;
                ExpirePlayerBuffs(now);
                ExpireMonsterStates(now);
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "The buff expiry tick failed");
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
                client.Connection.Send(GameSkillPackets.BuildStateRemoval(info.CharacterHandle,
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
                    client.Connection.Send(GameSkillPackets.BuildStateRemoval(handle, state.StateHandle,
                        (uint)state.StateId));
                }
            }
        }
    }
}
