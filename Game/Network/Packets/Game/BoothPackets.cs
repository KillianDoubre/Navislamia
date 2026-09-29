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
/// The Epic 7.3 layout of the two client packets of the player booth family
/// (docs/packet-specs/socle-booths.md §3). Both sizes are measured in the 7.3 client itself
/// (<c>SFrame.exe</c>, frame builders <c>VA 0x48CBD0</c> for 700 and <c>VA 0x48CC20</c> for 701):
/// <c>TM_CS_START_BOOTH</c> is <c>59 + 16×N</c> bytes, <c>TM_CS_STOP_BOOTH</c> is 7 bytes and carries
/// no field at all. Nothing here judges the values — a frame the client can build is read as it is;
/// the game rules that accept or refuse it live in <c>BoothRules</c>.
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
