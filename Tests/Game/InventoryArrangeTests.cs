using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Services;

namespace Tests.Game;

[TestFixture]
public class InventoryArrangeTests
{
    private static ItemOrderKey Key(int category, int group, int rank, long resourceId, long itemId)
    {
        return new ItemOrderKey(InventoryArrange.BuildResourceKey(category, group, rank, resourceId), itemId);
    }

    [Test]
    public void BuildResourceKey_OrdersByTheClientCategoryFirst()
    {
        var equippable = InventoryArrange.BuildResourceKey(1, 140, 0, 999999);
        var consumable = InventoryArrange.BuildResourceKey(3, 0, 7, 1);
        var cards = InventoryArrange.BuildResourceKey(2, 0, 7, 1);
        var creature = InventoryArrange.BuildResourceKey(6, 0, 7, 1);
        var other = InventoryArrange.BuildResourceKey(0, 0, 7, 1);

        equippable.Should().BeLessThan(consumable);
        consumable.Should().BeLessThan(cards);
        cards.Should().BeLessThan(creature);
        creature.Should().BeLessThan(other);
    }

    [Test]
    public void CategoryOrder_FoldsEveryUnmappedCategoryIntoOther()
    {
        InventoryArrange.CategoryOrder(1).Should().Be(0);
        InventoryArrange.CategoryOrder(3).Should().Be(1);
        InventoryArrange.CategoryOrder(2).Should().Be(2);
        InventoryArrange.CategoryOrder(6).Should().Be(3);

        foreach (var other in new[] { 0, 4, 5, 7, 99 })
        {
            InventoryArrange.CategoryOrder(other).Should().Be(4);
        }
    }

    [Test]
    public void BuildResourceKey_OrdersByGroupThenRankDescendingThenResourceId()
    {
        InventoryArrange.BuildResourceKey(1, 1, 0, 999999)
            .Should().BeLessThan(InventoryArrange.BuildResourceKey(1, 2, 7, 1), "the group comes before the rank");

        InventoryArrange.BuildResourceKey(1, 2, 7, 999999)
            .Should().BeLessThan(InventoryArrange.BuildResourceKey(1, 2, 1, 1), "a higher rank sorts first");

        InventoryArrange.BuildResourceKey(1, 2, 3, 100)
            .Should().BeLessThan(InventoryArrange.BuildResourceKey(1, 2, 3, 200), "the resource id breaks ties");
    }

    [Test]
    public void BuildUnknownResourceKey_SortsAfterEveryKnownResource()
    {
        var unknown = InventoryArrange.BuildUnknownResourceKey(1);
        var known = InventoryArrange.BuildResourceKey(0, 140, 0, 700000886);

        unknown.Should().BeGreaterThan(known);
    }

    [Test]
    public void BuildResourceKey_ClampsOutOfRangeFieldsInsteadOfCorruptingNeighbours()
    {
        var clamped = InventoryArrange.BuildResourceKey(0, int.MaxValue, int.MaxValue, 5);
        var maximum = InventoryArrange.BuildResourceKey(0, 255, 255, 5);

        clamped.Should().Be(maximum);
    }

    [Test]
    public void Apply_SortsItemsAndRenumbersIdxContiguously()
    {
        var potion = new ItemEntity { Id = 10, Idx = 0 };
        var sword = new ItemEntity { Id = 11, Idx = 1 };
        var shield = new ItemEntity { Id = 12, Idx = 2 };
        var items = new[] { potion, sword, shield };
        var keys = new[]
        {
            Key(5, 0, 0, 500, potion.Id),
            Key(1, 0, 7, 100, sword.Id),
            Key(1, 0, 3, 200, shield.Id)
        };

        InventoryArrange.Apply(items, keys).Should().BeTrue();

        items.Should().Equal(sword, shield, potion);
        sword.Idx.Should().Be(1);
        shield.Idx.Should().Be(2);
        potion.Idx.Should().Be(3);
    }

    [Test]
    public void Apply_ReportsNoChangeWhenTheInventoryIsAlreadySorted()
    {
        var sword = new ItemEntity { Id = 11, Idx = 1 };
        var potion = new ItemEntity { Id = 10, Idx = 2 };
        var items = new[] { sword, potion };
        var keys = new[] { Key(1, 0, 7, 100, sword.Id), Key(5, 0, 0, 500, potion.Id) };

        InventoryArrange.Apply(items, keys).Should().BeFalse();
    }

    [Test]
    public void Apply_BreaksTiesOnItemIdSoRepeatedArrangesAreStable()
    {
        var second = new ItemEntity { Id = 20, Idx = 0 };
        var first = new ItemEntity { Id = 15, Idx = 1 };
        var items = new[] { second, first };
        var keys = new[] { Key(1, 0, 1, 100, second.Id), Key(1, 0, 1, 100, first.Id) };

        InventoryArrange.Apply(items, keys);

        items.Should().Equal(first, second);
    }

    [Test]
    public void EnsureContiguousIndices_NumbersALegacyBagWhereEveryIdxIsZero()
    {
        var first = new ItemEntity { Id = 1, Idx = 0 };
        var second = new ItemEntity { Id = 2, Idx = 0 };
        var third = new ItemEntity { Id = 3, Idx = 0 };

        InventoryArrange.EnsureContiguousIndices(new[] { first, second, third }).Should().BeTrue();

        first.Idx.Should().Be(1);
        second.Idx.Should().Be(2);
        third.Idx.Should().Be(3);
    }

    [Test]
    public void EnsureContiguousIndices_RenumbersAZeroBasedBagBecauseTheClientIndexStartsAtOne()
    {
        var first = new ItemEntity { Id = 1, Idx = 0 };
        var second = new ItemEntity { Id = 2, Idx = 1 };

        InventoryArrange.EnsureContiguousIndices(new[] { first, second }).Should().BeTrue();

        first.Idx.Should().Be(1);
        second.Idx.Should().Be(2);
    }

    [Test]
    public void EnsureContiguousIndices_KeepsAValidPermutationSoThePlayerArrangementSurvives()
    {
        var first = new ItemEntity { Id = 1, Idx = 3 };
        var second = new ItemEntity { Id = 2, Idx = 1 };
        var third = new ItemEntity { Id = 3, Idx = 2 };

        InventoryArrange.EnsureContiguousIndices(new[] { first, second, third }).Should().BeFalse();

        first.Idx.Should().Be(3);
        second.Idx.Should().Be(1);
        third.Idx.Should().Be(2);
    }

    [Test]
    public void EnsureContiguousIndices_RenumbersWhenIndicesLeaveHolesOrExceedTheBag()
    {
        var first = new ItemEntity { Id = 1, Idx = 7 };
        var second = new ItemEntity { Id = 2, Idx = -1 };

        InventoryArrange.EnsureContiguousIndices(new[] { first, second }).Should().BeTrue();

        first.Idx.Should().Be(1);
        second.Idx.Should().Be(2);
    }

    [Test]
    public void Apply_HandlesAnEmptyBag()
    {
        InventoryArrange.Apply(System.Array.Empty<ItemEntity>(), System.Array.Empty<ItemOrderKey>())
            .Should().BeFalse();
    }
}
