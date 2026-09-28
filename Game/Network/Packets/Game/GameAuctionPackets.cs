using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// The common auction record, 96 bytes: <c>auction_uid</c>, the 75-byte <see cref="ItemFixedInfo"/>
/// motif, <c>duration_type</c> and the two 64-bit prices. See docs/packet-specs/socle-encheres.md §3.8.
/// </summary>
public readonly record struct AuctionInfo(
    int AuctionUid,
    ItemFixedInfo Item,
    byte DurationType,
    ulong BiddedPrice,
    ulong InstantPurchasePrice);

/// <summary>
/// An entry of <c>TM_SC_AUCTION_SEARCH</c> (1301): the common record plus the seller name and the
/// status byte the search window displays. The semantics of that byte are not established (§8.1).
/// </summary>
public readonly record struct SearchedAuctionInfo(AuctionInfo Auction, string SellerName, byte Flag);

/// <summary>
/// An entry of <c>TM_SC_AUCTION_SELLING_LIST</c> (1303), rzu's <c>TS_REGISTERED_AUCTION_INFO</c>:
/// the common record plus a status byte. Same layout as <see cref="BiddedAuctionInfo"/> under another
/// name — the client has two distinct legends for the two lists, so the two enumerations may differ.
/// </summary>
public readonly record struct RegisteredAuctionInfo(AuctionInfo Auction, byte Status);

/// <summary>
/// An entry of <c>TM_SC_AUCTION_BIDDED_LIST</c> (1305), rzu's <c>TS_BIDDED_AUCTION_LIST</c>.
/// </summary>
public readonly record struct BiddedAuctionInfo(AuctionInfo Auction, byte Status);

/// <summary>
/// The three server to client frames of the auction family, Epic 7.3 branch. The 7.3 client copies
/// every table in one block without ever looking at <c>auction_info_count</c> (5120 bytes for 1301,
/// 3880 for 1303 and 1305), so all forty slots are always written — a short frame is a silent
/// misalignment. Slots past the entries stay zero. See docs/packet-specs/socle-encheres.md §5.2.
/// </summary>
public static class GameAuctionPackets
{
    private const int HeaderSize = 7;
    private const int PageHeaderSize = 12;
    private const int TableOffset = HeaderSize + PageHeaderSize;

    // The TM_CS_AUCTION_BID (1306) request, sitting here — between the response constants and the
    // builders — and nowhere else: the three open sibling branches of this file each insert in another
    // part of it (hermes/packet-1300-auction-search right after SellerNameSize, 1302 right after the
    // builders, 1304 at the very end of the class), so this blank stretch is the only one a three way
    // merge of the four lots leaves untouched (measured, sheet 1306 §13.7).

    /// <summary>
    /// Absolute offset of <c>auction_uid</c> in the <c>TM_CS_AUCTION_BID</c> (1306) request: the header
    /// is seven bytes, so the payload starts at 7 (spec §3.1).
    /// </summary>
    public const int BidRequestAuctionUidOffset = HeaderSize;

    /// <summary>Absolute offset of <c>price</c> in the 1306 request: four bytes of <c>auction_uid</c> follow the header.</summary>
    public const int BidRequestPriceOffset = BidRequestAuctionUidOffset + 4;

    /// <summary>
    /// Total size of <c>TM_CS_AUCTION_BID</c> (1306): 7 header + 4 + 8 = 19, the literal <c>0x13</c> the
    /// 7.3 client writes in its construction routine (spec §3.1). No padding, no other field.
    /// </summary>
    public const int BidRequestSize = BidRequestPriceOffset + 8;

    /// <summary>
    /// Reads <c>TM_CS_AUCTION_BID</c> (1306), the nineteen-byte bid request. A shorter frame is refused
    /// rather than partially read, like the 1300 and 1302 readers.
    /// <c>price</c> is read as a <c>long</c>, never an <c>int</c>: it is the only 64-bit field of the
    /// family's requests (spec §3.1), and the client fills it from a 64-bit integer produced by its input
    /// control (spec §2). No bound is applied — the client's own scale and limits are not established
    /// (spec §7.2) — and <c>auction_uid</c> is read signed, exactly as rzu declares it.
    /// </summary>
    public static bool TryReadAuctionBid(ReadOnlySpan<byte> packet, out int auctionUid, out long price)
    {
        if (packet.Length < BidRequestSize)
        {
            auctionUid = 0;
            price = 0;
            return false;
        }

        auctionUid = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(BidRequestAuctionUidOffset, 4));
        price = BinaryPrimitives.ReadInt64LittleEndian(packet.Slice(BidRequestPriceOffset, 8));
        return true;
    }

    /// <summary>Number of entry slots in every response, written whether filled or not.</summary>
    public const int AuctionSlots = 40;

    /// <summary>Size of <see cref="AuctionInfo"/>, embedded in both entry kinds.</summary>
    public const int AuctionInfoSize = 96;

    /// <summary>Size of a <c>TM_SC_AUCTION_SEARCH</c> entry: 96 + <c>seller_name</c> (31) + <c>flag</c> (1).</summary>
    public const int SearchedAuctionEntrySize = 128;

    /// <summary>Size of a list entry: 96 + <c>status</c> (1).</summary>
    public const int RegisteredAuctionEntrySize = 97;

    /// <summary>Total size of <c>TM_SC_AUCTION_SEARCH</c> (1301): 7 + 12 + 40 × 128.</summary>
    public const int SearchPacketSize = HeaderSize + PageHeaderSize + AuctionSlots * SearchedAuctionEntrySize;

    /// <summary>Total size of <c>TM_SC_AUCTION_SELLING_LIST</c> (1303) and <c>TM_SC_AUCTION_BIDDED_LIST</c> (1305).</summary>
    public const int ListPacketSize = HeaderSize + PageHeaderSize + AuctionSlots * RegisteredAuctionEntrySize;

    private const int SellerNameSize = 31;

    /// <summary>
    /// <c>TM_SC_AUCTION_SEARCH</c> (1301), 5139 bytes. <paramref name="pageNum"/> is echoed back from
    /// the request; <paramref name="totalPageCount"/> comes from the caller, its rule is not
    /// established (§8.6).
    /// </summary>
    public static byte[] BuildAuctionSearch(int pageNum, int totalPageCount,
        IReadOnlyList<SearchedAuctionInfo> entries = null)
    {
        var packet = CreatePacket(GamePackets.TM_SC_AUCTION_SEARCH, SearchPacketSize);
        var payload = packet.AsSpan(HeaderSize);
        var count = WritePageHeader(payload, pageNum, totalPageCount, entries?.Count ?? 0);

        for (var i = 0; i < count; i++)
        {
            var entry = payload.Slice(TableOffset - HeaderSize + i * SearchedAuctionEntrySize, SearchedAuctionEntrySize);
            WriteAuctionInfo(entry, entries[i].Auction);
            WriteFixedAscii(entry.Slice(AuctionInfoSize, SellerNameSize), entries[i].SellerName);
            entry[AuctionInfoSize + SellerNameSize] = entries[i].Flag;
        }

        WriteChecksum(packet);
        return packet;
    }

    /// <summary><c>TM_SC_AUCTION_SELLING_LIST</c> (1303), 3899 bytes: the character's own announcements.</summary>
    public static byte[] BuildAuctionSellingList(int pageNum, int totalPageCount,
        IReadOnlyList<RegisteredAuctionInfo> entries = null)
    {
        var packet = CreatePacket(GamePackets.TM_SC_AUCTION_SELLING_LIST, ListPacketSize);
        var payload = packet.AsSpan(HeaderSize);
        var count = WritePageHeader(payload, pageNum, totalPageCount, entries?.Count ?? 0);

        for (var i = 0; i < count; i++)
        {
            var entry = payload.Slice(TableOffset - HeaderSize + i * RegisteredAuctionEntrySize, RegisteredAuctionEntrySize);
            WriteAuctionInfo(entry, entries[i].Auction);
            entry[AuctionInfoSize] = entries[i].Status;
        }

        WriteChecksum(packet);
        return packet;
    }

    /// <summary><c>TM_SC_AUCTION_BIDDED_LIST</c> (1305), 3899 bytes: the announcements the character bid on.</summary>
    public static byte[] BuildAuctionBiddedList(int pageNum, int totalPageCount,
        IReadOnlyList<BiddedAuctionInfo> entries = null)
    {
        var packet = CreatePacket(GamePackets.TM_SC_AUCTION_BIDDED_LIST, ListPacketSize);
        var payload = packet.AsSpan(HeaderSize);
        var count = WritePageHeader(payload, pageNum, totalPageCount, entries?.Count ?? 0);

        for (var i = 0; i < count; i++)
        {
            var entry = payload.Slice(TableOffset - HeaderSize + i * RegisteredAuctionEntrySize, RegisteredAuctionEntrySize);
            WriteAuctionInfo(entry, entries[i].Auction);
            entry[AuctionInfoSize] = entries[i].Status;
        }

        WriteChecksum(packet);
        return packet;
    }

    /// <summary>
    /// Writes <c>page_num</c> at 7, <c>total_page_count</c> at 11 and the entry count at 15, clamped to
    /// the forty slots the client reads. The rest of the table is already zero.
    /// </summary>
    private static int WritePageHeader(Span<byte> payload, int pageNum, int totalPageCount, int entryCount)
    {
        var count = Math.Clamp(entryCount, 0, AuctionSlots);

        BinaryPrimitives.WriteInt32LittleEndian(payload.Slice(0, 4), pageNum);
        BinaryPrimitives.WriteInt32LittleEndian(payload.Slice(4, 4), totalPageCount);
        BinaryPrimitives.WriteInt32LittleEndian(payload.Slice(8, 4), count);
        return count;
    }

    private static void WriteAuctionInfo(Span<byte> span, in AuctionInfo auction)
    {
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), auction.AuctionUid);
        ItemFixedInfoWriter.Write(span.Slice(4, ItemFixedInfoWriter.Size), auction.Item);
        span[79] = auction.DurationType;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(80, 8), auction.BiddedPrice);
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(88, 8), auction.InstantPurchasePrice);
    }

    private static void WriteFixedAscii(Span<byte> span, string value)
    {
        span.Clear();

        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var bytes = Encoding.ASCII.GetBytes(value);
        bytes.AsSpan(0, Math.Min(bytes.Length, span.Length)).CopyTo(span);
    }

    private static byte[] CreatePacket(GamePackets id, int total)
    {
        var packet = new byte[total];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)id);
        return packet;
    }

    private static void WriteChecksum(byte[] packet)
    {
        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        packet[6] = checksum;
    }
}
