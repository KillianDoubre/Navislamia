using System;
using System.Buffers.Binary;
using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// The Epic 7.3 base item motif: 75 bytes on the wire, named <c>TS_ITEM_FIXED_INFO</c> by rzu and
/// <c>TS_ITEM_BASE_INFO</c> by NGemity — the same layout under two names. It stops before the three
/// position fields <c>TM_SC_INVENTORY</c> appends (<c>wear_position</c>, <c>own_summon_handle</c>,
/// <c>index</c>), which is why the auction family carries 75 bytes per item and the inventory record
/// is 85. rzu only emits those position fields from <c>EPIC_9_8_1</c> on. See
/// docs/packet-specs/socle-encheres.md §3.7 and §4.6.
/// </summary>
public readonly record struct ItemFixedInfo(
    uint Handle,
    int Code,
    long Uid,
    long Count,
    int EtherealDurability,
    uint Endurance,
    byte Enhance,
    byte Level,
    uint Flag,
    long[] Sockets,
    int RemainTime,
    byte ElementalEffectType,
    int ElementalEffectRemainTime,
    int ElementalEffectAttackPoint,
    int ElementalEffectMagicPoint,
    int AppearanceCode)
{
    /// <summary>
    /// Reads an inventory item into the wire motif. The two fields the inventory serializer never
    /// filled stay zero: <c>elemental_effect.remain_time</c> (offset 59) and <c>appearance_code</c>
    /// (offset 71). The content of <c>appearance_code</c> is not established for this client — 0 is
    /// the only value the repository has ever put on the wire (docs/packet-specs/socle-encheres.md
    /// §8.3).
    /// </summary>
    public static ItemFixedInfo FromItem(ItemEntity item)
    {
        return new ItemFixedInfo(
            Handle: (uint)item.Id,
            Code: (int)item.ItemResourceId,
            Uid: item.Id,
            Count: item.Amount,
            EtherealDurability: item.EtherealDurability,
            Endurance: (uint)Math.Max(0, item.Endurance),
            Enhance: (byte)item.Enhance,
            Level: (byte)item.Level,
            Flag: unchecked((uint)item.Flag),
            Sockets: item.SocketItemIds,
            RemainTime: item.RemainingTime,
            ElementalEffectType: (byte)item.ElementalEffectType,
            ElementalEffectRemainTime: 0,
            ElementalEffectAttackPoint: item.ElementalEffectAttackPoint,
            ElementalEffectMagicPoint: item.ElementalEffectMagicPoint,
            AppearanceCode: 0);
    }
}

/// <summary>
/// Serializes <see cref="ItemFixedInfo"/>: the single writer of the 75-byte motif, shared by
/// <c>TM_SC_INVENTORY</c> (which appends its ten position bytes) and by the auction family
/// (1301/1303/1305, which carry the motif alone). Both callers must go through here so the
/// <c>appearance_code</c> decision of §4.6 cannot drift between them.
/// </summary>
public static class ItemFixedInfoWriter
{
    /// <summary>Total size of the motif, including <c>appearance_code</c> at offset 71.</summary>
    public const int Size = 75;

    private const int SocketCount = 4;
    private const int SocketOffset = 38;
    private const int SocketSize = 4;

    public static void Write(Span<byte> span, in ItemFixedInfo info)
    {
        if (span.Length < Size)
        {
            throw new ArgumentOutOfRangeException(nameof(span), span.Length,
                $"item_info needs {Size} bytes");
        }

        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0, 4), info.Handle);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), info.Code);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(8, 8), info.Uid);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(16, 8), info.Count);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(24, 4), info.EtherealDurability);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(28, 4), info.Endurance);
        span[32] = info.Enhance;
        span[33] = info.Level;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(34, 4), info.Flag);

        // The four sockets are always written, zeros included: an entry is copied to the client in one
        // block, so a socket left unwritten would ship whatever the buffer held.
        for (var i = 0; i < SocketCount; i++)
        {
            var socket = info.Sockets != null && i < info.Sockets.Length ? (int)info.Sockets[i] : 0;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(SocketOffset + i * SocketSize, SocketSize), socket);
        }

        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(54, 4), info.RemainTime);
        span[58] = info.ElementalEffectType;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(59, 4), info.ElementalEffectRemainTime);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(63, 4), info.ElementalEffectAttackPoint);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(67, 4), info.ElementalEffectMagicPoint);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(71, 4), info.AppearanceCode);
    }
}
