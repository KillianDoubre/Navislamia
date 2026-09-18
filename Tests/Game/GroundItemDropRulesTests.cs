using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// The two guards the drop path applies before removing anything from the inventory: the bound summon
/// card refusal and the clamp of a request larger than the stack.
/// </summary>
[TestFixture]
public class GroundItemDropRulesTests
{
    private const ItemFlag FlagSummon = unchecked((ItemFlag)0x80000000u);

    [Test]
    public void IsBoundSummonCard_RefusesASummonCardCarryingTheSummonBit()
    {
        GroundItemDropRules.IsBoundSummonCard(FlagSummon, ItemGroup.Summoncard).Should().BeTrue();
    }

    [Test]
    public void IsBoundSummonCard_ReadsTheStoredFlagAsTheRetailBitsetNotAsTheEnumIndex()
    {
        // ItemFlag.Summon is the bit index 31, not NGemity's FlagBits::ITEM_FLAG_SUMMON = 0x80000000.
        // The stored flag is the retail bitset (the very value the client receives in the inventory
        // record), so the index member alone must not be mistaken for the mask.
        GroundItemDropRules.IsBoundSummonCard(ItemFlag.Summon, ItemGroup.Summoncard).Should().BeFalse();
        GroundItemDropRules.SummonFlagMask.Should().Be(0x80000000u);
    }

    [Test]
    public void IsBoundSummonCard_DoesNotTreatTheNoneSentinelAsEveryBit()
    {
        // ItemFlag.None is -1: read as a uint it aliases to 0xFFFFFFFF, so a plain bit test would refuse
        // every summon card whose flag was never set. The sentinel is excluded explicitly.
        GroundItemDropRules.IsBoundSummonCard(ItemFlag.None, ItemGroup.Summoncard).Should().BeFalse();
    }

    [Test]
    public void IsBoundSummonCard_IgnoresTheSummonBitOnAnotherGroup()
    {
        GroundItemDropRules.IsBoundSummonCard(ItemFlag.Summon, ItemGroup.Consumable).Should().BeFalse();
    }

    [Test]
    public void IsBoundSummonCard_IgnoresACardWithoutTheSummonBit()
    {
        GroundItemDropRules.IsBoundSummonCard(ItemFlag.Card, ItemGroup.Summoncard).Should().BeFalse();
        GroundItemDropRules.IsBoundSummonCard((ItemFlag)0x40000000, ItemGroup.Summoncard).Should().BeFalse();
    }

    [Test]
    public void IsBoundSummonCard_LeavesAnUnknownResourceUngated()
    {
        // A resource absent from the catalog cannot be judged: the drop is allowed rather than refused,
        // the same choice the use guard makes for an unknown use level.
        GroundItemDropRules.IsBoundSummonCard(ItemFlag.Summon, null).Should().BeFalse();
    }

    [Test]
    public void ResolveDropCount_TakesTheRequestedUnitsWhenTheStackIsLargeEnough()
    {
        GroundItemDropRules.ResolveDropCount(5, 10).Should().Be(5);
        GroundItemDropRules.ResolveDropCount(10, 10).Should().Be(10);
    }

    [Test]
    public void ResolveDropCount_ClampsToTheStack()
    {
        GroundItemDropRules.ResolveDropCount(11, 10).Should().Be(10);
    }

    [Test]
    public void ResolveDropCount_RefusesAnEmptyOrNegativeRequest()
    {
        GroundItemDropRules.ResolveDropCount(0, 10).Should().Be(0);
        GroundItemDropRules.ResolveDropCount(-3, 10).Should().Be(0);
    }

    [Test]
    public void ResolveDropCount_RefusesAnEmptyStack()
    {
        GroundItemDropRules.ResolveDropCount(4, 0).Should().Be(0);
    }
}
