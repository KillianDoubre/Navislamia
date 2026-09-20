using System.Collections.Generic;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// The decisions of the storage path that hold without a database: the mode table, the clamp of an
/// over-large count, the destination slot, the account/character split of an item row and the row a
/// partial move creates. See docs/packet-specs/211-212-storage.md §5.3 and §6.
/// </summary>
[TestFixture]
public class StorageRulesTests
{
    private static ItemEntity Row(long? characterId = null, int? accountId = null, int? auctionId = null,
        int? storageId = null)
        => new()
        {
            Id = 12,
            ItemResourceId = 240100,
            Amount = 100,
            Idx = 3,
            CharacterId = characterId,
            AccountId = accountId,
            AuctionId = auctionId,
            StorageId = storageId
        };

    [TestCase(0, true)]
    [TestCase(1, true)]
    [TestCase(2, true)]
    [TestCase(3, true)]
    [TestCase(4, true)]
    [TestCase(5, false)]
    [TestCase(255, false)]
    public void IsKnownMode_AcceptsTheFiveModesOfTheReferenceOnly(byte mode, bool expected)
    {
        StorageRules.IsKnownMode(mode).Should().Be(expected);
    }

    [Test]
    public void ModeFamilies_AreSplitTheWayTheSessionSplitsThem()
    {
        StorageRules.IsItemMode(StorageRules.ItemToStorage).Should().BeTrue();
        StorageRules.IsItemMode(StorageRules.ItemToInventory).Should().BeTrue();
        StorageRules.IsItemMode(StorageRules.GoldToStorage).Should().BeFalse();
        StorageRules.IsItemMode(StorageRules.CloseMode).Should().BeFalse();

        StorageRules.IsGoldMode(StorageRules.GoldToStorage).Should().BeTrue();
        StorageRules.IsGoldMode(StorageRules.GoldToInventory).Should().BeTrue();
        StorageRules.IsGoldMode(StorageRules.ItemToStorage).Should().BeFalse();

        StorageRules.MovesToStorage(StorageRules.ItemToStorage).Should().BeTrue();
        StorageRules.MovesToStorage(StorageRules.GoldToStorage).Should().BeTrue();
        StorageRules.MovesToStorage(StorageRules.ItemToInventory).Should().BeFalse();
        StorageRules.MovesToStorage(StorageRules.GoldToInventory).Should().BeFalse();
    }

    [Test]
    public void MoveCount_ClampsARequestLargerThanTheStack()
    {
        // NGemity drops such a request without an answer (Player.cpp:2983-2987); the fiche keeps the clamp
        // so the item frames report the quantity really moved (§5.3).
        StorageRules.MoveCount(500, 100).Should().Be(100);
    }

    [Test]
    public void MoveCount_TakesTheRequestedUnitsWhenTheStackIsDeepEnough()
    {
        StorageRules.MoveCount(10, 100).Should().Be(10);
        StorageRules.MoveCount(100, 100).Should().Be(100);
    }

    [TestCase(0, 100)]
    [TestCase(-1, 100)]
    [TestCase(10, 0)]
    [TestCase(10, -1)]
    public void MoveCount_MovesNothingForANonPositiveRequestOrAnEmptyStack(long requested, long available)
    {
        StorageRules.MoveCount(requested, available).Should().Be(0);
    }

    [Test]
    public void NextFreeIndex_StartsAtZeroOnAnEmptyList()
    {
        StorageRules.NextFreeIndex(new List<int>()).Should().Be(0);
        StorageRules.NextFreeIndex(null).Should().Be(0);
    }

    [Test]
    public void NextFreeIndex_TakesTheLowestIndexNotUsed()
    {
        StorageRules.NextFreeIndex(new[] { 0, 1, 2 }).Should().Be(3);
        StorageRules.NextFreeIndex(new[] { 2, 0 }).Should().Be(1, "the gap is filled before the end is extended");
        StorageRules.NextFreeIndex(new[] { 5, 7 }).Should().Be(0);
    }

    [Test]
    public void IsInventoryRow_TellsTheCharactersStackFromAnotherOne()
    {
        StorageRules.IsInventoryRow(Row(characterId: 7), 7).Should().BeTrue();
        StorageRules.IsInventoryRow(Row(characterId: 7), 8).Should().BeFalse();

        // An inventory item carries no account column: the row of another character of the same account is
        // still not this character's item.
        StorageRules.IsInventoryRow(Row(characterId: null, accountId: 3), 7).Should().BeFalse();
    }

    [Test]
    public void IsStorageRow_TellsTheAccountsCounterFromAnotherOne()
    {
        StorageRules.IsStorageRow(Row(accountId: 3), 3).Should().BeTrue();
        StorageRules.IsStorageRow(Row(accountId: 3), 4).Should().BeFalse();
    }

    [Test]
    public void IsStorageRow_ExcludesAnAuctionOrKeepingRow()
    {
        StorageRules.IsStorageRow(Row(accountId: 3, auctionId: 9), 3).Should().BeFalse();
        StorageRules.IsStorageRow(Row(accountId: 3, storageId: 4), 3).Should().BeFalse();
        StorageRules.IsStorageRow(Row(characterId: 7, accountId: 3), 3).Should().BeFalse();
    }

    [Test]
    public void Own_PutsTheRowOnTheAskedSide()
    {
        var row = Row(characterId: 7);

        StorageRules.Own(row, true, 7, 3);

        row.CharacterId.Should().BeNull("the storage side is commanded by the account alone");
        row.AccountId.Should().Be(3);

        StorageRules.Own(row, false, 7, 3);

        row.CharacterId.Should().Be(7);
        row.AccountId.Should().BeNull("an inventory row carries no account column, like the character query");
    }

    [Test]
    public void Divide_CopiesTheItemDefiningColumnsAndNotTheLinksOfTheSourceRow()
    {
        var source = Row(characterId: 7);
        source.Level = 4;
        source.Enhance = 2;
        source.EtherealDurability = 30;
        source.Endurance = 55;
        source.Flag = ItemFlag.Card;
        source.WearInfo = ItemWearType.Weapon;
        source.SocketItemIds = new long[] { 11, 22 };
        source.RemainingTime = 600;
        source.ElementalEffectType = ElementalType.Fire;
        source.ElementalEffectAttackPoint = 7;
        source.ElementalEffectMagicPoint = 9;

        var divided = StorageRules.Divide(source, 40, 5);

        divided.ItemResourceId.Should().Be(source.ItemResourceId);
        divided.Level.Should().Be(4);
        divided.Enhance.Should().Be(2);
        divided.EtherealDurability.Should().Be(30);
        divided.Endurance.Should().Be(55);
        divided.Flag.Should().Be(ItemFlag.Card);
        divided.WearInfo.Should().Be(ItemWearType.Weapon);
        divided.RemainingTime.Should().Be(600);
        divided.ElementalEffectType.Should().Be(ElementalType.Fire);
        divided.ElementalEffectAttackPoint.Should().Be(7);
        divided.ElementalEffectMagicPoint.Should().Be(9);
        divided.SocketItemIds.Should().Equal(11L, 22L);

        divided.Id.Should().Be(0, "the divided row is a new row and takes its own identity");
        divided.Amount.Should().Be(40);
        divided.Idx.Should().Be(5, "the divided row takes the slot its destination list offered");
    }

    [Test]
    public void Divide_LeavesTheSourceItsOwnCountAndSockets()
    {
        var source = Row(characterId: 7);
        source.SocketItemIds = new long[] { 11, 22 };

        var divided = StorageRules.Divide(source, 40, 5);
        divided.SocketItemIds[0] = 99;

        source.Amount.Should().Be(100, "dividing does not touch the source");
        source.SocketItemIds[0].Should().Be(11, "the sockets are copied, not shared with the new row");
    }

    [Test]
    public void Divide_DoesNotCarryTheAuctionKeepingOrSummonLinksOfTheSource()
    {
        var source = Row(characterId: 7, auctionId: 9, storageId: 4);
        source.EquippedBySummonId = 6;

        var divided = StorageRules.Divide(source, 40, 5);

        divided.AuctionId.Should().BeNull();
        divided.StorageId.Should().BeNull();
        divided.EquippedBySummonId.Should().BeNull("a summoned pet's equipment does not follow a split");
    }
}
