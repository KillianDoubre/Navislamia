using System;

namespace Navislamia.Game.Services.Combat;

/// <summary>StructProc.cpp:9-38 and the weapon gate in CalculateStat.cpp:926-928.</summary>
public static class AttackProcConditions
{
    // StructCreature.h:651,654: a nonzero vital always reports at least one percent.
    public static int Percent(int current, float maximum) => current <= 0 ? 0
        : maximum > 0 ? Math.Max(1, (int)(current / maximum * 100f)) : 100;

    public static decimal Var(decimal[] values, int index) => values is { } v && index < v.Length ? v[index] : 0m;

    public static bool Weapon(decimal[] v, int weapon) => (int)Var(v, 8) == 99
        || (int)Var(v, 8) == weapon || (int)Var(v, 9) == weapon
        || (int)Var(v, 10) == weapon || (int)Var(v, 11) == weapon;

    public static bool Check(decimal[] v, int level, int hp, int otherHp, int roll,
        bool kill = false, bool dead = false)
    {
        // The C++ tag stores an int, truncating the fixed point ratio before rolling.
        if (roll >= (int)(Var(v, 6) + Var(v, 7) * level)) return false;
        var min = dead ? 0 : (int)Var(v, 14); var max = dead ? 0 : (int)Var(v, 15);
        if (min != 0 && hp < min || max != 0 && hp > max) return false;
        // _KILL_TAG's constructor deliberately passes 0, 0 to _PROC_TAG for target HP.
        if (kill || dead) return true;
        var otherMin = (int)Var(v, 16); var otherMax = (int)Var(v, 17);
        return !(otherMin != 0 && (otherHp == -1 || otherHp < otherMin)
            || otherMax != 0 && (otherHp == -1 || otherHp >= otherMax));
    }

    public static bool Attack(decimal[] v, int level, int weapon, uint type, int element,
        int hp, int otherHp, int roll) => Weapon(v, weapon) && Check(v, level, hp, otherHp, roll)
        && ((uint)Var(v, 12) & type) == type && ((int)Var(v, 18) == 99 || (int)Var(v, 18) == element);

    public static bool Death(decimal[] v, int level, int weapon, int hp, int otherHp,
        int levelDifference, int mpPercent, int roll, bool dead) => Weapon(v, weapon)
        && Check(v, level, hp, otherHp, roll, kill: !dead, dead: dead)
        && ((int)Var(v, 18) == -1 || levelDifference <= (int)Var(v, 18))
        && ((int)Var(v, 19) == 0 || mpPercent >= (int)Var(v, 19));
}
