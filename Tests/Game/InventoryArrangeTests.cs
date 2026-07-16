using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Services;

namespace Tests.Game;

[TestFixture]
public class InventoryArrangeTests
{
    private static ItemOrderKey Key(int group, int itemType, int rank, long resourceId, long itemId)
    {
        return new ItemOrderKey(InventoryArrange.BuildResourceKey(group, itemType, rank, resourceId), itemId);
    }

    [Test]
    public void BuildResourceKey_OrdersByGroupThenTypeThenRankDescendingThenResourceId()
    {
        var lowGroup = InventoryArrange.BuildResourceKey(1, 9, 0, 999999);
        var highGroup = InventoryArrange.BuildResourceKey(2, 0, 7, 1);
        lowGroup.Should().BeLessThan(highGroup, "the group is the primary key");

        var lowType = InventoryArrange.BuildResourceKey(2, 1, 0, 999999);
        var highType = InventoryArrange.BuildResourceKey(2, 2, 7, 1);
        lowType.Should().BeLessThan(highType, "the type outranks the rank");

        var betterRank = InventoryArrange.BuildResourceKey(2, 2, 7, 999999);
        var worseRank = InventoryArrange.BuildResourceKey(2, 2, 1, 1);
        betterRank.Should().BeLessThan(worseRank, "a higher rank sorts first");

        var lowId = InventoryArrange.BuildResourceKey(2, 2, 3, 100);
        var highId = InventoryArrange.BuildResourceKey(2, 2, 3, 200);
        lowId.Should().BeLessThan(highId, "the resource id breaks ties");
    }

    [Test]
    public void BuildUnknownResourceKey_SortsAfterEveryKnownResource()
    {
        var unknown = InventoryArrange.BuildUnknownResourceKey(1);
        var known = InventoryArrange.BuildResourceKey(140, 7, 0, 700000886);

        unknown.Should().BeGreaterThan(known);
    }

    [Test]
    public void BuildResourceKey_ClampsOutOfRangeFieldsInsteadOfCorruptingNeighbours()
    {
        var clamped = InventoryArrange.BuildResourceKey(int.MaxValue, int.MaxValue, int.MaxValue, 5);
        var maximum = InventoryArrange.BuildResourceKey(255, 255, 255, 5);

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
        sword.Idx.Should().Be(0);
        shield.Idx.Should().Be(1);
        potion.Idx.Should().Be(2);
    }

    [Test]
    public void Apply_ReportsNoChangeWhenTheInventoryIsAlreadySorted()
    {
        var sword = new ItemEntity { Id = 11, Idx = 0 };
        var potion = new ItemEntity { Id = 10, Idx = 1 };
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

        first.Idx.Should().Be(0);
        second.Idx.Should().Be(1);
        third.Idx.Should().Be(2);
    }

    [Test]
    public void EnsureContiguousIndices_KeepsAValidPermutationSoThePlayerArrangementSurvives()
    {
        var first = new ItemEntity { Id = 1, Idx = 2 };
        var second = new ItemEntity { Id = 2, Idx = 0 };
        var third = new ItemEntity { Id = 3, Idx = 1 };

        InventoryArrange.EnsureContiguousIndices(new[] { first, second, third }).Should().BeFalse();

        first.Idx.Should().Be(2);
        second.Idx.Should().Be(0);
        third.Idx.Should().Be(1);
    }

    [Test]
    public void EnsureContiguousIndices_RenumbersWhenIndicesLeaveHolesOrExceedTheBag()
    {
        var first = new ItemEntity { Id = 1, Idx = 7 };
        var second = new ItemEntity { Id = 2, Idx = -1 };

        InventoryArrange.EnsureContiguousIndices(new[] { first, second }).Should().BeTrue();

        first.Idx.Should().Be(0);
        second.Idx.Should().Be(1);
    }

    [Test]
    public void Apply_HandlesAnEmptyBag()
    {
        InventoryArrange.Apply(System.Array.Empty<ItemEntity>(), System.Array.Empty<ItemOrderKey>())
            .Should().BeFalse();
    }
}
