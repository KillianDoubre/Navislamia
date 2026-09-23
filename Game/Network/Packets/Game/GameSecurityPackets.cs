using System;
using System.Buffers.Binary;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// <c>TM_CS_SECURITY_NO</c> (9005), the security password the 7.3 client sends back after the server asked
/// for it with <c>TM_SC_REQUEST_SECURITY_NO</c> (9004). On the wire the frame is 30 bytes: the 7-byte
/// header, a signed <c>int32 mode</c> at offset 7, then a fixed 19-byte container for the code at offset
/// 11 — 18 usable characters at most, the 19th byte left at zero by the client.
/// See docs/packet-specs/9005-security-no.md §3.
/// </summary>
public static class GameSecurityPackets
{
    private const int HeaderSize = 7;
    private const int ModeSize = 4;
    private const int SecurityNoSize = 19;

    /// <summary>
    /// Total size of the 7.3 frame, <c>7 + 4 + 19</c>. The client writes it in hard (<c>0x1e</c>, VR
    /// <c>0x48cfb0</c>), so the exact form is the only one accepted: a short or padded frame is refused
    /// rather than partially read, exactly like <see cref="GameActionPackets.TryReadGetRegionInfo"/>.
    /// </summary>
    public const int PacketLength = HeaderSize + ModeSize + SecurityNoSize;

    /// <summary>
    /// Reads a 9005: <paramref name="mode"/> and the bounded <paramref name="securityNo"/>.
    /// </summary>
    /// <remarks>
    /// <c>mode</c> is deliberately never validated: rzu names <c>0</c>/<c>1</c>/<c>2</c> (none / open
    /// storage / delete character) but its own authentication test emits <c>42</c>, and no source fixes the
    /// domain the 7.3 client really sends (docs/packet-specs/9005-security-no.md §4.3, §7b).
    /// <para>
    /// The code is a reusable authentication secret — the same one guards character deletion and the
    /// warehouse — so it is handed back as a <b>view of the caller's own bytes</b>, never copied: a string
    /// would be an immutable copy on the heap that nothing can wipe, and the caller zeroes its frame once
    /// done. A caller may report the code's length but must never write the code to a log
    /// (docs/packet-specs/9005-security-no.md §5.5). A verifier, the day one exists, compares this view in
    /// constant time.
    /// </para>
    /// </remarks>
    /// <param name="packet">The whole frame, header included.</param>
    /// <param name="mode">The operation the code answers for, as sent.</param>
    /// <param name="securityNo">The code's bytes up to the first zero, 18 at most; empty on a refused frame.</param>
    public static bool TryReadSecurityNo(ReadOnlySpan<byte> packet, out int mode, out ReadOnlySpan<byte> securityNo)
    {
        if (packet.Length != PacketLength)
        {
            mode = 0;
            securityNo = ReadOnlySpan<byte>.Empty;
            return false;
        }

        mode = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(HeaderSize, ModeSize));
        securityNo = SecurityNoField(packet.Slice(HeaderSize + ModeSize, SecurityNoSize));
        return true;
    }

    /// <summary>
    /// A 19-byte container, not a maximum length: rzu converts at most <c>19 - 1</c> characters at read time
    /// (<c>MessageBuffer.cpp:104-109</c>) and the client copies 18 bytes, so the code ends at the first zero
    /// byte and is at most 18 characters. The six digits the client's own window asks for
    /// (<c>ui_text_6559</c>) are an interface constraint, not a protocol one, so no length in 0..18 — the
    /// empty code of the Cancel path included — is refused here (docs/packet-specs/9005-security-no.md §3.2,
    /// §6).
    /// </summary>
    private static ReadOnlySpan<byte> SecurityNoField(ReadOnlySpan<byte> field)
    {
        var length = field.IndexOf((byte)0);
        if (length < 0)
        {
            length = field.Length - 1;
        }

        return field.Slice(0, length);
    }
}
