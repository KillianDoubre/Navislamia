using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// <c>TM_SC_MIX_RESULT</c> (257) at Epic 7.3: header (7) + <c>count</c> <c>uint32</c> @7 + <c>count</c> item
/// handles from @11, <b>11 + 4 × M</b> bytes. rzu's <c>type</c> field is gated <c>>= EPIC_8_1</c> and absent
/// (docs/packet-specs/socle-artisanat-ressources.md §3.2). The handles are the items whose state the client
/// must refresh: the target of a successful craft, none after a failure.
/// </summary>
public static class GameCraftingPackets
{
    public const int MixResultHeaderLength = 11;

    public static byte[] BuildMixResult(IReadOnlyList<uint> handles)
    {
        var count = handles?.Count ?? 0;
        var length = MixResultHeaderLength + 4 * count;
        var packet = new byte[length];

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_SC_MIX_RESULT);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), (uint)count);
        for (var i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(MixResultHeaderLength + 4 * i, 4), handles[i]);
        }

        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        packet[6] = checksum;
        return packet;
    }
}
