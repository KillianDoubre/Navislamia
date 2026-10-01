using System;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services;

/// <summary>
/// A monster resource's combat stats, built the way the official server builds them: the
/// <c>StatResource</c> row named by <c>stat_id</c> (<c>StructMonster::GetBaseStat</c>, <c>0x1400b1660</c>),
/// the level seed and the derived bonuses every creature gets (<c>StructCreature::calcAttribute</c>,
/// <c>0x140046fc0</c>, the coefficients of <see cref="StatCalculator"/>), then the resource's own columns
/// added on top (<c>StructMonster::onApplyAttributeAdjustment</c>, <c>0x140048f00</c>).
/// </summary>
/// <remarks>
/// Shared by every instance of the resource and immutable: <see cref="Compute"/> builds a fresh block, so
/// a monster's active states (a debuff) can be folded in per hit without touching the shared value.
/// </remarks>
public sealed class MonsterCombatStats
{
    private readonly StatBaseStats? _baseStats;
    private readonly int _hp;
    private readonly int _mp;
    private readonly int _attackPoint;
    private readonly int _magicPoint;
    private readonly int _defence;
    private readonly int _magicDefence;
    private readonly int _attackSpeed;
    private readonly int _magicSpeed;
    private readonly int _accuracy;
    private readonly int _avoid;
    private readonly int _magicAccuracy;
    private readonly int _magicAvoid;
    private readonly StatBlock _plain;

    private MonsterCombatStats(MonsterResourceEntity resource, StatBaseStats? baseStats)
    {
        Level = Math.Max(1, resource.Level);
        _baseStats = baseStats;
        _hp = resource.Hp;
        _mp = resource.Mp;
        _attackPoint = resource.AttackPoint;
        _magicPoint = resource.MagicPoint;
        _defence = resource.Defence;
        _magicDefence = resource.MagicDefence;
        _attackSpeed = resource.AttackSpeed;
        _magicSpeed = resource.MagicSpeed;
        _accuracy = resource.Accuracy;
        _avoid = resource.Avoid;
        _magicAccuracy = resource.MagicAccuracy;
        _magicAvoid = resource.MagicAvoid;

        _plain = Compute(null);
        MaxHp = Math.Max(1, (int)_plain.MaxHp);
        MaxMp = Math.Max(0, (int)_plain.MaxMp);
    }

    public int Level { get; }

    /// <summary><c>hp + 20 × level + 33 × vitality</c>: the column alone is not the monster's life.</summary>
    public int MaxHp { get; }

    public int MaxMp { get; }

    /// <summary>The stats without any state, shared: read it, never change it.</summary>
    public StatBlock Plain => _plain;

    public static MonsterCombatStats From(MonsterResourceEntity resource, StatBaseStats? baseStats) =>
        new(resource, baseStats);

    /// <summary>
    /// The stats with <paramref name="stateEffects"/> applied after the resource columns, so a state that
    /// lowers defence lowers the whole of it, column included.
    /// </summary>
    public StatBlock Compute(IReadOnlyList<StatEffect> stateEffects)
    {
        var block = new StatBlock();
        if (_baseStats is { } stats)
        {
            StatCalculator.ApplyBaseStats(stats, block);
        }

        StatCalculator.SeedFromLevel(Level, block);
        StatCalculator.ApplyDerivedBonuses(block);

        block.MaxHp += _hp;
        block.MaxMp += _mp;
        block.AttackPointRight += _attackPoint;
        block.MagicPoint += _magicPoint;
        block.Defence += _defence;
        block.MagicDefence += _magicDefence;
        block.AttackSpeed += _attackSpeed;
        block.CastingSpeed += _magicSpeed;
        block.AccuracyRight += _accuracy;
        block.AccuracyLeft = block.AccuracyRight;
        block.Avoid += _avoid;
        block.MagicAccuracy += _magicAccuracy;
        block.MagicAvoid += _magicAvoid;

        if (stateEffects is { Count: > 0 })
        {
            StatCalculator.ApplyEffects(block, stateEffects);
        }

        return block;
    }
}
