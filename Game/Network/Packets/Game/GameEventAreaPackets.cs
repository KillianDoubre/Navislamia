using System;
using System.Buffers.Binary;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// <c>TM_CS_ENTER_EVENT_AREA</c> (15) and <c>TM_CS_LEAVE_EVENT_AREA</c> (16): the client's claim that
/// it entered or left an event area. The claim is an index, never an authority — the server checks it
/// against its own polygon and position (see <c>EventAreaService</c>).
/// </summary>
public static class GameEventAreaPackets
{
    private const int HeaderSize = 7;

    /// <summary>Header (7) + <c>event_area_id</c> (int32) + <c>area_index</c> (int32).</summary>
    public const int PacketLength = HeaderSize + 8;

    /// <summary>
    /// <c>area_index</c> is carried through untouched: nothing in rzu, NGemity or the client says
    /// which polygon of the area the index designates (the <c>.nfe</c> reader even overwrites
    /// multi-polygon areas), so the packet only transports and logs it.
    /// </summary>
    public readonly record struct EventAreaRequest(int EventAreaId, int AreaIndex);

    public static bool TryReadEventAreaRequest(ReadOnlySpan<byte> packet, out EventAreaRequest request)
    {
        if (packet.Length != PacketLength)
        {
            request = default;
            return false;
        }

        request = new EventAreaRequest(
            BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(HeaderSize, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(HeaderSize + 4, 4)));

        return true;
    }
}
