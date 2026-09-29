using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// The decisions of the commercial storage (item shop) container that hold without a database: who may see
/// a row, which rows become lines, the order and the cap of an emission, the two counters, the tailless
/// quantity and the bounds of a takeout. See docs/packet-specs/socle-stockage-commercial-conteneur.md
/// §5.1-§5.3 and §6.
/// </summary>
[TestFixture]
public class CommercialStorageRulesTests
{
    private const long Character = 41;
    private const long Account = 7;
    private const uint Uid = 0x48CE60u;

    private static PaidItemEntity Row(long id = 12, int code = 240100, int rest = 5, long? target = Character,
        long account = Account, int confirmed = 0, bool cancelled = false)
        => new()
        {
            Id = id,
            AccountId = account,
            CharacterId = target,
            ItemCode = code,
            ItemCount = rest,
            RestItemCount = rest,
            Confirmed = confirmed,
            IsCancel = cancelled
        };

    [Test]
    public void IsVisible_AcceptsTheTwoKindsOfOwnedRow()
    {
        CommercialStorageRules.IsVisible(Row(target: Character), Character, Account).Should().BeTrue();
        CommercialStorageRules.IsVisible(Row(target: null), Character, Account).Should().BeTrue(
            "an account-level delivery is takeable by any character of the account");
    }

    [Test]
    public void IsVisible_RefusesACancelledRow()
    {
        CommercialStorageRules.IsVisible(Row(cancelled: true), Character, Account).Should().BeFalse();
    }

    [Test]
    public void IsVisible_RefusesAnEmptyRow()
    {
        CommercialStorageRules.IsVisible(Row(rest: 0), Character, Account).Should().BeFalse(
            "nothing left to take: the row leaves the list, it is not deleted");
        CommercialStorageRules.IsVisible(Row(rest: -3), Character, Account).Should().BeFalse();
    }

    [Test]
    public void IsVisible_RefusesAnotherAccount()
    {
        CommercialStorageRules.IsVisible(Row(account: Account + 1), Character, Account).Should().BeFalse();
    }

    [Test]
    public void IsVisible_RefusesADeliveryAimedAtAnotherCharacterOfTheSameAccount()
    {
        CommercialStorageRules.IsVisible(Row(target: Character + 1), Character, Account).Should().BeFalse();
    }

    [Test]
    public void IsVisible_RefusesANullRow() => CommercialStorageRules.IsVisible(null, Character, Account).Should().BeFalse();

    [Test]
    public void IsAddressable_RefusesAnIdTheUidFieldCannotCarry()
    {
        CommercialStorageRules.IsAddressable(Row(id: 0)).Should().BeTrue();
        CommercialStorageRules.IsAddressable(Row(id: uint.MaxValue)).Should().BeTrue();
        CommercialStorageRules.IsAddressable(Row(id: (long)uint.MaxValue + 1)).Should().BeFalse(
            "truncating the identity key would name another row");
        CommercialStorageRules.IsAddressable(Row(id: -1)).Should().BeFalse();
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void TryToEntry_RefusesAnUnusableCode(int code)
    {
        CommercialStorageRules.TryToEntry(Row(code: code), out _).Should().BeFalse();
    }

    [Test]
    public void TryToEntry_TakesTheUidFromTheLineIdAndTheCountFromWhatIsLeft()
    {
        CommercialStorageRules.TryToEntry(Row(id: 0x48CE60, code: 4001, rest: 12), out var entry)
            .Should().BeTrue();

        entry.Uid.Should().Be(0x48CE60u);
        entry.Code.Should().Be(4001);
        entry.Count.Should().Be(12);
    }

    [TestCase(1, 1)]
    [TestCase(65535, 65535)]
    [TestCase(65536, 65535)]
    [TestCase(70000, 65535)]
    public void TryToEntry_AnnouncesTheRestClampedToTheUidField(int rest, int expected)
    {
        CommercialStorageRules.TryToEntry(Row(rest: rest), out var entry).Should().BeTrue();

        entry.Count.Should().Be((ushort)expected, "count is a uint16 on the wire");
    }

    [Test]
    public void BuildEntries_OrdersTheLinesOnTheLineId()
    {
        var entries = CommercialStorageRules.BuildEntries(new[] { Row(id: 30), Row(id: 10), Row(id: 20) });

        // Two emissions of the same content must be byte-identical.
        entries.Select(entry => entry.Uid).Should().Equal(new uint[] { 10u, 20u, 30u });
    }

    [Test]
    public void BuildEntries_LeavesTheRowsThatCannotBeNamedOrRenderedOut()
    {
        var entries = CommercialStorageRules.BuildEntries(new[]
        {
            Row(id: 1),
            Row(id: 2, code: 0),
            Row(id: (long)uint.MaxValue + 1),
            Row(id: 4)
        });

        entries.Select(entry => entry.Uid).Should().Equal(1u, 4u);
    }

    [Test]
    public void BuildEntries_LocksTheUidAgainstARowThatShadowsIt()
    {
        // §5.2.2: (uint)row.Id == uid would give two rows one identity. The row whose id is the uid plus
        // 2^32 is not addressable, so no line and no takeout can ever name it.
        var shadow = Row(id: (long)Uid + (1L << 32));
        CommercialStorageRules.IsAddressable(shadow).Should().BeFalse();

        var entries = CommercialStorageRules.BuildEntries(new[] { Row(id: Uid), shadow });

        entries.Should().ContainSingle().Which.Uid.Should().Be(Uid);
    }

    [Test]
    public void BuildEntries_CapsTheListAtTheUidFieldOfTheCount()
    {
        var rows = Enumerable.Range(1, CommercialStorageRules.MaxEntries + 5)
            .Select(index => Row(id: index))
            .ToArray();

        var entries = CommercialStorageRules.BuildEntries(rows);

        entries.Should().HaveCount(CommercialStorageRules.MaxEntries);
        entries[0].Uid.Should().Be(1u);
        entries[^1].Uid.Should().Be((uint)CommercialStorageRules.MaxEntries,
            "the first MaxEntries rows by id are emitted, the rest is left out");
    }

    [Test]
    public void BuildEntries_AcceptsAnEmptyOrMissingList()
    {
        CommercialStorageRules.BuildEntries(null).Should().BeEmpty();
        CommercialStorageRules.BuildEntries(Array.Empty<PaidItemEntity>()).Should().BeEmpty();
    }

    [Test]
    public void EmittedRows_IsTheOneSelectionTheLinesAndTheCountersShare()
    {
        var rows = new List<PaidItemEntity> { Row(id: 5), Row(id: 2, code: -1), Row(id: 9) };

        var emitted = CommercialStorageRules.EmittedRows(rows);

        emitted.Select(row => row.Id).Should().Equal(5L, 9L);
        CommercialStorageRules.BuildEntries(rows).Should().HaveCount(emitted.Length);
    }

    [Test]
    public void BuildCounters_CountsTheEmittedRowsAndTheUnconfirmedOnes()
    {
        var emitted = CommercialStorageRules.EmittedRows(new[]
        {
            Row(id: 1, confirmed: 0),
            Row(id: 2, confirmed: 1),
            Row(id: 3, confirmed: 0)
        });

        var counters = CommercialStorageRules.BuildCounters(emitted);

        counters.Total.Should().Be(3);
        counters.New.Should().Be(2, "new_item_count counts the lines whose confirmed is zero");
    }

    [Test]
    public void BuildCounters_KeepsTheTotalEqualToTheLineList()
    {
        // The two frames are built from one another: a total that disagrees with the count of the 10004
        // sent next is a window the client draws wrong.
        var rows = new[] { Row(id: 1), Row(id: 2, code: 0), Row(id: 3) };

        var emitted = CommercialStorageRules.EmittedRows(rows);
        var counters = CommercialStorageRules.BuildCounters(emitted);

        counters.Total.Should().Be((ushort)CommercialStorageRules.BuildEntries(rows).Length);
    }

    [Test]
    public void BuildCounters_AnswersZeroZeroOnAnEmptyContainer()
    {
        CommercialStorageRules.BuildCounters(null).Should().Be(((ushort)0, (ushort)0));
        CommercialStorageRules.BuildCounters(Array.Empty<PaidItemEntity>()).Should().Be(((ushort)0, (ushort)0));
    }

    [Test]
    public void BuildCounters_NeverOverflowsOnMoreRowsThanTheFieldCanHold()
    {
        var rows = Enumerable.Range(1, CommercialStorageRules.MaxEntries + 10)
            .Select(index => Row(id: index))
            .ToArray();

        var counters = CommercialStorageRules.BuildCounters(CommercialStorageRules.EmittedRows(rows));

        counters.Total.Should().Be((ushort)CommercialStorageRules.MaxEntries);
    }

    [TestCase(5, 5, true, 5)]
    [TestCase(5, 1, true, 5)]
    [TestCase(5, 6, false, 5)]
    [TestCase(5, 0, false, 5)]
    [TestCase(0, 1, false, 0)]
    [TestCase(-1, 1, false, 0)]
    [TestCase(70000, 65535, true, 65535)]
    public void TryTakeable_BoundsTheTakeoutOnWhatTheLineStillHolds(int rest, int count, bool expected,
        int takeable)
    {
        CommercialStorageRules.TryTakeable(Row(rest: rest), (ushort)count, out var announced).Should().Be(expected);

        announced.Should().Be((ushort)takeable);
    }

    [Test]
    public void TryTakeable_RefusesAMissingRow()
    {
        CommercialStorageRules.TryTakeable(null, 1, out var takeable).Should().BeFalse();
        takeable.Should().Be(0);
    }
}
