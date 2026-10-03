using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// The mix types ported from the official <c>MixManager</c> (docs/packet-specs/socle-artisanat-objets-officiel.md):
/// creature cards, item level, recycling, appearance, elements, sockets and ethereal durability.
/// </summary>
[TestFixture]
public class CraftingOfficialMixTests
{
    private const uint Main = 100;
    private const uint SubA = 200;
    private const uint SubB = 300;
    private const uint SubC = 400;

    private static MixMaterial Material(uint handle, int code = 1, int group = 0, long enhance = 0, long level = 1,
        long count = 1, ItemMixFields mix = null, MixInstance instance = null, int itemClass = 0) =>
        new(code, group, itemClass, 0, 0, level, enhance, 0, count, handle, 0, count, mix, instance);

    private static MixResolution Rule(int type, int[] values, params MixMaterial[] arranged)
    {
        var rule = new MixResourceEntity
        {
            Id = 1, MixType = type, MixValue01 = At(values, 0), MixValue02 = At(values, 1), MixValue03 = At(values, 2),
            MixValue04 = At(values, 3), MixValue05 = At(values, 4), MixValue06 = At(values, 5)
        };
        return new MixResolution(rule, arranged.Select(m => m.Count).ToArray(), arranged);
    }

    private static int At(int[] values, int index) => index < values.Length ? values[index] : 0;

    private static ItemEntity Apply(CraftPlan plan, uint handle, ItemEntity entity)
    {
        foreach (var mutation in plan.Mutations.Where(m => m.Handle == handle))
        {
            mutation.Apply(entity);
        }

        return entity;
    }

    /// <summary>Dice: <paramref name="value"/> for every draw, clamped into its range.</summary>
    private static Func<int, int, int> Always(int value) => (min, max) => Math.Clamp(value, min, max);

    private static ItemMixFields Fields(long price = 0, int maxEthereal = 0, decimal opt1 = 0, decimal opt2 = 0,
        decimal opt3 = 0, decimal opt4 = 0, int endurance = 0) =>
        new(0, 0, price, endurance, maxEthereal, 7, 0, opt1, opt2, opt3, opt4);

    // ---- creature cards ------------------------------------------------------------------------------------

    private static EnhanceResourceEntity CardEnhance(int failResult = 1) => new()
    {
        Id = 2000, MaxEnhance = 10, FailResult = (FailResultType)failResult,
        Percentage = Enumerable.Repeat(0.5m, 25).ToArray()
    };

    private static MixMaterial CardOf(uint handle, long enhance, int level = 10, bool formed = false, int rate = 2) =>
        Material(handle, code: 540014, group: 13, enhance: enhance, mix: Fields(maxEthereal: 1_000_000),
            instance: new MixInstance(400_000, 0, null, 0, 2101, rate, level, formed));

    [Test]
    public void A_creature_card_success_raises_it_and_refills_its_ethereal_durability()
    {
        var plan = CraftingEngine.Plan(Rule(104, new[] { 2000, 0, 0, 0, 0, 0 }, CardOf(SubA, 2), Material(SubB, 950000)),
            CardOf(Main, 2), CardEnhance(), Always(0));

        plan.Refusal.Should().Be(ResultCode.Success);
        plan.Consumed.Select(c => c.ItemHandle).Should().Equal(SubA, SubB);
        var card = Apply(plan, Main, new ItemEntity { Enhance = 2, EtherealDurability = 400_000 });
        card.Enhance.Should().Be(3);
        card.EtherealDurability.Should().Be(1_000_000);
        plan.CardEnhance.Should().Be(new CraftCardEnhance(Main, 3, true));
        plan.ResultHandles.Should().Equal(Main);
    }

    [Test]
    public void A_creature_card_chance_adds_both_summon_levels()
    {
        // 0.5 × 100 000 + (10 + 30) × 125 = 55 000: a draw of 54 999 succeeds, one of 55 001 fails.
        var resolution = Rule(104, new[] { 2000 }, CardOf(SubA, 2, level: 30), Material(SubB, 950000));
        CraftingEngine.Plan(resolution, CardOf(Main, 2), CardEnhance(), Always(54_999)).CardEnhance!.Value.Succeeded
            .Should().BeTrue();
        CraftingEngine.Plan(resolution, CardOf(Main, 2), CardEnhance(), (min, max) => max == 100_000 ? 55_001 : min)
            .CardEnhance!.Value.Succeeded.Should().BeFalse();
    }

    [Test]
    public void A_formed_card_a_joker_or_two_enhancements_are_refused()
    {
        var mixer = Material(SubB, 950000);
        CraftingEngine.Plan(Rule(104, new[] { 2000 }, CardOf(SubA, 2), mixer), CardOf(Main, 2, formed: true),
            CardEnhance(), Always(0)).Refusal.Should().Be(ResultCode.InvalidArgument);
        CraftingEngine.Plan(Rule(104, new[] { 2000 }, CardOf(SubA, 3), mixer), CardOf(Main, 2),
            CardEnhance(), Always(0)).Refusal.Should().Be(ResultCode.InvalidArgument);
        CraftingEngine.Plan(Rule(104, new[] { 2000 }, CardOf(SubA, 2), mixer),
            CardOf(Main, 2) with { ItemCode = CraftingEngine.JokerCardCode }, CardEnhance(), Always(0))
            .Refusal.Should().Be(ResultCode.InvalidArgument);
    }

    [Test]
    public void A_failed_plus_three_card_of_rate_one_may_give_the_consolation_item()
    {
        var plan = CraftingEngine.Plan(
            Rule(104, new[] { 2000, 0, 0, 540070, 0, 100 }, CardOf(SubA, 3), Material(SubB, 950000)),
            CardOf(Main, 3), CardEnhance(failResult: 3), (min, max) => max == 100_000 ? max : min);

        plan.Created.Should().ContainSingle(c => c.ItemCode == 540070);
        Apply(plan, Main, new ItemEntity { Enhance = 3 }).Enhance.Should().Be(0, "fail_result 3 at +3");
        plan.CardEnhance.Should().Be(new CraftCardEnhance(Main, 0, false));
    }

    // ---- item level ----------------------------------------------------------------------------------------

    [Test]
    public void Set_level_takes_the_value_and_keeps_what_it_leaves_at_zero()
    {
        var plan = CraftingEngine.Plan(Rule(201, new[] { 10 }, Material(SubA, 5)), Material(Main, enhance: 4, level: 2),
            null, Always(0));

        var item = Apply(plan, Main, new ItemEntity { Enhance = 4, Level = 2 });
        item.Level.Should().Be(10);
        item.Enhance.Should().Be(4, "an enhance of 0 in the value keeps the item's");
        plan.ResultHandles.Should().Equal(Main);
    }

    [Test]
    public void Set_level_create_item_makes_the_item_at_its_level_or_fails()
    {
        var made = CraftingEngine.Plan(Rule(202, new[] { 1, 0, 0, 700107, 205, 100 }, Material(SubA, 5)), null, null,
            Always(0));
        made.Created.Should().Equal(new CraftCreation(700107, 1, 5) { Enhance = 2 });
        made.ResultHandles.Should().Equal(0u);

        var missed = CraftingEngine.Plan(Rule(202, new[] { 1, 0, 0, 700107, 0, 30 }, Material(SubA, 5)), null, null,
            (min, max) => max == 100 ? 31 : min);
        missed.Created.Should().BeEmpty();
        missed.ResultHandles.Should().BeEmpty();
        missed.Consumed.Should().ContainSingle("the materials go even when nothing is made");
    }

    [Test]
    public void Set_level_from_a_sub_material_copies_its_level_and_enhancement()
    {
        var plan = CraftingEngine.Plan(Rule(214, new[] { 1, 30, 1 }, Material(SubA, 7, enhance: 6, level: 9)),
            Material(Main, enhance: 1, level: 1), null, Always(0));

        var item = Apply(plan, Main, new ItemEntity { Enhance = 1, Level = 1 });
        item.Enhance.Should().Be(6);
        item.Level.Should().Be(9);
        ((uint)item.Flag & (1u << 30)).Should().NotBe(0u);
    }

    // ---- recycling and appearance --------------------------------------------------------------------------

    [Test]
    public void Recycling_turns_an_enhanced_equipment_into_materials_and_spends_the_cube_either_way()
    {
        var equipment = Material(SubA, 101100, group: 1, enhance: 5);
        var cube = Material(SubB, 800003, group: 22);
        var plan = CraftingEngine.Plan(Rule(402, new[] { 700102, 1, 5000, 5000, 100 }, cube, equipment), null, null,
            Always(1));

        plan.Consumed.Select(c => (c.ItemHandle, c.Count)).Should().BeEquivalentTo(new[] { (SubB, 1L), (SubA, 1L) });
        plan.Created.Should().Equal(new[] { new CraftCreation(700102, 3, 1) }, "((5 × 5000 / 1000) + 5) / 10 = 3");
        plan.ReportCreated.Should().BeTrue();

        var failed = CraftingEngine.Plan(Rule(402, new[] { 700102, 1, 5000, 5000, 0 }, equipment, cube), null, null,
            (min, max) => max);
        failed.Created.Should().BeEmpty();
        failed.Consumed.Should().HaveCount(2, "the equipment is lost too");
    }

    [Test]
    public void The_appearance_comes_from_the_first_material()
    {
        var plan = CraftingEngine.Plan(Rule(603, Array.Empty<int>(), Material(SubA, 102233)), Material(Main, 101100),
            null, Always(0));
        Apply(plan, Main, new ItemEntity()).AppearanceCode.Should().Be(102233);
        plan.Consumed.Should().ContainSingle(c => c.ItemHandle == SubA);
    }

    // ---- elements ------------------------------------------------------------------------------------------

    [Test]
    public void An_effector_sets_or_clears_the_element()
    {
        var fire = CraftingEngine.Plan(Rule(701, Array.Empty<int>(), Material(SubA, 9, mix: Fields(opt1: 2, opt2: 3600))),
            Material(Main), null, Always(0));
        var item = Apply(fire, Main, new ItemEntity());
        item.ElementalEffectType.Should().Be((ElementalType)2);
        item.ElementalEffectExpireTime.Should().BeCloseTo(DateTime.UtcNow.AddHours(1), TimeSpan.FromMinutes(1));

        var clear = CraftingEngine.Plan(Rule(701, Array.Empty<int>(), Material(SubA, 9, mix: Fields())), Material(Main),
            null, Always(0));
        var cleared = Apply(clear, Main, new ItemEntity { ElementalEffectType = (ElementalType)2, ElementalEffectAttackPoint = 5 });
        cleared.ElementalEffectType.Should().Be(ElementalType.None);
        cleared.ElementalEffectAttackPoint.Should().Be(0);
    }

    [Test]
    public void An_enhancer_draws_the_attack_and_magic_points()
    {
        var plan = CraftingEngine.Plan(Rule(702, Array.Empty<int>(), Material(SubA, 9, mix: Fields(opt1: 10, opt2: 20))),
            Material(Main), null, (min, max) => max);
        var item = Apply(plan, Main, new ItemEntity { ElementalEffectMagicPoint = 7 });
        item.ElementalEffectAttackPoint.Should().Be(20);
        item.ElementalEffectMagicPoint.Should().Be(7, "a range of two zeros leaves the value");
    }

    // ---- sockets -------------------------------------------------------------------------------------------

    [Test]
    public void A_socket_takes_its_new_code_on_a_success_and_its_failure_code_otherwise()
    {
        var belt = Material(Main, 250, instance: new MixInstance(0, 0, new long[] { 5, 0, 0, 0 }, 0));
        var success = CraftingEngine.Plan(Rule(703, new[] { 5, 4, 10, 5, 0, 491003 }, Material(SubA, 1)), belt, null,
            Always(0));
        Apply(success, Main, new ItemEntity { SocketItemIds = new long[] { 5, 0, 0, 0 } }).SocketItemIds[0].Should().Be(4);
        success.Created.Should().ContainSingle(c => c.ItemCode == 491003);
        success.ResultHandles.Should().Equal(Main);

        var failure = CraftingEngine.Plan(Rule(703, new[] { 5, 4, 10, 3 }, Material(SubA, 1)), belt, null, Always(99));
        Apply(failure, Main, new ItemEntity { SocketItemIds = new long[] { 5, 0, 0, 0 } }).SocketItemIds[0].Should().Be(3);
        failure.ResultHandles.Should().BeEmpty();
    }

    [Test]
    public void Filling_an_empty_socket_adds_the_stone_endurance_when_asked()
    {
        var context = new MixContext(0, code => code == 7001 ? Fields(endurance: 50_000) : ItemMixFields.Empty);
        var plan = CraftingEngine.Plan(Rule(703, new[] { 0, 7001, 100, 0, 1 }, Material(SubA, 7001)),
            Material(Main, instance: new MixInstance(0, 0, null, 0)), null, Always(0), context: context);

        var item = Apply(plan, Main, new ItemEntity { Endurance = 10 });
        item.SocketItemIds.Should().Equal(7001, 0, 0, 0);
        item.Endurance.Should().Be(50_010);
    }

    [Test]
    public void The_sockets_move_from_the_donor_which_is_erased_or_emptied()
    {
        var donor = Material(SubA, 2, instance: new MixInstance(0, 0, new long[] { 9, 8, 0, 0 }, 0));
        var erase = CraftingEngine.Plan(Rule(704, new[] { 1, 100, 1, 2 }, donor, Material(SubB, 3)), Material(Main),
            null, Always(0));
        Apply(erase, Main, new ItemEntity()).SocketItemIds.Should().Equal(9, 8, 0, 0);
        erase.Consumed.Select(c => c.ItemHandle).Should().BeEquivalentTo(new[] { SubA, SubB });

        var reset = CraftingEngine.Plan(Rule(704, new[] { 1, 0, 1, 2 }, donor, Material(SubB, 3)), Material(Main),
            null, Always(99));
        Apply(reset, SubA, new ItemEntity { SocketItemIds = new long[] { 9, 8, 0, 0 } }).SocketItemIds
            .Should().Equal(0, 0, 0, 0);
        reset.Consumed.Select(c => c.ItemHandle).Should().Equal(SubB);
        reset.Mutations.Should().ContainSingle();
    }

    // ---- ethereal durability -------------------------------------------------------------------------------

    private static MixMaterial Worn(int current, int max = 1_370_000) =>
        Material(Main, 101100, group: 1, mix: Fields(maxEthereal: max), instance: new MixInstance(current, 0, null, 0));

    [Test]
    public void A_sacrifice_refills_the_item_from_its_price_and_the_first_material_is_only_spent()
    {
        var plan = CraftingEngine.Plan(
            Rule(801, new[] { 100, 0, 0, 100 }, Material(SubA, 1), Material(SubB, 2, mix: Fields(price: 300_000), count: 2)),
            Worn(500_000), null, Always(0));

        Apply(plan, Main, new ItemEntity()).EtherealDurability.Should().Be(1_100_000, "500 000 + 300 000 × 2");
        plan.ChatLines.Should().ContainSingle().Which.Should().Be("@7900\v#@Ethereal_Durability@#\v60");
        plan.Consumed.Select(c => c.ItemHandle).Should().Equal(SubA, SubB);
        plan.ResultHandles.Should().Equal(Main);
    }

    [Test]
    public void A_protected_sacrifice_stays_and_a_full_item_stops_the_refill()
    {
        var plan = CraftingEngine.Plan(
            Rule(804, new[] { 100, 0, 0b10, 100 }, Material(SubA, 1), Material(SubB, 2, mix: Fields(price: 5_000_000)),
                Material(SubC, 3, mix: Fields(price: 5_000_000))),
            Worn(1_000_000), null, Always(0));

        Apply(plan, Main, new ItemEntity()).EtherealDurability.Should().Be(1_370_000);
        plan.Consumed.Select(c => c.ItemHandle).Should().Equal(new[] { SubA }, "the second is protected, the loop stops before the third");
        plan.ChatLines.Should().BeEmpty("804 is the silent one");
    }

    [Test]
    public void The_ethereal_stone_takes_the_sacrifices_and_answers_without_a_257()
    {
        var plan = CraftingEngine.Plan(Rule(805, new[] { 100, 0, 0, 100 }, Material(SubA, 1, mix: Fields(price: 20_000))),
            null, null, Always(0), context: new MixContext(5_000, _ => ItemMixFields.Empty));

        plan.EtherealStoneDelta.Should().Be(20_000);
        plan.NoResult.Should().BeTrue();
        plan.ChatLines.Should().ContainSingle();
    }

    [Test]
    public void The_ethereal_stone_refills_an_item_by_whole_points()
    {
        // used = (1 370 000 − 500 000) / 10 000 = 87; the stone holds 30 points, 29 can leave.
        var plan = CraftingEngine.Plan(Rule(806, new[] { 100 }, Array.Empty<MixMaterial>()), Worn(500_000), null,
            Always(0), context: new MixContext(300_000, _ => ItemMixFields.Empty));

        plan.EtherealStoneDelta.Should().Be(-290_000);
        Apply(plan, Main, new ItemEntity()).EtherealDurability.Should().Be(790_000);
        plan.ResultHandles.Should().Equal(Main);
    }

    [Test]
    public void An_exhausted_item_recovers_a_share_of_its_maximum()
    {
        var plan = CraftingEngine.Plan(Rule(803, new[] { 10000, 0, 45 }, Material(SubA, 1)), Worn(1), null, Always(0));

        Apply(plan, Main, new ItemEntity()).EtherealDurability.Should().Be(626_501, "1 + 1 370 000 × 45 % + 10 000");
        plan.Consumed.Should().ContainSingle();

        CraftingEngine.Plan(Rule(803, new[] { 10000, 0, 45 }, Material(SubA, 1)), Worn(1), null, Always(99))
            .Mutations.Should().BeEmpty();
    }

    // ---- the commit ----------------------------------------------------------------------------------------

    [Test]
    public async Task A_mix_applies_mutations_copies_creations_and_the_stone_in_one_save()
    {
        var target = new ItemEntity { Id = 100, ItemResourceId = 1, Amount = 1, Level = 1, Enhance = 7, WearInfo = ItemWearType.None };
        var cube = new ItemEntity { Id = 200, ItemResourceId = 2, Amount = 3, Level = 1, WearInfo = ItemWearType.None };
        var character = new CharacterEntity
        {
            CharacterName = "Crafter", EtherealStoneDurability = 1000, Items = new List<ItemEntity> { target, cube }
        };
        var repository = A.Fake<ICharacterRepository>();
        A.CallTo(() => repository.GetCharacterByNameWithItemsAsync("Crafter")).Returns(character);
        var factory = A.Fake<ICharacterRepositoryFactory>();
        A.CallTo(() => factory.Create()).Returns(repository);
        var service = new CharacterService(A.Fake<IStarterItemsRepository>(), factory, new CharacterGate(),
            A.Fake<ILogger<CharacterService>>());
        var expected = new MixMaterial(1, 0, 0, 0, 0, 1, 7, 0, 1, 100);
        var plan = new CraftPlan(ResultCode.Success, new[] { new CraftConsumption(200, 1) }, null, Array.Empty<uint>())
        {
            Mutations = new[] { new CraftItemMutation(100, expected, item => item.AppearanceCode = 42) },
            Created = new[] { new CraftCreation(1, 1, 1) { CopyOf = 100, Enhance = 4 }, new CraftCreation(9, 5, 2) },
            EtherealStoneDelta = 500
        };

        var commit = await service.ApplyMixAsync("Crafter", plan);

        commit.Outcome.Should().Be(CraftCommitOutcome.Success);
        target.AppearanceCode.Should().Be(42);
        commit.Mutated.Should().ContainSingle().Which.Should().BeSameAs(target);
        commit.Created.Should().HaveCount(2);
        commit.Created.Should().Contain(item => item.ItemResourceId == 1 && item.Enhance == 4 && item.Amount == 1);
        commit.Created.Should().Contain(item => item.ItemResourceId == 9 && item.Amount == 5 && item.Level == 2);
        commit.EtherealStone.Should().Be(1500);
        character.EtherealStoneDurability.Should().Be(1500);
        cube.Amount.Should().Be(2);
        A.CallTo(() => repository.SaveChangesAsync()).MustHaveHappenedOnceExactly();

        target.Enhance = 8;
        (await service.ApplyMixAsync("Crafter", plan)).Outcome.Should().Be(CraftCommitOutcome.TargetChanged,
            "the item changed since the plan saw it");
    }
}
