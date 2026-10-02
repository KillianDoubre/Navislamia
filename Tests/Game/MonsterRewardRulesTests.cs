using System.Collections.Generic;
using FluentAssertions;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// The reward arithmetic of a monster's death, kept pure by <see cref="MonsterRewardRules"/> so it can be
/// checked without a world, a client or a database
/// (docs/packet-specs/socle-recompenses-monstres.md §7, §9.1).
/// </summary>
[TestFixture]
public class MonsterRewardRulesTests
{
    /// <summary>Hands out the scripted values in order, and remembers the bound it was asked for.</summary>
    private sealed class ScriptedRandom : ICombatRandom
    {
        private readonly Queue<int> _values;

        public ScriptedRandom(params int[] values) => _values = new Queue<int>(values);

        public int Bound { get; private set; }

        public int Next(int maxExclusive)
        {
            Bound = maxExclusive;
            return _values.Count > 0 ? _values.Dequeue() : maxExclusive - 1;
        }
    }

    [TestCase(0f, 0f, true)]
    [TestCase(499f, 0f, true)]
    [TestCase(500f, 0f, true)]
    [TestCase(501f, 0f, false)]
    [TestCase(0f, 501f, false)]
    public void ExperienceAndChaosReachFiveHundredUnitsAroundTheCorpse(float dx, float dy, bool expected) =>
        MonsterRewardRules.WithinRewardRange(dx, dy, 0, 0).Should().Be(expected);

    [TestCase(5, 5, 100, 100)]
    [TestCase(5, 4, 100, 100)]
    [TestCase(5, 10, 100, 75)]
    [TestCase(5, 12, 100, 64)]
    [TestCase(5, 25, 100, 0)]
    [TestCase(5, 40, 100, 0)]
    public void ExperienceLosesFivePercentPerLevelOfGap(int monsterLevel, int beneficiaryLevel, long amount,
        long expected) =>
        MonsterRewardRules.ScaleForLevelGap(amount, monsterLevel, beneficiaryLevel).Should().Be(expected);

    [Test]
    public void ExperienceTruncatesLikeTheReferenceAndNeverGoesNegative()
    {
        // 1 - 0.05 x 7 = 0.65 is not exact in binary: the product is 64.9999... and truncates to 64, which is
        // what the reference's float arithmetic does too.
        MonsterRewardRules.ScaleForLevelGap(100, 5, 12).Should().Be(64);
        MonsterRewardRules.ScaleForLevelGap(41, 5, 10).Should().Be(30, "41 x 0.75 = 30.75 truncates");
        MonsterRewardRules.ScaleForLevelGap(-5, 5, 1).Should().Be(0);
        MonsterRewardRules.ScaleForLevelGap(0, 5, 20).Should().Be(0);
    }

    [TestCase(0, 1.0)]
    [TestCase(10, 1.0)]
    [TestCase(11, 0.8)]
    [TestCase(14, 0.2)]
    [TestCase(15, 0.0)]
    [TestCase(25, 0.0)]
    public void LootLosesTwentyPercentPerLevelBeyondTen(int gap, double expected) =>
        MonsterRewardRules.LootFactor(5, 5 + gap).Should().BeApproximately(expected, 0.0001);

    [TestCase(100, 99, true)]
    [TestCase(100, 0, true)]
    [TestCase(50, 49, true)]
    [TestCase(50, 50, false)]
    [TestCase(0, 0, false)]
    [TestCase(-1, 0, false)]
    public void AChanceIsPerCentAgainstTheRoll(double chance, int roll, bool expected) =>
        MonsterRewardRules.PassesChance(chance, roll).Should().Be(expected);

    [Test]
    public void AChanceThatIsNotANumberNeverPasses() =>
        MonsterRewardRules.PassesChance(double.NaN, 0).Should().BeFalse();

    [Test]
    public void TheChanceRollDrawsFromTheHundredBound()
    {
        var pass = new ScriptedRandom(29);
        var fail = new ScriptedRandom(30);

        MonsterRewardRules.RollsChance(30, pass).Should().BeTrue();
        pass.Bound.Should().Be(100);
        MonsterRewardRules.RollsChance(30, fail).Should().BeFalse();
        MonsterRewardRules.RollsChance(30, null!).Should().BeFalse();
    }

    [Test]
    public void AnAmountRollsInsideItsBoundsInclusive()
    {
        var random = new ScriptedRandom();

        MonsterRewardRules.RollAmount(5, 9, random).Should().Be(9, "the source hands out the top of the span");
        random.Bound.Should().Be(5, "irand(5, 9) asks for 9 - 5 + 1 values");
        MonsterRewardRules.RollAmount(4, 4, random).Should().Be(4);
        random.Bound.Should().Be(1);
    }

    [Test]
    public void AnAmountToleratesReversedBoundsAndGivesNothingWithoutARange()
    {
        MonsterRewardRules.RollAmount(9, 5, new ScriptedRandom()).Should().Be(9);
        MonsterRewardRules.RollAmount(0, 0, new ScriptedRandom()).Should().Be(0);
        MonsterRewardRules.RollAmount(-6, -2, new ScriptedRandom()).Should().Be(0);
        MonsterRewardRules.RollAmount(3, 7, null!).Should().Be(0);
    }

    /// <summary>
    /// One row of the client's own export, to keep the arithmetic tied to real columns
    /// (reference/epic7part4/csv/MonsterResource.csv, id 7032030): level 32, exp 1905, jp 225, gold 80 % of
    /// [414, 690], chaos 10 % of [53, 88].
    /// </summary>
    [Test]
    public void ARealResourceRowRollsItsOwnColumns()
    {
        var random = new ScriptedRandom(79, 600, 9, 70, 0, 0);

        MonsterRewardRules.RollsChance(80 * 1.0 * 1.0, random).Should().BeTrue("79 is under 80 %");
        MonsterRewardRules.RollAmount(414, 690, random).Should().Be(414 + 600);
        MonsterRewardRules.RollsChance(10 * 1.0 * 1.0, random).Should().BeTrue("9 is under 10 %");
        MonsterRewardRules.RollAmount(53, 88, random).Should().Be(53 + 70);

        var outOfRange = new ScriptedRandom(80, 10);
        MonsterRewardRules.RollsChance(80 * 1.0 * 1.0, outOfRange).Should().BeFalse("80 % does not cover 80");
        MonsterRewardRules.RollsChance(10 * 1.0 * 1.0, outOfRange).Should().BeFalse("10 % does not cover 10");
    }
}
