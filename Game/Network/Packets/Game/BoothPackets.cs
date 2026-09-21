using System;
using System.Buffers.Binary;
using Navislamia.Game.Network.Packets;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// <c>TS_BOOTH_OPEN_ITEM_INFO</c>: the handle the client selected in its bag, the stack count and
/// the price, both kept verbatim — the socle never resolves the handle against the inventory and
/// never interprets the unit of the price (docs/packet-specs/socle-booths.md §7.3 and §7.8).
/// </summary>
public readonly record struct BoothOpenItem(uint ItemHandle, int Count, long Gold);

/// <summary>
/// A parsed and wire-valid <c>TM_CS_START_BOOTH</c>. <see cref="Name"/> holds the raw client bytes
/// up to the first nul (48 at most): the client copies them with <c>strncpy</c> and no conversion,
/// so the socle re-encodes nothing (docs/packet-specs/socle-booths.md §7.9).
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
}
