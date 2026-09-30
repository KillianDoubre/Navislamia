using System;
using System.Buffers.Binary;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// <c>TS_TRADE</c> (280 below <c>EPIC_9_6_3</c>), the only frame of the player trade, in both directions:
/// <c>target_player</c> @7, <c>mode</c> @11, then an inventory record (<c>TS_ITEM_TRADE_INFO</c>) @12 —
/// 85 bytes in this client, so <b>97</b> in all, the size the official server writes (<c>0x61</c>,
/// <c>onAddItem</c>). The record's <c>uid</c> sits @20 and its <c>count</c> @28: an item is named by its
/// uid, and gold travels in the count. See <c>docs/packet-specs/280-trade.md</c>.
/// </summary>
public static class PlayerTradePackets
{
    private const int HeaderSize = 7;
    public const int Size = HeaderSize + 4 + 1 + GameCharacterPackets.InventoryItemSize;
    private const int ItemOffset = HeaderSize + 5;
    private const int UidOffset = ItemOffset + 8;
    private const int CountOffset = ItemOffset + 16;

    public readonly record struct TradeRequest(uint TargetPlayer, TradeMode Mode, long ItemUid, long Count);

    /// <summary>Reads a 280 of exactly <see cref="Size"/> bytes; anything else is refused.</summary>
    public static bool TryRead(ReadOnlySpan<byte> packet, out TradeRequest request)
    {
        request = default;
        if (packet.Length != Size)
        {
            return false;
        }

        request = new TradeRequest(
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4)),
            (TradeMode)(sbyte)packet[HeaderSize + 4],
            BinaryPrimitives.ReadInt64LittleEndian(packet.Slice(UidOffset, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(packet.Slice(CountOffset, 8)));
        return true;
    }

    /// <summary>
    /// A 280 naming <paramref name="targetPlayer"/>. With an <paramref name="item"/>, its full record is
    /// written and its count replaced by <paramref name="count"/> — the count traded, not the stack's (the
    /// official server overwrites it, NGemity sends the stack); without one, <paramref name="count"/> alone
    /// is written, which is how gold travels.
    /// </summary>
    public static byte[] Build(uint targetPlayer, TradeMode mode, ItemEntity item = null, long count = 0)
    {
        var packet = new byte[Size];
        var span = packet.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0, 4), Size);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4, 2), (ushort)GamePackets.TM_TRADE);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(HeaderSize, 4), targetPlayer);
        packet[HeaderSize + 4] = (byte)mode;
        if (item is not null)
        {
            GameCharacterPackets.WriteInventoryItem(span.Slice(ItemOffset, GameCharacterPackets.InventoryItemSize), item);
        }

        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(CountOffset, 8), count);

        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        packet[6] = checksum;
        return packet;
    }
}

/// <summary>
/// <c>TRADE_MODE</c> (NGemity <c>Player.h</c>), confirmed by the official server's jump table in
/// <c>onTrade</c>: 2 and 9 are server answers, ignored when a client sends them.
/// </summary>
public enum TradeMode : sbyte
{
    Request = 0,
    Accept = 1,
    Begin = 2,
    Cancel = 3,
    Reject = 4,
    AddItem = 5,
    AddGold = 6,
    Freeze = 7,
    Confirm = 8,
    Process = 9,
    RemoveItem = 10,
    ModifyCount = 11,
}
