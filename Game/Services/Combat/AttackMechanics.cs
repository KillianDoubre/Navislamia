using System;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Services.Casting;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services.Combat;

/// <summary>An active state as the attack reads it: its rule (effect type and values) and its level.</summary>
public readonly record struct ActiveStateRule(StateRule Rule, int Level)
{
    public decimal Value(int index) =>
        Rule.Values is not null && index < Rule.Values.Length ? Rule.Values[index] : 0m;

    /// <summary><c>value_a + level × value_b</c>, the shape every combat state uses.</summary>
    public float Scaled(int a, int b) => (float)(Value(a) + Level * Value(b));
}

/// <summary>Extra damage a state adds to a hit: a flat amount or a share of the hit, of one element.</summary>
public readonly record struct AdditionalDamage(int Ratio, int Element, float Flat, float Percent);

/// <summary>A state sending part of the damage back to the attacker.</summary>
public readonly record struct DamageReflect(int Ratio, int Element, float Flat, float PhysicalRatio, float MagicalRatio);

/// <summary>
/// The official combat mechanics of a hit, pure (docs/packet-specs/socle-mecaniques-combat.md): double attack,
/// dual wield, bow, additional damage, mana shield and reflection, from <c>StructCreature::Attack</c>
/// (2012-11 <c>0x1400a18d0</c>) and the state effects NGemity ports from the same table
/// (<c>Unit::applyStateEffect</c>, <c>Unit::Attack</c>, <c>Unit::DealDamage</c>).
/// </summary>
public static class AttackMechanics
{
    public const int DoubleAttackEffect = 21;
    public const int AdditionalDamageOnAttack = 22;
    public const int AmpAdditionalDamageOnAttack = 23;
    public const int DamageReflectPercent = 43;
    public const int DamageReflect = 44;
    public const int ManaShield = 49;

    /// <summary><c>CLASS_EVERY_WEAPON</c>: the state applies whatever the weapon.</summary>
    public const int EveryWeapon = 99;

    /// <summary>The number of elements of <c>ATTACK_INFO.elemental_damage</c>.</summary>
    public const int Elements = 7;

    /// <summary><c>ATTACK_EVENT__ATTACK_FLAG</c>.</summary>
    public const byte FlagUsingBow = 1, FlagUsingCrossBow = 2, FlagDoubleWeapon = 4, FlagDoubleAttack = 8;

    /// <summary><c>ATTACK_EVENT__ATTACK_ACTION</c> of the aiming half of a bow shot.</summary>
    public const byte ActionAiming = 2;

    /// <summary>The share of the attack interval a bow spends aiming; the shot takes the rest.</summary>
    public const float AimShare = 0.8f;

    public static bool IsBow(ItemType? weapon) => weapon is ItemType.HeavyBow or ItemType.LightBow;

    public static bool IsCrossbow(ItemType? weapon) => weapon == ItemType.Crossbow;

    public static bool IsRanged(ItemType? weapon) => IsBow(weapon) || IsCrossbow(weapon);

    /// <summary>
    /// The arrow reserve a bow or crossbow shoots, <c>GetBulletCount()</c> of <c>onAttackRequest</c>: the shield
    /// slot's left hand, and only when it holds no weapon of its own (<see cref="LeftHandItem.WeaponType"/> unset
    /// for the <c>Bullet</c> group).
    /// </summary>
    public static bool HasArrows(LeftHandItem left) => left is { WeaponType: null } arrows && arrows.Amount >= 1;

    /// <summary>
    /// Two weapons are in use when the shield slot holds a weapon and the main hand is not a bow (the shield slot
    /// of an archer holds the arrows).
    /// </summary>
    public static bool IsDualWield(ItemType? right, ItemType? left) => left is not null && right is not null && !IsRanged(right);

    /// <summary>The weapon filter of a state: <c>value_8..value_11</c>, 99 meaning any weapon, no weapon none.</summary>
    public static bool MatchesWeapon(ActiveStateRule state, ItemType? weapon)
    {
        if (weapon is null)
        {
            return false;
        }

        if ((int)state.Value(8) == EveryWeapon)
        {
            return true;
        }

        for (var i = 8; i <= 11; i++)
        {
            if ((int)state.Value(i) == (int)weapon.Value)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>SEF_DOUBLE_ATTACK</c>: the chance out of 100 of doubling the hits, <c>value_0 + level × value_1</c>.</summary>
    public static float DoubleAttackRatio(IEnumerable<ActiveStateRule> states, ItemType? weapon)
    {
        var ratio = 0f;
        foreach (var state in states)
        {
            if (state.Rule.EffectType == DoubleAttackEffect && MatchesWeapon(state, weapon))
            {
                ratio += state.Scaled(0, 1);
            }
        }

        return ratio;
    }

    /// <summary>
    /// The number of hits of a swing: 2 with two weapons, doubled again when the double attack roll
    /// (<c>irand(1, 100) &lt; ratio</c>) succeeds.
    /// </summary>
    public static int HitCount(bool dualWield, bool doubleAttack) => (dualWield ? 2 : 1) * (doubleAttack ? 2 : 1);

    /// <summary>
    /// <c>SEF_ADDITIONAL_DAMAGE_ON_ATTACK</c> (flat) and <c>SEF_AMP_ADDITIONAL_DAMAGE_ON_ATTACK</c> (share of the hit):
    /// <c>value_11</c> 0 for melee, 1 for ranged, 99 for both; chance <c>value_6 + level × value_7</c>, element
    /// <c>value_8</c>, amount <c>value_0 + level × value_1</c>.
    /// </summary>
    public static IReadOnlyList<AdditionalDamage> AdditionalDamages(IEnumerable<ActiveStateRule> states, bool ranged)
    {
        List<AdditionalDamage> result = null;
        foreach (var state in states)
        {
            var effect = state.Rule.EffectType;
            if (effect is not (AdditionalDamageOnAttack or AmpAdditionalDamageOnAttack))
            {
                continue;
            }

            var applies = (int)state.Value(11);
            if (!(applies == 99 || applies == (ranged ? 1 : 0)))
            {
                continue;
            }

            var amount = state.Scaled(0, 1);
            var element = Math.Clamp((int)state.Value(8), 0, Elements - 1);
            (result ??= new List<AdditionalDamage>()).Add(new AdditionalDamage((int)state.Scaled(6, 7), element,
                effect == AdditionalDamageOnAttack ? amount : 0f,
                effect == AmpAdditionalDamageOnAttack ? amount : 0f));
        }

        return (IReadOnlyList<AdditionalDamage>)result ?? Array.Empty<AdditionalDamage>();
    }

    /// <summary>What an additional damage adds to a hit of <paramref name="hitDamage"/>, when its roll succeeds.</summary>
    public static int AdditionalAmount(AdditionalDamage extra, int hitDamage) =>
        extra.Flat != 0f ? (int)extra.Flat : (int)(extra.Percent * hitDamage);

    /// <summary>
    /// <c>SEF_MANA_SHIELD</c>: the share of a hit the MP absorb, <c>value_0 + level × value_1</c>, for physical hits
    /// (<c>value_4</c> 1 or 99) or magical ones (2 or 99), clamped to [0, 1].
    /// </summary>
    public static float ManaShieldRatio(IEnumerable<ActiveStateRule> states, bool magical)
    {
        var ratio = 0f;
        foreach (var state in states)
        {
            if (state.Rule.EffectType != ManaShield)
            {
                continue;
            }

            var target = (int)state.Value(4);
            if (target == 99 || target == (magical ? 2 : 1))
            {
                ratio += state.Scaled(0, 1);
            }
        }

        return Math.Clamp(ratio, 0f, 1f);
    }

    /// <summary>The MP a mana shield takes for a hit, never more than the MP left (<c>Unit::DealDamage</c>).</summary>
    public static int ManaShieldAbsorb(int damage, float ratio, int mp) => Math.Max(0, Math.Min((int)(damage * ratio), mp));

    /// <summary>
    /// <c>SEF_DAMAGE_REFLECT_PERCENT</c> (shares of the physical and magical damage, <c>value_0/1</c> and <c>value_2/3</c>)
    /// and <c>SEF_DAMAGE_REFLECT</c> (a flat <c>value_0 + level × value_1</c>); chance <c>value_6 + level × value_7</c>,
    /// element <c>value_8</c>.
    /// </summary>
    public static IReadOnlyList<DamageReflect> Reflects(IEnumerable<ActiveStateRule> states)
    {
        List<DamageReflect> result = null;
        foreach (var state in states)
        {
            var element = Math.Clamp((int)state.Value(8), 0, Elements - 1);
            if (state.Rule.EffectType == DamageReflectPercent)
            {
                (result ??= new List<DamageReflect>()).Add(new DamageReflect((int)state.Scaled(6, 7), element, 0f,
                    state.Scaled(0, 1), state.Scaled(2, 3)));
            }
            else if (state.Rule.EffectType == DamageReflect)
            {
                (result ??= new List<DamageReflect>()).Add(new DamageReflect((int)state.Scaled(6, 7), element,
                    state.Scaled(0, 1), 0f, 0f));
            }
        }

        return (IReadOnlyList<DamageReflect>)result ?? Array.Empty<DamageReflect>();
    }

    /// <summary>What a reflection sends back for a hit of <paramref name="damage"/>.</summary>
    public static int ReflectAmount(DamageReflect reflect, int damage, bool magical) =>
        reflect.Flat != 0f ? (int)reflect.Flat : (int)(damage * (magical ? reflect.MagicalRatio : reflect.PhysicalRatio));

    /// <summary>The flat attack and accuracy a weapon's own effects give.</summary>
    public static (float Attack, float Accuracy) WeaponContribution(IReadOnlyList<StatEffect> effects)
    {
        float attack = 0f, accuracy = 0f;
        if (effects is null)
        {
            return (0f, 0f);
        }

        foreach (var effect in effects)
        {
            if (effect.IsPercent)
            {
                continue;
            }

            if (effect.Target == StatTarget.AttackPointRight)
            {
                attack += effect.Value;
            }
            else if (effect.Target == StatTarget.AccuracyRight)
            {
                accuracy += effect.Value;
            }
        }

        return (attack, accuracy);
    }

    /// <summary>
    /// The left hand of a dual wielder: everything the right hand has (stats, armour, passives) but its own weapon
    /// in place of the right one (<c>Unit::calcAttribute</c> and <c>applyItemEffect</c>: a weapon worn in the shield
    /// slot counts for the left hand only, everything else for both).
    /// </summary>
    public static (float Attack, float Accuracy) LeftHand(StatBlock total, IReadOnlyList<StatEffect> rightWeapon,
        IReadOnlyList<StatEffect> leftWeapon)
    {
        var right = WeaponContribution(rightWeapon);
        var left = WeaponContribution(leftWeapon);
        return (Math.Max(0f, total.AttackPointRight - right.Attack + left.Attack),
            Math.Max(0f, total.AccuracyRight - right.Accuracy + left.Accuracy));
    }

    /// <summary>The <c>attack_flag</c> of a swing, last one wins as in <c>broadcastAttackMessage</c>.</summary>
    public static byte AttackFlag(bool doubleAttack, bool dualWield, ItemType? weapon)
    {
        byte flag = 0;
        if (doubleAttack) flag = FlagDoubleAttack;
        if (dualWield) flag = FlagDoubleWeapon;
        if (IsBow(weapon)) flag = FlagUsingBow;
        if (IsCrossbow(weapon)) flag = FlagUsingCrossBow;
        return flag;
    }
}
