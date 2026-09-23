using System;
using System.Buffers.Binary;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// <c>TS_SC_OPEN_STORAGE</c> (211), the frame that opens the character storage window. Nothing else is
/// sent on opening: the contents travel in the inventory frame (207) and the stored gold in the
/// <c>storage_gold</c> property. See docs/packet-specs/211-212-storage.md §3.2 and §5.3.
/// </summary>
/// <remarks>
/// The builder lives in its own file rather than next to the inventory builders of
/// <c>GameCharacterPackets</c>: that file is the collision zone of the item family, and a header-only
/// frame needs no item writer. The two private helpers below repeat its six header bytes byte for byte
/// (length, id, checksum = sum of the first six bytes).
/// </remarks>
public static class GameStoragePackets
{
    private const int HeaderSize = 7;

    /// <summary>
    /// The Epic 7.3 form of <c>TS_SC_OPEN_STORAGE</c>: the header alone, 7 bytes. rzu only carries
    /// <c>maxStorageItemCount</c> from <c>EPIC_7_4</c> on (librzu/src/packets/GameClient/TS_SC_OPEN_STORAGE.h:8)
    /// and NGemity builds the frame empty and says so ("jk, packet is empty",
    /// Chihiro/src/Network/Messages.cpp:1001-1009). The frame has no field of its own here.
    /// </summary>
    public static byte[] BuildOpenStorage()
    {
        var packet = new byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), HeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_SC_OPEN_STORAGE);
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
