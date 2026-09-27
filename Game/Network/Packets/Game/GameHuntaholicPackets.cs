using System;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// The client to server half of the HuntaHolic lobby family, <c>TM_CS_HUNTAHOLIC_LEAVE_INSTANCE</c> (4005).
///
/// The id is unconditional in rzu (<c>X(4005, true)</c>, which expands to <c>if (true) id = id_;</c>): the file
/// holds that single <c>X(...)</c> entry, so 4005 is the same in Epic 7.3 as in every other version, and it is
/// the whole list of ids of this frame. The frame is <b>7 bytes</b> — the bare header, with no payload at all:
/// rzu's <c>_(...)</c> macro list is empty, NGemity's <c>_DEF</c> is empty too, and the 7.3 client writes the
/// length 7 in hard in its only frame constructor for this id (<c>0x4c939b</c>, id <c>0xfa5</c> at
/// <c>0x4c9392</c>) and never touches a byte past offset 6.
///
/// Nothing answers a 4005: the client's own receive dispatcher routes the id to its "unhandled message" branch,
/// so a server to client 4005 would be logged as unknown rather than interpreted, and no other server to client
/// packet of the family carries this answer.
/// See docs/packet-specs/4005-huntaholic-leave-instance.md §3, §5.2.
/// </summary>
public static class GameHuntaholicPackets
{
    private const int HeaderSize = 7;

    /// <summary>
    /// Total size of TM_CS_HUNTAHOLIC_LEAVE_INSTANCE (4005): the 7-byte header, no payload at all. The 7.3
    /// client writes that length in hard at <c>0x4c939b</c> and never writes a byte past offset 6, so the
    /// header is the entire frame.
    /// </summary>
    public const int LeaveInstanceLength = HeaderSize;

    /// <summary>
    /// TM_CS_HUNTAHOLIC_LEAVE_INSTANCE (4005) is the "leave the instance" gesture of the HuntaHolic family and
    /// carries no payload: there is nothing to read and no field to extract. Only the exact 7-byte form is
    /// accepted — a shorter frame is truncated, and a longer one carries bytes no field accounts for, so the
    /// sender is not the 7.3 client. The frame's checksum byte at offset 6 is not verified, here as in every
    /// other packet of the family (an assumed gap, stated in the sheet §3 rather than papered over).
    /// </summary>
    public static bool IsLeaveInstance(ReadOnlySpan<byte> packet)
    {
        return packet.Length == LeaveInstanceLength;
    }
}
