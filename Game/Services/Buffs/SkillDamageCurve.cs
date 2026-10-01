using System;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.Services.Buffs;

/// <summary>
/// The base damage and the bonuses of a single-target offensive skill, from NGemity
/// <c>Skill::PHYSICAL_SINGLE_DAMAGE</c> and <c>Skill::SINGLE_MAGICAL_DAMAGE</c> (<c>Skill.cpp:1652-1726</c>).
/// The result then goes through the same <see cref="CombatFormulas.Resolve"/> as a swing.
/// </summary>
/// <remarks>
/// <c>var[i]</c> is our <c>Values[i]</c>, as in <see cref="HealCurve"/>. Enhancement is not modelled, so
/// the <c>var10</c>/<c>var11</c> (physical) and <c>var2</c>/<c>var5</c> (magical) enhance terms are zero.
/// </remarks>
public static class SkillDamageCurve
{
    /// <summary>
    /// Physical (30001): <c>attack × (var0 + var1 × lvl) + var2 + var3 × lvl</c>.
    /// Magical (231): <c>magicPoint × (var0 + var1 × lvl) + var3 + var4 × lvl</c>.
    /// </summary>
    public static float BaseDamage(SkillCastKind kind, decimal[] vars, int skillLevel, float attackPoint,
        float magicPoint)
    {
        if (vars is null || vars.Length < 5)
        {
            return 0f;
        }

        var damage = kind == SkillCastKind.MagicAttack
            ? magicPoint * (Var(vars, 0) + Var(vars, 1) * skillLevel) + Var(vars, 3) + Var(vars, 4) * skillLevel
            : attackPoint * (Var(vars, 0) + Var(vars, 1) * skillLevel) + Var(vars, 2) + Var(vars, 3) * skillLevel;
        return Math.Max(0f, (int)damage);
    }

    /// <summary><c>hit_bonus + (caster level − target level) × percentage</c>.</summary>
    public static int HitBonus(CastableBuffFields fields, int casterLevel, int targetLevel) =>
        fields.HitBonus + (casterLevel - targetLevel) * fields.Percentage;

    /// <summary><c>critical_bonus + critical_bonus_per_skl × lvl</c>.</summary>
    public static int CriticalBonus(CastableBuffFields fields, int skillLevel) =>
        fields.CriticalBonus + fields.CriticalBonusPerSkl * skillLevel;

    private static float Var(decimal[] vars, int index) => (float)vars[index];
}
