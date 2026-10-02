using System;
using System.Collections.Generic;
using System.Linq;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

[TestFixture]
public class ElementalResistanceTests
{
    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
    [TestCase(4)] [TestCase(5)] [TestCase(6)]
    public void Item_and_state_masks_address_the_same_element_without_changing_other_stats(int element)
    {
        var item = new ItemEffectFields(1, ItemType.OnehandSword, new short[] { 97 },
            new decimal[] { 1 << element }, new decimal[] { 60 }, null, null, null);
        var effects = ItemStatCatalog.BuildEffects(item);
        var values = new decimal[18];
        values[6] = 1 << element; values[7] = 10; values[8] = 20;
        var state = StateCatalog.BuildTemplates(new StateEffectFields(1, 1, values));
        var catalog = A.Fake<IStatCatalog>();
        var total = new StatCalculator(catalog).Compute(new StatCalculatorInput(0, null, 1, effects,
            BuffEffects: state.Select(t => t.Resolve(2)).ToArray())).Total;
        for (var e = 0; e < 7; e++) total.GetResistance(e).Should().Be(e == element ? 110 : 0);
        total.Strength.Should().Be(0, "ParameterB's first bit is resistance, not strength");
        var copy = total.Copy(); copy.Add((StatTarget)((int)StatTarget.NoneResistance + element), 1);
        total.GetResistance(element).Should().Be(110, "summon copies must own their resistances");
    }

    [Test]
    public void Passive_10006_scales_four_element_triplets_and_rejects_invalid_indices()
    {
        var templates = SkillPassiveCatalog.BuildTemplates(new SkillPassiveFields(99, 10006,
            new decimal[] { 1, 10, 5, 2, 20, 10, 6, 0, 15, 99, 100, 50 }, 0, true));
        templates.Select(t => t.Resolve(3)).Should().Equal(
            new StatEffect(StatTarget.FireResistance, 25, false),
            new StatEffect(StatTarget.WaterResistance, 50, false),
            new StatEffect(StatTarget.DarkResistance, 45, false));
    }

    [Test]
    public void Resistance_amplifiers_from_items_and_states_add_after_all_flat_sources()
    {
        var item = new ItemEffectFields(1, ItemType.OnehandSword, new short[] { 99, 97 },
            new decimal[] { 2, 2 }, new decimal[] { .5m, 60 }, null, null, null);
        var values = new decimal[18]; values[9] = 2; values[10] = .5m;
        var amp = StateCatalog.BuildTemplates(new StateEffectFields(1, 2, values)).Select(t => t.Resolve(1)).ToArray();
        var total = new StatCalculator(A.Fake<IStatCatalog>()).Compute(new StatCalculatorInput(0, null, 1,
            ItemStatCatalog.BuildEffects(item), new[] { new StatEffect(StatTarget.FireResistance, 40, false) }, amp)).Total;
        total.FireResistance.Should().Be(200, "100 flat points amplified by .5 + .5");
    }

    [TestCase(0, 600)] [TestCase(30, 540)] [TestCase(150, 300)]
    [TestCase(300, 0)] [TestCase(450, 0)] [TestCase(-150, 900)]
    public void Resistance_uses_fractional_division_and_never_turns_damage_into_healing(float resistance, int expected)
        => CombatFormulas.ResistedDamage(600, resistance).Should().Be(expected);

    private sealed class Dice(params int[] rolls) : ICombatRandom
    {
        private readonly Queue<int> _rolls = new(rolls);
        public int Next(int maxExclusive) => _rolls.Dequeue();
        public int Remaining => _rolls.Count;
    }

    [TestCase(DamageKind.Physical)] [TestCase(DamageKind.Magical)]
    public void Resistance_follows_critical_and_changes_only_the_matching_element(DamageKind kind)
    {
        var attacker = Combatant.From(new StatBlock { Critical = 100, CriticalPower = 80 }, 1);
        var target = Combatant.From(new StatBlock { FireResistance = 150 }, 1);
        var normal = CombatFormulas.Resolve(attacker, target, 600, kind, 0, 0, new Dice(0, 5000), 2);
        var dice = new Dice(0, 5000);
        var fire = CombatFormulas.Resolve(attacker, target, 600, kind, 0, 0, dice, 1);
        fire.Damage.Should().Be(normal.Damage / 2);
        fire.Flags.Should().Be(HitFlags.Critical);
        dice.Remaining.Should().Be(0, "resistance rolls no additional dice");
    }

    [Test]
    public void A_miss_and_a_perfect_block_remain_zero_even_with_negative_resistance()
    {
        var attacker = Combatant.From(new StatBlock(), 1);
        var target = Combatant.From(new StatBlock { FireResistance = -300, Avoid = 100 }, 1);
        var miss = CombatFormulas.Resolve(attacker, target, 600, DamageKind.Physical, 0, 0, new Dice(99), 1);
        miss.Should().Be(new HitResult(0, HitFlags.Miss));
        target = Combatant.From(new StatBlock { FireResistance = -300, BlockChance = 100, PerfectBlock = 100 }, 1);
        var block = CombatFormulas.Resolve(attacker, target, 600, DamageKind.Physical, 0, 0, new Dice(0, 0), 1);
        block.Should().Be(new HitResult(0, HitFlags.PerfectBlock));
    }
}
