using System;

namespace Navislamia.Game.Services.Buffs;

/// <summary>
/// The heal formula, ported verbatim from the reference emulator's <c>HEALING_SKILL_FUNCTOR</c>.
/// </summary>
/// <remarks>
/// <c>heal = magicPoint * (var0 + var1 * lvl) + var2 + var3 * lvl + enhance * var6
///        + targetMaxHp * (var4 + var5 * lvl + var7 * lvl)</c>
/// <para>
/// The emulator's <c>var[i]</c> is our <c>Values[i + 1]</c>. Enhancement is not modelled, so the
/// <c>var6</c> term is always zero. This is the first formula in the project that reads a character stat
/// (<c>magicPoint</c>) to produce a gameplay outcome.
/// </para>
/// </remarks>
public static class HealCurve
{
    public static int Amount(decimal[] vars, int skillLevel, float magicPoint, float targetMaxHp)
    {
        if (vars is null || vars.Length < 8)
        {
            return 0;
        }

        var perMagicPoint = Var(vars, 0) + Var(vars, 1) * skillLevel;
        var flat = Var(vars, 2) + Var(vars, 3) * skillLevel;
        var perMaxHp = Var(vars, 4) + Var(vars, 5) * skillLevel + Var(vars, 7) * skillLevel;

        var heal = magicPoint * perMagicPoint + flat + targetMaxHp * perMaxHp;
        return heal <= 0f ? 0 : (int)heal;
    }

    private static float Var(decimal[] vars, int index)
    {
        return (float)vars[index];
    }
}
