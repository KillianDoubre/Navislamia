using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services.Buffs;

namespace Tests.Game;

[TestFixture]
public class AuraTests
{
    private const int PowerSupport = 1201;
    private const int AgileStyle = 1202;

    [Test]
    public void Resolve_TurnsAnAuraOnWhenItsGroupIsEmpty()
    {
        AuraToggle.Resolve(0, PowerSupport).Should().Be(AuraAction.TurnOn);
    }

    [Test]
    public void Resolve_TurnsTheSameAuraOffWhenItIsRecast()
    {
        AuraToggle.Resolve(PowerSupport, PowerSupport).Should()
            .Be(AuraAction.TurnOff, "recasting an active aura is how the player switches it off");
    }

    [Test]
    public void Resolve_SwapsWhenAnotherAuraOfTheGroupIsActive()
    {
        AuraToggle.Resolve(AgileStyle, PowerSupport).Should()
            .Be(AuraAction.Swap, "m_vAura is keyed by toggle_group: one aura per group at a time");
    }
}

[TestFixture]
public class BuffCatalogTests
{
    private static CastableSkillRow Row(int id, int effectType, int target, bool harmful = false,
        int? stateId = 500, int toggleGroup = 0)
    {
        return new CastableSkillRow(id, effectType, harmful, target, stateId, toggleGroup,
            new decimal[20], 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    private static BuffCatalog Create(params CastableSkillRow[] rows)
    {
        var repository = A.Fake<ISkillResourceRepository>();
        A.CallTo(() => repository.GetCastableSkills()).Returns(rows);
        return new BuffCatalog(repository);
    }

    [Test]
    public void Classify_MapsEachFamilyToItsKind()
    {
        var catalog = Create(
            Row(1, BuffCatalog.AddState, target: 1),
            Row(2, BuffCatalog.AddRegionState, target: 2),
            Row(3, BuffCatalog.ToggleAura, target: 1, toggleGroup: 7),
            Row(4, BuffCatalog.AddHp, target: 1, stateId: null),
            Row(5, BuffCatalog.AddState, target: 1, harmful: true));

        catalog.TryGet(1, out var buff).Should().BeTrue();
        buff.Kind.Should().Be(SkillCastKind.Buff);
        catalog.TryGet(2, out var region).Should().BeTrue();
        region.Kind.Should().Be(SkillCastKind.Buff, "a region buff is a self buff while solo");
        catalog.TryGet(3, out var aura).Should().BeTrue();
        aura.Kind.Should().Be(SkillCastKind.Aura);
        aura.ToggleGroup.Should().Be(7);
        catalog.TryGet(4, out var heal).Should().BeTrue();
        heal.Kind.Should().Be(SkillCastKind.Heal, "a heal is the only kind that needs no state");
        catalog.TryGet(5, out var debuff).Should().BeTrue();
        debuff.Kind.Should().Be(SkillCastKind.Debuff);
    }

    [Test]
    public void Classify_RefusesSkillsWhoseTargetIsNotTheCaster()
    {
        var catalog = Create(
            Row(1, BuffCatalog.AddState, target: 31),
            Row(2, BuffCatalog.AddState, target: 32),
            Row(3, BuffCatalog.AddRegionState, target: 3));

        catalog.TryGet(1, out _).Should().BeFalse("target 31 is Summon, which nothing models");
        catalog.TryGet(2, out _).Should().BeFalse("target 32 is PartySummon");
        catalog.TryGet(3, out _).Should().BeFalse("target 3 is RegionWithout: it excludes the caster");
    }

    [Test]
    public void Classify_RefusesAStatelessSkillThatIsNotAHeal()
    {
        var catalog = Create(
            Row(1, BuffCatalog.AddState, target: 1, stateId: null),
            Row(2, BuffCatalog.AddState, target: 1, stateId: 0));

        catalog.TryGet(1, out _).Should().BeFalse();
        catalog.TryGet(2, out _).Should().BeFalse("state id 0 means none; no StateResource has id 0");
    }

    [Test]
    public void Classify_RefusesADebuffOrHealThatIsNotAimedAtOneTarget()
    {
        var catalog = Create(
            Row(1, BuffCatalog.AddState, target: 2, harmful: true),
            Row(2, BuffCatalog.AddHp, target: 2, stateId: null));

        catalog.TryGet(1, out _).Should().BeFalse("a harmful region skill needs area resolution");
        catalog.TryGet(2, out _).Should().BeFalse("a region heal is out of scope");
    }

    [Test]
    public void Classify_MapsTheOffensiveFamilies()
    {
        var catalog = Create(
            Row(1, BuffCatalog.PhysicalSingleDamage, target: 1, harmful: true, stateId: null),
            Row(2, BuffCatalog.MagicSingleDamage, target: 1, harmful: true, stateId: null));

        catalog.TryGet(1, out var physical).Should().BeTrue();
        physical.Kind.Should().Be(SkillCastKind.PhysicalAttack, "an attack carries no state");
        catalog.TryGet(2, out var magic).Should().BeTrue();
        magic.Kind.Should().Be(SkillCastKind.MagicAttack);
    }

    [Test]
    public void Classify_RefusesAnAttackThatIsNotHarmfulOrNotSingleTarget()
    {
        var catalog = Create(
            Row(1, BuffCatalog.PhysicalSingleDamage, target: 2, harmful: true, stateId: null),
            Row(2, BuffCatalog.PhysicalSingleDamage, target: 1, harmful: false, stateId: null));

        catalog.TryGet(1, out _).Should().BeFalse("a region attack needs area resolution");
        catalog.TryGet(2, out _).Should().BeFalse("an offensive skill must be harmful");
    }

    [Test]
    public void CountOf_ReportsEachKind()
    {
        var catalog = Create(
            Row(1, BuffCatalog.AddState, target: 1),
            Row(2, BuffCatalog.AddState, target: 1),
            Row(3, BuffCatalog.ToggleAura, target: 1));

        catalog.Count.Should().Be(3);
        catalog.CountOf(SkillCastKind.Buff).Should().Be(2);
        catalog.CountOf(SkillCastKind.Aura).Should().Be(1);
        catalog.CountOf(SkillCastKind.Heal).Should().Be(0);
    }
}
