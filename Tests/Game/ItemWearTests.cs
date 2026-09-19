using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// The destination slot of an equipment-set handle (<c>TM_CS_PUTON_ITEM_SET</c>, 281): the request
/// carries no position, so the slot comes from the <c>wear_type</c> of the item resource, and only a
/// slot <c>TM_SC_WEAR_INFO</c> (202) can report is usable.
/// </summary>
[TestFixture]
public class ItemWearTests
{
    private static ItemWearCatalog Catalog(params ItemWearFields[] fields)
    {
        var repository = A.Fake<IItemResourceRepository>();
        A.CallTo(() => repository.GetWearFields()).Returns(fields);
        return new ItemWearCatalog(repository);
    }

    [Test]
    public void Catalog_ExposesTheWearTypeOfAKnownResource()
    {
        var catalog = Catalog(new ItemWearFields(100201, ItemWearType.Armor),
            new ItemWearFields(100101, ItemWearType.Weapon));

        catalog.TryGetWearType(100201, out var armor).Should().BeTrue();
        armor.Should().Be(ItemWearType.Armor);
        catalog.TryGetWearType(100101, out var weapon).Should().BeTrue();
        weapon.Should().Be(ItemWearType.Weapon);
    }

    [Test]
    public void Catalog_LeavesAnUnknownResourceUnknown()
    {
        var catalog = Catalog(new ItemWearFields(100201, ItemWearType.Armor));

        catalog.TryGetWearType(999999, out _).Should().BeFalse();
    }

    [Test]
    public void Catalog_ReportsANonWearableResourceAsItIs()
    {
        // The catalog stays a faithful reader of wear_type: refusing the value is the caller's rule.
        var catalog = Catalog(new ItemWearFields(700001, ItemWearType.CantWear));

        catalog.TryGetWearType(700001, out var wearType).Should().BeTrue();
        wearType.Should().Be(ItemWearType.CantWear);
    }

    [Test]
    public void IsWearableSlot_AcceptsTheTwentyFourWearSlotsOfTheWearInfo()
    {
        ItemWearRules.IsWearableSlot(ItemWearType.Weapon).Should().BeTrue();
        ItemWearRules.IsWearableSlot(ItemWearType.BagSlot).Should().BeTrue();
        ItemWearRules.IsWearableSlot(ItemWearType.RideItem).Should().BeTrue();
        ItemWearRules.IsWearableSlot(ItemWearType.SecondRing).Should().BeTrue();
    }

    [Test]
    public void IsWearableSlot_RefusesAnItemThatIsNotWorn()
    {
        ItemWearRules.IsWearableSlot(ItemWearType.None).Should().BeFalse();
        ItemWearRules.IsWearableSlot(ItemWearType.CantWear).Should().BeFalse();
    }

    [Test]
    public void IsWearableSlot_RefusesTheTypesOutsideTheWearInfo()
    {
        // 24 to 27 (the spare slots), 94, 99, 100 and 200 have no index in TM_SC_WEAR_INFO: writing
        // them would mark the item worn while hiding it from the client.
        ItemWearRules.IsWearableSlot(ItemWearType.SpareWeapon).Should().BeFalse();
        ItemWearRules.IsWearableSlot(ItemWearType.SpareShield).Should().BeFalse();
        ItemWearRules.IsWearableSlot(ItemWearType.SpareDecoWeapon).Should().BeFalse();
        ItemWearRules.IsWearableSlot(ItemWearType.SpareDecoShield).Should().BeFalse();
        ItemWearRules.IsWearableSlot(ItemWearType.TwofingerRing).Should().BeFalse();
        ItemWearRules.IsWearableSlot(ItemWearType.Twohand).Should().BeFalse();
        ItemWearRules.IsWearableSlot(ItemWearType.Skill).Should().BeFalse();
        ItemWearRules.IsWearableSlot(ItemWearType.SummonOnly).Should().BeFalse();
    }

    [Test]
    public void IsWearableSlot_JudgesThePositionOfPutonItemTheSameWay()
    {
        ItemWearRules.IsWearableSlot((sbyte)0).Should().BeTrue();
        ItemWearRules.IsWearableSlot((sbyte)23).Should().BeTrue();
        ItemWearRules.IsWearableSlot((sbyte)24).Should().BeFalse();
        ItemWearRules.IsWearableSlot((sbyte)-1).Should().BeFalse();
    }
}
