using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// The two merchant packets, both server to client and both on the 7-byte header of
/// <see cref="GameCharacterPackets"/>.
/// <para>
/// <c>TM_SC_MARKET</c> (250) is what opens the trade window: the handle of the NPC whose dialog was
/// selected, then one 16-byte line per catalogue entry (<c>code</c>, absolute <c>price</c>,
/// <c>huntaholic_point</c>) — <c>13 + 16 × n</c> bytes. <c>arena_point</c> is gated <c>&gt;= EPIC_8_1</c>
/// and is therefore absent in 7.3; rzu's trailing <c>4 × n</c> padding has no version gate but the 7.3
/// client reads <c>count × 16</c> contiguous bytes from offset 13 (<c>SFrame.exe</c> <c>0x66ffa5</c>), so
/// the frame is emitted compact and by field position only.
/// </para>
/// <para>
/// <c>TM_SC_NPC_TRADE_INFO</c> (240) is the echo of a single buy or sell, not a catalogue: it has no
/// producer outside the <c>TM_CS_BUY_ITEM</c> (251) and <c>TM_CS_SELL_ITEM</c> (252) handlers, which are
/// out of scope here. The builder is delivered with the offsets of the 36-byte frame so those two lots
/// do not have to re-derive them.
/// </para>
/// See docs/packet-specs/socle-marche-npc.md.
/// </summary>
public static class GameTradePackets
{
    private const int HeaderSize = 7;

    /// <summary>7-byte header + <c>npc_handle</c> (4) + line count (2).</summary>
    public const int MarketInfoHeaderSize = 13;

    /// <summary><c>code</c> (4) + <c>price</c> (8) + <c>huntaholic_point</c> (4), no padding.</summary>
    public const int MarketLineSize = 16;

    /// <summary>7-byte header + <c>is_sell</c> (1) + 6 aligned fields: 1 + 7 × 4.</summary>
    public const int NpcTradeInfoSize = 36;

    /// <summary>
    /// <c>TM_SC_MARKET</c> (250). The catalogue must be non-empty: no producer of a size-13 frame
    /// (<c>n = 0</c>) is known, so the market service refuses instead of sending one.
    /// </summary>
    public static byte[] BuildMarketInfo(uint npcHandle, IReadOnlyList<MarketLine> lines)
    {
        if (lines == null)
        {
            throw new ArgumentNullException(nameof(lines));
        }

        if (lines.Count > ushort.MaxValue)
        {
            throw new ArgumentException("TM_SC_MARKET carries its line count in a UInt16", nameof(lines));
        }

        var packet = new byte[MarketInfoHeaderSize + (MarketLineSize * lines.Count)];
        var span = packet.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4, 2), (ushort)GamePackets.TM_SC_MARKET);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(7, 4), npcHandle);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(11, 2), (ushort)lines.Count);

        var offset = MarketInfoHeaderSize;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), line.Code);
            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(offset + 4, 8), line.Price);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset + 12, 4), line.HuntaholicPoint);
            offset += MarketLineSize;
        }

        WriteChecksum(packet);
        return packet;
    }

    /// <summary>
    /// <c>TM_SC_NPC_TRADE_INFO</c> (240). <paramref name="isSell"/> is written as the client's
    /// <c>int8</c> flag (0 = bought, 1 = sold); <paramref name="price"/> is the whole amount of the
    /// transaction and <paramref name="target"/> the handle of the merchant NPC.
    /// </summary>
    public static byte[] BuildNpcTradeInfo(bool isSell, int code, long count, long price,
        int huntaholicPoint, uint target)
    {
        var packet = new byte[NpcTradeInfoSize];
        var span = packet.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4, 2), (ushort)GamePackets.TM_SC_NPC_TRADE_INFO);
        span[HeaderSize] = isSell ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(8, 4), code);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(12, 8), count);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(20, 8), price);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(28, 4), huntaholicPoint);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(32, 4), target);
        WriteChecksum(packet);
        return packet;
    }

    /// <summary>
    /// The client's own header checksum: the low byte of the sum of the six bytes before it. Same rule
    /// as <see cref="GameNpcDialogPackets"/> and <see cref="GameCharacterPackets"/>.
    /// </summary>
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
