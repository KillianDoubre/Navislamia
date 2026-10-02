using System;
using System.Collections.Generic;
using Navislamia.Game.Services.Buffs;

namespace Navislamia.Game.Services.MonsterSkills;

/// <summary>The pure half of monster skills: which one is cast and how hard it hits.</summary>
public static class MonsterSkillRules
{
    /// <summary>
    /// The official pick (<c>StructMonster::AI_processAttack</c>, <c>0x140166a40</c>-<c>0x140166b40</c>): when
    /// the monster may attack, each entry in order draws a value in 0..9999 and is cast when
    /// <c>probability × 10000</c> exceeds it; a cast refused by the readiness/range gate moves on to the next
    /// entry, and the first cast replaces the swing. Null when nothing is cast.
    /// </summary>
    public static MonsterSkill Pick(IReadOnlyList<MonsterSkill> skills, Func<MonsterSkill, bool> ready,
        ICombatRandom random)
    {
        foreach (var skill in skills)
        {
            if (skill.Effect == MonsterSkillEffect.Unsupported || skill.Probability <= 0) continue;
            var roll = random.Next(10000);
            if (skill.Probability * 10000 > roll && ready(skill))
            {
                return skill;
            }
        }

        return null;
    }

    /// <summary>
    /// The base damage of a damage skill. The two <c>_T1</c> families add to the stat (NGemity
    /// <c>SINGLE_PHYSICAL_DAMAGE_T1</c>, <c>SINGLE_MAGICAL_DAMAGE_T1</c>); the other two are the player
    /// curves of <see cref="SkillDamageCurve"/>. Enhancement terms are zero.
    /// </summary>
    public static float BaseDamage(MonsterSkill skill, float attackPoint, float magicPoint)
    {
        var vars = skill.Fields.Vars;
        var level = skill.Level;
        return skill.Effect switch
        {
            MonsterSkillEffect.PhysicalFlat => Flat(attackPoint, vars, level),
            MonsterSkillEffect.MagicFlat => Flat(magicPoint, vars, level),
            MonsterSkillEffect.PhysicalScaled or MonsterSkillEffect.MagicScaled =>
                SkillAreaRules.Damage(skill.Fields, level, attackPoint, magicPoint),
            _ => 0f
        };
    }

    private static float Flat(float stat, decimal[] vars, int level)
    {
        if (vars is null || vars.Length < 2)
        {
            return Math.Max(0f, (int)stat);
        }

        return Math.Max(0f, (int)(stat + (float)vars[0] + (float)vars[1] * level));
    }
}
