using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Casting;

namespace Navislamia.Game.Services;

public partial class SkillCastService
{
    private void ToggleSummonAura(SummonCast cast, uint now)
    {
        var actor = cast.Actor; var fields = cast.Cast.Fields;
        int previous;
        lock (actor.BuffLock) previous = actor.ActiveAuras.GetValueOrDefault(fields.ToggleGroup);
        if (previous != 0)
        {
            lock (actor.BuffLock) actor.ActiveAuras.Remove(fields.ToggleGroup);
            RemoveSummonAuraProjections(cast.Master, actor.Handle, previous);
            SendToSelfAndWatchers(cast.Master, GameSkillPackets.BuildAura(actor.Handle, (ushort)previous, false));
        }
        if (previous == fields.SkillId) return;
        lock (actor.BuffLock) actor.ActiveAuras[fields.ToggleGroup] = fields.SkillId;
        SendToSelfAndWatchers(cast.Master, GameSkillPackets.BuildAura(actor.Handle, (ushort)fields.SkillId, true));
        PulseSummonAura(cast, now);
    }

    private void PulseSummonAuras(GameClient master, SummonPresence actor, uint now)
    {
        int[] skills;
        lock (actor.BuffLock) skills = actor.ActiveAuras.Values.ToArray();
        if (actor.Hp <= 0 || master.ConnectionInfo.CharacterHp <= 0)
        {
            RemoveSummonAuraProjections(master, actor.Handle);
            return;
        }
        foreach (var skill in skills)
        {
            if (!_catalog.TryGet(skill, out var fields)) continue;
            byte level = 0;
            lock (master.ConnectionInfo.SummonLock)
                level = master.ConnectionInfo.CreatureCards.Values.FirstOrDefault(c => c.SummonHandle == actor.Handle)
                    ?.Skills.GetValueOrDefault(skill) ?? 0;
            if (level == 0) continue;
            var request = new GameActionPackets.SkillRequest((ushort)skill, actor.Handle, actor.Handle,
                0, 0, 0, (sbyte)actor.Layer, level);
            PulseSummonAura(new SummonCast
            {
                Master = master,
                Actor = actor,
                Cast = new PendingCast(request, fields, level, -1, now, now)
            }, now);
        }
    }

    private void PulseSummonAura(SummonCast cast, uint now)
    {
        var f = cast.Cast.Fields;
        var targets = SummonSupportTargets(cast, now);
        RemoveSummonAuraProjections(cast.Master, cast.Actor.Handle, f.SkillId, targets.Select(t => t.Handle).ToHashSet());
        foreach (var target in targets)
        {
            var states = target.Summon?.ActiveBuffs ?? target.Owner.ConnectionInfo.ActiveBuffs;
            lock (target.Summon?.BuffLock ?? target.Owner.ConnectionInfo.BuffLock)
                if (states.Any(s => s.AuraProjection && s.SourceHandle == cast.Actor.Handle
                    && s.SkillId == f.SkillId && unchecked((int)(s.EndTick - now)) > 100)) continue;
            if (ApplySupportState(target, f.StateId, f.SkillId, BuffCurve.StateLevel(f, cast.Cast.SkillLevel), now,
                unchecked(now + 1100), cast.Actor.Handle, true)) RefreshSupportStats(target);
        }
    }

    private void RemoveSummonAuraProjections(GameClient master, uint source, int? skill = null, HashSet<uint> keep = null)
    {
        foreach (var target in SupportUnits(master))
        {
            if (keep?.Contains(target.Handle) == true) continue;
            var states = target.Summon?.ActiveBuffs ?? target.Owner.ConnectionInfo.ActiveBuffs;
            var removed = new List<ActiveBuff>();
            lock (target.Summon?.BuffLock ?? target.Owner.ConnectionInfo.BuffLock)
                for (var i = states.Count - 1; i >= 0; i--)
                    if (states[i].AuraProjection && states[i].SourceHandle == source
                        && (!skill.HasValue || states[i].SkillId == skill.Value))
                    { removed.Add(states[i]); states.RemoveAt(i); }
            foreach (var state in removed) SendToSelfAndWatchers(target.Owner,
                GameSkillPackets.BuildStateRemoval(target.Handle, state.StateHandle, (uint)state.StateId));
            if (removed.Count > 0) RefreshSupportStats(target);
        }
    }

    public void ForgetSummonCaster(GameClient master, uint handle)
    {
        CancelSummonCast(master, handle, true);
        var actor = Array.Find(master.ConnectionInfo.Summons, s => s.Handle == handle);
        int[] auras = Array.Empty<int>();
        if (actor is not null)
            lock (actor.BuffLock) { auras = actor.ActiveAuras.Values.ToArray(); actor.ActiveAuras.Clear(); }
        foreach (var skill in auras) SendToSelfAndWatchers(master, GameSkillPackets.BuildAura(handle, (ushort)skill, false));
        RemoveSummonAuraProjections(master, handle);
    }
}
