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

/// <summary>An entry of <c>TM_SC_ITEM_KEEPING_LIST</c> (1351), rzu's <c>TS_ITEM_KEEPING_INFO</c>.</summary>
public readonly record struct ItemKeepingEntry(int KeepingUid, ItemFixedInfo Item, int DurationSeconds, byte KeepingType,
    int RelatedItemCode, int RelatedItemEnhance, int RelatedItemLevel);

/// <summary>
/// <c>TM_CS_AUCTION_SEARCH</c> (1300): the search the player launched in the auction house. The two
/// category ids are the first two columns of the client's <c>db_auctioncategoryresource.rdb</c>. The
/// effect of <paramref name="IsEquipable"/> and the filtering policy itself are not established —
/// the fields are only read and logged. See docs/packet-specs/1300-auction-search.md §5.5, §5.6.
/// </summary>
public readonly record struct AuctionSearchRequest(int CategoryId, int SubCategoryId, string Keyword,
    int PageNum, bool IsEquipable);

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
    /// Size of the <c>keyword</c> zone of <c>TM_CS_AUCTION_SEARCH</c> (1300): a fixed, not
    /// length-prefixed, NUL-terminated ASCII area (spec §3.2).
    /// </summary>
    public const int KeywordSize = 31;

    /// <summary>Absolute offset of <c>category_id</c> in the 1300 request.</summary>
    public const int SearchRequestCategoryOffset = HeaderSize;

    /// <summary>Absolute offset of <c>sub_category_id</c> in the 1300 request.</summary>
    public const int SearchRequestSubCategoryOffset = SearchRequestCategoryOffset + 4;

    /// <summary>Absolute offset of <c>keyword</c> in the 1300 request.</summary>
    public const int SearchRequestKeywordOffset = SearchRequestSubCategoryOffset + 4;

    /// <summary>Absolute offset of <c>page_num</c> in the 1300 request.</summary>
    public const int SearchRequestPageNumOffset = SearchRequestKeywordOffset + KeywordSize;

    /// <summary>Absolute offset of <c>is_equipable</c>, the last byte of the 1300 request.</summary>
    public const int SearchRequestIsEquipableOffset = SearchRequestPageNumOffset + 4;

    /// <summary>
    /// Total size of <c>TM_CS_AUCTION_SEARCH</c> (1300): 7 header + 4 + 4 + 31 + 4 + 1 = 51, the literal
    /// the 7.3 client's constructor and sender both write (spec §3.1).
    /// </summary>
    public const int SearchRequestSize = SearchRequestIsEquipableOffset + 1;

    /// <summary>
    /// Reads <c>TM_CS_AUCTION_SEARCH</c> (1300), the 51-byte search request. A shorter frame is refused
    /// rather than partially read: the layout is fixed, so four unread bytes would silently misalign the
    /// tail of the frame. Nothing else in the frame carries a player, item or price (spec §5.1).
    /// </summary>
    public static bool TryReadAuctionSearch(ReadOnlySpan<byte> packet, out AuctionSearchRequest request)
    {
        if (packet.Length < SearchRequestSize)
        {
            request = default;
            return false;
        }

        request = new AuctionSearchRequest(
            BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(SearchRequestCategoryOffset, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(SearchRequestSubCategoryOffset, 4)),
            ReadFixedAscii(packet.Slice(SearchRequestKeywordOffset, KeywordSize)),
            BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(SearchRequestPageNumOffset, 4)),
            packet[SearchRequestIsEquipableOffset] != 0);
        return true;
    }

    /// <summary>
    /// Reads a fixed ASCII zone: the client's copy is a bounded <c>strncpy</c> of 31 bytes that is not
    /// guaranteed to leave a terminator when the keyword reaches the zone's size, so the NUL ends the
    /// string when present and is not required to be (spec §3.2, §12.5).
    /// </summary>
    private static string ReadFixedAscii(ReadOnlySpan<byte> span)
    {
        var terminator = span.IndexOf((byte)0);
        return Encoding.ASCII.GetString(terminator < 0 ? span : span.Slice(0, terminator));
    }

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
    /// Absolute offset of <c>page_num</c> in the <c>TM_CS_AUCTION_SELLING_LIST</c> (1302) request: the
    /// header is seven bytes, so the payload starts at 7 and the frame is eleven bytes long.
    /// </summary>
    public const int SellingListRequestPageNumOffset = HeaderSize;

    /// <summary>
    /// Total size of <c>TM_CS_AUCTION_SELLING_LIST</c> (1302): 7 header + 4, the literal <c>0xb</c> the
    /// 7.3 client writes in its constructor-and-sender (spec §3.1). No padding, no other field.
    /// </summary>
    public const int SellingListRequestSize = SellingListRequestPageNumOffset + 4;

    /// <summary>
    /// Reads <c>TM_CS_AUCTION_SELLING_LIST</c> (1302), the eleven-byte page request. A shorter frame is
    /// refused rather than partially read, and only <c>page_num</c> is read: the frame carries no
    /// character, category or filter — the server knows whose list it is from the session (spec §3.2).
    /// The page is one-based and the value is echoed back as it arrived: what an out-of-range page
    /// should answer is not established (spec §7.3, §8 q3).
    /// Its constants and reader sit after the three builders — and not with the size constants above —
    /// so that the sibling branch <c>hermes/packet-1300-auction-search</c> (MR #65), which appends the
    /// 1300 request constants right after <c>SellerNameSize</c>, merges into this file without touching
    /// its lines: <c>git merge-tree --write-tree</c> against that branch exits 0, no conflicted path.
    /// </summary>
    public static bool TryReadAuctionSellingList(ReadOnlySpan<byte> packet, out int pageNum)
    {
        if (packet.Length < SellingListRequestSize)
        {
            pageNum = 0;
            return false;
        }

        pageNum = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(SellingListRequestPageNumOffset, 4));
        return true;
    }

    /// <summary>
    /// Writes <c>page_num</c> at 7, <c>total_page_count</c> at 11 and the entry count at 15, clamped to
    /// the forty slots the client reads. The rest of the table is already zero.
    /// </summary>
    /// <summary>One entry of <c>TM_SC_ITEM_KEEPING_LIST</c> (1351): <c>TS_ITEM_KEEPING_INFO</c>, 96 bytes.</summary>
    public const int ItemKeepingInfoSize = 96;

    /// <summary><c>TM_SC_ITEM_KEEPING_LIST</c> (1351): 7 + 12 + 40 × 96 = 3 859 bytes, all forty slots written.</summary>
    public const int ItemKeepingListPacketSize = HeaderSize + PageHeaderSize + AuctionSlots * ItemKeepingInfoSize;

    /// <summary><c>TM_CS_ITEM_KEEPING_LIST</c> (1350) and <c>TM_CS_ITEM_KEEPING_TAKE</c> (1352): one int32 at 7, 11 bytes.</summary>
    public const int ItemKeepingRequestSize = HeaderSize + 4;

    /// <summary>
    /// <c>TM_SC_ITEM_KEEPING_LIST</c> (1351), rzu <c>TS_SC_ITEM_KEEPING_LIST</c>: page header, then
    /// <c>keeping_uid</c> @0, the 75-byte item motif @4, <c>duration</c> (seconds left) @79, <c>keeping_type</c> @83,
    /// <c>related_item_code</c> @84, <c>related_item_enhance</c> @88, <c>related_item_level</c> @92.
    /// </summary>
    public static byte[] BuildItemKeepingList(int pageNum, int totalPageCount, IReadOnlyList<ItemKeepingEntry> entries)
    {
        var packet = CreatePacket(GamePackets.TM_SC_ITEM_KEEPING_LIST, ItemKeepingListPacketSize);
        var payload = packet.AsSpan(HeaderSize);
        var count = WritePageHeader(payload, pageNum, totalPageCount, entries?.Count ?? 0);
        for (var i = 0; i < count; i++)
        {
            var entry = payload.Slice(TableOffset - HeaderSize + i * ItemKeepingInfoSize, ItemKeepingInfoSize);
            var info = entries[i];
            BinaryPrimitives.WriteInt32LittleEndian(entry.Slice(0, 4), info.KeepingUid);
            ItemFixedInfoWriter.Write(entry.Slice(4, ItemFixedInfoWriter.Size), info.Item);
            BinaryPrimitives.WriteInt32LittleEndian(entry.Slice(79, 4), info.DurationSeconds);
            entry[83] = info.KeepingType;
            BinaryPrimitives.WriteInt32LittleEndian(entry.Slice(84, 4), info.RelatedItemCode);
            BinaryPrimitives.WriteInt32LittleEndian(entry.Slice(88, 4), info.RelatedItemEnhance);
            BinaryPrimitives.WriteInt32LittleEndian(entry.Slice(92, 4), info.RelatedItemLevel);
        }

        WriteChecksum(packet);
        return packet;
    }

    /// <summary>Reads 1350 (<c>page_num</c>) or 1352 (<c>keeping_uid</c>): exactly 11 bytes, the int32 at 7.</summary>
    public static bool TryReadItemKeepingRequest(ReadOnlySpan<byte> packet, out int value)
    {
        value = 0;
        if (packet.Length != ItemKeepingRequestSize)
        {
            return false;
        }

        value = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(HeaderSize, 4));
        return true;
    }

    private static int WritePageHeader(Span<byte> payload, int pageNum, int totalPageCount, int entryCount)
    {
        var count = Math.Clamp(entryCount, 0, AuctionSlots);

        BinaryPrimitives.WriteInt32LittleEndian(payload.Slice(0, 4), pageNum);
        BinaryPrimitives.WriteInt32LittleEndian(payload.Slice(4, 4), totalPageCount);
        BinaryPrimitives.WriteInt32LittleEndian(payload.Slice(8, 4), count);
        return count;
    }

    // The TM_CS_AUCTION_INSTANT_PURCHASE (1308) request lives here, between the page-header writer and the
    // record writer, and nowhere else: the four open sibling branches of this file each insert in another
    // stretch (1300 right after SellerNameSize, 1302 right after the three builders, 1304 at the very end
    // of the class, 1306 right after TableOffset), so this block is the one a merge of the five lots
    // leaves untouched (measured with git merge-tree, sheet 1308 §14.6).

    /// <summary>
    /// Absolute offset of <c>auction_uid</c> in the <c>TM_CS_AUCTION_INSTANT_PURCHASE</c> (1308) request:
    /// the header is seven bytes, so the payload starts at 7 (spec §3.1).
    /// </summary>
    public const int InstantPurchaseRequestAuctionUidOffset = HeaderSize;

    /// <summary>
    /// Total size of <c>TM_CS_AUCTION_INSTANT_PURCHASE</c> (1308): 7 header + 4, the literal <c>0xb</c>
    /// the 7.3 client writes in its construction routine (spec §3.1). No padding, no other field — the
    /// client moves one <c>dword</c> read from <c>message+0x13</c> and nothing else.
    /// </summary>
    public const int InstantPurchaseRequestSize = InstantPurchaseRequestAuctionUidOffset + 4;

    /// <summary>
    /// Reads <c>TM_CS_AUCTION_INSTANT_PURCHASE</c> (1308), the eleven-byte buy-now request. A shorter
    /// frame is refused rather than partially read, like the 1300 and 1306 readers: the layout is fixed,
    /// so a partial read would only misalign its single field. <c>auction_uid</c> is read **unsigned** —
    /// the collection's own decision (socle-encheres.md §3.5, spec §4), taken where rzu writes an
    /// <c>int32_t</c> for this frame and a <c>uint32_t</c> for its twin 1310: no auction identifier is
    /// negative, and the sign is invisible on the wire.
    /// </summary>
    public static bool TryReadAuctionInstantPurchase(ReadOnlySpan<byte> packet, out uint auctionUid)
    {
        if (packet.Length < InstantPurchaseRequestSize)
        {
            auctionUid = 0;
            return false;
        }

        auctionUid = BinaryPrimitives.ReadUInt32LittleEndian(
            packet.Slice(InstantPurchaseRequestAuctionUidOffset, 4));
        return true;
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

    // The TM_CS_AUCTION_CANCEL (1310) request lives here, between the fixed-ascii writer and the frame
    // factory, and nowhere else: the six sibling branches of this file each insert in another stretch
    // (1300 after SellerNameSize, 1302 after the three builders, 1304 at the very end of the class,
    // 1306 after TableOffset, 1308 between the page-header writer and the record writer, 1309 before
    // WriteChecksum), so this block is the one a merge of the seven lots leaves untouched. Measured with
    // `git merge-tree --write-tree --name-only <sibling> HEAD`; sheet 1310 §14.6.

    /// <summary>
    /// Absolute offset of <c>auction_uid</c> in the <c>TM_CS_AUCTION_CANCEL</c> (1310) request: the
    /// header is seven bytes, so the payload starts at 7 (spec §3.1).
    /// </summary>
    public const int AuctionCancelRequestAuctionUidOffset = HeaderSize;

    /// <summary>
    /// Total size of <c>TM_CS_AUCTION_CANCEL</c> (1310): 7 header + 4, the literal <c>0xb</c> the 7.3
    /// client writes in its construction routine (spec §3.1). No padding, no other field — the client
    /// moves one <c>dword</c> read from <c>message+0x13</c> to <c>frame+7</c> and nothing else, which
    /// makes this frame identical to 1308's but for the identifier.
    /// </summary>
    public const int AuctionCancelRequestSize = AuctionCancelRequestAuctionUidOffset + 4;

    /// <summary>
    /// Reads <c>TM_CS_AUCTION_CANCEL</c> (1310), the eleven-byte dismissal request. The frame's layout is
    /// fixed, so the exact eleven-byte form is the only one accepted (spec §5.2): a short or padded frame
    /// is refused rather than partially read, like <see cref="GameActionPackets.TryReadSummonCardSkillList"/>.
    /// <c>auction_uid</c> is read <b>unsigned</b>: this is the one frame of the family where rzu and
    /// NGemity both declare <c>uint32_t</c> (spec §4.3), and no auction identifier is negative.
    /// </summary>
    public static bool TryReadAuctionCancel(ReadOnlySpan<byte> packet, out uint auctionUid)
    {
        if (packet.Length != AuctionCancelRequestSize)
        {
            auctionUid = 0;
            return false;
        }

        auctionUid = BinaryPrimitives.ReadUInt32LittleEndian(
            packet.Slice(AuctionCancelRequestAuctionUidOffset, 4));
        return true;
    }

    private static byte[] CreatePacket(GamePackets id, int total)
    {
        var packet = new byte[total];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)id);
        return packet;
    }

    // The TM_CS_AUCTION_REGISTER (1309) request lives here, after the ASCII writer and before CreatePacket:
    // the five open sibling branches of this file each insert in another stretch (1300 right after
    // SellerNameSize, 1302 right after the three builders, 1304 at the very end of the class, 1306 right
    // after TableOffset, 1308 between the page-header writer and the record writer), so this block is the
    // one a merge of the six lots leaves untouched (measured with git merge-tree, sheet 1309 §14.6).

    /// <summary>
    /// Absolute offset of <c>item_handle</c> in the <c>TM_CS_AUCTION_REGISTER</c> (1309) request: the
    /// header is seven bytes, so the payload starts at 7 (sheet §3.1).
    /// </summary>
    public const int AuctionRegisterRequestItemHandleOffset = HeaderSize;

    /// <summary>Absolute offset of <c>item_count</c>, the second 32-bit field of the request.</summary>
    public const int AuctionRegisterRequestItemCountOffset = AuctionRegisterRequestItemHandleOffset + 4;

    /// <summary>Absolute offset of the 64-bit <c>start_price</c>.</summary>
    public const int AuctionRegisterRequestStartPriceOffset = AuctionRegisterRequestItemCountOffset + 4;

    /// <summary>Absolute offset of the 64-bit <c>instant_purchase_price</c>.</summary>
    public const int AuctionRegisterRequestInstantPurchasePriceOffset =
        AuctionRegisterRequestStartPriceOffset + 8;

    /// <summary>
    /// Absolute offset of <c>duration_type</c>, the last byte of the frame: nothing follows it, which is
    /// why the size below is this offset plus one.
    /// </summary>
    public const int AuctionRegisterRequestDurationTypeOffset =
        AuctionRegisterRequestInstantPurchasePriceOffset + 8;

    /// <summary>
    /// Total size of <c>TM_CS_AUCTION_REGISTER</c> (1309): 7 header + 25, the literal <c>0x20</c> the 7.3
    /// client writes in its construction routine (sheet §3.1). It is the widest frame of the family, and
    /// the only one carrying two 64-bit prices. The client's internal message reserves four extra bytes
    /// between <c>item_count</c> and <c>start_price</c> and its sender skips them, so the wire frame is
    /// contiguous: do not add a field in that gap (sheet §3.2).
    /// </summary>
    public const int AuctionRegisterRequestSize = AuctionRegisterRequestDurationTypeOffset + 1;

    /// <summary>
    /// Reads <c>TM_CS_AUCTION_REGISTER</c> (1309), the thirty-two byte request that registers an
    /// announcement in the auction house. Any length other than the exact one is refused — short or
    /// completed — like <see cref="Game.GameActionPackets.TryReadSummonCardSkillList"/>: the layout is
    /// fixed and a padded frame is a frame the 7.3 client cannot build (sheet §5.2).
    /// <para>
    /// The values are handed over as the client typed them, never judged here: the sheet measures two
    /// 64-bit prices parsed straight from the two edit controls with no visible bound (§7b), an
    /// <c>item_count</c> taken from the selected row without a domain check (§7c) and a
    /// <c>duration_type</c> whose <c>0</c> is never emitted but whose fate is an open decision (§7a).
    /// Refusing any of those in the reader would invent a rule, so the reader only bounds the frame.
    /// </para>
    /// </summary>
    public static bool TryReadAuctionRegister(ReadOnlySpan<byte> packet, out uint itemHandle,
        out int itemCount, out long startPrice, out long instantPurchasePrice, out byte durationType)
    {
        if (packet.Length != AuctionRegisterRequestSize)
        {
            itemHandle = 0;
            itemCount = 0;
            startPrice = 0;
            instantPurchasePrice = 0;
            durationType = 0;
            return false;
        }

        itemHandle = BinaryPrimitives.ReadUInt32LittleEndian(
            packet.Slice(AuctionRegisterRequestItemHandleOffset, 4));
        itemCount = BinaryPrimitives.ReadInt32LittleEndian(
            packet.Slice(AuctionRegisterRequestItemCountOffset, 4));
        startPrice = BinaryPrimitives.ReadInt64LittleEndian(
            packet.Slice(AuctionRegisterRequestStartPriceOffset, 8));
        instantPurchasePrice = BinaryPrimitives.ReadInt64LittleEndian(
            packet.Slice(AuctionRegisterRequestInstantPurchasePriceOffset, 8));
        durationType = packet[AuctionRegisterRequestDurationTypeOffset];
        return true;
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

    /// <summary>
    /// Absolute offset of <c>page_num</c> in the <c>TM_CS_AUCTION_BIDDED_LIST</c> (1304) request: the
    /// header is seven bytes, so the payload starts at 7 and the frame is eleven bytes long.
    /// </summary>
    public const int BiddedListRequestPageNumOffset = HeaderSize;

    /// <summary>
    /// Total size of <c>TM_CS_AUCTION_BIDDED_LIST</c> (1304): 7 header + 4, the literal <c>0xb</c> the
    /// 7.3 client writes in its constructor-and-sender (spec §3.1). No padding, no other field.
    /// </summary>
    public const int BiddedListRequestSize = BiddedListRequestPageNumOffset + 4;

    /// <summary>
    /// Reads <c>TM_CS_AUCTION_BIDDED_LIST</c> (1304), the eleven-byte page request. A shorter frame is
    /// refused rather than partially read, and only <c>page_num</c> is read: the frame carries no
    /// character, no category and no filter — the list is the requester's own by construction, its entry
    /// carrying neither a seller nor a bidder name (spec §5.5). The page is one-based and the value is
    /// echoed back as it arrived: what an out-of-range page should answer is not established
    /// (spec §7.6, §8 q7).
    /// Its constants and reader sit at the end of the class, after the shared private helpers, and not
    /// with the size constants above or after the three builders: the sibling branches
    /// <c>hermes/packet-1300-auction-search</c> (MR #65, right after <c>SellerNameSize</c>) and
    /// <c>hermes/packet-1302-auction-selling-list</c> (MR #66, right after the builders) each insert in
    /// the upper half of this file, so an insertion point they do not touch keeps the three-way merge of
    /// the three lots free of conflicts (measured, sheet §14.6).
    /// </summary>
    public static bool TryReadAuctionBiddedList(ReadOnlySpan<byte> packet, out int pageNum)
    {
        if (packet.Length < BiddedListRequestSize)
        {
            pageNum = 0;
            return false;
        }

        pageNum = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(BiddedListRequestPageNumOffset, 4));
        return true;
    }
}
