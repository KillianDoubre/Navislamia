using FluentAssertions;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

[TestFixture]
public class ParameterBitsetTests
{
    [TestCase(0, StatTarget.Strength)]
    [TestCase(1, StatTarget.Vitality)]
    [TestCase(2, StatTarget.Agility)]
    [TestCase(3, StatTarget.Dexterity)]
    [TestCase(5, StatTarget.Wisdom)]
    [TestCase(6, StatTarget.Luck)]
    [TestCase(7, StatTarget.AttackPointRight)]
    [TestCase(16, StatTarget.Critical)]
    [TestCase(21, StatTarget.MaxHp)]
    [TestCase(22, StatTarget.MaxMp)]
    [TestCase(29, StatTarget.MaxWeight)]
    public void Resolve_MapsBitToTheTooltipBitsetEntry(int bit, StatTarget expected)
    {
        ParameterBitset.Resolve(bit).Should().Be(expected);
    }

    [TestCase(26)]
    [TestCase(30)]
    [TestCase(31)]
    [TestCase(-1)]
    [TestCase(99)]
    public void Resolve_ReturnsNoneOutsideTheEpic73Fields(int bit)
    {
        ParameterBitset.Resolve(bit).Should().Be(StatTarget.None);
    }

    [Test]
    public void Decode_AllStatsMaskIsTheSixStatsWithoutLuck()
    {
        ParameterBitset.Decode(63).Should().Equal(
            StatTarget.Strength, StatTarget.Vitality, StatTarget.Agility,
            StatTarget.Dexterity, StatTarget.Intelligence, StatTarget.Wisdom);
    }

    [Test]
    public void Decode_ResolvesTheValidatedCompositeMasks()
    {
        ParameterBitset.Decode(384).Should().Equal(StatTarget.AttackPointRight, StatTarget.MagicPoint);
        ParameterBitset.Decode(1536).Should().Equal(StatTarget.Defence, StatTarget.MagicDefence);
        ParameterBitset.Decode(49152).Should().Equal(StatTarget.AccuracyRight, StatTarget.MagicAccuracy);
        ParameterBitset.Decode(50331648).Should().Equal(StatTarget.HpRegenPoint, StatTarget.MpRegenPoint);
        ParameterBitset.Decode(402653184).Should().Equal(StatTarget.HpRegenPercentage, StatTarget.MpRegenPercentage);
        ParameterBitset.Decode(6144).Should().Equal(StatTarget.AttackSpeed, StatTarget.CastingSpeed);
        ParameterBitset.Decode(65536).Should().Equal(StatTarget.Critical);
    }

    [Test]
    public void Decode_SkipsBitsWithNoEpic73Field()
    {
        ParameterBitset.Decode(1073741824).Should().BeEmpty();
        ParameterBitset.Decode(0).Should().BeEmpty();
    }
}
