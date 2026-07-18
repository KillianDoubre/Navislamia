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

        public int? NextMin { get; private set; }

        public int? NextMax { get; private set; }

        public override double NextDouble() => _doubles.Dequeue();

        public override int Next(int minValue, int maxValue)
        {
            NextMin = minValue;
            NextMax = maxValue;
            return minValue;
        }
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
    public void Roll_RollsAStackWithAnInclusiveUpperBound()
    {
        var random = new SequenceRandom(0.0);

        DropRoll.Roll(new[] { new DropEntry(100, 1.0, 30, 50) }, random).Single().Count.Should().Be(30);
        random.NextMin.Should().Be(30);
        random.NextMax.Should().Be(51, "Random.Next excludes the upper bound, so 50 must stay reachable");
    }

    [Test]
    public void Roll_CanDropSeveralItemsFromOneMonster()
    {
        var entries = new[]
        {
            new DropEntry(100, 0.5, 1, 1),
            new DropEntry(200, 0.5, 1, 1)
        };

        DropRoll.Roll(entries, new SequenceRandom(0.1, 0.2)).Should().HaveCount(2);
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
    public void Roll_ResolvesANegativeEntryToAGroupItemByWeight()
    {
        var entries = new[] { new DropEntry(-700307, 1.0, 1, 1) };
        var groups = new Dictionary<int, DropGroupEntry[]>
        {
            [-700307] = new[]
            {
                new DropGroupEntry(700112, 0.075, 1, 1),
                new DropGroupEntry(700212, 0.125, 1, 1),
                new DropGroupEntry(700401, 0.80, 1, 1)
            }
        };

        // Slot fires (0.0), then the group pick lands in the last (80%) band (0.5 of total 1.0).
        DropRoll.Roll(entries, groups, new SequenceRandom(0.0, 0.5)).Single().ItemId.Should().Be(700401);
        // A pick low in the range lands on the first member.
        DropRoll.Roll(entries, groups, new SequenceRandom(0.0, 0.01)).Single().ItemId.Should().Be(700112);
    }

    [Test]
    public void Roll_FollowsNestedGroupReferences()
    {
        var entries = new[] { new DropEntry(-1, 1.0, 1, 1) };
        var groups = new Dictionary<int, DropGroupEntry[]>
        {
            [-1] = new[] { new DropGroupEntry(-2, 1.0, 1, 1) },
            [-2] = new[] { new DropGroupEntry(555, 1.0, 1, 1) }
        };

        DropRoll.Roll(entries, groups, new SequenceRandom(0.0, 0.5, 0.5)).Single().ItemId.Should().Be(555);
    }

    [Test]
    public void Roll_SamplesAGroupOncePerRolledCount()
    {
        // Slot fires with count 3, so the group is picked three times.
        var entries = new[] { new DropEntry(-9, 1.0, 3, 3) };
        var groups = new Dictionary<int, DropGroupEntry[]>
        {
            [-9] = new[] { new DropGroupEntry(700, 1.0, 1, 1) }
        };

        DropRoll.Roll(entries, groups, new SequenceRandom(0.0, 0.5, 0.5, 0.5)).Should().HaveCount(3);
    }

    [Test]
    public void Roll_DropsNothingWhenAReferencedGroupIsMissing()
    {
        var entries = new[] { new DropEntry(-404, 1.0, 1, 1) };

        DropRoll.Roll(entries, new Dictionary<int, DropGroupEntry[]>(), new SequenceRandom(0.0))
            .Should().BeEmpty();
    }

    [Test]
    public void TryResolveGroup_GivesUpOnACircularReference()
    {
        var groups = new Dictionary<int, DropGroupEntry[]>
        {
            [-1] = new[] { new DropGroupEntry(-1, 1.0, 1, 1) }
        };

        DropRoll.TryResolveGroup(-1, groups, new SequenceRandom(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5),
            out _).Should().BeFalse();
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
