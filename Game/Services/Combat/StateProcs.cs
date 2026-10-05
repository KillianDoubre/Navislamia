using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.Services.Combat;

public enum StateProcEvent { Attack, BeingAttacked, Critical, BeingCritical, Avoid, Block, PerfectBlock, Kill, Dead }
public readonly record struct StateProc(int SkillId, int StateId, int Level, uint Duration, int MpCost, bool Self);
public readonly record struct StateProcTag(int SkillId, int Effect, int Level, decimal[] Values, bool FromState = false);

/// <summary>CalculateStat.cpp:923-1070 (skills), :2051-2267 (states); distinct effect id spaces.</summary>
public sealed class StateProcs
{
    public static readonly int[] SkillEffects = Enumerable.Range(10048, 15).ToArray();
    private readonly FrozenDictionary<int, (int Effect, decimal[] Values)> _skills;

    public StateProcs(ISkillResourceRepository repository) : this(repository.GetSkillRowsByEffectType(SkillEffects)
        .Select(r => (r.SkillId, r.EffectType, r.Vars))) { }

    public StateProcs(IEnumerable<(int SkillId, int Effect, decimal[] Values)> skills) =>
        _skills = skills.ToFrozenDictionary(s => s.SkillId, s => (s.Effect, s.Values ?? Array.Empty<decimal>()));

    public IEnumerable<StateProcTag> Learned(IEnumerable<KeyValuePair<int, byte>> skills) => skills
        .Where(s => s.Value > 0 && _skills.ContainsKey(s.Key))
        .Select(s => new StateProcTag(s.Key, _skills[s.Key].Effect, s.Value, _skills[s.Key].Values));

    public static bool Describe(int effect, bool state, out StateProcEvent trigger, out bool self)
    {
        var mapped = state ? effect switch
        {
            26 => 10048, 36 => 10048, 37 => 10049, 38 => 10050, 39 => 10051,
            >= 3201 and <= 3211 => effect - 3201 + 10052,
            3311 => -1, _ => 0
        } : effect;
        (trigger, self) = mapped switch
        {
            10048 => (StateProcEvent.Attack, false), 10049 => (StateProcEvent.Attack, true),
            10050 => (StateProcEvent.BeingAttacked, false), 10051 => (StateProcEvent.BeingAttacked, true),
            10052 => (StateProcEvent.Kill, true), -1 => (StateProcEvent.Dead, true),
            10053 => (StateProcEvent.Critical, false), 10054 => (StateProcEvent.Critical, true),
            10055 => (StateProcEvent.BeingCritical, false), 10056 => (StateProcEvent.BeingCritical, true),
            10057 => (StateProcEvent.Avoid, false), 10058 => (StateProcEvent.Avoid, true),
            10059 => (StateProcEvent.Block, false), 10060 => (StateProcEvent.Block, true),
            10061 => (StateProcEvent.PerfectBlock, false), 10062 => (StateProcEvent.PerfectBlock, true),
            _ => default
        };
        return mapped is >= 10048 and <= 10062 or -1;
    }

    /// <summary>Collect before executing: a proc may replace a state which provided another proc.</summary>
    public static IReadOnlyList<StateProc> Resolve(IEnumerable<StateProcTag> tags, StateProcEvent trigger,
        int weapon, uint type, int element, int hp, int otherHp, int levelDifference, int mpPercent, ICombatRandom random)
    {
        var result = new List<StateProc>();
        foreach (var tag in tags)
        {
            if (tag.Level <= 0 || !Describe(tag.Effect, tag.FromState, out var kind, out var self) || kind != trigger) continue;
            var v = tag.Values ?? Array.Empty<decimal>();
            // State 26 retains its weapon gate; its tag fixes normal attacks/any element and has no HP bounds.
            var old = tag.FromState && tag.Effect == 26;
            if (!AttackProcConditions.Weapon(v, weapon)) continue;
            var applies = old ? type == EnergyProcs.NormalAttack
                    && random.Next(100) < (int)(AttackProcConditions.Var(v, 6) + AttackProcConditions.Var(v, 7) * tag.Level)
                : trigger is StateProcEvent.Kill or StateProcEvent.Dead
                    ? AttackProcConditions.Death(v, tag.Level, weapon, hp, otherHp, levelDifference, mpPercent,
                        random.Next(100), trigger == StateProcEvent.Dead)
                    : AttackProcConditions.Attack(v, tag.Level, weapon, type, element, hp, otherHp, random.Next(100));
            if (!applies) continue;
            var stateId = (int)AttackProcConditions.Var(v, 0);
            var level = (int)(AttackProcConditions.Var(v, 2) + AttackProcConditions.Var(v, 3) * tag.Level);
            var duration = AttackProcConditions.Var(v, 4) + AttackProcConditions.Var(v, 5) * tag.Level;
            if (stateId <= 0 || level <= 0 || duration < 0) continue;
            result.Add(new StateProc(tag.SkillId, stateId, level,
                (uint)Math.Min(uint.MaxValue - 1m, duration * ServerClock.TicksPerSecond),
                old ? 0 : (int)AttackProcConditions.Var(v, 13), self));
        }
        return result;
    }
}
