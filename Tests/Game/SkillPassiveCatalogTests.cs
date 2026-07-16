using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

[TestFixture]
public class SkillPassiveCatalogTests
{
    private const int BodyTraining = 1004;
    private const int DefenseTraining = 1003;
    private const int MindDefense = 61011;
    private const int OffenseTraining = 61001;
    private const int CrossbowMastery = 1111;
    private const int FightersCombatSkill = 61012;

    private static decimal[] Vars(params decimal[] values)
    {
        var full = new decimal[20];
        values.CopyTo(full, 0);
        return full;
    }

    private static SkillPassiveFields Passive(int id, int effectType, decimal[] vars,
        SkillWeaponFlag weapons = SkillWeaponFlag.None, bool weaponNotRequired = true)
    {
        return new SkillPassiveFields(id, effectType, vars, weapons, weaponNotRequired);
    }

    private static SkillPassiveCatalog Create(params SkillPassiveFields[] passives)
    {
        var repository = A.Fake<ISkillResourceRepository>();
        A.CallTo(() => repository.GetStatPassives()).Returns(passives);
        return new SkillPassiveCatalog(repository);
    }

    private static SkillPassiveCatalog Default()
    {
        return Create(
            Passive(BodyTraining, SkillPassiveCatalog.IncreaseHpMp, Vars(0, 30)),
            Passive(DefenseTraining, SkillPassiveCatalog.IncreaseBaseAttribute, Vars(0, 3)),
            Passive(MindDefense, SkillPassiveCatalog.IncreaseBaseAttribute, Vars(0, 0, 0, 3)));
    }

    [Test]
    public void Resolve_BodyTrainingRaisesMaxHpPerSkillLevel()
    {
        Default().Resolve(BodyTraining, 1, null).Should().Equal(new StatEffect(StatTarget.MaxHp, 30f, false));
        Default().Resolve(BodyTraining, 3, null).Should().Equal(new StatEffect(StatTarget.MaxHp, 90f, false));
    }

    [Test]
    public void Resolve_UsesTheFirstPairForPhysicalDefenceAndTheSecondForMagic()
    {
        Default().Resolve(DefenseTraining, 2, null).Should().Equal(new StatEffect(StatTarget.Defence, 6f, false));
        Default().Resolve(MindDefense, 2, null).Should().Equal(new StatEffect(StatTarget.MagicDefence, 6f, false));
    }

    [Test]
    public void Resolve_AppliesTheBasePlusPerLevelFormula()
    {
        var catalog = Create(Passive(BodyTraining, SkillPassiveCatalog.IncreaseHpMp, Vars(10, 5)));

        catalog.Resolve(BodyTraining, 1, null).Should().Equal(new StatEffect(StatTarget.MaxHp, 15f, false));
        catalog.Resolve(BodyTraining, 4, null).Should().Equal(new StatEffect(StatTarget.MaxHp, 30f, false));
    }

    [Test]
    public void Resolve_ReadsBothPairsOfOneSkill()
    {
        var catalog = Create(Passive(BodyTraining, SkillPassiveCatalog.IncreaseHpMp, Vars(0, 30, 0, 20)));

        catalog.Resolve(BodyTraining, 1, null).Should().Equal(
            new StatEffect(StatTarget.MaxHp, 30f, false),
            new StatEffect(StatTarget.MaxMp, 20f, false));
    }

    [Test]
    public void Resolve_ReturnsNothingForAnUnknownOrUnlearnedSkill()
    {
        Default().Resolve(9999, 1, null).Should().BeEmpty();
        Default().Resolve(BodyTraining, 0, null).Should().BeEmpty();
    }

    [Test]
    public void Resolve_OffenseTrainingRaisesAttackPowerWithoutAWeapon()
    {
        var catalog = Create(Passive(OffenseTraining, SkillPassiveCatalog.WeaponMastery, Vars(0, 3)));

        catalog.Resolve(OffenseTraining, 1, null).Should()
            .Equal(new StatEffect(StatTarget.AttackPointRight, 3f, false));
        catalog.Resolve(OffenseTraining, 4, null).Should()
            .Equal(new StatEffect(StatTarget.AttackPointRight, 12f, false));
    }

    [Test]
    public void Resolve_WeaponMasteryReadsAttackFromPairOneAndAttackSpeedFromPairTwo()
    {
        var catalog = Create(Passive(FightersCombatSkill, SkillPassiveCatalog.WeaponMastery, Vars(12, 6, 5, 0),
            SkillWeaponFlag.OneHandSword, false));

        catalog.Resolve(FightersCombatSkill, 1, ItemType.OnehandSword).Should().Equal(
            new[]
            {
                new StatEffect(StatTarget.AttackPointRight, 18f, false),
                new StatEffect(StatTarget.AttackSpeed, 5f, false)
            },
            "the tooltip reads 'Lv 1 also increases P. Atk. Spd. by 5', a flat pair-two bonus");

        catalog.Resolve(FightersCombatSkill, 3, ItemType.OnehandSword).Should().Equal(
            new StatEffect(StatTarget.AttackPointRight, 30f, false),
            new StatEffect(StatTarget.AttackSpeed, 5f, false));
    }

    [Test]
    public void Resolve_AWeaponGatedMasteryOnlyAppliesWhileItsWeaponIsEquipped()
    {
        var catalog = Create(Passive(CrossbowMastery, SkillPassiveCatalog.WeaponMastery, Vars(10, 5),
            SkillWeaponFlag.Crossbow, false));

        catalog.Resolve(CrossbowMastery, 2, ItemType.Crossbow).Should()
            .Equal(new StatEffect(StatTarget.AttackPointRight, 20f, false));
        catalog.Resolve(CrossbowMastery, 2, ItemType.OnehandSword).Should().BeEmpty();
        catalog.Resolve(CrossbowMastery, 2, null).Should().BeEmpty();
    }

    [Test]
    public void Resolve_AMasteryAcceptsAnyOfItsFlaggedWeapons()
    {
        var catalog = Create(Passive(FightersCombatSkill, SkillPassiveCatalog.WeaponMastery, Vars(12, 6),
            SkillWeaponFlag.OneHandSword | SkillWeaponFlag.TwoHandSword | SkillWeaponFlag.Axe
            | SkillWeaponFlag.OneHandAxe | SkillWeaponFlag.DoubleAxe, false));

        catalog.Resolve(FightersCombatSkill, 1, ItemType.TwohandSword).Should().NotBeEmpty();
        catalog.Resolve(FightersCombatSkill, 1, ItemType.TwohandAxe).Should()
            .NotBeEmpty("vf_axe is the two-handed axe");
        catalog.Resolve(FightersCombatSkill, 1, ItemType.OnehandAxe).Should().NotBeEmpty();
        catalog.Resolve(FightersCombatSkill, 1, ItemType.TwohandStaff).Should().BeEmpty();
    }

    [Test]
    public void Resolve_AnUnconditionalPassiveIgnoresTheEquippedWeapon()
    {
        Default().Resolve(BodyTraining, 1, ItemType.Crossbow).Should()
            .Equal(new StatEffect(StatTarget.MaxHp, 30f, false));
    }

    [Test]
    public void BuildTemplates_IgnoresAnUnsupportedEffectTypeAndEmptyPairs()
    {
        SkillPassiveCatalog.BuildTemplates(Passive(1, 10011, Vars(10, 5)))
            .Should().BeEmpty("AmplifyBaseAttribute has no readable value in this data");

        SkillPassiveCatalog.BuildTemplates(Passive(1, SkillPassiveCatalog.IncreaseHpMp, Vars()))
            .Should().BeEmpty();

        SkillPassiveCatalog.BuildTemplates(Passive(1, SkillPassiveCatalog.IncreaseHpMp, null))
            .Should().BeEmpty();
    }
}
