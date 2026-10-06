using System;
using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Creatures;

namespace Tests.Game;

/// <summary>
/// The creature farm socle of docs/packet-specs/socle-ferme-creatures-officielle.md §5.6 points 1 to 3 and
/// 6: the 7.3 slots and constants, the card's farm bit, and the 6001 the storage feeds — its total size and
/// the position of every field of an entry (120 bytes, offsets of §3.2).
/// </summary>
[TestFixture]
public class CreatureFarmSocleTests
{
    private const int FarmInfoHeaderSize = 8;
    private const int EntrySize = 120;

    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0);

    private static ItemFixedInfo Card() => new(
        Handle: 77, Code: 710001, Uid: 77, Count: 1, EtherealDurability: 0, Endurance: 100, Enhance: 0,
        Level: 1, Flag: CreatureFarmRules.FarmedSummonMask, Sockets: new long[4], RemainTime: 0,
        ElementalEffectType: 0, ElementalEffectRemainTime: 0, ElementalEffectAttackPoint: 0,
        ElementalEffectMagicPoint: 0, AppearanceCode: 0);

    private static byte[] OneEntry(DateTime registrationTime, DateTime? nursingTime = null,
        int durationSeconds = 604800, bool isCash = false, bool isUsingCracker = false)
    {
        var info = CreatureFarmRules.SummonInfo(0, 1234567, "Kaiser", durationSeconds, registrationTime,
            nursingTime, isCash, isUsingCracker, Card(), Now);
        return GameFarmPackets.BuildFarmInfo(new[] { info });
    }

    // --- Sizes and offsets (criterion: total size and the position of every field) --------------------

    [Test]
    public void AFarmOfNEntriesIsEightBytesPlus120PerEntry()
    {
        GameFarmPackets.GetFarmInfoSize(0).Should().Be(8);
        GameFarmPackets.GetFarmInfoSize(1).Should().Be(128);
        GameFarmPackets.GetFarmInfoSize(3).Should().Be(368);
        GameFarmPackets.GetSummonEntryOffset(0).Should().Be(FarmInfoHeaderSize);
        GameFarmPackets.GetSummonEntryOffset(1).Should().Be(128);
        GameFarmPackets.GetSummonEntryOffset(2).Should().Be(248);
        GameFarmPackets.SummonEntrySize.Should().Be(EntrySize);
    }

    [Test]
    public void EveryFieldOfAnEntrySitsAtItsDocumentedOffset()
    {
        var packet = OneEntry(new DateTime(2026, 10, 3, 12, 0, 0));

        packet.Length.Should().Be(FarmInfoHeaderSize + EntrySize);
        packet[7].Should().Be(1, "creature_count");
        // Entry-relative offsets of §3.2, on top of the 8-byte frame header.
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(8)).Should().Be(0, "index = the farm slot");
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(12)).Should().Be(1234567, "exp");
        packet.AsSpan(20, 19).Slice(0, 6).ToArray().Should().Equal("Kaiser"u8.ToArray(), "name");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(39)).Should().Be(604800, "duration");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(43)).Should().Be(3 * 24 * 3600, "elasped_time");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(47)).Should().Be(0, "refresh_time, never nursed");
        packet[51].Should().Be(0, "using_cash");
        packet[52].Should().Be(0, "using_cracker");
        // card_info is the 75-byte motif of the card itself, at +45 of the entry; its own offsets are
        // the ones ItemFixedInfoWriter writes (handle +0, level +33), not a second frame header.
        var cardInfo = GameFarmPackets.GetSummonEntryOffset(0) + 45;
        packet[cardInfo].Should().Be(77, "card_info.handle");
        packet[cardInfo + 4].Should().Be(0x71, "card_info.code, first byte of 710001");
        packet[cardInfo + 33].Should().Be(1, "card_info.level");
    }

    [Test]
    public void ThreeDepositsFillThreeEntriesWithTheirSlots()
    {
        var entries = new[]
        {
            CreatureFarmRules.SummonInfo(0, 1, "A", 100, Now, null, false, false, Card(), Now),
            CreatureFarmRules.SummonInfo(1, 2, "B", 100, Now, null, true, true, Card(), Now),
            CreatureFarmRules.SummonInfo(2, 3, "C", 100, Now, null, true, false, Card(), Now),
        };

        var packet = GameFarmPackets.BuildFarmInfo(entries);

        packet.Length.Should().Be(368);
        packet[7].Should().Be(3);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(8)).Should().Be(0);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(128)).Should().Be(1);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(248)).Should().Be(2);
        // Per entry, cash and cracker are entry-relative +43 and +44 (not entry start + 8 + 43).
        packet[GameFarmPackets.GetSummonEntryOffset(1) + 43].Should().Be(1, "using_cash of the premium entry");
        packet[GameFarmPackets.GetSummonEntryOffset(1) + 44].Should().Be(1, "using_cracker of the second entry");
        packet[GameFarmPackets.GetSummonEntryOffset(2) + 43].Should().Be(1, "using_cash of the third entry");
        packet[GameFarmPackets.GetSummonEntryOffset(2) + 44].Should().Be(0, "using_cracker of the third entry");
    }

    // --- The 7.3 constants (§5.2, §6.2) ---------------------------------------------------------------

    [Test]
    public void TheConstantsAreTheMeasured73OnesNotThe2015Ones()
    {
        CreatureFarmRules.MaxCount.Should().Be(3);
        CreatureFarmRules.NonCashMaxCount.Should().Be(1);
        CreatureFarmRules.MaxLevel.Should().Be(100, "measured 7.3; the 2015 source says 150");
        CreatureFarmRules.NormalFormLevelCap.Should().Be(60);
        CreatureFarmRules.GrowthFormLevelCap.Should().Be(115);
        CreatureFarmRules.NormalExpPerHour.Should().Be(137700, "measured 7.3; the 2015 source says 145763");
        CreatureFarmRules.GrowthExpPerHour.Should().Be(347264, "measured 7.3; the 2015 source says 1118029");
        CreatureFarmRules.CrackerRate.Should().Be(1.5);
        CreatureFarmRules.NursingResetHour.Should().Be(6);
    }

    // --- Slots (§5.2) ---------------------------------------------------------------------------------

    [Test]
    public void SlotZeroIsForOrdinaryTicketsAndTheLastTwoForPremiumOnes()
    {
        CreatureFarmRules.TryFindSlot(false, Array.Empty<int>(), out var ordinary).Should().BeTrue();
        ordinary.Should().Be(0);

        CreatureFarmRules.TryFindSlot(true, Array.Empty<int>(), out var premium).Should().BeTrue();
        premium.Should().Be(1, "slot 0 is reserved for the ordinary tickets");

        CreatureFarmRules.TryFindSlot(false, new[] { 0 }, out _).Should().BeFalse("only slot 0 takes an ordinary ticket");
        CreatureFarmRules.TryFindSlot(true, new[] { 1 }, out var second).Should().BeTrue();
        second.Should().Be(2);
        CreatureFarmRules.TryFindSlot(true, new[] { 1, 2 }, out _).Should().BeFalse("three slots at most");
    }

    // --- The flag bit 27 (§5.6 point 2) ---------------------------------------------------------------

    [Test]
    public void TheFarmedBitIsBit27AndTheNursedOneBit28()
    {
        CreatureFarmRules.FarmedSummonMask.Should().Be(0x08000000u, "orl $0x8000000");
        CreatureFarmRules.NursedSummonMask.Should().Be(0x10000000u);

        CreatureFarmRules.IsFarmed(ItemFlag.None).Should().BeFalse();
        CreatureFarmRules.WithFarmedSummon(ItemFlag.None).Should().Be((ItemFlag)0x08000000);
        CreatureFarmRules.IsFarmed(CreatureFarmRules.WithFarmedSummon(ItemFlag.None)).Should().BeTrue();
        CreatureFarmRules.WithoutFarmedSummon(CreatureFarmRules.WithFarmedSummon(ItemFlag.None))
            .Should().Be(ItemFlag.None, "the retrieval clears the bit and leaves a flag of no bit at all");
    }

    [Test]
    public void TheFarmBitLeavesTheOtherBitsOfTheCardAlone()
    {
        var bound = unchecked((ItemFlag)0x80000000u); // ITEM_FLAG_SUMMON (bit 31) of a card already bound
        var farmed = CreatureFarmRules.WithFarmedSummon(bound);

        farmed.Should().Be(unchecked((ItemFlag)0x88000000u));
        CreatureFarmRules.IsFarmed(farmed).Should().BeTrue();
        CreatureFarmRules.WithoutFarmedSummon(farmed).Should().Be(bound);
    }

    // --- Experience and level limit (§5.3, §6.2) ------------------------------------------------------

    [Test]
    public void FarmedHoursAreWholeHoursCappedByTheTicketDuration()
    {
        var registered = new DateTime(2026, 10, 3, 12, 0, 0);

        CreatureFarmRules.FarmedHours(registered, 604800, registered).Should().Be(0);
        CreatureFarmRules.FarmedHours(registered, 604800, registered.AddHours(5).AddMinutes(59)).Should().Be(5);
        // Past the ticket the count stops: the farmed hours never exceed the duration.
        CreatureFarmRules.FarmedHours(registered, 3 * 24 * 3600, registered.AddDays(30)).Should().Be(72);
    }

    [Test]
    public void TheCrackerMultipliesTheFarmedExperienceAndTheLevelLimitIsCappedTwice()
    {
        CreatureFarmRules.GainedExp(10, CreatureFarmRules.NormalExpPerHour, false).Should().Be(1_377_000);
        CreatureFarmRules.GainedExp(10, CreatureFarmRules.NormalExpPerHour, true).Should().Be(2_065_500);
        CreatureFarmRules.GainedExp(10, CreatureFarmRules.GrowthExpPerHour, false).Should().Be(3_472_640);
        CreatureFarmRules.GainedExp(0, CreatureFarmRules.NormalExpPerHour, true).Should().Be(0);

        CreatureFarmRules.LevelLimit(150, CreatureFarmRules.NormalFormLevelCap).Should().Be(60);
        CreatureFarmRules.LevelLimit(150, CreatureFarmRules.GrowthFormLevelCap).Should().Be(100);
        CreatureFarmRules.LevelLimit(42, CreatureFarmRules.GrowthFormLevelCap).Should().Be(42);
    }

    // --- The 06:00 nursing reset (§5.4) ---------------------------------------------------------------

    [Test]
    public void TheNursingDayStartsAtSixAndAnEntryNursedSinceReadsZero()
    {
        CreatureFarmRules.LastNursingReset(new DateTime(2026, 10, 6, 5, 30, 0))
            .Should().Be(new DateTime(2026, 10, 5, 6, 0, 0), "before 06:00 the marker is yesterday's");
        CreatureFarmRules.LastNursingReset(new DateTime(2026, 10, 6, 12, 0, 0))
            .Should().Be(new DateTime(2026, 10, 6, 6, 0, 0));

        CreatureFarmRules.RefreshSeconds(null, Now).Should().Be(0, "never nursed");
        CreatureFarmRules.RefreshSeconds(new DateTime(2026, 10, 6, 9, 0, 0), Now)
            .Should().Be(0, "nursed after the last 06:00");
        CreatureFarmRules.RefreshSeconds(new DateTime(2026, 10, 6, 2, 0, 0), Now)
            .Should().Be(18 * 3600, "seconds until the next 06:00");
        CreatureFarmRules.RefreshSeconds(new DateTime(2026, 10, 5, 23, 0, 0), Now)
            .Should().Be(18 * 3600);
    }

    [Test]
    public void AnEntryCarriesTheRefreshTimeComputedFromItsNursingTime()
    {
        var registered = new DateTime(2026, 10, 3, 12, 0, 0);
        var packet = OneEntry(registered, new DateTime(2026, 10, 6, 2, 0, 0));

        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(47)).Should().Be(18 * 3600);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(43)).Should().Be(3 * 24 * 3600);
    }

    [Test]
    public void AnEmptyFarmStaysTheEightByteFrame()
    {
        var packet = GameFarmPackets.BuildFarmInfo(Array.Empty<GameFarmPackets.FarmSummonInfo>());

        packet.Length.Should().Be(8);
        packet[7].Should().Be(0);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4)).Should().Be(6001);
    }
}
