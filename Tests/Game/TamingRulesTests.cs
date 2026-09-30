using System.Linq;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;

namespace Tests.Game;

/// <summary>
/// The pure half of the taming socle: the retail flag masks, the monster's own checks and the result code
/// each refusal was decided to carry. Every check has its NGemity source (<c>Skill::PrepareTaming</c>,
/// <c>Chihiro/src/Skills/Skill.cpp:1611-1638</c>), so none of them needs a database, a world or a session.
/// <para>
/// The refusal codes of the last fixture are <em>decisions</em>, not observations: étape 0 does not emit
/// 90-93 because whether the 7.3 client renders them is <c>NON ÉTABLI</c>
/// (docs/packet-specs/socle-apprivoisement-invocation.md §6, §7 point 3).
/// </para>
/// </summary>
[TestFixture]
public class TamingRulesTests
{
    private const int RequiredCard = 720201;

    /// <summary>An attempt that every check accepts, so a test bends exactly one of them.</summary>
    private static TamingAttempt Valid()
    {
        return new TamingAttempt(
            TargetIsLivingMonster: true,
            MonsterTamingId: RequiredCard,
            MonsterHasTamer: false,
            MonsterHp: 500,
            MonsterMaxHp: 500,
            HasFreeCard: true,
            TamerBusy: false);
    }

    [Test]
    public void FlagMasks_AreTheRetailBitsets()
    {
        // NGemity FlagBits::ITEM_FLAG_SUMMON (ItemTemplate.hpp:176) and ITEM_FLAG_TAMING (:174).
        TamingRules.SummonCardMask.Should().Be(0x80000000u);
        TamingRules.TamingCardMask.Should().Be(0x20000000u);
        TamingRules.SummonCardMask.Should().Be(GroundItemDropRules.SummonFlagMask,
            "the inventory record's flag field is the same bitset the drop path already reads");
    }

    [Test]
    public void FlagMasks_AreNotTheEnumMembers()
    {
        // The trap: ItemFlag holds bit *indices*. Writing the member instead of the mask matches nothing
        // the client displays, and neither rule must recognise it.
        ((int)ItemFlag.Summon).Should().Be(31);
        ((int)ItemFlag.Taming).Should().Be(29);

        TamingRules.IsBoundSummonCard((ItemFlag)ItemFlag.Summon).Should().BeFalse(
            "31 is an index, not the bound-card bit");
        TamingRules.IsTamingCard((ItemFlag)ItemFlag.Taming).Should().BeFalse(
            "29 is an index, not the in-progress bit");
    }

    [Test]
    public void IsBoundSummonCard_ReadsTheBoundBitAndRefusesTheSentinel()
    {
        TamingRules.IsBoundSummonCard(unchecked((ItemFlag)0x80000000u)).Should().BeTrue();
        TamingRules.IsBoundSummonCard(unchecked((ItemFlag)0xC0000000u)).Should().BeTrue(
            "a card can carry both bits at once");
        TamingRules.IsBoundSummonCard((ItemFlag)0x20000000u).Should().BeFalse();
        TamingRules.IsBoundSummonCard(ItemFlag.None).Should().BeFalse(
            "-1 is the \"no flag\" sentinel and aliases to every bit read as uint");
        TamingRules.IsBoundSummonCard(ItemFlag.Card).Should().BeFalse();
    }

    [Test]
    public void IsTamingCard_ReadsTheInProgressBit()
    {
        TamingRules.IsTamingCard((ItemFlag)0x20000000u).Should().BeTrue();
        TamingRules.IsTamingCard(unchecked((ItemFlag)0x80000000u)).Should().BeFalse();
        TamingRules.IsTamingCard(ItemFlag.None).Should().BeFalse();
    }

    [Test]
    public void IsBoundSummonCard_AgreesWithTheDropPathGuard()
    {
        // The two rules read the same field, so they must never disagree: a bound card is exactly what the
        // drop path refuses to remove.
        foreach (var flag in new[] { unchecked((ItemFlag)0x80000000u), (ItemFlag)0x20000000u, ItemFlag.None, ItemFlag.Card })
        {
            TamingRules.IsBoundSummonCard(flag).Should()
                .Be(GroundItemDropRules.IsBoundSummonCard(flag, ItemGroup.Summoncard));
        }
    }

    [Test]
    public void IsTamable_NeedsACardCode()
    {
        TamingRules.IsTamable(0).Should().BeFalse("taming_id 0 means the monster carries no taming card");
        TamingRules.IsTamable(RequiredCard).Should().BeTrue();
    }

    [Test]
    public void HasFullHp_OnlyAFullMonsterQualifies()
    {
        TamingRules.HasFullHp(500, 500).Should().BeTrue();
        TamingRules.HasFullHp(499, 500).Should().BeFalse("a wounded monster cannot be tamed at all");
        TamingRules.HasFullHp(501, 500).Should().BeFalse(
            "the reference compares equality (Skill.cpp:1627-1630), it does not ask for at least full");
    }

    [Test]
    public void Resolve_AcceptsAnAttemptEveryCheckAgreesWith() 
    {
        TamingRules.Resolve(Valid()).Should().Be(TamingRefusal.None);
    }

    [Test]
    public void Resolve_RefusesANonMonsterTarget()
    {
        TamingRules.Resolve(Valid() with { TargetIsLivingMonster = false })
            .Should().Be(TamingRefusal.NotActable);
    }

    [Test]
    public void Resolve_RefusesAMonsterWithNoTamingId()
    {
        TamingRules.Resolve(Valid() with { MonsterTamingId = 0 })
            .Should().Be(TamingRefusal.NotTamable, "taming_id == 0 means the monster cannot be tamed");
    }

    [Test]
    public void Resolve_RefusesAMonsterSomebodyIsAlreadyTaming()
    {
        TamingRules.Resolve(Valid() with { MonsterHasTamer = true })
            .Should().Be(TamingRefusal.TargetAlreadyBeingTamed);
    }

    [Test]
    public void Resolve_RefusesAWoundedMonster()
    {
        TamingRules.Resolve(Valid() with { MonsterHp = 499 })
            .Should().Be(TamingRefusal.NotEnoughTargetHp);
    }

    [Test]
    public void Resolve_RefusesWhenTheRequiredCardIsMissing()
    {
        TamingRules.Resolve(Valid() with { HasFreeCard = false })
            .Should().Be(TamingRefusal.NotEnoughSummonCard);
    }

    [Test]
    public void Resolve_RefusesWhenTheTamerIsAlreadyBusy()
    {
        TamingRules.Resolve(Valid() with { TamerBusy = true })
            .Should().Be(TamingRefusal.AlreadyTaming);
    }

    [Test]
    public void Resolve_JudgesAnUntamableMonsterBeforeItsTamer()
    {
        // The reference returns on the first check it fails (Skill.cpp:1621-1636): taming_id comes before
        // the tamer, so a monster that cannot be tamed is never reported as already tamed.
        TamingRules.Resolve(Valid() with { MonsterTamingId = 0, MonsterHasTamer = true })
            .Should().Be(TamingRefusal.NotTamable);
    }

    [Test]
    public void Resolve_JudgesTheHealthBeforeTheCard()
    {
        TamingRules.Resolve(Valid() with { MonsterHp = 1, HasFreeCard = false })
            .Should().Be(TamingRefusal.NotEnoughTargetHp, "the health check precedes the card lookup");
    }

    [Test]
    public void Resolve_JudgesTheCardBeforeTheTamerState()
    {
        TamingRules.Resolve(Valid() with { HasFreeCard = false, TamerBusy = true })
            .Should().Be(TamingRefusal.NotEnoughSummonCard, "the card lookup precedes the tamer check");
    }

    [Test]
    public void Resolve_JudgesANonMonsterTargetBeforeEverythingElse()
    {
        TamingRules.Resolve(new TamingAttempt(false, 0, true, 1, 500, false, true))
            .Should().Be(TamingRefusal.NotActable);
    }

    [Test]
    public void ResultCodeOf_CarriesTheDecidedCodes()
    {
        // ResultCode.cs:104-113 already holds all five: the socle only had to decide which check answers
        // which one (fiche §6). étape 0 emits none of them (class remarks).
        TamingRules.ResultCodeOf(TamingRefusal.None).Should().Be(ResultCode.Success);
        TamingRules.ResultCodeOf(TamingRefusal.NotActable).Should().Be(ResultCode.NotActable);
        TamingRules.ResultCodeOf(TamingRefusal.AlreadyTaming).Should().Be(ResultCode.AlreadyTaming);
        TamingRules.ResultCodeOf(TamingRefusal.NotTamable).Should().Be(ResultCode.NotTamable);
        TamingRules.ResultCodeOf(TamingRefusal.TargetAlreadyBeingTamed)
            .Should().Be(ResultCode.TargetAlreadyBeingTamed);
        TamingRules.ResultCodeOf(TamingRefusal.NotEnoughTargetHp).Should().Be(ResultCode.NotEnoughTargetHP);
        TamingRules.ResultCodeOf(TamingRefusal.NotEnoughSummonCard)
            .Should().Be(ResultCode.NotEnoughSummonCard);

        ((ushort)ResultCode.AlreadyTaming).Should().Be(70);
        ((ushort)ResultCode.NotTamable).Should().Be(90);
        ((ushort)ResultCode.TargetAlreadyBeingTamed).Should().Be(91);
        ((ushort)ResultCode.NotEnoughTargetHP).Should().Be(92);
        ((ushort)ResultCode.NotEnoughSummonCard).Should().Be(93);
    }
}

/// <summary>
/// The classification half: the three creature spells must be resolvable by <c>BuffCatalog</c> — otherwise
/// the cast path answers "unknown skill" — and the key must stay bounded to the ids the 7.3 client carries.
/// </summary>
[TestFixture]
public class TamingClassificationTests
{
    private const int SummonSkill = 4001;
    private const int UnsummonSkill = 4002;
    private const int TamingSkill = 4003;

    private static CastableSkillRow Row(int id, int effectType, int target = 1, int? stateId = null)
    {
        return new CastableSkillRow(id, effectType, false, target, stateId, 0,
            new decimal[20], 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    private static BuffCatalog Create(params CastableSkillRow[] rows)
    {
        var repository = A.Fake<ISkillResourceRepository>();
        A.CallTo(() => repository.GetCastableSkills()).Returns(rows);
        return new BuffCatalog(repository);
    }

    [Test]
    public void SkillEffectType_DeclaresTamingAt603()
    {
        ((int)SkillEffectType.Summon).Should().Be(601);
        ((int)SkillEffectType.Unsummon).Should().Be(602);
        ((int)SkillEffectType.Taming).Should().Be(603);
    }

    [Test]
    public void CastableEffectTypes_AskTheDatabaseForTheThreeCreatureSpells()
    {
        // The repository filters tf_skill_resource on this list, so a missing member means the row is never
        // loaded and the skill stays unknown however well the classification is written.
        BuffCatalog.CastableEffectTypes.Should().Contain(new[] { 601, 602, 603 });
    }

    [Test]
    public void Classify_MapsTheThreeCreatureSpellsToTheirKinds()
    {
        var catalog = Create(
            Row(SummonSkill, BuffCatalog.Summon),
            Row(UnsummonSkill, BuffCatalog.Unsummon),
            Row(TamingSkill, BuffCatalog.Taming));

        catalog.Count.Should().Be(3);
        catalog.TryGet(SummonSkill, out var summon).Should().BeTrue();
        summon.Kind.Should().Be(SkillCastKind.Summon);
        catalog.TryGet(UnsummonSkill, out var unsummon).Should().BeTrue();
        unsummon.Kind.Should().Be(SkillCastKind.Unsummon);
        catalog.TryGet(TamingSkill, out var taming).Should().BeTrue();
        taming.Kind.Should().Be(SkillCastKind.Taming);
        catalog.CountOf(SkillCastKind.Taming).Should().Be(1);
    }

    [Test]
    public void Classify_NeedsNoStateForACreatureSpell()
    {
        // A creature spell carries its effect itself: 4003 creates a tamer on the monster, it is not a
        // state. Requiring a state_id would silently drop all three.
        var catalog = Create(Row(TamingSkill, BuffCatalog.Taming, stateId: null));

        catalog.TryGet(TamingSkill, out var taming).Should().BeTrue();
        taming.StateId.Should().Be(0);
    }

    [Test]
    public void Classify_KeepsTheKeyBoundedToTheDecidedIds()
    {
        // Our key is the effect type, which is wider than the reference's per-skill entry: 603 alone would
        // also cover 4004, a second taming skill the 7.3 client does not carry (fiche §6). The id bounds it.
        var catalog = Create(Row(4004, BuffCatalog.Taming));

        catalog.Count.Should().Be(0);
        catalog.TryGet(4004, out _).Should().BeFalse();
    }

    [Test]
    public void Classify_RefusesACreatureSpellIdCarryingAnotherEffect()
    {
        var catalog = Create(Row(TamingSkill, BuffCatalog.AddState));

        catalog.Count.Should().Be(0, "4003 is the taming skill, not a state carrier");
        catalog.TryGet(TamingSkill, out _).Should().BeFalse();
    }
}

/// <summary>
/// Étape 0.5: the monster's taming columns must be reachable without a database round trip once the world
/// is loaded, which is what <see cref="MonsterInstance"/> now carries. The draw itself is étape 1.
/// </summary>
[TestFixture]
public class MonsterInstanceTamingColumnsTests
{
    private const int TamableMonster = 2101;
    private const int UntamableMonster = 2102;

    private static MonsterInstance[] Build()
    {
        var spawns = new[]
        {
            new MonsterSpawnPoint { MonsterId = TamableMonster, X = 100, Y = 200, Count = 1 },
            new MonsterSpawnPoint { MonsterId = UntamableMonster, X = 100, Y = 200, Count = 1 }
        };

        var resources = new[]
        {
            new MonsterResourceEntity { Id = TamableMonster, Level = 5, Hp = 900, Race = 1, TamingId = 720201, TamingPercentage = 12.5m },
            new MonsterResourceEntity { Id = UntamableMonster, Level = 5, Hp = 900, Race = 1, TamingId = 0 }
        };

        return MonsterInstanceFactory.Build(spawns, resources).ToArray();
    }

    [Test]
    public void Build_CarriesTheTamingColumnsOfTheResource()
    {
        var instances = Build();

        instances.Should().HaveCount(2);
        var tamable = instances.Single(instance => instance.MonsterId == TamableMonster);
        tamable.TamingId.Should().Be(720201);
        tamable.TamingPercentage.Should().Be(12.5m);

        var untamable = instances.Single(instance => instance.MonsterId == UntamableMonster);
        untamable.TamingId.Should().Be(0, "the column is copied as it stands, zero included");
        untamable.TamingPercentage.Should().Be(0m);
    }

    [Test]
    public void Build_FeedsTheTamableCheck()
    {
        var instances = Build();

        TamingRules.IsTamable(instances.Single(instance => instance.MonsterId == TamableMonster).TamingId)
            .Should().BeTrue();
        TamingRules.IsTamable(instances.Single(instance => instance.MonsterId == UntamableMonster).TamingId)
            .Should().BeFalse();
    }
}
