using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Buffs;

namespace Navislamia.Game.Services;

public partial class SkillCastService
{
    public void ProcessBuffs(uint now)
    {
        ExpirePlayerBuffs(now); ExpireMonsterStates(now);
        GameClient[] clients; lock (_lock) clients = _clients.ToArray();
        foreach (var client in clients)
        {
            foreach (var summon in client.ConnectionInfo.Summons)
            {
                PulseSummonAuras(client, summon, now);
                var expired = new List<ActiveBuff>();
                lock (summon.BuffLock)
                    for (var i = summon.ActiveBuffs.Count - 1; i >= 0; i--)
                    {
                        var state = summon.ActiveBuffs[i];
                        if (state.EndTick == uint.MaxValue || unchecked((int)(state.EndTick - now)) > 0) continue;
                        expired.Add(state); summon.ActiveBuffs.RemoveAt(i);
                    }
                foreach (var state in expired) SendToSelfAndWatchers(client,
                    GameSkillPackets.BuildStateRemoval(summon.Handle, state.StateHandle, (uint)state.StateId));
                if (expired.Count > 0) RefreshSupportStats(new SupportTarget(client, summon));
            }
            int[] auras;
            lock (client.ConnectionInfo.BuffLock) auras = client.ConnectionInfo.ActiveAuras.Values.ToArray();
            foreach (var skillId in auras)
                if (_catalog.TryGet(skillId, out var fields)) PulseSupportAura(client, fields, now);
        }
    }

    private void PulseSupportAura(GameClient caster, CastableBuffFields fields, uint now)
    {
        if (caster.ConnectionInfo.CharacterHp <= 0) { RemoveAuraProjections(caster, fields.SkillId); return; }
        var level = caster.ConnectionInfo.LearnedSkills.TryGetValue(fields.SkillId, out var learned) ? learned : 1;
        var targets = SelectSupportTargets(caster, caster.ConnectionInfo.CharacterHandle, fields, now)
            .Where(t => t.Summon is not null || !ReferenceEquals(t.Owner, caster)).ToArray();
        // A short lease refreshed by the aura tick; leaving the party/radius removes it immediately.
        RemoveAuraProjections(caster, fields.SkillId, targets.Select(t => t.Handle).ToHashSet());
        foreach (var target in targets)
        {
            var states = target.Summon?.ActiveBuffs ?? target.Owner.ConnectionInfo.ActiveBuffs;
            lock (target.Summon?.BuffLock ?? target.Owner.ConnectionInfo.BuffLock)
                if (states.Any(s => s.AuraProjection && s.SourceHandle == caster.ConnectionInfo.CharacterHandle
                    && s.SkillId == fields.SkillId && unchecked((int)(s.EndTick - now)) > 100)) continue;
            if (ApplySupportState(target, fields.StateId, fields.SkillId, BuffCurve.StateLevel(fields, level), now,
                unchecked(now + 1100), caster.ConnectionInfo.CharacterHandle, projection: true)) RefreshSupportStats(target);
        }
    }

    private void RemoveAllAuraProjections(GameClient caster)
    {
        int[] skills; lock (caster.ConnectionInfo.BuffLock) skills = caster.ConnectionInfo.ActiveAuras.Values.ToArray();
        foreach (var skill in skills) RemoveAuraProjections(caster, skill);
    }

    private void RemoveAuraProjections(GameClient caster, int skill, HashSet<uint> keep = null)
    {
        foreach (var target in SupportUnits(caster))
        {
            if (keep?.Contains(target.Handle) == true) continue;
            var states = target.Summon?.ActiveBuffs ?? target.Owner.ConnectionInfo.ActiveBuffs;
            var expired = new List<ActiveBuff>();
            lock (target.Summon?.BuffLock ?? target.Owner.ConnectionInfo.BuffLock)
                for (var i = states.Count - 1; i >= 0; i--)
                    if (states[i].AuraProjection && states[i].SourceHandle == caster.ConnectionInfo.CharacterHandle
                        && states[i].SkillId == skill) { expired.Add(states[i]); states.RemoveAt(i); }
            foreach (var state in expired) SendToSelfAndWatchers(target.Owner,
                GameSkillPackets.BuildStateRemoval(target.Handle, state.StateHandle, (uint)state.StateId));
            if (expired.Count > 0) RefreshSupportStats(target);
        }
    }
}
