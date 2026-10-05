using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.Services.Combat;

/// <summary>
/// <c>StructCooldownProc</c>: on its event, the owner's skills (all of them, or one or two named ones) get
/// <c>inc</c> seconds added to the cool time they still have (a negative <c>inc</c> shortens it).
/// </summary>
public readonly record struct CooldownProc(int SkillId, bool AllSkills, int Inc, int SkillId1, int Inc1, int SkillId2,
    int Inc2);

/// <summary>
/// <c>EF_INC_SKILL_COOL_TIME_ON_*</c> (10063-10070), <c>CalculateStat.cpp:1096-1182</c>: the same <c>m_vProcBy*</c>
/// lists as the state procs, with their own var layout (docs/packet-specs/socle-passifs-combat-recharge-bouclier.md).
/// </summary>
public sealed class CooldownProcs
{
    public static readonly int[] SkillEffects = Enumerable.Range(10063, 8).Append(OnSkillOfId).ToArray();

    /// <summary>
    /// <c>EF_INC_SKILL_COOL_TIME_ON_SKILL_OF_ID</c> (<c>CalculateStat.cpp:1184-1203</c>): the same proc, keyed by the
    /// skills <c>var11..13</c> (until a 0) in <c>m_mapProcBySkillId</c>, fired by <c>StructCreature::ProcBySkillId</c>
    /// when one of them lands (<c>OnAttack</c> with its id, <c>StructSkill.cpp:2828</c>).
    /// </summary>
    public const int OnSkillOfId = 32281;

    /// <summary><c>SKILL_GRACE</c>: never touched (<c>StructCreature::AddRemainCoolTime</c>).</summary>
    public const int Grace = 50401;

    /// <summary><c>SKILL_RULER_OF_TIME</c>: left out of an all-skill change while it is being cast.</summary>
    public const int RulerOfTime = 31324;

    private readonly FrozenDictionary<int, (int Effect, decimal[] Values)> _skills;

    public CooldownProcs(ISkillResourceRepository repository) : this(repository.GetSkillRowsByEffectType(SkillEffects)
        .Select(r => (r.SkillId, r.EffectType, r.Vars))) { }

    private readonly FrozenSet<int> _triggerSkills;

    public CooldownProcs(IEnumerable<(int SkillId, int Effect, decimal[] Values)> skills)
    {
        _skills = skills.ToFrozenDictionary(s => s.SkillId, s => (s.Effect, s.Values ?? Array.Empty<decimal>()));
        _triggerSkills = _skills.Values.Where(s => s.Effect == OnSkillOfId).SelectMany(s => TriggerSkills(s.Values))
            .ToFrozenSet();
    }

    /// <summary>The skills whose hit fires a 32281 passive: <c>var11..13</c>, the list ending at the first 0.</summary>
    public static IEnumerable<int> TriggerSkills(decimal[] values)
    {
        for (var i = 11; i < 14; i++)
        {
            var id = (int)AttackProcConditions.Var(values, i);
            if (id == 0) yield break;
            yield return id;
        }
    }

    /// <summary>Whether some 32281 passive listens to <paramref name="skillId"/>: most hits skip the lookup.</summary>
    public bool ListensTo(int skillId) => _triggerSkills.Contains(skillId);

    /// <summary>
    /// <c>ProcBySkillId</c>: the owner's 32281 passives naming <paramref name="castSkillId"/>, each judged by its
    /// <c>_PROC_TAG</c> — chance <c>var9 + var10 × lvl</c>, own HP <c>var14..15</c>, target HP <c>var16..17</c> — then
    /// <c>StructCooldownProc</c> from <c>var0..8</c>, the same as the event procs.
    /// </summary>
    public IReadOnlyList<CooldownProc> ResolveForSkill(IEnumerable<KeyValuePair<int, byte>> learned, int castSkillId,
        int hp, int otherHp, ICombatRandom random)
    {
        var result = new List<CooldownProc>();
        if (!ListensTo(castSkillId)) return result;
        foreach (var (skillId, level) in learned)
        {
            if (level <= 0 || !_skills.TryGetValue(skillId, out var skill) || skill.Effect != OnSkillOfId) continue;
            foreach (var trigger in TriggerSkills(skill.Values))
            {
                // One _PROC_TAG per listed id: a passive naming the cast skill twice would fire twice.
                if (trigger != castSkillId
                    || !AttackProcConditions.Check(Conditions(skill.Values, kill: false), level, hp, otherHp,
                        random.Next(100))) continue;
                result.Add(Build(skillId, skill.Values, level));
            }
        }

        return result;
    }

    private static CooldownProc Build(int skillId, decimal[] v, int level)
    {
        int Amount(int baseIndex) => (int)(AttackProcConditions.Var(v, baseIndex) + AttackProcConditions.Var(v, baseIndex + 1) * level);
        return new CooldownProc(skillId, AttackProcConditions.Var(v, 0) != 0, Amount(1),
            (int)AttackProcConditions.Var(v, 3), Amount(4), (int)AttackProcConditions.Var(v, 6), Amount(7));
    }

    /// <summary>The event each effect listens to: the attack and kill ones on the attacker, the others on the target.</summary>
    public static bool Describe(int effect, out StateProcEvent trigger)
    {
        (trigger, var known) = effect switch
        {
            10063 => (StateProcEvent.Attack, true),
            10064 => (StateProcEvent.BeingAttacked, true),
            10065 => (StateProcEvent.Kill, true),
            10066 => (StateProcEvent.Critical, true),
            10067 => (StateProcEvent.BeingCritical, true),
            10068 => (StateProcEvent.Avoid, true),
            10069 => (StateProcEvent.Block, true),
            10070 => (StateProcEvent.PerfectBlock, true),
            _ => (default(StateProcEvent), false)
        };
        return known;
    }

    /// <summary>
    /// The tag's values moved to the layout <see cref="AttackProcConditions"/> reads: chance <c>var9 + var10 × lvl</c>,
    /// weapons <c>var11</c>/<c>var12</c>, attack type <c>var13</c>, HP bounds <c>var14..17</c>, element <c>var18</c>; for a
    /// kill (<c>_KILL_TAG</c>) the level gap <c>var18</c> and the MP floor <c>var19</c>.
    /// </summary>
    public static decimal[] Conditions(decimal[] v, bool kill)
    {
        var c = new decimal[20];
        c[6] = AttackProcConditions.Var(v, 9);
        c[7] = AttackProcConditions.Var(v, 10);
        c[8] = AttackProcConditions.Var(v, 11);
        c[9] = AttackProcConditions.Var(v, 12);
        c[10] = c[9];
        c[11] = c[9];
        c[12] = AttackProcConditions.Var(v, 13);
        c[14] = AttackProcConditions.Var(v, 14);
        c[15] = AttackProcConditions.Var(v, 15);
        c[16] = kill ? 0 : AttackProcConditions.Var(v, 16);
        c[17] = kill ? 0 : AttackProcConditions.Var(v, 17);
        c[18] = AttackProcConditions.Var(v, 18);
        c[19] = kill ? AttackProcConditions.Var(v, 19) : 0;
        return c;
    }

    public IReadOnlyList<CooldownProc> Resolve(IEnumerable<KeyValuePair<int, byte>> learned, StateProcEvent trigger,
        int weapon, uint type, int element, int hp, int otherHp, int levelDifference, int mpPercent, ICombatRandom random)
    {
        var result = new List<CooldownProc>();
        foreach (var (skillId, level) in learned)
        {
            if (level <= 0 || !_skills.TryGetValue(skillId, out var skill) || !Describe(skill.Effect, out var kind)
                || kind != trigger) continue;
            var kill = trigger == StateProcEvent.Kill;
            var c = Conditions(skill.Values, kill);
            var applies = kill
                ? AttackProcConditions.Death(c, level, weapon, hp, otherHp, levelDifference, mpPercent, random.Next(100),
                    dead: false)
                : AttackProcConditions.Attack(c, level, weapon, type, element, hp, otherHp, random.Next(100));
            if (!applies) continue;
            result.Add(Build(skillId, skill.Values, level));
        }

        return result;
    }

    /// <summary>
    /// <c>SetRemainCoolTime(remain + inc × 100)</c>, floored at ready: the new ready tick, or null when the skill is
    /// ready now. Only a skill still cooling is changed.
    /// </summary>
    public static uint? Shift(uint now, uint readyAt, int incSeconds)
    {
        var remain = unchecked((int)(readyAt - now));
        if (remain <= 0) return readyAt;
        var shifted = (long)remain + (long)incSeconds * ServerClock.TicksPerSecond;
        return shifted <= 0 ? null : unchecked(now + (uint)Math.Min(shifted, int.MaxValue));
    }
}
