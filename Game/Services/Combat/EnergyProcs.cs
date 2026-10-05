using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services.Combat;

/// <summary>
/// The energy passives (docs/packet-specs/socle-etats-periodiques-energie.md): <c>EF_ADD_ENERGY_ON_ATTACK</c> (32262)
/// and <c>EF_ADD_ENERGY_ON_BEING_ATTACKED</c> (32263), registered by <c>CalculateStat.cpp</c> as an
/// <c>_ATTACK_TAG</c> carrying a <c>StructEnergyProc</c> and fired by <c>StructCreature::OnAttack</c> on a hit that
/// landed. The tag's fields are the skill's vars: ratio <c>var6 + var7 × level</c>, energy <c>var0 + var1 × level</c>,
/// weapon classes <c>var8..var11</c> (99 = any), HP bounds <c>var14..var17</c>, attack type mask <c>var12</c> and
/// element <c>var18</c> (99 = any).
/// </summary>
public sealed class EnergyProcs
{
    /// <summary><c>StructState</c> attack types (<c>StructMisc.h:313</c>).</summary>
    public const uint NormalAttack = 1 << 0, PhysicalSkill = 1 << 1, MagicalSkill = 1 << 2, Helpful = 1 << 3, Harmful = 1 << 4;

    public const int OnAttackEffect = 32262;
    public const int OnBeingAttackedEffect = 32263;

    private readonly FrozenDictionary<int, (bool Attacking, decimal[] Vars)> _procs;
    private readonly Random _random;

    public EnergyProcs(ISkillResourceRepository repository, Random random = null)
        : this(repository.GetSkillRowsByEffectType(new[] { OnAttackEffect, OnBeingAttackedEffect })
            .Select(r => (r.SkillId, r.EffectType == OnAttackEffect, r.Vars)), random)
    {
    }

    public EnergyProcs(IEnumerable<(int SkillId, bool Attacking, decimal[] Vars)> procs, Random random = null)
    {
        _procs = procs.ToFrozenDictionary(p => p.SkillId, p => (p.Attacking, p.Vars ?? Array.Empty<decimal>()));
        _random = random ?? Random.Shared;
    }

    /// <summary><paramref name="client"/> landed a hit (<c>ProcByAttack(…, true, …)</c>).</summary>
    public void OnAttack(GameClient client, uint attackType, int element, int targetHpPercent) =>
        Run(client, attacking: true, attackType, element, targetHpPercent);

    /// <summary><paramref name="client"/> was hit (<c>pTarget->ProcByAttack(…, false, …)</c>).</summary>
    public void OnBeingAttacked(GameClient client, uint attackType, int element, int attackerHpPercent) =>
        Run(client, attacking: false, attackType, element, attackerHpPercent);

    private void Run(GameClient client, bool attacking, uint attackType, int element, int otherHpPercent)
    {
        var info = client.ConnectionInfo;
        if (info.CharacterHandle == 0 || _procs.Count == 0)
        {
            return;
        }

        var added = 0;
        foreach (var (skillId, level) in info.LearnedSkills.ToArray())
        {
            if (!_procs.TryGetValue(skillId, out var proc) || proc.Attacking != attacking || level <= 0)
            {
                continue;
            }

            var hpPercent = AttackProcConditions.Percent(info.CharacterHp, info.CharacterMaxHp);
            if (Applies(proc.Vars, level, info.EquippedWeapon is { } weapon ? (int)weapon : 0, attackType, element,
                    hpPercent, otherHpPercent, _random.Next(100)))
            {
                added += info.Energy.Add((int)Var(proc.Vars, 0) + (int)(Var(proc.Vars, 1) * level),
                    EnergyCounter.Capacity(info), ServerClock.Now);
            }
        }

        if (added > 0)
        {
            EnergyCounter.Publish(client);
        }
    }

    /// <summary><c>CalculateStat.cpp</c>'s weapon gate, then <c>_ATTACK_TAG::CheckProcByAttack</c>.</summary>
    public static bool Applies(decimal[] vars, int level, int weaponClass, uint attackType, int element,
        int hpPercent, int otherHpPercent, int roll)
    {
        return AttackProcConditions.Attack(vars, level, weaponClass, attackType, element,
            hpPercent, otherHpPercent, roll);
    }

    private static decimal Var(decimal[] vars, int index) => vars is { } v && v.Length > index ? v[index] : 0m;
}
