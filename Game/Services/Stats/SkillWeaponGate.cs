using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;

namespace Navislamia.Game.Services.Stats;

[Flags]
public enum SkillWeaponFlag
{
    None = 0,
    OneHandSword = 1 << 0,
    TwoHandSword = 1 << 1,
    DoubleSword = 1 << 2,
    Dagger = 1 << 3,
    DoubleDagger = 1 << 4,
    Spear = 1 << 5,
    Axe = 1 << 6,
    OneHandAxe = 1 << 7,
    DoubleAxe = 1 << 8,
    OneHandMace = 1 << 9,
    TwoHandMace = 1 << 10,
    Lightbow = 1 << 11,
    Heavybow = 1 << 12,
    Crossbow = 1 << 13,
    OneHandStaff = 1 << 14,
    TwoHandStaff = 1 << 15,

    /// <summary><c>vf_shield_only</c>: no weapon class maps to it; it asks for a shield in the shield slot.</summary>
    Shield = 1 << 16
}

public static class SkillWeaponGate
{
    private static readonly FrozenDictionary<ItemType, SkillWeaponFlag> WeaponFlags =
        new Dictionary<ItemType, SkillWeaponFlag>
        {
            [ItemType.OnehandSword] = SkillWeaponFlag.OneHandSword,
            [ItemType.TwohandSword] = SkillWeaponFlag.TwoHandSword,
            [ItemType.DoubleSword] = SkillWeaponFlag.DoubleSword,
            [ItemType.Dagger] = SkillWeaponFlag.Dagger,
            [ItemType.DoubleDagger] = SkillWeaponFlag.DoubleDagger,
            [ItemType.TwohandSpear] = SkillWeaponFlag.Spear,
            [ItemType.TwohandAxe] = SkillWeaponFlag.Axe,
            [ItemType.OnehandAxe] = SkillWeaponFlag.OneHandAxe,
            [ItemType.DoubleAxe] = SkillWeaponFlag.DoubleAxe,
            [ItemType.OnehandMace] = SkillWeaponFlag.OneHandMace,
            [ItemType.TwohandMace] = SkillWeaponFlag.TwoHandMace,
            [ItemType.LightBow] = SkillWeaponFlag.Lightbow,
            [ItemType.HeavyBow] = SkillWeaponFlag.Heavybow,
            [ItemType.Crossbow] = SkillWeaponFlag.Crossbow,
            [ItemType.OnehandStaff] = SkillWeaponFlag.OneHandStaff,
            [ItemType.TwohandStaff] = SkillWeaponFlag.TwoHandStaff
        }.ToFrozenDictionary();

    public static bool IsWeapon(ItemType itemType)
    {
        return WeaponFlags.ContainsKey(itemType);
    }

    public static SkillWeaponFlag Resolve(ItemType itemType)
    {
        return WeaponFlags.TryGetValue(itemType, out var flag) ? flag : SkillWeaponFlag.None;
    }

    public static bool Allows(SkillWeaponFlag required, bool weaponNotRequired, ItemType? equipped) =>
        Allows(required, weaponNotRequired, equipped, wearsShield: false);

    /// <summary>
    /// <c>applyStatByPassiveSkill</c>/<c>applyPassiveSkillEffect</c>: a skill that needs a weapon and is usable with a
    /// shield needs the shield (<c>IsWearShield</c>), whatever the weapon; otherwise the main-hand weapon decides.
    /// </summary>
    public static bool Allows(SkillWeaponFlag required, bool weaponNotRequired, ItemType? equipped, bool wearsShield)
    {
        if (weaponNotRequired)
        {
            return true;
        }

        if ((required & SkillWeaponFlag.Shield) != SkillWeaponFlag.None)
        {
            return wearsShield;
        }

        return equipped.HasValue && (required & Resolve(equipped.Value)) != SkillWeaponFlag.None;
    }
}
