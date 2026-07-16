using System;
using System.Collections.Generic;

namespace Navislamia.Game.Services.Stats;

public static class ParameterBitset
{
    private static readonly StatTarget[] Targets =
    {
        StatTarget.Strength,
        StatTarget.Vitality,
        StatTarget.Agility,
        StatTarget.Dexterity,
        StatTarget.Intelligence,
        StatTarget.Wisdom,
        StatTarget.Luck,
        StatTarget.AttackPointRight,
        StatTarget.MagicPoint,
        StatTarget.Defence,
        StatTarget.MagicDefence,
        StatTarget.AttackSpeed,
        StatTarget.CastingSpeed,
        StatTarget.MoveSpeed,
        StatTarget.AccuracyRight,
        StatTarget.MagicAccuracy,
        StatTarget.Critical,
        StatTarget.BlockChance,
        StatTarget.BlockDefence,
        StatTarget.Avoid,
        StatTarget.MagicAvoid,
        StatTarget.MaxHp,
        StatTarget.MaxMp,
        StatTarget.MaxStamina,
        StatTarget.HpRegenPoint,
        StatTarget.MpRegenPoint,
        StatTarget.None,
        StatTarget.HpRegenPercentage,
        StatTarget.MpRegenPercentage,
        StatTarget.MaxWeight,
        StatTarget.None,
        StatTarget.None
    };

    public static StatTarget Resolve(int bit)
    {
        return bit >= 0 && bit < Targets.Length ? Targets[bit] : StatTarget.None;
    }

    public static IReadOnlyList<StatTarget> Decode(uint mask)
    {
        if (mask == 0)
        {
            return Array.Empty<StatTarget>();
        }

        List<StatTarget> targets = null;
        for (var bit = 0; bit < Targets.Length; bit++)
        {
            if ((mask & (1u << bit)) == 0 || Targets[bit] == StatTarget.None)
            {
                continue;
            }

            targets ??= new List<StatTarget>();
            targets.Add(Targets[bit]);
        }

        return (IReadOnlyList<StatTarget>)targets ?? Array.Empty<StatTarget>();
    }
}
