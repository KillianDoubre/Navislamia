using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Casting;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services;

public partial class SkillCastService
{
    private sealed record SupportTarget(GameClient Owner, SummonPresence Summon = null)
    {
        public uint Handle => Summon?.Handle ?? Owner.ConnectionInfo.CharacterHandle;
        public byte Layer => Summon?.Layer ?? Owner.ConnectionInfo.Layer;
        public int Hp => Summon?.Hp ?? Owner.ConnectionInfo.CharacterHp;
        public (float X, float Y) Position(uint now) => Summon is { } s ? s.PositionAt(now) : Owner.ConnectionInfo.PositionAt(now);
    }

    private static bool IsSupport(CastableBuffFields fields) => fields.Kind is SkillCastKind.Buff or SkillCastKind.Heal;
    private static bool IsRegionHeal(CastableBuffFields fields) => fields.EffectType is 508 or 521;
    private static float SupportRadius(CastableBuffFields fields) => Math.Max(0,
        IsRegionHeal(fields) ? SupportVar(fields, 10) * CombatRange.PlayerUnitSize : fields.ValidRange * CombatRange.PlayerUnitSize);
    private static float SupportVar(CastableBuffFields fields, int index) =>
        fields.Vars is { } values && values.Length > index ? (float)values[index] : 0;

    private IEnumerable<GameClient> SupportPlayers(GameClient caster)
    {
        if (_players?.Registry is { } registry) return registry.Clients.Append(caster).Distinct().ToArray();
        lock (_lock) return _clients.Append(caster).Distinct().ToArray();
    }

    private static bool SameParty(GameClient first, GameClient second) => ReferenceEquals(first, second)
        || first.ConnectionInfo.PartyId is { } party && party == second.ConnectionInfo.PartyId;

    private IEnumerable<SupportTarget> SupportUnits(GameClient caster)
    {
        foreach (var player in SupportPlayers(caster))
        {
            yield return new SupportTarget(player);
            foreach (var summon in player.ConnectionInfo.Summons) yield return new SupportTarget(player, summon);
        }
    }

    private bool SupportAllowed(GameClient caster, SupportTarget target, CastableBuffFields fields)
    {
        if (target.Layer != caster.ConnectionInfo.Layer || target.Hp <= 0) return false;
        if (_combatService.ArePlayerEnemies(caster, target.Owner)) return false;
        if (target.Summon is null ? !fields.UseOnCharacter : !fields.UseOnSummon) return false;
        return ReferenceEquals(caster, target.Owner) ? fields.UseOnSelf
            : SameParty(caster, target.Owner) ? fields.UseOnParty : fields.UseOnNeutral;
    }

    private SupportTarget SupportAnchor(GameClient caster, uint handle)
    {
        if (handle == 0 || handle == caster.ConnectionInfo.CharacterHandle) return new SupportTarget(caster);
        var target = SupportUnits(caster).FirstOrDefault(t => t.Handle == handle);
        if (target is null) return null;
        if (!ReferenceEquals(caster, target.Owner))
            lock (caster.ConnectionInfo.PlayerVisibilityLock)
                if (!caster.ConnectionInfo.SpawnedPlayers.ContainsKey(target.Owner.ConnectionInfo.CharacterHandle)) return null;
        return target;
    }

    private bool ValidateSupport(GameClient caster, GameActionPackets.SkillRequest request, CastableBuffFields fields,
        uint now, out ResultCode error)
    {
        if (fields.Target is 101 or 102) { error = ResultCode.NotActable; return false; }
        if (caster.ConnectionInfo.CharacterHp <= 0) { error = ResultCode.NotActable; return false; }
        var anchor = SupportAnchor(caster, request.Target);
        if (anchor is null) { error = ResultCode.NotExist; return false; }
        if (anchor.Layer != caster.ConnectionInfo.Layer || anchor.Hp <= 0)
        { error = ResultCode.NotActable; return false; }
        if (fields.Target is 21 or 32 or 51 && !SameParty(caster, anchor.Owner)
            || fields.Target == 31 && anchor.Summon is null && !ReferenceEquals(caster, anchor.Owner))
        { error = ResultCode.NotOwn; return false; }
        // Party-summon skills may name the player while selecting only their summons.
        if (fields.Target is not (21 or 31 or 32 or 45 or 51) && !SupportAllowed(caster, anchor, fields))
        { error = ResultCode.NotActable; return false; }
        var p = caster.ConnectionInfo.PositionAt(now); var q = anchor.Position(now);
        if (anchor.Handle != caster.ConnectionInfo.CharacterHandle && !CastRules.InRange(fields.CastRange,
            _statService.Compute(caster.ConnectionInfo).Total.AttackRange, p.X, p.Y,
            CombatRange.PlayerUnitSize, q.X, q.Y, CombatRange.PlayerUnitSize, false))
        { error = ResultCode.TooFar; return false; }
        error = ResultCode.Success; return true;
    }

    private SupportTarget[] SelectSupportTargets(GameClient caster, uint handle, CastableBuffFields fields, uint now)
    {
        var anchor = SupportAnchor(caster, handle);
        if (anchor is null || anchor.Hp <= 0 || anchor.Layer != caster.ConnectionInfo.Layer) return Array.Empty<SupportTarget>();
        var candidates = SupportUnits(caster);
        var radius = SupportRadius(fields);
        var center = fields.Target is 21 or 31 or 32 or 45 or 51 ? caster.ConnectionInfo.PositionAt(now) : anchor.Position(now);
        var area = IsRegionHeal(fields) || fields.EffectType == 302 || fields.Target is 2 or 3 or 4 or 6;
        candidates = fields.Target switch
        {
            21 => candidates.Where(t => t.Summon is null && SameParty(caster, t.Owner)),
            32 => candidates.Where(t => t.Summon is not null && SameParty(caster, t.Owner)),
            51 => candidates.Where(t => SameParty(caster, t.Owner)),
            45 => candidates.Where(t => ReferenceEquals(t.Owner, anchor.Owner)),
            31 => anchor.Summon is not null ? new[] { anchor }
                : candidates.Where(t => ReferenceEquals(t.Owner, caster) && t.Summon is not null).Take(1),
            _ => area ? candidates : new[] { anchor }
        };
        if (fields.Target is 3 or 6) candidates = candidates.Where(t => t.Handle != caster.ConnectionInfo.CharacterHandle);
        if (area || fields.Target is 21 or 32 or 45 or 51)
            candidates = candidates.Where(t =>
            { var p = t.Position(now); return CombatRange.Distance(center.X, center.Y, p.X, p.Y) <= radius; });
        if (IsRegionHeal(fields))
        {
            var relationship = (int)SupportVar(fields, 11);
            if (relationship == 2) candidates = candidates.Where(t => SameParty(caster, t.Owner));
            if (relationship == 3) return Array.Empty<SupportTarget>(); // No hostile player combat target is modelled here.
            var creature = (int)SupportVar(fields, 12);
            if (fields.EffectType == 508 && creature == 1) candidates = candidates.Where(t => t.Summon is null);
            if (fields.EffectType == 508 && creature == 2) candidates = candidates.Where(t => t.Summon is not null);
        }
        return candidates.Where(t => SupportAllowed(caster, t, fields)).DistinctBy(t => t.Handle).ToArray();
    }

    private void FireSupport(GameClient caster, PendingCast cast, uint now)
    {
        var request = cast.Request; var fields = cast.Fields;
        if (!ValidateSupport(caster, request, fields, now, out _))
        { SendSkill(caster, request, fields.Kind, -1, SkillPacketType.Cancel, 0, 0); return; }
        var targets = SelectSupportTargets(caster, request.Target, fields, now);
        var hits = ApplySupportEffects(caster, targets, fields, cast.SkillLevel, now);
        SendSupportFire(caster, request, hits, targets, SupportRadius(fields));
        SendSkill(caster, request, fields.Kind, -1, SkillPacketType.Complete, 0, 0);
        var cooldown = BuffCurve.CooldownTicks(fields, cast.SkillLevel);
        caster.Connection.Send(GameCharacterPackets.BuildSkillList(caster.ConnectionInfo.CharacterHandle,
            new[] { new SkillListEntry(request.SkillId, cast.SkillLevel, cooldown, cooldown) }));
    }

    private List<SkillHit> ApplySupportEffects(GameClient caster, IReadOnlyList<SupportTarget> targets,
        CastableBuffFields fields, int level, uint now)
    {
        var hits = new List<SkillHit>();
        foreach (var target in targets)
        {
            if (fields.Kind == SkillCastKind.Buff)
            {
                var end = unchecked(now + BuffCurve.DurationTicks(fields, level));
                if (ApplySupportState(target, fields.StateId, fields.SkillId,
                    BuffCurve.StateLevel(fields, level), now, end, caster.ConnectionInfo.CharacterHandle))
                    RefreshSupportStats(target);
            }
            else hits.Add(HealSupport(caster, target, fields, level));
        }
        return hits;
    }

    private bool ApplySupportItem(GameClient client, CastableBuffFields fields, int level, uint target = 0)
    {
        var request = new GameActionPackets.SkillRequest((ushort)fields.SkillId, client.ConnectionInfo.CharacterHandle,
            target, 0, 0, 0, unchecked((sbyte)client.ConnectionInfo.Layer), 1);
        var now = ServerClock.Now;
        if (!ValidateSupport(client, request, fields, now, out _)) return false;
        var targets = SelectSupportTargets(client, target, fields, now);
        if (targets.Length == 0) return false;
        ApplySupportEffects(client, targets, fields, Math.Max(1, level), now);
        return true;
    }

    private SkillHit HealSupport(GameClient caster, SupportTarget target, CastableBuffFields fields, int level,
        float? casterMagic = null, uint casterSummon = 0)
    {
        var magic = casterMagic ?? _statService.Compute(caster.ConnectionInfo).Total.MagicPoint;
        var stats = target.Summon?.Stats ?? _statService.Compute(target.Owner.ConnectionInfo).Total;
        var hpAmount = fields.EffectType is 505 or 508
            ? magic * (SupportVar(fields, 0) + SupportVar(fields, 1) * level) + SupportVar(fields, 2) * level
                + stats.MaxHp * SupportVar(fields, 3) * level
            : fields.EffectType == 521
                ? magic * (SupportVar(fields, 0) + SupportVar(fields, 1) * level) + SupportVar(fields, 2)
                    + SupportVar(fields, 3) * level + stats.MaxHp * (SupportVar(fields, 4) + SupportVar(fields, 5) * level)
                : HealCurve.Amount(fields.Vars, level, magic, stats.MaxHp);
        var mpAmount = fields.EffectType is 505 or 508
            ? magic * (SupportVar(fields, 5) + SupportVar(fields, 6) * level) + SupportVar(fields, 7) * level
                + stats.MaxMp * SupportVar(fields, 8) * level : 0;
        int hp, mp, addedHp, addedMp;
        lock (target.Summon?.BuffLock ?? target.Owner.ConnectionInfo.ProgressLock)
        {
            hp = target.Hp; mp = target.Summon?.Mp ?? target.Owner.ConnectionInfo.CharacterMp;
            if (hp <= 0) return new SkillHit(SkillHitType.AddHp, target.Handle, hp, 0);
            addedHp = Math.Min(SupportAmount(hpAmount), Math.Max(0, SupportAmount(stats.MaxHp) - hp));
            addedMp = Math.Min(SupportAmount(mpAmount), Math.Max(0, SupportAmount(stats.MaxMp) - mp));
            hp += addedHp; mp += addedMp;
            if (target.Summon is { } summon) { summon.Hp = hp; summon.Mp = mp; }
            else { target.Owner.ConnectionInfo.CharacterHp = hp; target.Owner.ConnectionInfo.CharacterMp = mp; }
        }
        SendSupportProperty(target, "hp", hp);
        if (fields.EffectType is 505 or 508) SendSupportProperty(target, "mp", mp);
        _monsterState.AddHateFromHelp(target.Owner, caster,
            HateRules.SkillHate(fields.HateMod, fields.HateBasic, fields.HatePerSkl, level, addedHp), casterSummon);
        if (target.Summon is not null && _creatures is not null)
        {
            Creatures.CreatureCard card;
            lock (target.Owner.ConnectionInfo.SummonLock) card = target.Owner.ConnectionInfo.CreatureCards.Values
                .FirstOrDefault(c => c.SummonHandle == target.Handle);
            if (card is not null) _creatures.SetSummonVitals(target.Owner, card, hp, mp);
        }
        return new SkillHit(fields.EffectType is 505 or 508 ? SkillHitType.AddHpMpSp : SkillHitType.AddHp,
            target.Handle, hp, addedHp, IncMp: addedMp, TargetMp: mp);
    }

    private static int SupportAmount(float value) => !float.IsFinite(value) || value <= 0 ? 0 : (int)Math.Min(int.MaxValue, (double)value);

    private void SendSupportProperty(SupportTarget target, string name, int value) =>
        SendToSelfAndWatchers(target.Owner, GameStatPackets.BuildProperty(target.Handle, name, value));

    private void RefreshSupportStats(SupportTarget target)
    {
        if (target.Summon is null) { SendStatRefresh(target.Owner, target.Owner.ConnectionInfo); return; }
        SummonBuffStats.Refresh(target.Summon, _stateCatalog);
        target.Owner.Connection.Send(GameStatPackets.BuildStatInfo(target.Handle, target.Summon.Stats, StatInfoType.Total));
        SendSupportProperty(target, "max_hp", SupportAmount(target.Summon.Stats.MaxHp));
        SendSupportProperty(target, "max_mp", SupportAmount(target.Summon.Stats.MaxMp));
        SendSupportProperty(target, "hp", target.Summon.Hp); SendSupportProperty(target, "mp", target.Summon.Mp);
    }

    private bool ApplySupportState(SupportTarget target, int stateId, int skillId, int level, uint now, uint end,
        uint source, bool projection = false)
    {
        if (target.Summon is null) return ApplyState(target.Owner, stateId, skillId, level, now, end, source, projection);
        var summon = target.Summon; var rule = _stateCatalog.GetRule(stateId);
        var pulse = NewStatePulse(stateId, now, source, target.Owner);
        var removed = new List<ActiveBuff>(); ActiveBuff applied;
        lock (summon.BuffLock)
        {
            var decision = StateStacking.Decide(summon.ActiveBuffs, stateId, rule, level, end, _stateCatalog.GetRule);
            if (decision.Refused) return false;
            var handle = decision.RefreshIndex >= 0 ? summon.ActiveBuffs[decision.RefreshIndex].StateHandle : ++summon.NextStateHandle;
            var indices = decision.Removed.ToList();
            if (decision.RefreshIndex >= 0) indices.Add(decision.RefreshIndex);
            foreach (var i in indices.Distinct().OrderDescending())
            { if (i != decision.RefreshIndex) removed.Add(summon.ActiveBuffs[i]); summon.ActiveBuffs.RemoveAt(i); }
            applied = new ActiveBuff(handle, stateId, skillId, decision.Level, now, end, source, projection, pulse);
            summon.ActiveBuffs.Add(applied);
        }
        foreach (var state in removed) SendToSelfAndWatchers(target.Owner,
            GameSkillPackets.BuildStateRemoval(target.Handle, state.StateHandle, (uint)state.StateId));
        SendToSelfAndWatchers(target.Owner, GameSkillPackets.BuildState(target.Handle, applied.StateHandle,
            (uint)stateId, (ushort)applied.StateLevel, end, now));
        if (Casting.CastRules.InterruptsCasting(stateId, rule.EffectType, rule.Values))
            CancelSummonCast(target.Owner, summon.Handle, true);
        return true;
    }

    private void SendSupportFire(GameClient caster, GameActionPackets.SkillRequest request, IReadOnlyList<SkillHit> hits,
        IReadOnlyList<SupportTarget> targets, float radius)
    {
        var recipients = new HashSet<GameClient> { caster };
        foreach (var target in targets)
        {
            recipients.Add(target.Owner); if (_players is not null)
                foreach (var peer in _players.Observers(target.Owner)) recipients.Add(peer);
        }
        if (_players is not null) foreach (var peer in _players.Observers(caster)) recipients.Add(peer);
        foreach (var recipient in recipients)
        {
            var visible = hits.Where(h => ReferenceEquals(caster, recipient) || targets.Any(t => t.Handle == h.TargetHandle
                && (ReferenceEquals(t.Owner, recipient) || SeesPlayer(recipient, t.Owner)))).ToArray();
            recipient.Connection.Send(GameSkillPackets.BuildSkill((ushort)request.SkillId, request.SkillLevel,
                caster.ConnectionInfo.CharacterHandle, request.Target == 0 ? caster.ConnectionInfo.CharacterHandle : request.Target,
                request.X, request.Y, request.Z, caster.ConnectionInfo.Layer, SkillPacketType.Fire, 0, 0,
                caster.ConnectionInfo.CharacterHp, caster.ConnectionInfo.CharacterMp, hits: visible,
                multiple: targets.Count > 1, range: radius));
        }
    }

    private static bool SeesPlayer(GameClient viewer, GameClient target)
    {
        lock (viewer.ConnectionInfo.PlayerVisibilityLock)
            return viewer.ConnectionInfo.SpawnedPlayers.ContainsKey(target.ConnectionInfo.CharacterHandle);
    }
}
