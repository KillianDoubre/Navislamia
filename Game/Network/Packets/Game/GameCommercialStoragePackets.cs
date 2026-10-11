using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// The commercial storage (item shop container) socle, both packets server to client and both on the
/// 7-byte header of <see cref="GameCharacterPackets"/>.
/// <para>
/// <c>TM_SC_COMMERCIAL_STORAGE_INFO</c> (10003) is the pair of counters the server pushes when the
/// character enters the world: <c>total_item_count</c> at offset 7 then <c>new_item_count</c> at offset 9
/// — a fixed 11-byte frame, exactly what rzu sends at 0/0 at the end of its own world entry sequence
/// (<c>Character.cpp:308-311</c>).
/// </para>
/// <para>
/// <c>TM_SC_COMMERCIAL_STORAGE_LIST</c> (10004) is the container itself: a <c>uint16 count</c> at offset
/// 7 then <c>count</c> contiguous 10-byte lines from offset 9 — <c>9 + 10 × n</c> bytes, 9 for an empty
/// container. The 7.3 client trusts <c>count</c> and never compares it with <c>Length</c>: it walks
/// <c>count</c> lines of 10 bytes from offset 9 (<c>SFrame.exe</c> <c>lea edi,[ebx+0x9]</c> VA
/// <c>0x67ed93</c>, <c>add edi,0xa</c> VA <c>0x67edca</c>), so the frame must be emitted compact and the
/// count must match it.
/// </para>
/// <para>
/// There is no builder for <c>TM_CS_TAKEOUT_COMMERCIAL_ITEM</c> (10005) here, on purpose: the server never
/// emits it (see docs/packet-specs/socle-stockage-commercial.md §5.3).
/// </para>
/// See docs/packet-specs/socle-stockage-commercial.md.
/// </summary>
public static class GameCommercialStoragePackets
{
    private const int HeaderSize = 7;

    /// <summary>7-byte header + <c>total_item_count</c> (2) + <c>new_item_count</c> (2).</summary>
    public const int CommercialStorageInfoSize = 11;

    /// <summary>7-byte header + <c>count</c> (2): the frame of an empty container.</summary>
    public const int CommercialStorageListHeaderSize = 9;

    /// <summary><c>commercial_item_uid</c> (4) + <c>code</c> (4) + <c>count</c> (2), no padding.</summary>
    public const int CommercialStorageItemSize = 10;

    /// <summary>
    /// <c>TM_SC_COMMERCIAL_STORAGE_INFO</c> (10003). The two counters travel in the order rzu declares
    /// them; no version gate applies to either field in 7.3.
    /// </summary>
    public static byte[] BuildCommercialStorageInfo(ushort totalItemCount, ushort newItemCount)
    {
        var packet = new byte[CommercialStorageInfoSize];
        var span = packet.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4, 2), (ushort)GamePackets.TM_SC_COMMERCIAL_STORAGE_INFO);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(HeaderSize, 2), totalItemCount);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(HeaderSize + 2, 2), newItemCount);

        WriteChecksum(packet);
        return packet;
    }

    /// <summary>
    /// <c>TM_SC_COMMERCIAL_STORAGE_LIST</c> (10004). An empty list is a valid 9-byte frame (<c>count</c>
    /// = 0), a state the 7.3 client handles explicitly; <c>count</c> is written from
    /// <paramref name="items"/> alone, so the announced count can never disagree with the frame length.
    /// </summary>
    public static byte[] BuildCommercialStorageList(IReadOnlyList<(uint Uid, int Code, ushort Count)> items)
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        if (items.Count > ushort.MaxValue)
        {
            throw new ArgumentException("TM_SC_COMMERCIAL_STORAGE_LIST carries its line count in a UInt16",
                nameof(items));
        }

        var packet = new byte[CommercialStorageListHeaderSize + (CommercialStorageItemSize * items.Count)];
        var span = packet.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4, 2), (ushort)GamePackets.TM_SC_COMMERCIAL_STORAGE_LIST);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(HeaderSize, 2), (ushort)items.Count);

        var offset = CommercialStorageListHeaderSize;
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), item.Uid);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset + 4, 4), item.Code);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset + 8, 2), item.Count);
            offset += CommercialStorageItemSize;
        }

        WriteChecksum(packet);
        return packet;
    }

    /// <summary>7-byte header + three <c>int32</c> + <c>raw_server_name[32]</c> (docs/packet-specs/10000-open-item-shop.md §3.2).</summary>
    public const int OpenItemShopSize = 51;

    /// <summary>The bytes <c>raw_server_name</c> can hold before its terminating zero.</summary>
    public const int ServerNameCapacity = 31;

    /// <summary>
    /// <c>TM_SC_OPEN_ITEM_SHOP</c> (10001): <c>client_id</c> @7, <c>account_id</c> @11, <c>one_time_password</c> @15,
    /// <c>raw_server_name</c> @19 zero padded to 32 bytes, the official <c>onOpenItemShop</c> (GameMessage.cpp:5061-5070).
    /// The client turns it into its own <c>open_item_shop</c> command and builds the shop URL from it. A name longer
    /// than 31 bytes is cut, never left without its zero (the client copies it with <c>strcpy</c>).
    /// </summary>
    public static byte[] BuildOpenItemShop(int clientId, int accountId, int oneTimePassword, string serverName)
    {
        var packet = new byte[OpenItemShopSize];
        var span = packet.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4, 2), (ushort)GamePackets.TM_SC_OPEN_ITEM_SHOP);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(HeaderSize, 4), clientId);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(HeaderSize + 4, 4), accountId);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(HeaderSize + 8, 4), oneTimePassword);
        var name = System.Text.Encoding.ASCII.GetBytes(serverName ?? string.Empty);
        name.AsSpan(0, Math.Min(name.Length, ServerNameCapacity)).CopyTo(span.Slice(HeaderSize + 12));

        WriteChecksum(packet);
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
