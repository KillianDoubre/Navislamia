using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Navislamia.Configuration.Options;
using Navislamia.Game.Services;

namespace Tests.Game;

[TestFixture]
public class DropRollTests
{
    private sealed class SequenceRandom : Random
    {
        private readonly Queue<double> _doubles;

        public SequenceRandom(params double[] values) => _doubles = new Queue<double>(values);

        public override double NextDouble() => _doubles.Dequeue();

        public override int Next(int minValue, int maxValue) => minValue;
    }

    [Test]
    public void Roll_TreatsChanceAsAProbabilityBetweenZeroAndOne()
    {
        var entries = new[] { new DropEntry(100, 0.5, 1, 1) };

        DropRoll.Roll(entries, new SequenceRandom(0.49)).Should().ContainSingle();
        DropRoll.Roll(entries, new SequenceRandom(0.51)).Should().BeEmpty();
    }

    [Test]
    public void Roll_AlwaysDropsAGuaranteedEntry()
    {
        var entries = new[] { new DropEntry(100, 1.0, 1, 1) };

        DropRoll.Roll(entries, new SequenceRandom(0.999999)).Should().ContainSingle();
    }

    [Test]
    public void Roll_EvaluatesEverySlotIndependently()
    {
        var entries = new[]
        {
            new DropEntry(100, 0.5, 1, 1),
            new DropEntry(200, 0.5, 1, 1),
            new DropEntry(300, 0.5, 1, 1)
        };

        var dropped = DropRoll.Roll(entries, new SequenceRandom(0.1, 0.9, 0.2));

        dropped.Select(item => item.ItemId).Should().Equal(100, 300);
    }

    [Test]
    public void Roll_UsesTheMinimumCountWhenTheRangeIsFixed()
    {
        var entries = new[] { new DropEntry(100, 1.0, 3, 3) };

        DropRoll.Roll(entries, new SequenceRandom(0.0)).Single().Count.Should().Be(3);
    }

    [Test]
    public void Roll_ScalesChancesByTheMultiplierAndClampsAtCertainty()
    {
        var entries = new[] { new DropEntry(100, 0.02, 1, 1) };

        DropRoll.Roll(entries, new SequenceRandom(0.5)).Should().BeEmpty();
        DropRoll.Roll(entries, new SequenceRandom(0.5), 100).Should().ContainSingle();
        DropRoll.Roll(entries, new SequenceRandom(0.999999), 100).Should().ContainSingle();
    }

    [Test]
    public void Roll_ReturnsNothingForAnEmptyTable()
    {
        DropRoll.Roll(Array.Empty<DropEntry>(), new SequenceRandom()).Should().BeEmpty();
    }

    [Test]
    public void Catalog_ResolvesMonstersThroughTheirDropTableLink()
    {
        var options = new MonsterDropOptions
        {
            Tables = new Dictionary<int, List<MonsterDropEntryOptions>>
            {
                [106] = new() { new MonsterDropEntryOptions { ItemId = 900, Chance = 0.25, MinCount = 1, MaxCount = 2 } }
            },
            Monsters = new Dictionary<int, int> { [106] = 106, [107] = 106, [108] = 106 }
        };

        var catalog = new MonsterDropCatalog(options);

        catalog.MonsterCount.Should().Be(3);
        catalog.GetDrops(107).Should().ContainSingle(entry => entry.ItemId == 900);
        catalog.GetDrops(108).Should().BeEquivalentTo(catalog.GetDrops(106));
        catalog.GetDrops(999).Should().BeEmpty();
    }

    [Test]
    public void Catalog_DropsEntriesThatCanNeverFire()
    {
        var options = new MonsterDropOptions
        {
            Tables = new Dictionary<int, List<MonsterDropEntryOptions>>
            {
                [1] = new()
                {
                    new MonsterDropEntryOptions { ItemId = 900, Chance = 0, MinCount = 1, MaxCount = 1 },
                    new MonsterDropEntryOptions { ItemId = 0, Chance = 0.5, MinCount = 1, MaxCount = 1 }
                }
            },
            Monsters = new Dictionary<int, int> { [1] = 1 }
        };

        new MonsterDropCatalog(options).GetDrops(1).Should().BeEmpty();
    }
}
