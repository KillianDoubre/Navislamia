using System;
using System.Collections.Generic;

namespace Navislamia.Game.Services.Creatures;

/// <summary>
/// <c>StructCreature::m_Expert</c>, filled by <c>EF_HUNTING_TRAINING</c> (10013, <c>CalculateStat.cpp:828-856</c>): per
/// creature type (<c>CREATURE_TYPE</c>, 0..9), a damage bonus against that type and a share of the damage that type
/// deals which is avoided. <c>ProvideAttackerInfo</c>/<c>ProvideTargetInfo</c> (<c>StructCreature.cpp:6704, 6777</c>)
/// multiply the raw damage by <c>1 + damage[target type]</c> and <c>1 − avoid[attacker type]</c>, before the defence
/// (<c>DamageCalculator::CalculateActualDamage</c>, <c>Damage.cpp:287-288</c>).
/// </summary>
public sealed class CreatureExpertise
{
    /// <summary><c>MAX_CREATURE_TYPE_NUMBER</c>: ETC 0 .. HUMAN 9.</summary>
    public const int TypeCount = 10;

    /// <summary><c>CREATURE_ALL</c>: the bonus goes to every type.</summary>
    public const int AllTypes = 99;

    /// <summary><c>CREATURE_HUMAN</c>: a player's type (<c>StructPlayer::GetCreatureGroup</c>).</summary>
    public const int Human = 9;

    public static readonly CreatureExpertise None = new();

    private readonly float[] _damage = new float[TypeCount];
    private readonly float[] _avoid = new float[TypeCount];

    public bool IsEmpty { get; private set; } = true;

    /// <summary>The three <c>(type, damage base, damage per level, avoid base, avoid per level)</c> groups of each skill.</summary>
    public static CreatureExpertise From(IEnumerable<(decimal[] Vars, int Level)> skills)
    {
        var expertise = new CreatureExpertise();
        foreach (var (vars, level) in skills)
        {
            if (vars is null || level <= 0) continue;
            for (var i = 0; i < 3 && i * 5 + 4 < vars.Length; i++)
            {
                var type = (int)vars[i * 5];
                var damage = (float)(vars[i * 5 + 1] + vars[i * 5 + 2] * level);
                var avoid = (float)(vars[i * 5 + 3] + vars[i * 5 + 4] * level);
                if (damage == 0f && avoid == 0f) continue;
                if (type == AllTypes)
                {
                    for (var t = 0; t < TypeCount; t++) expertise.Add(t, damage, avoid);
                }
                else if (type is >= 0 and < TypeCount)
                {
                    expertise.Add(type, damage, avoid);
                }
            }
        }

        return expertise;
    }

    private void Add(int type, float damage, float avoid)
    {
        _damage[type] += damage;
        _avoid[type] += avoid;
        IsEmpty = false;
    }

    /// <summary><c>expertiseAdvantage.amp</c>: what the raw damage against a creature of this type is multiplied by.</summary>
    public float DamageAgainst(int targetType) => targetType is >= 0 and < TypeCount ? 1f + _damage[targetType] : 1f;

    /// <summary><c>expertisePenalty.amp</c>: what the raw damage a creature of this type deals is multiplied by.</summary>
    public float DamageTakenFrom(int attackerType) =>
        attackerType is >= 0 and < TypeCount ? Math.Max(0f, 1f - _avoid[attackerType]) : 1f;
}
