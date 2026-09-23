using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// The auction family skeleton: the shared 75-byte item motif and the three server to client frames,
/// Epic 7.3 branch. Every response always carries its forty slots, filled or not, because the 7.3 client
/// copies the table in one block without looking at auction_info_count. The client reads appearance_code
/// inside the motif although rzu gates the field to EPIC_7_4, which is what makes the responses 5139 and
/// 3899 bytes instead of 4979 and 3739.
/// See docs/packet-specs/socle-encheres.md.
/// </summary>
[TestFixture]
public class AuctionPacketsTests
{
    private const int HeaderSize = 7;
    private const int TableOffset = 19;

    private static ItemFixedInfo SampleItem => new(
        Handle: 0x80000111u,
        Code: 240100,
        Uid: 0x1122334455667788L,
        Count: 7,
        EtherealDurability: 12,
        Endurance: 34,
        Enhance: 5,
        Level: 6,
        Flag: 0x80000000u,
        Sockets: new long[] { 101, 102, 103, 104 },
        RemainTime: 7200,
        ElementalEffectType: 3,
        ElementalEffectRemainTime: 45,
        ElementalEffectAttackPoint: 56,
        ElementalEffectMagicPoint: 67,
        AppearanceCode: 89);

    private static AuctionInfo SampleAuction(int uid = 4321) => new(
        AuctionUid: uid,
        Item: SampleItem,
        DurationType: 2,
        BiddedPrice: 1_000_000,
        InstantPurchasePrice: 5_000_000);

    private static byte Checksum(byte[] packet)
    {
        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        return checksum;
    }

    private static void AssertFrame(byte[] packet, GamePackets id, int length)
    {
        packet.Length.Should().Be(length);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be((uint)length);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be((ushort)id);
        packet[6].Should().Be(Checksum(packet));
    }

    private static void AssertEntry(byte[] packet, int baseOffset, AuctionInfo auction)
    {
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(baseOffset, 4)).Should().Be(auction.AuctionUid);

        var item = packet.AsSpan(baseOffset + 4, ItemFixedInfoWriter.Size);
        BinaryPrimitives.ReadUInt32LittleEndian(item.Slice(0, 4)).Should().Be(auction.Item.Handle);
        BinaryPrimitives.ReadInt32LittleEndian(item.Slice(71, 4)).Should().Be(auction.Item.AppearanceCode);

        packet[baseOffset + 79].Should().Be(auction.DurationType);
        BinaryPrimitives.ReadUInt64LittleEndian(packet.AsSpan(baseOffset + 80, 8)).Should().Be(auction.BiddedPrice);
        BinaryPrimitives.ReadUInt64LittleEndian(packet.AsSpan(baseOffset + 88, 8)).Should().Be(auction.InstantPurchasePrice);
    }

    [Test]
    public void AuctionIds_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_SC_AUCTION_SEARCH).Should().Be(1301);
        ((ushort)GamePackets.TM_SC_AUCTION_SELLING_LIST).Should().Be(1303);
        ((ushort)GamePackets.TM_SC_AUCTION_BIDDED_LIST).Should().Be(1305);

        // rzu remaps the family to 2xxx from EPIC_9_6_3 on; 7.3 stays on the low branch, and a missing
        // enum member would be dropped as an "Undefined packet ID" before any dispatch.
        Enum.IsDefined(typeof(GamePackets), (ushort)1301).Should().BeTrue();
        Enum.IsDefined(typeof(GamePackets), (ushort)1303).Should().BeTrue();
        Enum.IsDefined(typeof(GamePackets), (ushort)1305).Should().BeTrue();
    }

    [Test]
    public void ItemFixedInfo_IsTheSeventyFiveByteMotif()
    {
        ItemFixedInfoWriter.Size.Should().Be(75);

        var buffer = new byte[ItemFixedInfoWriter.Size];
        ItemFixedInfoWriter.Write(buffer, SampleItem);

        buffer.Should().HaveCount(75);
    }

    [Test]
    public void ItemFixedInfoWritesEveryFieldAtItsEpic73Offset()
    {
        var buffer = new byte[ItemFixedInfoWriter.Size + 1];
        buffer[ItemFixedInfoWriter.Size] = 0xAB;

        ItemFixedInfoWriter.Write(buffer.AsSpan(0, ItemFixedInfoWriter.Size), SampleItem);

        BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(0, 4)).Should().Be(0x80000111u);
        BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(4, 4)).Should().Be(240100);
        BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(8, 8)).Should().Be(0x1122334455667788L);
        BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(16, 8)).Should().Be(7);
        BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(24, 4)).Should().Be(12);
        BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(28, 4)).Should().Be(34u);
        buffer[32].Should().Be(5);
        buffer[33].Should().Be(6);
        BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(34, 4)).Should().Be(0x80000000u);
        BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(38, 4)).Should().Be(101);
        BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(42, 4)).Should().Be(102);
        BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(46, 4)).Should().Be(103);
        BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(50, 4)).Should().Be(104);
        BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(54, 4)).Should().Be(7200);
        buffer[58].Should().Be(3);
        BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(59, 4)).Should().Be(45);
        BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(63, 4)).Should().Be(56);
        BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(67, 4)).Should().Be(67);
        BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(71, 4)).Should().Be(89);

        // item_info ends at 75: a motif of 71 bytes (the rzu 7.3 reading) would leave appearance_code out
        // and shift every following byte.
        buffer[ItemFixedInfoWriter.Size].Should().Be(0xAB);
    }

    [Test]
    public void ItemFixedInfoWriter_RejectsASpanShorterThanTheMotif()
    {
        var buffer = new byte[ItemFixedInfoWriter.Size - 1];

        var write = () => ItemFixedInfoWriter.Write(buffer, SampleItem);

        write.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void ItemFixedInfoWriter_ZeroesTheSocketsOfAnItemWithoutAny()
    {
        var buffer = new byte[ItemFixedInfoWriter.Size];
        ItemFixedInfoWriter.Write(buffer, SampleItem with { Sockets = new long[] { 101 } });

        BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(38, 4)).Should().Be(101);
        BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(42, 4)).Should().Be(0);
        BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(46, 4)).Should().Be(0);
        BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(50, 4)).Should().Be(0);
    }

    [Test]
    public void FromItem_FillsTheMotifAndLeavesTheTwoUnestablishedFieldsAtZero()
    {
        var entity = new ItemEntity
        {
            Id = 0x0A000001,
            ItemResourceId = 240100,
            Amount = 9,
            EtherealDurability = -3,
            Endurance = 120,
            Enhance = 4,
            Level = 5,
            Flag = ItemFlag.Card,
            SocketItemIds = new long[] { 11, 12 },
            RemainingTime = 60,
            ElementalEffectType = ElementalType.Fire,
            ElementalEffectAttackPoint = 21,
            ElementalEffectMagicPoint = 22
        };

        var frame = new byte[ItemFixedInfoWriter.Size];
        ItemFixedInfoWriter.Write(frame, ItemFixedInfo.FromItem(entity));

        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(0, 4)).Should().Be(0x0A000001u);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(4, 4)).Should().Be(240100);
        BinaryPrimitives.ReadInt64LittleEndian(frame.AsSpan(8, 8)).Should().Be(0x0A000001);
        BinaryPrimitives.ReadInt64LittleEndian(frame.AsSpan(16, 8)).Should().Be(9);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(24, 4)).Should().Be(-3);
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(28, 4)).Should().Be(120u);
        frame[32].Should().Be(4);
        frame[33].Should().Be(5);
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(34, 4)).Should().Be((uint)ItemFlag.Card);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(38, 4)).Should().Be(11);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(42, 4)).Should().Be(12);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(54, 4)).Should().Be(60);
        frame[58].Should().Be((byte)ElementalType.Fire);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(63, 4)).Should().Be(21);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(67, 4)).Should().Be(22);

        // Neither elemental_effect.remain_time (59) nor appearance_code (71) is established; the
        // inventory serializer has always shipped zeros there and the auction entries must match it.
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(59, 4)).Should().Be(0);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(71, 4)).Should().Be(0);
    }

    [Test]
    public void InventoryRecord_StillCarriesTheMotifPlusItsTenPositionBytes()
    {
        var item = new ItemEntity
        {
            Id = 0x0A000001,
            ItemResourceId = 240100,
            Amount = 2,
            Endurance = 50,
            WearInfo = ItemWearType.Armor,
            EquippedBySummonId = 77,
            Idx = 6
        };

        var packet = GameCharacterPackets.BuildInventory(new[] { item }).Single();
        var record = packet.AsSpan(HeaderSize + 2, 85);

        AssertFrame(packet, GamePackets.TM_SC_INVENTORY, HeaderSize + 2 + 85);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(HeaderSize, 2)).Should().Be(1);

        var motif = new byte[ItemFixedInfoWriter.Size];
        ItemFixedInfoWriter.Write(motif, ItemFixedInfo.FromItem(item));
        record.Slice(0, ItemFixedInfoWriter.Size).ToArray().Should().Equal(motif);

        BinaryPrimitives.ReadInt16LittleEndian(record.Slice(75, 2)).Should().Be((short)ItemWearType.Armor);
        BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(77, 4)).Should().Be(77u);
        BinaryPrimitives.ReadInt32LittleEndian(record.Slice(81, 4)).Should().Be(6);
    }

    [Test]
    public void AuctionSearch_FillsAPageAndKeepsItsFortySlots()
    {
        var packet = GameAuctionPackets.BuildAuctionSearch(3, 9,
            new[] { new SearchedAuctionInfo(SampleAuction(), "Selene", 1) });

        AssertFrame(packet, GamePackets.TM_SC_AUCTION_SEARCH, 5139);
        GameAuctionPackets.SearchPacketSize.Should().Be(5139);
        GameAuctionPackets.SearchedAuctionEntrySize.Should().Be(128);
        GameAuctionPackets.AuctionSlots.Should().Be(40);

        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(3);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(9);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(15, 4)).Should().Be(1);

        AssertEntry(packet, TableOffset, SampleAuction());

        var seller = packet.AsSpan(TableOffset + 96, 31);
        Encoding.ASCII.GetString(seller.Slice(0, 6)).Should().Be("Selene");
        seller.Slice(6).ToArray().Should().OnlyContain(b => b == 0);
        packet[TableOffset + 127].Should().Be(1);

        // The client copies 5120 bytes whatever auction_info_count says: the slots past the entry must be
        // zero, not residues, and the frame must stay 5139 bytes long.
        packet.AsSpan(TableOffset + 128).ToArray().Should().OnlyContain(b => b == 0);
    }

    [Test]
    public void AuctionSearch_SendsAnEmptyPageAsAFullZeroedTable()
    {
        var packet = GameAuctionPackets.BuildAuctionSearch(0, 0);

        AssertFrame(packet, GamePackets.TM_SC_AUCTION_SEARCH, 5139);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(15, 4)).Should().Be(0);
        packet.AsSpan(TableOffset).ToArray().Should().OnlyContain(b => b == 0);
    }

    [Test]
    public void AuctionSearch_ClampsTheEntryCountToTheFortySlots()
    {
        var entries = Enumerable.Range(0, 45)
            .Select(i => new SearchedAuctionInfo(SampleAuction(i), $"S{i}", 0))
            .ToArray();

        var packet = GameAuctionPackets.BuildAuctionSearch(0, 1, entries);

        AssertFrame(packet, GamePackets.TM_SC_AUCTION_SEARCH, 5139);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(15, 4)).Should().Be(40);

        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(TableOffset + 39 * 128, 4)).Should().Be(39);
        Encoding.ASCII.GetString(packet.AsSpan(TableOffset + 39 * 128 + 96, 3)).Should().Be("S39");
    }

    [Test]
    public void AuctionSellingList_FillsItsPageOfNinetySevenByteEntries()
    {
        var packet = GameAuctionPackets.BuildAuctionSellingList(1, 2,
            new[] { new RegisteredAuctionInfo(SampleAuction(), 4) });

        AssertFrame(packet, GamePackets.TM_SC_AUCTION_SELLING_LIST, 3899);
        GameAuctionPackets.ListPacketSize.Should().Be(3899);
        GameAuctionPackets.RegisteredAuctionEntrySize.Should().Be(97);

        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(1);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(2);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(15, 4)).Should().Be(1);

        AssertEntry(packet, TableOffset, SampleAuction());
        packet[TableOffset + 96].Should().Be(4);

        packet.AsSpan(TableOffset + 97).ToArray().Should().OnlyContain(b => b == 0);
    }

    [Test]
    public void AuctionBiddedList_FillsItsPageOfNinetySevenByteEntries()
    {
        var packet = GameAuctionPackets.BuildAuctionBiddedList(2, 3,
            new[] { new BiddedAuctionInfo(SampleAuction(987), 7) });

        AssertFrame(packet, GamePackets.TM_SC_AUCTION_BIDDED_LIST, 3899);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(2);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(3);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(15, 4)).Should().Be(1);

        AssertEntry(packet, TableOffset, SampleAuction(987));
        packet[TableOffset + 96].Should().Be(7);

        packet.AsSpan(TableOffset + 97).ToArray().Should().OnlyContain(b => b == 0);
    }

    [Test]
    public void AuctionCateryResourceEntity_MapsTheArcadiaTable()
    {
        var options = new DbContextOptionsBuilder<ArcadiaContext>()
            .UseNpgsql("Host=localhost;Database=arcadia;Username=postgres;Password=postgres")
            .Options;

        using var context = new ArcadiaContext(options);
        var entity = context.Model.FindEntityType(typeof(AuctionCateryResourceEntity));
        var primaryKey = entity?.FindPrimaryKey();

        // No surrogate id on that table: the natural pair carries the key.
        primaryKey.Should().NotBeNull();
        primaryKey!.Properties.Select(property => property.Name).Should().Equal("CateryId", "SubCateryId");

        // The six columns of ArcadiaSchemaPSQL.sql:1-9, exposed but not interpreted.
        entity!.GetProperties().Select(property => property.Name).Should()
            .BeEquivalentTo("CateryId", "SubCateryId", "NameId", "LocalFlag", "ItemGroup", "ItemClass");

        context.AuctionCateryResources.Should().NotBeNull();
    }
}
