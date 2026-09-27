using System;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// The client to server half of the HuntaHolic instance family, <c>TM_CS_HUNTAHOLIC_BEGIN_HUNTING</c> (4011).
///
/// The id is unconditional in rzu (<c>X(4011, true)</c>, which expands to <c>if (true) id = id_;</c>): the file
/// holds that single <c>X(...)</c> entry, so 4011 is the same in Epic 7.3 as in every other version of the
/// family, and it is the whole id list of this frame. The frame is <b>7 bytes</b> — the bare header, with no
/// payload at all: rzu's <c>_(...)</c> macro list is empty, NGemity's <c>_DEF</c> is empty too, and the 7.3
/// client writes the length 7 in hard in its only frame constructor for this id (<c>0x4c93d0</c>, id
/// <c>0xfab</c> at <c>0x4c93e2</c>, checksum over the first six bytes at <c>0x4c93f8</c>-<c>0x4c9405</c>) and
/// never touches a byte past offset 6.
///
/// Nothing answers a 4011: the client's own receive dispatcher routes the id to its "unhandled message" branch,
/// so a server to client 4011 would be logged as unknown rather than interpreted, and no other packet of the
/// family carries this answer. The play flow that follows (<c>4012</c>, <c>4009</c>, <c>4006</c>/<c>4007</c>,
/// <c>4010</c>) has its formats established but its order and cadence are not; it is out of this lot.
/// See docs/packet-specs/4011-huntaholic-begin-hunting.md §2.1, §3, §5.2, §5.4.
/// </summary>
public static class GameHuntaholicPackets
{
    private const int HeaderSize = 7;

    /// <summary>
    /// Total size of <c>TM_CS_HUNTAHOLIC_BEGIN_HUNTING</c> (4011): the 7-byte header, no payload at all. The
    /// 7.3 client writes that length in hard (<c>mov DWORD PTR [ebp-0x7],0x7</c> at <c>0x4c93eb</c>) and never
    /// writes a byte past offset 6, so the header is the entire frame.
    /// </summary>
    public const int BeginHuntingLength = HeaderSize;

    /// <summary>
    /// <c>TM_CS_HUNTAHOLIC_BEGIN_HUNTING</c> (4011) is the "start the hunt" gesture of the HuntaHolic instance
    /// window and carries no payload: there is nothing to read and no field to extract. Only the exact 7-byte
    /// form is accepted — a shorter frame is truncated, and a longer one carries bytes no field accounts for
    /// (an 11-byte frame is the <c>4009</c> shape and 6 bytes is a cut header), so the sender is not the 7.3
    /// client. The frame's checksum byte at offset 6 is not verified here, as in every other packet of the
    /// family (an assumed gap, stated in the sheet §3 rather than papered over); the receive loop does check it
    /// before dispatching.
    /// </summary>
    public static bool IsBeginHunting(ReadOnlySpan<byte> packet)
    {
        return packet.Length == BeginHuntingLength;
    }
}
