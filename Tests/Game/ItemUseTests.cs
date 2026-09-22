using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// The level gate of an item use: the catalog reads <c>use_min_level</c> / <c>use_max_level</c> of
/// the item resource, the rules apply them the way NGemity's <c>Player::IsUseableItem</c> does.
/// </summary>
[TestFixture]
public class ItemUseTests
{
    private static ItemUseCatalog Catalog(params ItemUseFields[] fields)
    {
        var repository = A.Fake<IItemResourceRepository>();
        A.CallTo(() => repository.GetUseFields()).Returns(fields);
        return new ItemUseCatalog(repository);
    }

    [Test]
    public void Catalog_ExposesTheLevelsOfAKnownResource()
    {
        var catalog = Catalog(new ItemUseFields(240100, 10, 0, ItemBaseType.Supply),
            new ItemUseFields(240101, 0, 60, ItemBaseType.Supply));

        catalog.TryGetLevels(240100, out var unbound).Should().BeTrue();
        unbound.MinLevel.Should().Be(10);
        unbound.MaxLevel.Should().Be(0);

        catalog.TryGetLevels(240101, out var capped).Should().BeTrue();
        capped.MinLevel.Should().Be(0);
        capped.MaxLevel.Should().Be(60);
    }

    [Test]
    public void Catalog_LeavesAnUnknownResourceUngated()
    {
        var catalog = Catalog(new ItemUseFields(240100, 10, 0, ItemBaseType.Supply));

        catalog.TryGetLevels(999999, out _).Should().BeFalse();
    }

    [Test]
    public void Catalog_SparesOnlyTheReusableType()
    {
        var catalog = Catalog(new ItemUseFields(240100, 0, 0, ItemBaseType.Supply),
            new ItemUseFields(540017, 0, 0, ItemBaseType.Use));

        catalog.IsConsumedOnUse(240100).Should().BeTrue();
        catalog.IsConsumedOnUse(540017).Should().BeFalse();
    }

    [Test]
    public void Catalog_ConsumesAnUnknownResource()
    {
        Catalog().IsConsumedOnUse(999999).Should().BeTrue();
    }

    [Test]
    public void CheckUseLevel_AcceptsACharacterAtBothBounds()
    {
        ItemUseRules.CheckUseLevel(10, new ItemUseLevels(10, 60)).Should().Be(ResultCode.Success);
        ItemUseRules.CheckUseLevel(60, new ItemUseLevels(10, 60)).Should().Be(ResultCode.Success);
    }

    [Test]
    public void CheckUseLevel_RefusesACharacterBelowTheMinimum()
    {
        ItemUseRules.CheckUseLevel(9, new ItemUseLevels(10, 0)).Should().Be(ResultCode.LimitMin);
    }

    [Test]
    public void CheckUseLevel_RefusesACharacterAboveACeiling()
    {
        ItemUseRules.CheckUseLevel(61, new ItemUseLevels(0, 60)).Should().Be(ResultCode.LimitMax);
    }

    [Test]
    public void CheckUseLevel_TreatsAZeroCeilingOrMinimumAsUnbounded()
    {
        ItemUseRules.CheckUseLevel(1, new ItemUseLevels(0, 0)).Should().Be(ResultCode.Success);
        ItemUseRules.CheckUseLevel(200, new ItemUseLevels(0, 0)).Should().Be(ResultCode.Success);
    }

    [Test]
    public void CheckUseLevel_ReportsTheCeilingFirstWhenBothBoundsRefuse()
    {
        // NGemity tests the ceiling before the floor, so LimitMax wins on a contradictory template.
        ItemUseRules.CheckUseLevel(61, new ItemUseLevels(70, 60)).Should().Be(ResultCode.LimitMax);
    }
}
