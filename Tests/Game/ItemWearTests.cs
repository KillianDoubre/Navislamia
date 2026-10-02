using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// The port of an item resource: the destination slot of an equipment-set handle
/// (<c>TM_CS_PUTON_ITEM_SET</c>, 281 — the request carries no position, so the slot comes from the
/// <c>wear_type</c> of the item resource, and only a slot <c>TM_SC_WEAR_INFO</c> (202) can report is
/// usable) and the level requirements the equip path reads out of the same row.
/// </summary>
[TestFixture]
public class ItemWearTests
{
    private static ItemWearFields Wear(int id, ItemWearType wearType, int rank = 0, int useMinLevel = 0,
        int useMaxLevel = 0)
    {
        return new ItemWearFields(id, wearType, rank, useMinLevel, useMaxLevel,
            ItemRaceRestriction.Deva | ItemRaceRestriction.Asura | ItemRaceRestriction.Gaia,
            ItemJobRestriction.Fighter | ItemJobRestriction.Hunter | ItemJobRestriction.Magician | ItemJobRestriction.Summoner, 15);
    }

    private static ItemWearCatalog Catalog(params ItemWearFields[] fields)
    {
        var repository = A.Fake<IItemResourceRepository>();
        A.CallTo(() => repository.GetWearFields()).Returns(fields);
        return new ItemWearCatalog(repository);
    }

    [Test]
    public void Catalog_ExposesTheWearTypeOfAKnownResource()
    {
        var catalog = Catalog(Wear(100201, ItemWearType.Armor), Wear(100101, ItemWearType.Weapon));

        catalog.TryGetWearFields(100201, out var armor).Should().BeTrue();
        armor.WearType.Should().Be(ItemWearType.Armor);
        catalog.TryGetWearFields(100101, out var weapon).Should().BeTrue();
        weapon.WearType.Should().Be(ItemWearType.Weapon);
    }

    [Test]
    public void Catalog_ExposesTheLevelRequirementsOfTheSameResource()
    {
        // The equip path reads rank, use_min_level and use_max_level from the row it already has to
        // read for the slot: no second query per equip (sheet §5.6-2).
        var catalog = Catalog(Wear(100201, ItemWearType.Armor, rank: 7, useMinLevel: 160, useMaxLevel: 300),
            Wear(100101, ItemWearType.Weapon));

        catalog.TryGetWearFields(100201, out var armor).Should().BeTrue();
        armor.Rank.Should().Be(7);
        armor.UseMinLevel.Should().Be(160);
        armor.UseMaxLevel.Should().Be(300);

        catalog.TryGetWearFields(100101, out var weapon).Should().BeTrue();
        weapon.Rank.Should().Be(0);
        weapon.UseMinLevel.Should().Be(0);
        weapon.UseMaxLevel.Should().Be(0);
    }

    [Test]
    public void Catalog_LeavesAnUnknownResourceUnknown()
    {
        var catalog = Catalog(Wear(100201, ItemWearType.Armor));

        catalog.TryGetWearFields(999999, out _).Should().BeFalse();
    }

    [Test]
    public void Catalog_ReportsANonWearableResourceAsItIs()
    {
        // The catalog stays a faithful reader of wear_type: refusing the value is the caller's rule.
        var catalog = Catalog(Wear(700001, ItemWearType.CantWear));

        catalog.TryGetWearFields(700001, out var fields).Should().BeTrue();
        fields.WearType.Should().Be(ItemWearType.CantWear);
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

    [Test]
    public void TryResolveSlot_KeepsAWearTypeThatIsAlreadyASlot()
    {
        ItemWearRules.TryResolveSlot(ItemWearType.Armor, out var armor).Should().BeTrue();
        armor.Should().Be(ItemWearType.Armor);

        ItemWearRules.TryResolveSlot(ItemWearType.SecondRing, out var secondRing).Should().BeTrue();
        secondRing.Should().Be(ItemWearType.SecondRing);
    }

    [Test]
    public void TryResolveSlot_FoldsTheTwoTypesNgemityFoldsOntoASlot()
    {
        // Player.cpp:1754-1758: WEAR_TWOHAND becomes the weapon slot, WEAR_TWOFINGER_RING the first
        // ring slot.
        ItemWearRules.TryResolveSlot(ItemWearType.Twohand, out var twohand).Should().BeTrue();
        twohand.Should().Be(ItemWearType.Weapon);

        ItemWearRules.TryResolveSlot(ItemWearType.TwofingerRing, out var twofinger).Should().BeTrue();
        twofinger.Should().Be(ItemWearType.Ring);
    }

    [Test]
    public void TryResolveSlot_RefusesTheWearTypesWithoutASinglePort()
    {
        // 24..27 are the spare slots NGemity accepts without storing them, 100 and 200 belong to
        // skills and summons: none of them has one port, so the caller must not guess one.
        ItemWearRules.TryResolveSlot(ItemWearType.SpareWeapon, out _).Should().BeFalse();
        ItemWearRules.TryResolveSlot(ItemWearType.SpareDecoShield, out _).Should().BeFalse();
        ItemWearRules.TryResolveSlot(ItemWearType.Skill, out _).Should().BeFalse();
        ItemWearRules.TryResolveSlot(ItemWearType.SummonOnly, out _).Should().BeFalse();
        ItemWearRules.TryResolveSlot(ItemWearType.None, out _).Should().BeFalse();
        ItemWearRules.TryResolveSlot(ItemWearType.CantWear, out _).Should().BeFalse();
    }
}
