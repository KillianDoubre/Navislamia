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
    public static readonly int[] SkillEffects = Enumerable.Range(10063, 8).ToArray();

    /// <summary><c>SKILL_GRACE</c>: never touched (<c>StructCreature::AddRemainCoolTime</c>).</summary>
    public const int Grace = 50401;

    /// <summary><c>SKILL_RULER_OF_TIME</c>: left out of an all-skill change while it is being cast.</summary>
    public const int RulerOfTime = 31324;

    private readonly FrozenDictionary<int, (int Effect, decimal[] Values)> _skills;

    public CooldownProcs(ISkillResourceRepository repository) : this(repository.GetSkillRowsByEffectType(SkillEffects)
        .Select(r => (r.SkillId, r.EffectType, r.Vars))) { }

    public CooldownProcs(IEnumerable<(int SkillId, int Effect, decimal[] Values)> skills) =>
        _skills = skills.ToFrozenDictionary(s => s.SkillId, s => (s.Effect, s.Values ?? Array.Empty<decimal>()));

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
            var v = skill.Values;
            int Amount(int baseIndex) => (int)(AttackProcConditions.Var(v, baseIndex) + AttackProcConditions.Var(v, baseIndex + 1) * level);
            result.Add(new CooldownProc(skillId, AttackProcConditions.Var(v, 0) != 0, Amount(1),
                (int)AttackProcConditions.Var(v, 3), Amount(4), (int)AttackProcConditions.Var(v, 6), Amount(7)));
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
