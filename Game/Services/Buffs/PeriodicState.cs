using System;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services.Buffs;

/// <summary>StateResource.base_effect_id is independent of effect_type. StructMisc.h:109.</summary>
public readonly record struct PeriodicStateRule(int StateId, int BaseEffect, int IntervalSeconds, int Element,
    decimal Amplify, decimal AmplifyPerLevel, int Add, int AddPerLevel, decimal[] Values)
{
    public bool Damage => BaseEffect is >= 1 and <= 4 or 6;
    public bool Supported => Damage || BaseEffect is 11 or 12 or 21 or 22 or 24 or 25;
    public uint Interval => (uint)Math.Clamp((long)IntervalSeconds * 100, 0, int.MaxValue);
    private decimal V(int i) => Values is { } v && i < v.Length ? v[i] : 0m;
    private static int Amount(decimal value) => (int)Math.Clamp(value, 0m, int.MaxValue);

    public (int Hp, int Mp) Amounts(int level, int baseDamage, int maxHp, int maxMp)
    {
        var flat = Amount((BaseEffect is >= 1 and <= 4 or 11 or 12 ? baseDamage * (Amplify + AmplifyPerLevel * level) : 0m)
            + Add + (decimal)AddPerLevel * level);
        return BaseEffect switch
        {
            6 => (Amount((V(0) + V(1) * level) * maxHp), Amount((V(2) + V(3) * level) * maxMp)),
            25 => (Amount((V(0) + V(1) * level) * maxHp), Amount((V(3) + V(4) * level) * maxMp)),
            12 or 22 => (0, flat),
            24 => (flat, flat),
            _ => (flat, 0)
        };
    }

    public int SnapshotDamage(StatBlock caster) => caster is null ? 0
        : (int)(BaseEffect is 1 or 2 or 6 ? caster.AttackPointRight : caster.MagicPoint);
}

/// <summary>One state occurrence, shared by snapshots of ActiveBuff, reset when that state is refreshed.</summary>
public sealed class StatePulse
{
    public StatePulse(uint start, int baseDamage) { LastFire = start; BaseDamage = baseDamage; }
    public uint LastFire { get; set; }
    public uint? LastUpdateTick { get; set; }
    public int MonsterLife { get; init; } = -1;
    public int BaseDamage { get; }
    public long MonsterId { get; init; } = -1;
    public int Total { get; set; }
}
