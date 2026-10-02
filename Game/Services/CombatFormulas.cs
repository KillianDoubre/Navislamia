using System;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services;

/// <summary>
/// <c>ATTACK_INFO__FLAG</c>: what became of a hit. The same values travel in the <c>flag</c> of a skill's
/// <c>HIT_DAMAGE_INFO</c> (NGemity <c>Skill.cpp:117-126</c>).
/// </summary>
[Flags]
public enum HitFlags : byte
{
    None = 0,
    PerfectBlock = 1,
    Block = 2,
    Miss = 4,
    Critical = 8
}

public enum DamageKind
{
    Physical,
    Magical
}

/// <summary>The outcome of one hit: the damage dealt (0 on a miss or a perfect block) and its flags.</summary>
public readonly record struct HitResult(int Damage, HitFlags Flags);

/// <summary>The fight-relevant part of a <see cref="StatBlock"/>, for one side of a hit.</summary>
public readonly record struct Combatant(
    int Level,
    float AttackPoint,
    float MagicPoint,
    float Defence,
    float MagicDefence,
    float Accuracy,
    float Avoid,
    float MagicAccuracy,
    float MagicAvoid,
    float Critical,
    float CriticalPower,
    float BlockChance,
    float BlockDefence,
    float PerfectBlock,
    StatBlock Resistances = null)
{
    public static Combatant From(StatBlock stats, int level) => new(
        level,
        stats.AttackPointRight,
        stats.MagicPoint,
        stats.Defence,
        stats.MagicDefence,
        stats.AccuracyRight,
        stats.Avoid,
        stats.MagicAccuracy,
        stats.MagicAvoid,
        stats.Critical,
        stats.CriticalPower,
        stats.BlockChance,
        stats.BlockDefence,
        stats.PerfectBlock,
        stats);
}

/// <summary>The dice a hit rolls; scripted in the tests.</summary>
public interface ICombatRandom
{
    /// <summary>A value in <c>[0, maxExclusive)</c>, the reference's <c>rand() % maxExclusive</c>.</summary>
    int Next(int maxExclusive);
}

public sealed class CombatRandom : ICombatRandom
{
    public static readonly CombatRandom Shared = new();

    public int Next(int maxExclusive) => Random.Shared.Next(maxExclusive);
}

/// <summary>
/// The official damage rules, from <c>DamageCalculator</c> in the Epic 7 server
/// (<c>CaptainHerlockServer.exe</c> 2012-11 with its PDB): <c>CalculateDirectDamageWithDefence</c>
/// (<c>0x14038ab20</c>), <c>SimulateDamageCalculation</c> (<c>0x14038ad40</c>) and
/// <c>SimulateAndCheckBlocking</c> (<c>0x14038a8d0</c>). NGemity's <c>Unit::CalcDamage</c> has the same
/// defence formula. See <c>docs/packet-specs/socle-combat-reel.md</c>.
/// </summary>
public static class CombatFormulas
{
    /// <summary>Resistance points reduce damage by resistance / 300, after critical and before mana shield.
    /// Uses floating point division to fix the integer division bug in ProvideTargetInfo (Epic 7).
    /// Negative resistance increases damage; 300 or more absorbs the hit completely.</summary>
    public static int ResistedDamage(int damage, float resistance)
    {
        if (damage <= 0) return 0;
        return (int)Math.Min(int.MaxValue, Math.Max(0d, Math.Floor(damage * (1d - resistance / 300d))));
    }

    /// <summary>The floor the attack interval divides by, so a crushing slow cannot divide by zero.</summary>
    public const float MinimumAttackSpeed = 10f;

    /// <summary>
    /// The time between two swings, in ar_time ticks (10 ms): <c>100 / attackSpeed × 115</c>
    /// (<c>Unit::GetAttackInterval</c>). The base speed of 100 swings every 1.15 s.
    /// </summary>
    public static uint AttackIntervalTicks(float attackSpeed) =>
        (uint)(100f / Math.Max(attackSpeed, MinimumAttackSpeed) * 115f);

    /// <summary>
    /// The chance out of 100 that a hit lands: <c>7 + max(10, 88 + 2 × (attacker − target level)) ×
    /// accuracy / avoid + bonus</c>, and a sure hit against a target with no avoid.
    /// </summary>
    public static int HitChance(int attackerLevel, int targetLevel, float accuracy, float avoid, int bonus)
    {
        if (avoid <= 0f)
        {
            return 100;
        }

        var levelFactor = Math.Max(10, 88 + 2 * (attackerLevel - targetLevel));
        return (int)(7f + levelFactor * (accuracy / avoid) + bonus);
    }

    /// <summary>
    /// The damage left after defence: <c>level × 1.7 × max(1 − 0.4 × def/atk, 0.3) +
    /// atk × max(1 − 0.5 × def/atk, 0.05)</c>, at least 1. The attack is floored at 1 so the ratio
    /// stays finite.
    /// </summary>
    public static int DefendedDamage(int attackerLevel, float damage, float defence)
    {
        var attack = Math.Max(damage, 1f);
        var ratio = defence / attack;
        var result = attackerLevel * 1.7f * Math.Max(1f - 0.4f * ratio, 0.3f)
                     + attack * Math.Max(1f - 0.5f * ratio, 0.05f);
        return Math.Max(1, (int)result);
    }

    /// <summary>
    /// One hit, in the official order: the hit roll, the block roll (physical only, and only for a target
    /// that can block), the critical roll, then the defence and the ±5 % spread.
    /// </summary>
    public static HitResult Resolve(in Combatant attacker, in Combatant target, float baseDamage,
        DamageKind kind, int accuracyBonus, int criticalBonus, ICombatRandom random, int element = 0)
    {
        var physical = kind == DamageKind.Physical;

        var (accuracy, avoid) = physical
            ? (attacker.Accuracy, target.Avoid)
            : (attacker.MagicAccuracy, target.MagicAvoid);
        if (avoid > 0f)
        {
            var chance = HitChance(attacker.Level, target.Level, accuracy, avoid, accuracyBonus);
            if (random.Next(100) > chance)
            {
                return new HitResult(0, HitFlags.Miss);
            }
        }

        var flags = HitFlags.None;
        float defence;
        if (physical)
        {
            defence = target.Defence;
            if (target.BlockChance > 0f && random.Next(100) < target.BlockChance)
            {
                if (random.Next(100) < target.PerfectBlock)
                {
                    return new HitResult(0, HitFlags.PerfectBlock);
                }

                flags |= HitFlags.Block;
                defence += target.BlockDefence;
            }
        }
        else
        {
            defence = target.MagicDefence;
        }

        // The critical roll comes before the spread (SimulateDamageCalculation draws them in that order);
        // both only multiply the defended damage, so the order matters for the dice, not the arithmetic.
        if (random.Next(100) <= (int)(attacker.Critical + criticalBonus))
        {
            flags |= HitFlags.Critical;
        }

        float damage = DefendedDamage(attacker.Level, baseDamage, defence);
        damage *= 1f + (random.Next(10001) - 5000) / 100000f;
        if ((flags & HitFlags.Critical) != 0)
        {
            damage += damage * (attacker.CriticalPower / 100f);
        }

        return new HitResult(ResistedDamage(Math.Max(1, (int)damage), target.Resistances?.GetResistance(element) ?? 0f), flags);
    }
}
