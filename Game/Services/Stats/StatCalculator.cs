using System;
using System.Collections.Generic;

namespace Navislamia.Game.Services.Stats;

public readonly record struct StatCalculatorInput(
    int Job,
    IReadOnlyList<(int Job, int JobLevel)> JobHistory,
    int Level,
    IReadOnlyList<StatEffect> ItemEffects,
    IReadOnlyList<StatEffect> PassiveEffects = null,
    IReadOnlyList<StatEffect> BuffEffects = null);

public readonly record struct CharacterStatResult(StatBlock Total, StatBlock ByItem);

public class StatCalculator
{
    private const int JobLevelRanges = 3;
    private const float DefaultMaxStamina = 500000f;
    private const float DefaultMaxChaos = 500f;

    private readonly IStatCatalog _catalog;

    public StatCalculator(IStatCatalog catalog)
    {
        _catalog = catalog;
    }

    public CharacterStatResult Compute(StatCalculatorInput input)
    {
        var total = new StatBlock();
        ApplyBaseStats(input.Job, total);
        ApplyJobLevelBonus(input.JobHistory, total);

        var level = Math.Max(1, input.Level);
        SeedFromLevel(level, total);

        ApplyEffects(total, input.ItemEffects, input.PassiveEffects, input.BuffEffects);

        var byItem = new StatBlock();
        ApplyEffects(byItem, input.ItemEffects, null);

        ApplyDerivedBonuses(total);

        return new CharacterStatResult(total, byItem);
    }

    private void ApplyBaseStats(int job, StatBlock block)
    {
        if (!_catalog.TryGetBaseStats(job, out var stats))
        {
            return;
        }

        block.StatId = stats.StatId;
        block.Strength = stats.Strength;
        block.Vitality = stats.Vitality;
        block.Dexterity = stats.Dexterity;
        block.Agility = stats.Agility;
        block.Intelligence = stats.Intelligence;
        block.Wisdom = stats.Wisdom;
        block.Luck = stats.Luck;
    }

    private void ApplyJobLevelBonus(IReadOnlyList<(int Job, int JobLevel)> history, StatBlock block)
    {
        if (history is null)
        {
            return;
        }

        Span<int> parts = stackalloc int[JobLevelRanges];
        foreach (var (job, jobLevel) in history)
        {
            if (!_catalog.TryGetJobBonus(job, out var bonus))
            {
                continue;
            }

            JobLevelBonusCurve.FillLevelParts(jobLevel, parts);
            foreach (var entry in bonus.Entries)
            {
                block.Add(entry.Target, JobLevelBonusCurve.SumProduct(entry.PerLevel, parts) + entry.Default);
            }
        }
    }

    private static void SeedFromLevel(int level, StatBlock block)
    {
        block.AttackPointRight = level;
        block.AccuracyRight = level;
        block.AccuracyLeft = level;
        block.MagicPoint = level;
        block.Defence = level;
        block.Avoid = level;
        block.MagicAccuracy = level;
        block.MagicDefence = level;
        block.MagicAvoid = level;

        block.AttackSpeed = 100;
        block.MoveSpeed = 120;
        block.CastingSpeed = 100;
        block.CoolTimeSpeed = 100;
        block.Critical = 3;
        block.CriticalPower = 80;
        block.PerfectBlock = 20;
        block.AttackRange = 50;

        block.HpRegenPercentage = 5;
        block.MpRegenPercentage = 5;
        block.HpRegenPoint = 48 + 2 * level;
        block.MpRegenPoint = 48 + 2 * level;

        block.MaxWeight = 10 * level;
        block.MaxHp = 20 * level;
        block.MaxMp = 20 * level;
        block.MaxStamina = DefaultMaxStamina;
        block.MaxChaos = DefaultMaxChaos;
    }

    private static void ApplyEffects(StatBlock block, params IReadOnlyList<StatEffect>[] sources)
    {
        foreach (var effects in sources)
        {
            if (effects is null)
            {
                continue;
            }

            foreach (var effect in effects)
            {
                if (!effect.IsPercent)
                {
                    block.Add(effect.Target, effect.Value);
                }
            }
        }

        foreach (var effects in sources)
        {
            if (effects is null)
            {
                continue;
            }

            foreach (var effect in effects)
            {
                if (effect.IsPercent)
                {
                    block.Amplify(effect.Target, effect.Value);
                }
            }
        }
    }

    private static void ApplyDerivedBonuses(StatBlock block)
    {
        block.AttackPointRight += 2.8f * block.Strength;
        block.AccuracyRight += 0.5f * block.Dexterity;
        block.AccuracyLeft = block.AccuracyRight;
        block.MagicPoint += 2f * block.Intelligence;
        block.Defence += 1.6f * block.Vitality;
        block.Avoid += 0.5f * block.Agility;
        block.AttackSpeed += 0.1f * block.Agility;
        block.MagicAccuracy += 0.4f * block.Wisdom;
        block.MagicDefence += 2f * block.Wisdom;
        block.MagicAvoid += 0.5f * block.Wisdom;
        block.Critical += block.Luck / 5f;
        block.MpRegenPoint += 4.1f * block.Wisdom;
        block.MaxWeight += 10f * block.Strength;
        block.ItemChance += block.Luck / 5f;
        block.MaxHp += 33f * block.Vitality;
        block.MaxMp += 30f * block.Intelligence;
    }
}
