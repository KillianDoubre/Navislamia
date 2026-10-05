using System.Linq;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

/// <summary>Shield Mastery (10009) and Avoidance Expert (10011), socle-passifs-combat-recharge-bouclier.md §2.</summary>
[TestFixture]
public class SkillPassiveShieldTests
{
    private const int ShieldMastery = 1211;
    private const int AvoidanceExpert = 1221;
    private const int CreatureRegen = 40191;

    private static decimal[] Vars(params (int Index, decimal Value)[] values)
    {
        var full = new decimal[20];
        foreach (var (index, value) in values) full[index] = value;
        return full;
    }

    // Epic 7 SkillResource rows: 1211 var4 = 1 with vf_shield_only, 1221 var8 = 0.02 with vf_is_not_need_weapon,
    // 40191 (a creature's) var1 = 10 with vf_is_not_need_weapon; the cool time row is synthetic.
    private static SkillPassiveCatalog Catalog()
    {
        var repository = A.Fake<ISkillResourceRepository>();
        A.CallTo(() => repository.GetStatPassives()).Returns(new[]
        {
            new SkillPassiveFields(ShieldMastery, 10009, Vars((4, 1m)), SkillWeaponFlag.Shield, false),
            new SkillPassiveFields(AvoidanceExpert, 10011, Vars((8, 0.02m)), SkillWeaponFlag.None, true),
            new SkillPassiveFields(CreatureRegen, 10009, Vars((1, 10m)), SkillWeaponFlag.None, true),
            new SkillPassiveFields(9001, 10009, Vars((6, 2m), (9, 0.5m)), SkillWeaponFlag.None, true)
        });
        return new SkillPassiveCatalog(repository);
    }

    [Test]
    public void Shield_mastery_adds_block_chance_per_level_only_with_a_shield_worn()
    {
        var catalog = Catalog();
        catalog.Resolve(ShieldMastery, 5, ItemType.OnehandSword, wearsShield: true).Should()
            .Equal(new StatEffect(StatTarget.BlockChance, 5f, false));
        catalog.Resolve(ShieldMastery, 5, ItemType.OnehandSword, wearsShield: false).Should()
            .BeEmpty("IsWearShield: the shield slot holds no shield");
        catalog.Resolve(ShieldMastery, 5, ItemType.OnehandSword).Should().BeEmpty("without the shield slot known");
    }

    [Test]
    public void Extension_attribute_reads_var_times_level_and_subtracts_the_cool_time_speed()
    {
        var catalog = Catalog();
        catalog.Resolve(CreatureRegen, 2, null).Should().Equal(new StatEffect(StatTarget.HpRegenPoint, 20f, false));
        catalog.Resolve(9001, 3, null).Should().Equal(new StatEffect(StatTarget.Critical, 6f, false),
            new StatEffect(StatTarget.CoolTimeSpeed, -1.5f, false));
    }

    [Test]
    public void Avoidance_expert_amplifies_avoid_by_two_percent_per_level()
    {
        var effect = Catalog().Resolve(AvoidanceExpert, 3, null).Single();
        effect.Target.Should().Be(StatTarget.Avoid);
        effect.IsPercent.Should().BeTrue("m_AttributeAmplifier");
        effect.Value.Should().BeApproximately(0.06f, 1e-6f);
    }

    [Test]
    public void Increase_base_attribute_reads_ten_per_level_values_like_the_official_server()
    {
        // 40041 (a creature's): var0 = 5 is +5 attack per level, not +5 defence; 9002: var4 = 5 is attack speed;
        // 1003 Defense Training: var1 = 3 is +3 defence per level, as before.
        var repository = A.Fake<ISkillResourceRepository>();
        A.CallTo(() => repository.GetStatPassives()).Returns(new[]
        {
            new SkillPassiveFields(40041, 10008, Vars((0, 5m)), SkillWeaponFlag.None, true),
            new SkillPassiveFields(9002, 10008, Vars((4, 5m)), SkillWeaponFlag.None, true),
            new SkillPassiveFields(1003, 10008, Vars((1, 3m)), SkillWeaponFlag.None, true)
        });
        var catalog = new SkillPassiveCatalog(repository);
        catalog.Resolve(40041, 2, null).Should().Equal(new StatEffect(StatTarget.AttackPointRight, 10f, false));
        catalog.Resolve(9002, 2, null).Should().Equal(new StatEffect(StatTarget.AttackSpeed, 10f, false));
        catalog.Resolve(1003, 4, null).Should().Equal(new StatEffect(StatTarget.Defence, 12f, false));
    }

    [Test]
    public void A_shield_usable_skill_asks_for_the_shield_whatever_the_weapon()
    {
        SkillWeaponGate.Allows(SkillWeaponFlag.Shield | SkillWeaponFlag.OneHandSword, false, ItemType.OnehandSword,
            wearsShield: false).Should().BeFalse("applyStatByPassiveSkill tests the shield first");
        SkillWeaponGate.Allows(SkillWeaponFlag.Shield, false, null, wearsShield: true).Should().BeTrue();
        SkillWeaponGate.Allows(SkillWeaponFlag.OneHandSword, false, ItemType.OnehandSword, wearsShield: false).Should()
            .BeTrue();
    }

    [Test]
    public void The_item_catalog_knows_the_shields()
    {
        var repository = A.Fake<IItemResourceRepository>();
        A.CallTo(() => repository.GetEffectFields()).Returns(new[]
        {
            new ItemEffectFields(100, ItemType.Shield, null, null, null, null, null, null),
            new ItemEffectFields(101, ItemType.DecoShield, null, null, null, null, null, null)
        });
        var catalog = new ItemStatCatalog(repository);
        catalog.IsShield(100).Should().BeTrue();
        catalog.IsShield(101).Should().BeFalse("a decorative shield is not CLASS_SHIELD");
    }
}
