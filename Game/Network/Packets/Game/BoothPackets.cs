using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// <c>TS_BOOTH_OPEN_ITEM_INFO</c>: the handle the client selected in its bag, the stack count and
/// the price, both kept verbatim — the socle never resolves the handle against the inventory and
/// never interprets the unit of the price (docs/packet-specs/socle-booths.md §7.3 and §7.8).
/// <para>
/// <see cref="Gold"/> is an <c>int64</c> because Epic 7.3 says so, not by assumption: rzu gates the
/// field as <c>int32</c> before <c>EPIC_4_1_1</c> and <c>int64</c> from <c>EPIC_4_1_1</c> on
/// (<c>TS_BOOTH_OPEN_ITEM_INFO.h:8-9</c>), and the 7.3 client writes it as two dwords
/// (<c>VA 0x48D654</c> / <c>VA 0x48D65B</c>), which fixes the 16 byte stride of the record.
/// </para>
/// </summary>
public readonly record struct BoothOpenItem(uint ItemHandle, int Count, long Gold);

/// <summary>
/// One item of a <c>TM_SC_WATCH_BOOTH</c> (703): the <strong>resolved</strong> 75 byte motif of the
/// owner's inventory item, followed by the price the owner declared in its <c>700</c> and kept
/// verbatim — whether that price is a unit or a total is not established
/// (docs/packet-specs/socle-booths-visibilite.md §5.2 point 3 and §7.7).
/// </summary>
public readonly record struct BoothWatchItem(ItemFixedInfo Item, long Gold);

/// <summary>
/// A parsed and wire-valid <c>TM_CS_START_BOOTH</c>. <see cref="Name"/> holds the raw client bytes up
/// to the first nul (the 49 byte field, so up to 49 bytes when it carries no nul at all — such a name
/// is then refused by the rules, which cap it at 40): the client copies them with <c>strncpy</c> and
/// no conversion, so the socle re-encodes nothing (docs/packet-specs/socle-booths.md §7.9).
/// </summary>
public sealed record StartBoothRequest(byte Type, byte[] Name, BoothOpenItem[] Items);

/// <summary>
/// One item of a <c>TM_CS_BUY_FROM_BOOTH</c> (705): the fields of the 75 byte motif the server acts on.
/// The client copies the first 75 bytes of the 83 byte record its 703 carried
/// (<c>SFrame.exe 0x48E717-0x48E72D</c>), so <see cref="ItemHandle"/> and <see cref="Code"/> are the
/// owner's, and <see cref="Count"/> is the quantity the buyer asks for
/// (docs/packet-specs/705-buy-from-booth.md §3).
/// </summary>
public readonly record struct BoothBuyLine(uint ItemHandle, int Code, long Count);

/// <summary>A parsed <c>TM_CS_BUY_FROM_BOOTH</c> (705): the booth handle and the items asked for.</summary>
public sealed record BuyFromBoothRequest(uint Target, BoothBuyLine[] Lines);

/// <summary>A parsed <c>TM_CS_SELL_TO_BOOTH</c> (706): one item of the seller's bag offered to a booth.</summary>
public readonly record struct SellToBoothRequest(uint Target, uint ItemHandle, int Count);

/// <summary>One name of <c>TM_SC_GET_BOOTHS_NAME</c> (708): the booth handle and its raw name bytes.</summary>
public readonly record struct BoothName(uint Handle, byte[] Name);

/// <summary>
/// The Epic 7.3 layout of the client packets of the player booth family
/// (docs/packet-specs/socle-booths.md §3, docs/packet-specs/711-check-booth-startable.md §3). All
/// three sizes are measured in the 7.3 client itself (<c>SFrame.exe</c>, frame builders
/// <c>VA 0x48CBD0</c> for 700, <c>VA 0x48CC20</c> for 701 and <c>VA 0x48CFD0</c> for 711):
/// <c>TM_CS_START_BOOTH</c> is <c>59 + 16×N</c> bytes, <c>TM_CS_STOP_BOOTH</c> and
/// <c>TM_CS_CHECK_BOOTH_STARTABLE</c> are 7 bytes and carry no field at all. Nothing here judges the
/// values — a frame the client can build is read as it is; the game rules that accept or refuse it
/// live in <c>BoothRules</c>, and 711 decides nothing at all (no rule is established for it).
/// </summary>
public static class BoothPackets
{
    public const int HeaderSize = 7;

    /// <summary>The empty booth frame: header (7) + name (49) + type (1) + count (2).</summary>
    public const int StartBoothMinLength = 59;

    /// <summary>One <c>TS_BOOTH_OPEN_ITEM_INFO</c> record: handle (4) + count (4) + gold (8).</summary>
    public const int StartBoothItemSize = 16;

    /// <summary>
    /// The name field the client fills with <c>strncpy(frame+7, name, 0x30)</c>: 48 characters plus the
    /// nul the buffer initialisation leaves behind.
    /// </summary>
    public const int BoothNameFieldLength = 49;

    /// <summary>
    /// <c>MAX_BOOTH_ITEM_COUNT</c> of the reference server (<c>CLAUDE.md:1081</c>), applied on
    /// reception: the <c>count</c> field is a <c>uint16</c>, so a hostile client can announce 65535
    /// items and the ceiling is what stops it.
    /// </summary>
    public const int MaxBoothItemCount = 8;

    /// <summary>Length of <c>TM_CS_STOP_BOOTH</c> (701): the header and nothing else.</summary>
    public const int StopBoothLength = HeaderSize;

    /// <summary>
    /// <c>TM_CS_WATCH_BOOTH</c> (702) and <c>TM_CS_STOP_WATCH_BOOTH</c> (704): the 7 byte header plus the
    /// booth handle, 11 bytes in all. The 7.3 client builds both from the same control through one
    /// emitter, on a flag (<c>0x48CC70</c> / <c>0x48CCC0</c>) — 11 is the only length it produces
    /// (docs/packet-specs/socle-booths-visibilite.md §3.2 and §3.3).
    /// </summary>
    public const int WatchBoothLength = HeaderSize + 4;

    /// <summary>Offset of the booth handle in 702 and 704, written by the client at <c>frame+7</c>.</summary>
    public const int WatchBoothTargetOffset = HeaderSize;

    /// <summary>
    /// <c>TM_SC_WATCH_BOOTH</c> (703) with an empty item list: header (7) + <c>target</c> (4) +
    /// <c>type</c> (1) + <c>count</c> (2).
    /// </summary>
    public const int WatchBoothHeaderLength = HeaderSize + 7;

    public const int WatchBoothTypeOffset = HeaderSize + 4;
    public const int WatchBoothCountOffset = HeaderSize + 5;
    public const int WatchBoothItemsOffset = HeaderSize + 7;

    /// <summary>
    /// One <c>TS_BOOTH_ITEM_INFO</c> of 703: the shared 75 byte motif plus the declared <c>gold</c> as an
    /// <c>int64</c>. The stride is measured twice in the 7.3 client — the loop step
    /// <c>imul esi,esi,0x53</c> = 83 of the 703 handler and the flat 83 byte copy it performs
    /// (docs/packet-specs/socle-booths-visibilite.md §3.4 and §3.5). <c>item_type</c> (9.8.1) is absent,
    /// and the motif already carries <c>appearance_code</c> despite its own rzu gating (§4.21).
    /// </summary>
    public const int WatchBoothItemSize = ItemFixedInfoWriter.Size + 8;

    /// <summary>Offset of the declared price inside a 703 record, right after the motif.</summary>
    public const int WatchBoothItemGoldOffset = ItemFixedInfoWriter.Size;
    /// <summary>
    /// Length of <c>TM_CS_CHECK_BOOTH_STARTABLE</c> (711): the header and nothing else, like 701.
    /// Both references declare the frame's field list empty (rzu
    /// <c>TS_CS_CHECK_BOOTH_STARTABLE.h:5-6</c>) and the 7.3 builder (<c>VA 0x48CFD0</c>) writes a
    /// literal <c>7</c>, never recomputed from a count — unlike 700 (<c>BoothPackets.cs</c>,
    /// <c>59 + 16×N</c>).
    /// </summary>
    public const int CheckBoothStartableLength = HeaderSize;

    public const int NameOffset = HeaderSize;
    public const int TypeOffset = 56;
    public const int CountOffset = 57;
    public const int ItemsOffset = StartBoothMinLength;

    /// <summary>
    /// Reads <c>TM_CS_START_BOOTH</c> (700). Only the frame is judged here, in the order of
    /// docs/packet-specs/socle-booths.md §5.3 point 2: a frame shorter than 59 bytes, a declared count
    /// above the server ceiling and a declared count the frame does not actually carry are all refused
    /// before any field is read. <paramref name="result"/> carries the code the handler must answer
    /// with — <see cref="ResultCode.LimitMax"/> for the ceiling, <see cref="ResultCode.InvalidArgument"/>
    /// otherwise.
    /// </summary>
    public static bool TryReadStartBooth(ReadOnlySpan<byte> packet, out StartBoothRequest request,
        out ResultCode result)
    {
        request = null;
        result = ResultCode.InvalidArgument;

        if (packet.Length < StartBoothMinLength)
        {
            return false;
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(CountOffset, 2));
        if (count > MaxBoothItemCount)
        {
            result = ResultCode.LimitMax;
            return false;
        }

        if (packet.Length < StartBoothMinLength + StartBoothItemSize * count)
        {
            return false;
        }

        var items = new BoothOpenItem[count];
        for (var i = 0; i < count; i++)
        {
            var record = packet.Slice(ItemsOffset + i * StartBoothItemSize, StartBoothItemSize);
            items[i] = new BoothOpenItem(
                BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(0, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(record.Slice(4, 4)),
                BinaryPrimitives.ReadInt64LittleEndian(record.Slice(8, 8)));
        }

        result = ResultCode.Success;
        request = new StartBoothRequest(packet[TypeOffset], ReadName(packet), items);
        return true;
    }

    /// <summary>
    /// Reads <c>TM_CS_STOP_BOOTH</c> (701). The frame has no field, so only its header is required;
    /// trailing bytes are ignored, as every other reader of this repository does.
    /// </summary>
    public static bool TryReadStopBooth(ReadOnlySpan<byte> packet)
    {
        return packet.Length >= StopBoothLength;
    }

    /// <summary>
    /// Reads <c>TM_CS_WATCH_BOOTH</c> (702): 11 bytes, the handle of the booth to observe at +7. The
    /// 7.3 client never builds another length for this id, so a shorter frame cannot come from it and
    /// the caller refuses it with <see cref="ResultCode.InvalidArgument"/>
    /// (docs/packet-specs/socle-booths-visibilite.md §3.2, §5.2 point 2). Nothing else is judged here:
    /// whether the handle names a servable booth is a game rule.
    /// </summary>
    public static bool TryReadWatchBooth(ReadOnlySpan<byte> packet, out uint target)
    {
        return TryReadWatchTarget(packet, out target);
    }

    /// <summary>
    /// Reads <c>TM_CS_STOP_WATCH_BOOTH</c> (704): the same 11 byte frame as 702, id apart, and its
    /// <c>target</c> is read for the log only — closing an observation is keyed on the connection, not
    /// on the handle (docs/packet-specs/socle-booths-visibilite.md §3.3, §5.2 point 5).
    /// </summary>
    public static bool TryReadStopWatchBooth(ReadOnlySpan<byte> packet, out uint target)
    {
        return TryReadWatchTarget(packet, out target);
    }

    private static bool TryReadWatchTarget(ReadOnlySpan<byte> packet, out uint target)
    {
        target = 0;
        if (packet.Length < WatchBoothLength)
        {
            return false;
        }

        target = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(WatchBoothTargetOffset, 4));
        return true;
    }

    /// <summary>
    /// Builds <c>TM_SC_WATCH_BOOTH</c> (703) — the only frame of the family a 7.3 client can display
    /// (docs/packet-specs/socle-booths-visibilite.md §2.4). The declared length is exactly
    /// <c>14 + 83 × count</c>: the client copies <c>count</c> records of 83 bytes from +14 without
    /// checking either, so a false size or count would misalign the whole window (§3.6).
    /// <paramref name="items"/> is cut at <see cref="MaxBoothItemCount"/>, so the count on the wire is
    /// always the number of records actually written. Every 75 byte motif goes through
    /// <see cref="ItemFixedInfoWriter"/>, the shared serializer of the inventory and auction families.
    /// </summary>
    public static byte[] BuildWatchBooth(uint target, byte type, IReadOnlyList<BoothWatchItem> items = null)
    {
        var count = Math.Min(items?.Count ?? 0, MaxBoothItemCount);
        var length = WatchBoothHeaderLength + WatchBoothItemSize * count;
        var packet = new byte[length];

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_SC_WATCH_BOOTH);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(WatchBoothTargetOffset, 4), target);
        packet[WatchBoothTypeOffset] = type;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(WatchBoothCountOffset, 2), (ushort)count);

        for (var i = 0; i < count; i++)
        {
            var record = packet.AsSpan(WatchBoothItemsOffset + i * WatchBoothItemSize, WatchBoothItemSize);
            ItemFixedInfoWriter.Write(record.Slice(0, ItemFixedInfoWriter.Size), items[i].Item);
            BinaryPrimitives.WriteInt64LittleEndian(record.Slice(WatchBoothItemGoldOffset, 8), items[i].Gold);
        }

        WriteChecksum(packet);
        return packet;
    }

    /// <summary>
    /// <c>TM_CS_BUY_FROM_BOOTH</c> (705): header (7) + <c>target</c> (4) + <c>count</c> <c>int16</c> (2),
    /// then <c>count</c> items of <b>75</b> bytes. The client's frame builder sizes it
    /// <c>13 + 0x4B × count</c> (<c>SFrame.exe 0x48E665-0x48E668</c>); the booth sheet had
    /// <c>13 + 85 × N</c> from rzu's <c>TS_ITEM_FIXED_INFO</c>, and the client is the authority.
    /// </summary>
    public const int BuyFromBoothHeaderLength = HeaderSize + 6;

    public const int BuyFromBoothTargetOffset = HeaderSize;
    public const int BuyFromBoothCountOffset = HeaderSize + 4;
    public const int BuyFromBoothItemsOffset = HeaderSize + 6;
    public const int BuyFromBoothItemSize = ItemFixedInfoWriter.Size;

    /// <summary>
    /// <c>TM_CS_SELL_TO_BOOTH</c> (706): header (7) + <c>target</c> + <c>item_handle</c> + <c>cnt</c>
    /// <c>int32</c>, 19 bytes (<c>SFrame.exe 0x48E7C5</c> allocates 0x13, one frame per offered item).
    /// </summary>
    public const int SellToBoothLength = HeaderSize + 12;

    /// <summary>
    /// <c>TM_CS_GET_BOOTHS_NAME</c> (707): header (7) + <c>count</c> <c>int32</c> + <c>count</c> handles,
    /// <c>11 + 4 × count</c> (<c>SFrame.exe 0x48F15E-0x48F165</c>).
    /// </summary>
    public const int GetBoothsNameHeaderLength = HeaderSize + 4;

    /// <summary>
    /// The largest handle list a 707 may carry here: the client only asks for the booths it sees, and the
    /// frame must fit the 32 KiB receive buffer anyway.
    /// </summary>
    public const int MaxBoothNameQueries = 1024;

    /// <summary>
    /// One record of <c>TM_SC_GET_BOOTHS_NAME</c> (708): handle (4) + name (49). The client's handler
    /// compares the <c>uint32</c> count at +7 and steps by <c>0x35</c> = 53 from +11
    /// (<c>SFrame.exe 0x6736C7</c>, <c>0x67367B</c>, <c>0x67370B</c>).
    /// </summary>
    public const int BoothNameRecordSize = 4 + BoothNameFieldLength;

    /// <summary><c>TM_SC_BOOTH_CLOSED</c> (709): header (7) + <c>target</c> (4).</summary>
    public const int BoothClosedLength = HeaderSize + 4;

    /// <summary>
    /// <c>TM_SC_BOOTH_TRADE_INFO</c> (710): header (7) + <c>target</c> (4) + <c>is_sell</c> (1) +
    /// <c>count</c> <c>uint16</c> (2), then 83 byte records like 703 (handler <c>SFrame.exe 0x6734D0</c>).
    /// </summary>
    public const int BoothTradeInfoHeaderLength = HeaderSize + 7;

    /// <summary>
    /// Reads <c>TM_CS_BUY_FROM_BOOTH</c> (705). The length must be exactly <c>13 + 75 × count</c> and the
    /// count between 1 and <see cref="MaxBoothItemCount"/>: a booth never lists more, and the frame builder
    /// writes no other length. Only the handle, the code and the requested count of each motif are kept;
    /// the rest describes the owner's item, which the server reads from the owner's bag.
    /// </summary>
    public static bool TryReadBuyFromBooth(ReadOnlySpan<byte> packet, out BuyFromBoothRequest request)
    {
        request = null;
        if (packet.Length < BuyFromBoothHeaderLength)
        {
            return false;
        }

        var count = BinaryPrimitives.ReadInt16LittleEndian(packet.Slice(BuyFromBoothCountOffset, 2));
        if (count < 1 || count > MaxBoothItemCount
            || packet.Length != BuyFromBoothHeaderLength + count * BuyFromBoothItemSize)
        {
            return false;
        }

        var lines = new BoothBuyLine[count];
        for (var i = 0; i < count; i++)
        {
            var item = packet.Slice(BuyFromBoothItemsOffset + i * BuyFromBoothItemSize, BuyFromBoothItemSize);
            lines[i] = new BoothBuyLine(
                BinaryPrimitives.ReadUInt32LittleEndian(item.Slice(0, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(item.Slice(4, 4)),
                BinaryPrimitives.ReadInt64LittleEndian(item.Slice(16, 8)));
        }

        request = new BuyFromBoothRequest(
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(BuyFromBoothTargetOffset, 4)), lines);
        return true;
    }

    /// <summary>Reads <c>TM_CS_SELL_TO_BOOTH</c> (706): exactly 19 bytes.</summary>
    public static bool TryReadSellToBooth(ReadOnlySpan<byte> packet, out SellToBoothRequest request)
    {
        request = default;
        if (packet.Length != SellToBoothLength)
        {
            return false;
        }

        request = new SellToBoothRequest(
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize + 4, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(HeaderSize + 8, 4)));
        return true;
    }

    /// <summary>
    /// Reads <c>TM_CS_GET_BOOTHS_NAME</c> (707): the length must be exactly <c>11 + 4 × count</c>, with
    /// <c>count</c> between 0 and <see cref="MaxBoothNameQueries"/>.
    /// </summary>
    public static bool TryReadGetBoothsName(ReadOnlySpan<byte> packet, out uint[] handles)
    {
        handles = null;
        if (packet.Length < GetBoothsNameHeaderLength)
        {
            return false;
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(HeaderSize, 4));
        if (count < 0 || count > MaxBoothNameQueries || packet.Length != GetBoothsNameHeaderLength + count * 4)
        {
            return false;
        }

        handles = new uint[count];
        for (var i = 0; i < count; i++)
        {
            handles[i] = BinaryPrimitives.ReadUInt32LittleEndian(
                packet.Slice(GetBoothsNameHeaderLength + i * 4, 4));
        }

        return true;
    }

    /// <summary>
    /// Builds <c>TM_SC_GET_BOOTHS_NAME</c> (708): <c>11 + 53 × count</c>, each name written into its 49
    /// byte field and cut at 48 bytes, so the field always keeps its nul.
    /// </summary>
    public static byte[] BuildGetBoothsName(IReadOnlyList<BoothName> names)
    {
        var count = names?.Count ?? 0;
        var length = GetBoothsNameHeaderLength + BoothNameRecordSize * count;
        var packet = new byte[length];

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_SC_GET_BOOTHS_NAME);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), (uint)count);

        for (var i = 0; i < count; i++)
        {
            var record = packet.AsSpan(GetBoothsNameHeaderLength + i * BoothNameRecordSize, BoothNameRecordSize);
            BinaryPrimitives.WriteUInt32LittleEndian(record.Slice(0, 4), names[i].Handle);
            var name = names[i].Name ?? Array.Empty<byte>();
            name.AsSpan(0, Math.Min(name.Length, BoothNameFieldLength - 1)).CopyTo(record.Slice(4));
        }

        WriteChecksum(packet);
        return packet;
    }

    /// <summary>Builds <c>TM_SC_BOOTH_CLOSED</c> (709): the handle of the booth that closed.</summary>
    public static byte[] BuildBoothClosed(uint target)
    {
        var packet = new byte[BoothClosedLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), BoothClosedLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_SC_BOOTH_CLOSED);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), target);
        WriteChecksum(packet);
        return packet;
    }

    /// <summary>
    /// Builds <c>TM_SC_BOOTH_TRADE_INFO</c> (710): <c>14 + 83 × count</c>, the traded items with their
    /// traded count and unit price, cut at <see cref="MaxBoothItemCount"/> like 703.
    /// </summary>
    public static byte[] BuildBoothTradeInfo(uint target, bool isSell, IReadOnlyList<BoothWatchItem> items)
    {
        var count = Math.Min(items?.Count ?? 0, MaxBoothItemCount);
        var length = BoothTradeInfoHeaderLength + WatchBoothItemSize * count;
        var packet = new byte[length];

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_SC_BOOTH_TRADE_INFO);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize, 4), target);
        packet[HeaderSize + 4] = isSell ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(HeaderSize + 5, 2), (ushort)count);

        for (var i = 0; i < count; i++)
        {
            var record = packet.AsSpan(BoothTradeInfoHeaderLength + i * WatchBoothItemSize, WatchBoothItemSize);
            ItemFixedInfoWriter.Write(record.Slice(0, ItemFixedInfoWriter.Size), items[i].Item);
            BinaryPrimitives.WriteInt64LittleEndian(record.Slice(WatchBoothItemGoldOffset, 8), items[i].Gold);
        }

        WriteChecksum(packet);
        return packet;
    }

    /// <summary>
    /// Reads <c>TM_CS_CHECK_BOOTH_STARTABLE</c> (711), the client's "can a booth be started here?"
    /// frame (docs/packet-specs/711-check-booth-startable.md §3 and §5.2). The frame has no field at
    /// all, so 711 is the one reader of this family that requires the header <b>and its declared
    /// length exactly</b>: the 7.3 builder writes the literal <c>7</c> and never recomputes it, so a
    /// longer frame is an anomaly, not the padded frame the <c>&gt;=</c> readers of this repository
    /// tolerate. Nothing is decided from the frame — no zone, level, distance or booth-state rule is
    /// established for this packet, so the caller only logs it.
    /// </summary>
    public static bool TryReadCheckBoothStartable(ReadOnlySpan<byte> packet)
    {
        return packet.Length == CheckBoothStartableLength;
    }

    /// <summary>The 49 byte name field, cut at the first nul byte — what the client's strncpy wrote.</summary>
    private static byte[] ReadName(ReadOnlySpan<byte> packet)
    {
        var field = packet.Slice(NameOffset, BoothNameFieldLength);
        var length = field.IndexOf((byte)0);
        if (length < 0)
        {
            length = field.Length;
        }

        return field.Slice(0, length).ToArray();
    }

    /// <summary>
    /// The client's own rule, recomputed after the body is written: the checksum is the sum of the six
    /// header bytes, written at +6 (docs/packet-specs/socle-booths-visibilite.md §3.1). The receive loop
    /// of this repository rejects a frame that fails it, so a 703 must carry a valid one.
    /// </summary>
    private static void WriteChecksum(byte[] packet)
    {
        var sum = 0;
        for (var i = 0; i < 6; i++)
        {
            sum += packet[i];
        }

        packet[6] = (byte)sum;
    }
}
