using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

[TestFixture]
public class SkillWeaponGateTests
{
    [TestCase(ItemType.OnehandSword, SkillWeaponFlag.OneHandSword)]
    [TestCase(ItemType.TwohandSword, SkillWeaponFlag.TwoHandSword)]
    [TestCase(ItemType.Dagger, SkillWeaponFlag.Dagger)]
    [TestCase(ItemType.TwohandSpear, SkillWeaponFlag.Spear)]
    [TestCase(ItemType.TwohandAxe, SkillWeaponFlag.Axe)]
    [TestCase(ItemType.OnehandAxe, SkillWeaponFlag.OneHandAxe)]
    [TestCase(ItemType.DoubleAxe, SkillWeaponFlag.DoubleAxe)]
    [TestCase(ItemType.OnehandMace, SkillWeaponFlag.OneHandMace)]
    [TestCase(ItemType.TwohandMace, SkillWeaponFlag.TwoHandMace)]
    [TestCase(ItemType.HeavyBow, SkillWeaponFlag.Heavybow)]
    [TestCase(ItemType.LightBow, SkillWeaponFlag.Lightbow)]
    [TestCase(ItemType.Crossbow, SkillWeaponFlag.Crossbow)]
    [TestCase(ItemType.OnehandStaff, SkillWeaponFlag.OneHandStaff)]
    [TestCase(ItemType.TwohandStaff, SkillWeaponFlag.TwoHandStaff)]
    public void Resolve_MapsEachWeaponClassToItsSkillFlag(ItemType itemType, SkillWeaponFlag expected)
    {
        SkillWeaponGate.Resolve(itemType).Should().Be(expected);
        SkillWeaponGate.IsWeapon(itemType).Should().BeTrue();
    }

    [TestCase(ItemType.Armor)]
    [TestCase(ItemType.Shield)]
    [TestCase(ItemType.Ring)]
    [TestCase(ItemType.Etc)]
    public void Resolve_TreatsNonWeaponsAsNoFlag(ItemType itemType)
    {
        SkillWeaponGate.Resolve(itemType).Should().Be(SkillWeaponFlag.None);
        SkillWeaponGate.IsWeapon(itemType).Should().BeFalse();
    }

    [Test]
    public void Allows_AlwaysAcceptsAPassiveThatNeedsNoWeapon()
    {
        SkillWeaponGate.Allows(SkillWeaponFlag.None, true, null).Should().BeTrue();
        SkillWeaponGate.Allows(SkillWeaponFlag.None, true, ItemType.Crossbow).Should().BeTrue();
    }

    [Test]
    public void Allows_RequiresOneOfTheFlaggedWeaponsOtherwise()
    {
        var required = SkillWeaponFlag.OneHandMace | SkillWeaponFlag.TwoHandMace;

        SkillWeaponGate.Allows(required, false, ItemType.OnehandMace).Should().BeTrue();
        SkillWeaponGate.Allows(required, false, ItemType.TwohandMace).Should().BeTrue();
        SkillWeaponGate.Allows(required, false, ItemType.Dagger).Should().BeFalse();
        SkillWeaponGate.Allows(required, false, ItemType.Armor).Should().BeFalse();
    }

    [Test]
    public void Allows_RefusesAGatedPassiveWhenNoWeaponIsEquipped()
    {
        SkillWeaponGate.Allows(SkillWeaponFlag.Crossbow, false, null).Should().BeFalse();
    }
}
