using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// The resolution of a `TM_CS_MIX` frame: <see cref="MixResourceMatcher"/> against hand-built
/// `MixResource` rows. The port is judged against `MixManager.cpp:242-298` (the walk and the arrangement)
/// and `:345-452` (the twenty condition codes).
///
/// Two decisions this file makes visible, both documented in the matcher:
/// the codes the reference leaves inert are refused rather than satisfied — 11, 12 and 15-18 in the
/// pre-arrangement check, 20 in the post-arrangement where the reference puts it — and the
/// frame quantity is consumed as 1 unless the group checked it with `CHECK_ITEM_COUNT` — NGemity's
/// `bIsCountChecked` is never assigned, so its own answer is always 1 (`MixManager.cpp:449-450`).
/// A third one is assumed rather than established: the materials are paired by permutation, which the
/// executed reference loop does not do (spec §7, §9 point 5 and §10 point 7).
/// See docs/packet-specs/socle-artisanat-ressources.md §6.1, §6.2 and §8 (L1b).
/// </summary>
[TestFixture]
public class MixResourceMatcherTests
{
    private static MixMaterial Material(int code = 100, int group = 0, int itemClass = 0, int rank = 0,
        int wear = 0, long level = 1, long enhance = 0, int flag = 0, long count = 1)
    {
        return new MixMaterial(code, group, itemClass, rank, wear, level, enhance, flag, count);
    }

    /// <summary>
    /// A row with the conditions spelled out: <c>Main(...)</c> fills <c>main_type_01..05</c>,
    /// <c>Sub(1, ...)</c> fills <c>sub01_type_01..05</c>. The pairs are written in the order the reference
    /// reads them, and <c>subMaterialCount</c> is set as the row declares it — including on purpose when it
    /// disagrees with the groups filled.
    /// </summary>
    private sealed class RuleBuilder
    {
        private readonly MixResourceEntity _rule;

        public RuleBuilder(long id, int subMaterialCount, int mixType = 101)
        {
            _rule = new MixResourceEntity
            {
                Id = id,
                MixType = mixType,
                SubMaterialCount = subMaterialCount
            };
        }

        public RuleBuilder Main(params (int Code, int Value)[] conditions)
        {
            return Set("Main", conditions);
        }

        /// <summary>The <paramref name="group"/>-th sub group, 1-based: <c>Sub(1, …)</c> is <c>sub01_*</c>.</summary>
        public RuleBuilder Sub(int group, params (int Code, int Value)[] conditions)
        {
            return Set($"Sub{group:00}", conditions);
        }

        public MixResourceEntity Build()
        {
            return _rule;
        }

        private RuleBuilder Set(string prefix, (int Code, int Value)[] conditions)
        {
            conditions.Length.Should().BeLessOrEqualTo(MixResourceRules.MaterialInfoCount,
                "a group carries five (type, value) pairs");

            for (var i = 0; i < conditions.Length; i++)
            {
                var position = i + 1;
                Property($"{prefix}Type0{position}").SetValue(_rule, conditions[i].Code);
                Property($"{prefix}Value0{position}").SetValue(_rule, conditions[i].Value);
            }

            return this;
        }

        private static PropertyInfo Property(string name)
        {
            return typeof(MixResourceEntity).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!;
        }
    }

    [Test]
    public void TheFirstRuleAcceptingTheFrameWins()
    {
        var first = new RuleBuilder(1016, 1)
            .Main((MixResourceMatcher.CheckItemGroup, 3))
            .Sub(1, (MixResourceMatcher.CheckItemGroup, 3))
            .Build();
        var second = new RuleBuilder(1017, 1)
            .Main((MixResourceMatcher.CheckItemGroup, 3))
            .Sub(1, (MixResourceMatcher.CheckItemGroup, 3))
            .Build();

        MixResourceMatcher.TryResolve(new[] { first, second }, Material(group: 3),
            new[] { Material(group: 3) }, out var resolution).Should().BeTrue();

        resolution.Rule.Id.Should().Be(1016, "the table is walked in order and the first match is kept");
    }

    [Test]
    public void NoRuleAcceptsAnEmptyTable()
    {
        MixResourceMatcher.TryResolve(Array.Empty<MixResourceEntity>(), Material(), Array.Empty<MixMaterial>(),
            out _).Should().BeFalse();
    }

    [Test]
    public void TheDeclaredGroupCountGatesTheFrame()
    {
        var twoGroups = new RuleBuilder(1154, 2)
            .Main((MixResourceMatcher.CheckItemGroup, 3))
            .Sub(1, (MixResourceMatcher.CheckItemGroup, 3))
            .Sub(2, (MixResourceMatcher.CheckItemGroup, 3))
            .Build();

        MixResourceMatcher.TryResolve(new[] { twoGroups }, Material(group: 3), new[] { Material(group: 3) }, out _)
            .Should().BeFalse("the row declares two sub materials, the frame names one");

        MixResourceMatcher.TryResolve(new[] { twoGroups }, Material(group: 3),
            new[] { Material(group: 3), Material(group: 3) }, out _).Should().BeTrue();
    }

    [Test]
    public void ARowThatDeclaresNoGroupAcceptsAFrameWithoutMaterial()
    {
        var onlyTarget = new RuleBuilder(601, 0).Main((MixResourceMatcher.CheckItemGroup, 3)).Build();

        MixResourceMatcher.TryResolve(new[] { onlyTarget }, Material(group: 3), Array.Empty<MixMaterial>(), out _)
            .Should().BeTrue();

        MixResourceMatcher.TryResolve(new[] { onlyTarget }, Material(group: 3), new[] { Material(group: 3) }, out _)
            .Should().BeFalse();
    }

    [Test]
    public void AGroupWithoutConditionOnlyAcceptsAnAbsentMaterial()
    {
        // `if (info.type[0] == 0) return pItem == nullptr;` — MixManager.cpp:347-348. A row that declares a
        // group but asks nothing of it is not a wildcard: it wants that slot left empty.
        var blankGroup = new RuleBuilder(1016, 1).Main((MixResourceMatcher.CheckItemGroup, 3)).Sub(1).Build();

        MixResourceMatcher.TryResolve(new[] { blankGroup }, Material(group: 3), Array.Empty<MixMaterial>(), out _)
            .Should().BeFalse("the row declares one sub material");

        MixResourceMatcher.TryResolve(new[] { blankGroup }, Material(group: 3), new[] { Material(group: 3) }, out _)
            .Should().BeFalse("a named material cannot fill a group that asks for nothing");
    }

    [Test]
    public void ATargetlessFrameIsAcceptedOnlyByARowThatAsksNothingOfItsTarget()
    {
        var asksForNothing = new RuleBuilder(1016, 1).Sub(1, (MixResourceMatcher.CheckItemGroup, 3)).Build();
        var asksForAGroup = new RuleBuilder(1017, 1).Main((MixResourceMatcher.CheckItemGroup, 3))
            .Sub(1, (MixResourceMatcher.CheckItemGroup, 3)).Build();

        MixResourceMatcher.TryResolve(new[] { asksForNothing }, null, new[] { Material(group: 3) }, out _)
            .Should().BeTrue();
        MixResourceMatcher.TryResolve(new[] { asksForAGroup }, null, new[] { Material(group: 3) }, out _)
            .Should().BeFalse("the row names a target, the frame names none");
        MixResourceMatcher.TryResolve(new[] { asksForNothing }, Material(group: 3), new[] { Material(group: 3) }, out _)
            .Should().BeFalse("the row wants no target, the frame named one");
    }

    [Test]
    public void TheConditionsCompareTheTemplateColumnsAndTheInstance()
    {
        var rule = new RuleBuilder(1154, 0)
            .Main(
                (MixResourceMatcher.CheckItemGroup, 3),
                (MixResourceMatcher.CheckItemClass, 2),
                (MixResourceMatcher.CheckItemId, 700201),
                (MixResourceMatcher.CheckItemRank, 4),
                (MixResourceMatcher.CheckItemLevel, 12))
            .Build();
        var rules = new[] { rule };

        MixResourceMatcher.TryResolve(rules, Material(code: 700201, group: 3, itemClass: 2, rank: 4, level: 12),
            Array.Empty<MixMaterial>(), out _).Should().BeTrue();

        MixResourceMatcher.TryResolve(rules, Material(code: 700201, group: 3, itemClass: 2, rank: 4, level: 12,
                wear: 7), Array.Empty<MixMaterial>(), out _)
            .Should().BeTrue("the row asks nothing of the wear position");

        // Each column the row asks for refuses a value that differs.
        MixResourceMatcher.TryResolve(rules, Material(code: 700201, group: 4, itemClass: 2, rank: 4, level: 12),
            Array.Empty<MixMaterial>(), out _).Should().BeFalse();
        MixResourceMatcher.TryResolve(rules, Material(code: 700201, group: 3, itemClass: 9, rank: 4, level: 12),
            Array.Empty<MixMaterial>(), out _).Should().BeFalse();
        MixResourceMatcher.TryResolve(rules, Material(code: 700202, group: 3, itemClass: 2, rank: 4, level: 12),
            Array.Empty<MixMaterial>(), out _).Should().BeFalse();
        MixResourceMatcher.TryResolve(rules, Material(code: 700201, group: 3, itemClass: 2, rank: 5, level: 12),
            Array.Empty<MixMaterial>(), out _).Should().BeFalse();
        MixResourceMatcher.TryResolve(rules, Material(code: 700201, group: 3, itemClass: 2, rank: 4, level: 13),
            Array.Empty<MixMaterial>(), out _)
            .Should().BeFalse("CHECK_ITEM_LEVEL is an equality in the reference, not a ceiling");
    }

    [Test]
    public void TheFlagConditionsBuildTheMaskFromTheIndexTheRowCarries()
    {
        var flagOn = new[] { new RuleBuilder(1016, 0).Main((MixResourceMatcher.CheckFlagOn, 3)).Build() };
        var flagOff = new[] { new RuleBuilder(1016, 0).Main((MixResourceMatcher.CheckFlagOff, 3)).Build() };

        MixResourceMatcher.TryResolve(flagOn, Material(flag: 1 << 3), Array.Empty<MixMaterial>(), out _)
            .Should().BeTrue();
        MixResourceMatcher.TryResolve(flagOn, Material(flag: 1 << 4), Array.Empty<MixMaterial>(), out _)
            .Should().BeFalse();
        MixResourceMatcher.TryResolve(flagOn, Material(flag: 0), Array.Empty<MixMaterial>(), out _)
            .Should().BeFalse();

        MixResourceMatcher.TryResolve(flagOff, Material(flag: 1 << 4), Array.Empty<MixMaterial>(), out _)
            .Should().BeTrue();
        MixResourceMatcher.TryResolve(flagOff, Material(flag: 1 << 3), Array.Empty<MixMaterial>(), out _)
            .Should().BeFalse();

        // The repository stores `ItemFlag.None` as -1; read as a bitset that is every bit set, the
        // convention GroundItemDropRules already documents.
        MixResourceMatcher.TryResolve(flagOn, Material(flag: -1), Array.Empty<MixMaterial>(), out _)
            .Should().BeTrue();
        MixResourceMatcher.TryResolve(flagOff, Material(flag: -1), Array.Empty<MixMaterial>(), out _)
            .Should().BeFalse();
    }

    [Test]
    public void TheEnhanceConditionsAreImplementedForBothDirections()
    {
        var match = new[] { new RuleBuilder(1016, 0).Main((MixResourceMatcher.CheckEnhanceMatch, 5)).Build() };
        var mismatch = new[] { new RuleBuilder(1016, 0).Main((MixResourceMatcher.CheckEnhanceDismatch, 5)).Build() };

        MixResourceMatcher.TryResolve(match, Material(enhance: 5), Array.Empty<MixMaterial>(), out _)
            .Should().BeTrue();
        MixResourceMatcher.TryResolve(match, Material(enhance: 4), Array.Empty<MixMaterial>(), out _)
            .Should().BeFalse();

        MixResourceMatcher.TryResolve(mismatch, Material(enhance: 4), Array.Empty<MixMaterial>(), out _)
            .Should().BeTrue("code 9 is used by 45 rows of the reference dump");
        MixResourceMatcher.TryResolve(mismatch, Material(enhance: 5), Array.Empty<MixMaterial>(), out _)
            .Should().BeFalse();
    }

    [Test]
    public void TheWearPositionConditionsReadTheTemplateWearType()
    {
        var match = new[] { new RuleBuilder(1016, 0).Main((MixResourceMatcher.CheckItemWearPositionMatch, 2)).Build() };
        var mismatch = new[] { new RuleBuilder(1016, 0).Main((MixResourceMatcher.CheckItemWearPositionMismatch, 2)).Build() };

        MixResourceMatcher.TryResolve(match, Material(wear: 2), Array.Empty<MixMaterial>(), out _).Should().BeTrue();
        MixResourceMatcher.TryResolve(match, Material(wear: 3), Array.Empty<MixMaterial>(), out _).Should().BeFalse();
        MixResourceMatcher.TryResolve(mismatch, Material(wear: 3), Array.Empty<MixMaterial>(), out _).Should().BeTrue();
        MixResourceMatcher.TryResolve(mismatch, Material(wear: 2), Array.Empty<MixMaterial>(), out _).Should().BeFalse();
    }

    [Test]
    public void TheFrameQuantityIsConsumedUnlessTheGroupChecksIt()
    {
        var checked_ = new RuleBuilder(1154, 1).Sub(1, (MixResourceMatcher.CheckItemCount, 4)).Build();
        var unchecked_ = new RuleBuilder(1154, 1).Sub(1, (MixResourceMatcher.CheckItemGroup, 3)).Build();

        MixResourceMatcher.TryResolve(new[] { checked_ }, null, new[] { Material(count: 4) }, out var exact)
            .Should().BeTrue();
        exact.ConsumedCounts.Should().Equal(new long[] { 4L },
            "the group checked the quantity and it agreed");

        MixResourceMatcher.TryResolve(new[] { checked_ }, null, new[] { Material(count: 3) }, out _)
            .Should().BeFalse();

        MixResourceMatcher.TryResolve(new[] { unchecked_ }, null, new[] { Material(group: 3, count: 7) },
            out var forced).Should().BeTrue();
        forced.ConsumedCounts.Should().Equal(new long[] { 1L },
            "without CHECK_ITEM_COUNT the reference replaces the frame quantity with 1");
    }

    [Test]
    public void TheMaterialsAreArrangedByConditionWhateverTheirOrder()
    {
        // GetProperMixInfoAndArrangeSubMaterials: each condition takes the first stack not yet claimed that
        // satisfies it, and the stacks come out in condition order.
        var rule = new RuleBuilder(1154, 2)
            .Sub(1, (MixResourceMatcher.CheckItemId, 700202), (MixResourceMatcher.CheckItemCount, 9))
            .Sub(2, (MixResourceMatcher.CheckItemId, 700201), (MixResourceMatcher.CheckItemCount, 3))
            .Build();

        MixResourceMatcher.TryResolve(new[] { rule }, null,
            new[] { Material(code: 700201, count: 3), Material(code: 700202, count: 9) }, out var swapped)
            .Should().BeTrue("the official arranges the stacks instead of pairing them by position");
        swapped.ConsumedCounts.Should().Equal(9L, 3L);
        swapped.Arranged.Select(material => material.ItemCode).Should().Equal(700202, 700201);

        MixResourceMatcher.TryResolve(new[] { rule }, null,
            new[] { Material(code: 700202, count: 9), Material(code: 700201, count: 3) }, out var straight)
            .Should().BeTrue();
        straight.Arranged.Select(material => material.ItemCode).Should().Equal(700202, 700201);
    }

    [Test]
    public void TheFirstConditionClaimsTheFirstStackThatFits()
    {
        // Condition 1 accepts both stacks and claims the first; condition 2 then has only the second.
        var rule = new RuleBuilder(1154, 2)
            .Sub(1, (MixResourceMatcher.CheckItemGroup, 3))
            .Sub(2, (MixResourceMatcher.CheckItemId, 700201))
            .Build();

        MixResourceMatcher.TryResolve(new[] { rule }, null,
            new[] { Material(code: 700201, group: 3), Material(code: 700209, group: 3) }, out _)
            .Should().BeFalse("the greedy arrangement gave 700201 to the first condition");
        MixResourceMatcher.TryResolve(new[] { rule }, null,
            new[] { Material(code: 700209, group: 3), Material(code: 700201, group: 3) }, out _)
            .Should().BeTrue();
    }

    [Test]
    public void AMaterialNoGroupAcceptsRefusesTheRow()
    {
        var rule = new RuleBuilder(1154, 2)
            .Sub(1, (MixResourceMatcher.CheckItemId, 700202))
            .Sub(2, (MixResourceMatcher.CheckItemId, 700201))
            .Build();

        MixResourceMatcher.TryResolve(new[] { rule }, null,
            new[] { Material(code: 700201), Material(code: 700203) }, out _)
            .Should().BeFalse();
    }

    [Test]
    public void TheSameItemIdConditionComparesTheArrangedStackToTheTarget()
    {
        var rule = new RuleBuilder(1154, 1)
            .Main((MixResourceMatcher.CheckItemGroup, 3))
            .Sub(1, (MixResourceMatcher.CheckItemId, 700202), (MixResourceMatcher.CheckSameItemId, 0))
            .Build();

        MixResourceMatcher.TryResolve(new[] { rule }, Material(code: 700201, group: 3),
            new[] { Material(code: 700202, group: 3) }, out _)
            .Should().BeFalse("the material is not the same item as the target");

        MixResourceMatcher.TryResolve(new[] { rule }, Material(code: 700202, group: 3),
            new[] { Material(code: 700202, group: 3) }, out _)
            .Should().BeTrue();
    }

    [Test]
    public void ASlotIndexOutsideTheArrangementIsRefused()
    {
        var rule = new RuleBuilder(1154, 1)
            .Main((MixResourceMatcher.CheckItemGroup, 3))
            .Sub(1, (MixResourceMatcher.CheckSameItemId, 5))
            .Build();

        MixResourceMatcher.TryResolve(new[] { rule }, Material(group: 3), new[] { Material(group: 3) }, out _)
            .Should().BeFalse("slot 5 names no material of this arrangement");
    }

    private static MixMaterial Card(int summonCode) =>
        Material(group: 13) with { Instance = new MixInstance(0, 0, null, 0, summonCode) };

    [Test]
    public void TheSameSummonCodeConditionComparesWithTheMainMaterial()
    {
        // post_arrange_check_material_info: the resource's summon_id first, else the card's summon code.
        var rule = new[] { new RuleBuilder(1016, 1)
            .Main((MixResourceMatcher.CheckItemGroup, 13))
            .Sub(1, (MixResourceMatcher.CheckItemGroup, 13), (MixResourceMatcher.CheckSameSummonCode, 1))
            .Build() };

        MixResourceMatcher.TryResolve(rule, Card(2101), new[] { Card(2101) }, out _).Should().BeTrue();
        MixResourceMatcher.TryResolve(rule, Card(2101), new[] { Card(2201) }, out _).Should().BeFalse();
    }

    [TestCase(0)]
    [TestCase(42)]
    [TestCase(MixResourceMatcher.CheckAwakenItem)]
    public void ACodeWithoutAnEstablishedMeaningIsRefused(int code)
    {
        var rules = new[] { new RuleBuilder(1016, 0).Main((code, 1)).Build() };

        MixResourceMatcher.TryResolve(rules, Material(code: 700201), Array.Empty<MixMaterial>(), out _)
            .Should().BeFalse("a condition nobody established cannot validate a recipe");
    }

    private static bool Accepts(int code, int value, MixMaterial material)
    {
        var rules = new[] { new RuleBuilder(1016, 0).Main((MixResourceMatcher.CheckItemGroup, material.ItemGroup), (code, value)).Build() };
        return MixResourceMatcher.TryResolve(rules, material, Array.Empty<MixMaterial>(), out _);
    }

    [Test]
    public void TheElementalConditionsReadTheElementAsABit()
    {
        var fire = Material(group: 1) with { Instance = new MixInstance(0, 2, null, 0) };
        var none = Material(group: 1) with { Instance = new MixInstance(0, 0, null, 0) };

        Accepts(MixResourceMatcher.CheckElementalEffectMatch, 0b10, fire).Should().BeTrue("element 2 is bit 1");
        Accepts(MixResourceMatcher.CheckElementalEffectMatch, 0b01, fire).Should().BeFalse();
        Accepts(MixResourceMatcher.CheckElementalEffectMatch, 0, none).Should().BeTrue();
        Accepts(MixResourceMatcher.CheckElementalEffectMismatch, 0b01, fire).Should().BeTrue();
        Accepts(MixResourceMatcher.CheckElementalEffectMismatch, 0, none).Should().BeFalse();
    }

    [Test]
    public void TheEtherealGradeAndSocketConditionsReadTheirFields()
    {
        var mix = new ItemMixFields(1, 3, 1000, 0, 1_370_000, 7, 0, 0, 0, 0, 0);
        var spent = Material(group: 1) with { Mix = mix, Instance = new MixInstance(5, 0, new long[] { 4 }, 0) };
        var never = Material(group: 1) with { Mix = mix, Instance = new MixInstance(0, 0, null, 0) };

        Accepts(MixResourceMatcher.CheckItemEtherealDurabilityE, 5, spent).Should().BeTrue();
        Accepts(MixResourceMatcher.CheckItemEtherealDurabilityNe, 1_370_000, spent).Should().BeTrue();
        Accepts(MixResourceMatcher.CheckItemEtherealDurabilityE, 0, never).Should().BeTrue(
            "a stored 0 is an exhausted item since the wear and the backfill");
        Accepts(MixResourceMatcher.CheckItemGrade, 3, spent).Should().BeTrue();
        Accepts(MixResourceMatcher.CheckItemGrade, 2, spent).Should().BeFalse();
        Accepts(MixResourceMatcher.CheckFirstSocketCodeMatch, 4, spent).Should().BeTrue();
        Accepts(MixResourceMatcher.CheckMaxEtherealDurabilityNe, 0, spent).Should().BeTrue();
        Accepts(MixResourceMatcher.CheckItemType, 1, spent).Should().BeTrue();
    }

    [Test]
    public void ACountAtLeastConditionKeepsTheFrameCount()
    {
        var rule = new RuleBuilder(1154, 1).Sub(1, (MixResourceMatcher.CheckItemId, 7), (MixResourceMatcher.CheckItemCountGe, 5)).Build();

        MixResourceMatcher.TryResolve(new[] { rule }, null, new[] { Material(code: 7, count: 4) }, out _).Should().BeFalse();
        MixResourceMatcher.TryResolve(new[] { rule }, null, new[] { Material(code: 7, count: 8) }, out var resolution)
            .Should().BeTrue();
        resolution.ConsumedCounts.Should().Equal(8L);
    }
}
