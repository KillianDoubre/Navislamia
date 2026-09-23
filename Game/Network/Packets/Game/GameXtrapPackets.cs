using System;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// Frame layout of the XTrap integrity pair: <c>TM_CS_XTRAP_CHECK</c> (59), client to server, and its
/// server to client counterpart <c>TM_SC_XTRAP_CHECK</c> (58). Both are the same frame with a different
/// id: a 7 byte header followed by a fixed <c>uint8[128]</c> payload and nothing else.
/// </summary>
/// <remarks>
/// The layout comes from rzu <c>TS_CS_XTRAP_CHECK.h</c> on the Epic 7.3 side of its version gating: the
/// payload is declared <c>_(array)(uint8_t, pCheckBuffer, 128)</c>, the three argument form that expands
/// to <c>SIZE_F_ARRAY3</c> — 128 unconditional bytes with <strong>no length field</strong>, unlike
/// <c>TM_CS_ANTI_HACK</c> (54) which carries an <c>nLength</c>. The total is therefore a constant
/// 135 bytes: no variant, no padding, no trailing field.
/// <para>
/// rzu gates the id alone: 59 below <c>EPIC_9_6_3</c>, 1059 from 9.6.3 on. An Epic 7.3 client is below
/// that step, so its id is 59 and 1059 is not part of this packet.
/// </para>
/// <para>
/// The content of <c>pCheckBuffer</c> is <strong>not established</strong>: no reference says what the
/// buffer holds. The reader below hands back the raw bytes without interpreting, comparing or
/// validating them — nothing in rzu, NGemity or the 7.3 client authorises a rule about the content.
/// See <c>docs/packet-specs/59-xtrap-check.md</c>.
/// </para>
/// </remarks>
public static class GameXtrapPackets
{
    private const int HeaderSize = 7;

    /// <summary>Total size of the frame, header included: 135 bytes.</summary>
    public const int XtrapCheckPacketSize = HeaderSize + XtrapCheckBufferSize;

    /// <summary>Size of the fixed payload after the 7 byte header: 128 bytes.</summary>
    public const int XtrapCheckBufferSize = 128;

    /// <summary>Offset of the first payload byte in the frame: the header is 7 bytes wide.</summary>
    public const int XtrapCheckBufferOffset = HeaderSize;

    /// <summary>
    /// Reads the fixed 128 byte payload of a XTrap check frame. The frame has no length field, so the
    /// exact 135 byte form is the only one accepted: a short or padded frame is refused rather than
    /// partially read — the specification defines no answer at all for a request of another length,
    /// exactly as for <c>TM_CS_GET_REGION_INFO</c> (550).
    /// </summary>
    /// <param name="packet">The whole frame, header included.</param>
    /// <param name="checkBuffer">
    /// A view of the caller's own bytes, starting at offset 7 and 128 bytes long. Handed back unread:
    /// its content is not established and nothing here interprets it.
    /// </param>
    public static bool TryReadXtrapCheck(ReadOnlySpan<byte> packet, out ReadOnlySpan<byte> checkBuffer)
    {
        if (packet.Length != XtrapCheckPacketSize)
        {
            checkBuffer = ReadOnlySpan<byte>.Empty;
            return false;
        }

        checkBuffer = packet.Slice(XtrapCheckBufferOffset, XtrapCheckBufferSize);
        return true;
    }
}
