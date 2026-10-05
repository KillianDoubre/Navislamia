using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services;

public partial class SkillCastService
{
    private readonly object _periodicGate = new();

    private SupportTarget StateCaster(GameClient owner, uint handle) => handle == 0
        ? new SupportTarget(owner) : SupportUnits(owner).FirstOrDefault(t => t.Handle == handle);

    private StatePulse NewStatePulse(int stateId, uint now, uint source, GameClient owner)
    {
        var rule = _stateCatalog.Periodic(stateId);
        if (!rule.Supported) return null;
        var caster = StateCaster(owner, source);
        var stats = caster?.Summon?.Stats ?? (caster is null ? null : _statService.Compute(caster.Owner.ConnectionInfo).Total);
        return new StatePulse(now, rule.SnapshotDamage(stats));
    }

    public void ApplyMonsterState(GameClient client, int stateId, int stateLevel, uint durationTicks, long monsterId)
    {
        var now = ServerClock.Now;
        var pulse = new StatePulse(now, _stateCatalog.Periodic(stateId).SnapshotDamage(_combatService.GetMonsterStats(monsterId)))
        { MonsterId = monsterId, MonsterLife = _monsterState.LifeVersion(monsterId) };
        ApplyState(client, stateId, 0, stateLevel, now, unchecked(now + durationTicks),
            client.ConnectionInfo.GetMonsterHandle(monsterId), suppliedPulse: pulse);
    }

    /// <summary>Called before expiry: the last fire may be at end_time. A late scheduler processes one fire per update.</summary>
    public void ProcessPeriodicStates(uint now)
    {
        lock (_periodicGate)
        {
            GameClient[] clients; lock (_lock) clients = _clients.ToArray();
            foreach (var client in clients)
            {
                if (client.ConnectionInfo.CharacterHandle == 0) continue;
                var retention = EnergyRetention(client.ConnectionInfo, now);
                if (client.ConnectionInfo.Energy.Expire(now, () => retention > 0 && _random.Next(100) < retention)) EnergyCounter.Publish(client);
                ProcessUnitStates(new SupportTarget(client), now);
                foreach (var summon in client.ConnectionInfo.Summons) ProcessUnitStates(new SupportTarget(client, summon), now);
            }
            foreach (var id in _monsterState.StatefulInstances()) ProcessMonsterStates(id, clients, now);
        }
    }

    private int EnergyRetention(ConnectionInfo info, uint now)
    {
        decimal total = 0;
        lock (info.BuffLock)
            foreach (var state in info.ActiveBuffs)
            {
                if (state.EndTick != NeverExpires && unchecked((int)(now - state.EndTick)) > 0) continue;
                var rule = _stateCatalog.GetRule(state.StateId);
                if (rule.EffectType == 3301 && rule.Values is { Length: >= 2 } values)
                    total += values[0] + values[1] * state.StateLevel;
            }
        return (int)Math.Clamp(total, 0m, 100m);
    }

    private bool Due(ActiveBuff state, PeriodicStateRule rule, uint now, out uint fire, out bool final)
    {
        fire = unchecked((state.Pulse?.LastFire ?? state.StartTick) + rule.Interval);
        final = state.EndTick != NeverExpires && unchecked((int)(fire + rule.Interval - state.EndTick)) > 0;
        return rule.Supported && (state.Pulse?.LastUpdateTick is not { } update || unchecked((int)(now - update)) > 100)
            && unchecked((int)(now - fire)) > 0
            && (state.EndTick == NeverExpires || unchecked((int)(fire - state.EndTick)) <= 0);
    }

    private void ProcessUnitStates(SupportTarget target, uint now)
    {
        var list = target.Summon?.ActiveBuffs ?? target.Owner.ConnectionInfo.ActiveBuffs;
        var gate = target.Summon?.BuffLock ?? target.Owner.ConnectionInfo.BuffLock;
        ActiveBuff[] states; lock (gate) states = list.ToArray();
        foreach (var original in states)
        {
            if (target.Hp <= 0) break;
            var state = original;
            var rule = _stateCatalog.Periodic(state.StateId);
            if (!Due(state, rule, now, out var fire, out var final)) continue;
            var caster = StateCaster(target.Owner, state.SourceHandle);
            var monster = state.Pulse?.MonsterId ?? -1;
            if (rule.Damage && (monster >= 0 ? !_monsterState.TryGetInstance(monster, out _)
                || (state.Pulse.MonsterLife >= 0 && state.Pulse.MonsterLife != _monsterState.LifeVersion(monster)) : caster is null))
            {
                lock (gate) list.Remove(state);
                SendToSelfAndWatchers(target.Owner, GameSkillPackets.BuildStateRemoval(target.Handle, state.StateHandle, (uint)state.StateId));
                RefreshSupportStats(target);
                continue;
            }
            var newPulse = state.Pulse ?? NewStatePulse(state.StateId, state.StartTick, state.SourceHandle, target.Owner);
            lock (gate)
            {
                var index = list.IndexOf(state); if (index < 0) continue;
                if (state.Pulse is null)
                { state = state with { Pulse = newPulse }; list[index] = state; }
                state.Pulse.LastFire = fire; state.Pulse.LastUpdateTick = now;
            }
            var stats = target.Summon?.Stats ?? _statService.Compute(target.Owner.ConnectionInfo).Total;
            var maxHp = SupportAmount(stats.MaxHp); var maxMp = SupportAmount(stats.MaxMp);
            var (hp, mp) = rule.Amounts(state.StateLevel, state.Pulse.BaseDamage, maxHp, maxMp);
            var source = monster >= 0 ? target.Owner.ConnectionInfo.GetMonsterHandle(monster) : caster?.Handle ?? state.SourceHandle;
            if (rule.Damage)
            {
                var sourceStats = monster >= 0 ? _combatService.GetMonsterStats(monster)
                    : caster?.Summon?.Stats ?? (caster is null ? null : _statService.Compute(caster.Owner.ConnectionInfo).Total);
                var level = monster >= 0 && _monsterState.TryGetInstance(monster, out var instance) ? instance.Level
                    : caster?.Owner.ConnectionInfo.CharacterLevel ?? 1;
                hp = StateDamage(rule, hp, sourceStats, stats, level);
                var wasAlive = target.Hp > 0;
                var remaining = target.Summon is null ? _combatService.DamagePlayer(target.Owner, hp)
                    : _creatures?.DamageSummon(target.Owner, target.Handle, hp) ?? target.Hp;
                if (wasAlive && remaining <= 0 && (monster >= 0 || caster is not null))
                    _combatService.NotifyDeath(monster >= 0 ? new Combat.CombatActor(target.Owner, MonsterId: monster)
                        : new Combat.CombatActor(caster.Owner, caster.Summon?.Handle ?? 0),
                        new Combat.CombatActor(target.Owner, target.Summon?.Handle ?? 0));
                state.Pulse.Total = SaturatedAdd(state.Pulse.Total, hp);
                SendUnitPulse(target, state, source, StateResultType.DamageHp, hp, remaining, final, state.Pulse.Total, monster);
            }
            else
            {
                var oldHp = target.Hp;
                var oldMp = target.Summon?.Mp ?? target.Owner.ConnectionInfo.CharacterMp;
                hp = Math.Min(hp, Math.Max(0, maxHp - oldHp)); mp = Math.Min(mp, Math.Max(0, maxMp - oldMp));
                if (target.Summon is null)
                {
                    lock (target.Owner.ConnectionInfo.ProgressLock)
                    {
                        if (target.Hp <= 0) continue;
                        hp = Math.Min(hp, Math.Max(0, maxHp - target.Hp));
                        mp = Math.Min(mp, Math.Max(0, maxMp - target.Owner.ConnectionInfo.CharacterMp));
                        target.Owner.ConnectionInfo.CharacterHp += hp; target.Owner.ConnectionInfo.CharacterMp += mp;
                    }
                }
                else
                {
                    Creatures.CreatureCard card;
                    lock (target.Owner.ConnectionInfo.SummonLock) card = target.Owner.ConnectionInfo.CreatureCards.Values.FirstOrDefault(c => c.SummonHandle == target.Handle);
                    if (card is null || _creatures is null) continue;
                    _creatures.SetSummonVitals(target.Owner, card, oldHp + hp, oldMp + mp);
                }
                state.Pulse.Total = SaturatedAdd(state.Pulse.Total, hp > 0 ? hp : mp);
                if (hp > 0)
                {
                    SendUnitPulse(target, state, source, StateResultType.HealHp, hp, target.Hp, final, state.Pulse.Total, monster);
                    SendSupportProperty(target, "hp", target.Hp);
                }
                if (mp > 0)
                {
                    var currentMp = target.Summon?.Mp ?? target.Owner.ConnectionInfo.CharacterMp;
                    SendUnitPulse(target, state, source, StateResultType.HealMp, mp, currentMp, final, hp > 0 ? 0 : state.Pulse.Total, monster);
                    SendSupportProperty(target, "mp", currentMp);
                }
            }
        }
    }

    private int StateDamage(PeriodicStateRule rule, int amount, StatBlock caster, StatBlock target, int level)
    {
        if (amount <= 0) return 0;
        var physical = rule.BaseEffect is 1 or 2 or 6;
        if (rule.BaseEffect is 1 or 3) amount = CombatFormulas.DefendedDamage(level, amount, physical ? target.Defence : target.MagicDefence);
        if (caster is not null && _random.Next(100) <= (int)caster.Critical)
            amount = SupportAmount(amount * (1f + caster.CriticalPower / 100f));
        return CombatFormulas.ResistedDamage(amount, target.GetResistance(rule.Element));
    }

    private static int SaturatedAdd(int first, int second) => (int)Math.Min(int.MaxValue, (long)first + second);

    private void SendUnitPulse(SupportTarget target, ActiveBuff state, uint source, StateResultType type, int amount,
        int value, bool final, int total, long monster)
    {
        var recipients = (_players?.Observers(target.Owner) ?? Array.Empty<GameClient>()).Append(target.Owner).Distinct();
        foreach (var viewer in recipients)
        {
            var casterHandle = monster >= 0 ? viewer.ConnectionInfo.GetMonsterHandle(monster) : source;
            viewer.Connection.Send(GameStateResultPackets.StateResult(casterHandle, target.Handle, state.StateId,
                (ushort)state.StateLevel, type, amount, value, final, total));
        }
    }

    private void ProcessMonsterStates(long id, GameClient[] clients, uint now)
    {
        if (!_monsterState.IsAlive(id) || !_monsterState.TryGetInstance(id, out var instance)) return;
        foreach (var state in _monsterState.GetStates(id))
        {
            if (!_monsterState.IsAlive(id)) break;
            var rule = _stateCatalog.Periodic(state.StateId);
            if (!Due(state, rule, now, out var fire, out var final)) continue;
            var owner = clients.FirstOrDefault(c => c.ConnectionInfo.CharacterHandle == state.SourceHandle
                || c.ConnectionInfo.Summons.Any(s => s.Handle == state.SourceHandle));
            var caster = owner is null ? null : StateCaster(owner, state.SourceHandle);
            if (rule.Damage && caster is null)
            {
                _monsterState.RemoveState(id, state);
                foreach (var viewer in clients)
                    if (viewer.ConnectionInfo.GetMonsterHandle(id) is > 0 and var handle)
                        viewer.Connection.Send(GameSkillPackets.BuildStateRemoval(handle, state.StateHandle, (uint)state.StateId));
                continue;
            }
            // Every production application supplies its snapshot. A state manually inserted by a script
            // without a caster can still perform an item heal.
            var pulse = state.Pulse;
            if (pulse is null) continue;
            pulse.LastFire = fire; pulse.LastUpdateTick = now;
            var stats = _combatService.GetMonsterStats(id) ?? new StatBlock { MaxHp = instance.Hp };
            var (amount, mp) = rule.Amounts(state.StateLevel, pulse.BaseDamage, SupportAmount(stats.MaxHp), SupportAmount(stats.MaxMp));
            int value;
            var type = rule.Damage ? StateResultType.DamageHp : StateResultType.HealHp;
            if (rule.Damage)
            {
                var casterStats = caster.Summon?.Stats ?? _statService.Compute(owner.ConnectionInfo).Total;
                amount = StateDamage(rule, amount, casterStats, stats, owner.ConnectionInfo.CharacterLevel);
                value = _combatService.ApplyDamage(owner, id, owner.ConnectionInfo.GetMonsterHandle(id), amount);
            }
            else
            {
                amount = _monsterState.Heal(id, amount, SupportAmount(stats.MaxHp));
                mp = mp > 0 ? Math.Max(0, _monsterState.ChangeMp(id, mp, SupportAmount(stats.MaxMp))) : 0;
                value = _monsterState.GetHp(id);
            }
            pulse.Total = SaturatedAdd(pulse.Total, rule.Damage || amount > 0 ? amount : mp);
            if (!rule.Damage && amount == 0 && mp == 0) continue;
            foreach (var viewer in clients)
            {
                var handle = viewer.ConnectionInfo.GetMonsterHandle(id); if (handle == 0) continue;
                var source = pulse.MonsterId >= 0 ? viewer.ConnectionInfo.GetMonsterHandle(pulse.MonsterId) : state.SourceHandle;
                if (rule.Damage || amount > 0)
                    viewer.Connection.Send(GameStateResultPackets.StateResult(source, handle, state.StateId,
                        (ushort)state.StateLevel, type, amount, value, final, pulse.Total));
                if (!rule.Damage && mp > 0)
                    viewer.Connection.Send(GameStateResultPackets.StateResult(source, handle, state.StateId,
                        (ushort)state.StateLevel, StateResultType.HealMp, mp, _monsterState.GetMp(id), final, amount > 0 ? 0 : pulse.Total));
            }
        }
    }
}
