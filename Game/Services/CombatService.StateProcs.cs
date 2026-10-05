using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Combat;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services;

public partial class CombatService
{
    private readonly StateProcs _stateProcs;
    private readonly CooldownProcs _cooldownProcs;
    private readonly IItemStatCatalog _procItemStats;

    public void NotifyHit(CombatActor attacker, CombatActor target, HitResult hit,
        uint attackType = EnergyProcs.NormalAttack, int element = 0, bool attackProcs = true)
    {
        if (attacker.Owner is null || target.Owner is null) return;
        if ((hit.Flags & HitFlags.Miss) == 0 && attackProcs)
        {
            RunStateProcs(attacker, target, StateProcEvent.Attack, attackType, element);
            RunStateProcs(target, attacker, StateProcEvent.BeingAttacked, attackType, element);
            if (!attacker.IsMonster && !attacker.IsSummon)
                _energyProcs?.OnAttack(attacker.Owner, attackType, element, ActorHpPercent(target));
            if (!target.IsMonster && !target.IsSummon)
                _energyProcs?.OnBeingAttacked(target.Owner, attackType, element, ActorHpPercent(attacker));
        }
        if ((hit.Flags & HitFlags.Critical) != 0)
        {
            RunStateProcs(attacker, target, StateProcEvent.Critical, attackType, element);
            RunStateProcs(target, attacker, StateProcEvent.BeingCritical, attackType, element);
        }
        if ((hit.Flags & HitFlags.Block) != 0)
            RunStateProcs(target, attacker, StateProcEvent.Block, attackType, element);
        if ((hit.Flags & HitFlags.PerfectBlock) != 0)
            RunStateProcs(target, attacker, StateProcEvent.PerfectBlock, attackType, element);
        if ((hit.Flags & HitFlags.Miss) != 0)
            RunStateProcs(target, attacker, StateProcEvent.Avoid, attackType, element);
    }

    // Called exactly at a death transition. Monster kill credit is instead raised by reward distribution.
    public void NotifyDeath(CombatActor killer, CombatActor victim)
    {
        RunStateProcs(killer, victim, StateProcEvent.Kill);
        RunStateProcs(victim, killer, StateProcEvent.Dead);
    }

    private void RewardKillProcs(Network.Clients.GameClient client, long monster)
    {
        var victim = new CombatActor(client, MonsterId: monster);
        RunStateProcs(new CombatActor(client), victim, StateProcEvent.Kill);
        var now = ServerClock.Now; var (x, y) = _worldState.GetPosition(monster);
        foreach (var summon in client.ConnectionInfo.Summons)
        {
            var p = summon.PositionAt(now);
            // StructMonster.cpp:1070-1073 uses GameRule::VISIBLE_RANGE (GameRule.h:22).
            if (CombatRange.Distance(p.X, p.Y, x, y) <= 525f)
                RunStateProcs(new CombatActor(client, summon.Handle), victim, StateProcEvent.Kill);
        }
    }

    private void RunStateProcs(CombatActor owner, CombatActor other, StateProcEvent trigger, uint type = 0, int element = 0)
    {
        if (_stateProcs is null && _cooldownProcs is null || owner.Owner?.ConnectionInfo.CharacterHandle is not > 0
            || other.Owner is null) return;
        var tags = new List<StateProcTag>();
        // EF_INC_SKILL_COOL_TIME_ON_* are passive skills only: a player's or a summon's, never a monster's or a state's.
        KeyValuePair<int, byte>[] learned = null;
        int weapon = 0;
        if (owner.IsMonster)
        {
            if (!_worldState.TryGetInstance(owner.MonsterId, out _)) return;
            AddStates(_worldState.GetStates(owner.MonsterId));
        }
        else if (owner.IsSummon)
        {
            var info = owner.Owner.ConnectionInfo;
            var summon = Array.Find(info.Summons, s => s.Handle == owner.SummonHandle);
            if (summon is null) return;
            lock (info.SummonLock)
            {
                var card = info.CreatureCards.Values.FirstOrDefault(c => c.SummonHandle == owner.SummonHandle);
                if (card is null) return;
                learned = card.Skills.ToArray();
                if (_stateProcs is not null) tags.AddRange(_stateProcs.Learned(learned));
                foreach (var item in card.Equipment.Where(i => !i.Exhausted))
                    if (_procItemStats?.GetWeaponType(item.ResourceId) is { } itemWeapon) { weapon = (int)itemWeapon; break; }
            }
            lock (summon.BuffLock) AddStates(summon.ActiveBuffs.ToArray());
        }
        else
        {
            var info = owner.Owner.ConnectionInfo;
            learned = info.LearnedSkills.ToArray();
            if (_stateProcs is not null) tags.AddRange(_stateProcs.Learned(learned));
            weapon = info.EquippedWeapon is { } w ? (int)w : 0;
            lock (info.BuffLock) AddStates(info.ActiveBuffs.ToArray());
        }
        if (tags.Count == 0 && (learned is null || _cooldownProcs is null)) return;
        var stats = ActorStats(owner);
        var mp = owner.IsMonster ? _worldState.GetMp(owner.MonsterId)
            : owner.IsSummon ? Array.Find(owner.Owner.ConnectionInfo.Summons, s => s.Handle == owner.SummonHandle)?.Mp ?? 0
            : owner.Owner.ConnectionInfo.CharacterMp;
        var mpPercent = AttackProcConditions.Percent(mp, stats?.MaxMp ?? 0);
        var resolved = StateProcs.Resolve(tags, trigger, weapon, type, element, ActorHpPercent(owner), ActorHpPercent(other),
            ActorLevel(owner) - ActorLevel(other), mpPercent, _random);
        foreach (var proc in resolved) _casts?.ApplyCombatState(proc.Self ? owner : other, owner, proc);
        if (learned is not null && _cooldownProcs is not null)
        {
            // StructCooldownProc::Proc: the cool time changes on the passive's owner, whichever side it was on.
            foreach (var proc in _cooldownProcs.Resolve(learned, trigger, weapon, type, element, ActorHpPercent(owner),
                         ActorHpPercent(other), ActorLevel(owner) - ActorLevel(other), mpPercent, _random))
                _casts?.ApplyCooldownProc(owner, proc);
        }

        void AddStates(IEnumerable<ActiveBuff> states)
        {
            var now = ServerClock.Now;
            foreach (var state in states)
            {
                if (state.EndTick != uint.MaxValue && unchecked((int)(now - state.EndTick)) > 0) continue;
                var rule = _states.GetRule(state.StateId);
                tags.Add(new StateProcTag(0, rule.EffectType, state.StateLevel, rule.Values, FromState: true));
            }
        }
    }

    private StatBlock ActorStats(CombatActor actor) => actor.IsMonster ? GetMonsterStats(actor.MonsterId)
        : actor.IsSummon ? Array.Find(actor.Owner.ConnectionInfo.Summons, s => s.Handle == actor.SummonHandle)?.Stats
        : GetPlayerStats(actor.Owner);

    private int ActorLevel(CombatActor actor) => actor.IsMonster
        ? _worldState.TryGetInstance(actor.MonsterId, out var instance) ? instance.Level : 1
        : actor.IsSummon ? Array.Find(actor.Owner.ConnectionInfo.Summons, s => s.Handle == actor.SummonHandle)?.Entry.Level ?? 1
        : actor.Owner.ConnectionInfo.CharacterLevel;

    private int ActorHpPercent(CombatActor actor)
    {
        if (actor.Owner is null) return -1;
        var hp = actor.IsMonster ? _worldState.GetHp(actor.MonsterId)
            : actor.IsSummon ? Array.Find(actor.Owner.ConnectionInfo.Summons, s => s.Handle == actor.SummonHandle)?.Hp ?? 0
            : actor.Owner.ConnectionInfo.CharacterHp;
        var max = actor.IsMonster ? ActorStats(actor)?.MaxHp ?? 0
            : actor.IsSummon ? ActorStats(actor)?.MaxHp ?? 0 : actor.Owner.ConnectionInfo.CharacterMaxHp;
        return AttackProcConditions.Percent(hp, max);
    }
}
