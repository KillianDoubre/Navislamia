using System;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// Frame layout of <c>TM_CS_REQUEST</c> (60), the only variable length frame of the anti-cheat family:
/// a 7 byte header, a one byte selector <c>t</c> at offset 7, then the <c>command</c> bytes at offset 8,
/// always closed by a single NUL terminator.
/// </summary>
/// <remarks>
/// <para>
/// The layout comes from rzu <c>TS_CS_REQUEST.h</c>: <c>_(simple)(uint8_t, t)</c> plus
/// <c>_(endstring)(command, true)</c>. An <c>endstring</c> carries <strong>no length prefix</strong>, so
/// the field runs to the end of the datagram and its size is <c>Length - 9</c> with <c>L</c> = the number
/// of command bytes before the terminator; the third macro argument is the NUL terminator, counted in
/// the frame size (<c>PacketDeclaration.h:287</c>) and written (<c>:383-384</c>). Hence
/// <c>Length = 9 + L</c>, and a 10 byte frame carries a <em>one</em> byte command. The same rule is why
/// the command is <strong>never</strong> read "up to the first NUL": an internal NUL is just a byte of
/// the field, and only the last byte of the datagram is the terminator.
/// </para>
/// <para>
/// rzu gates the id alone — 60 below <c>EPIC_9_6_3</c>, 1060 from 9.6.3 on — and declares both fields
/// unconditionally, so <strong>no field of this packet is version dependent</strong>.
/// </para>
/// <para>
/// The command is <strong>opaque</strong>. The single producer found in the reference trees (the NGemity
/// supervision tool, <c>Tools/ServerMonitor/src/Client/MonitorSession.cpp:85-86</c>) sends
/// <c>t = 'u'</c> and a zlib + simple cipher blob as hexadecimal ASCII, and no source declares a
/// charset. Nothing here decodes, decrypts, interprets, whitelists or executes it: the raw bytes are
/// handed back to the caller, which only measures them.
/// </para>
/// <para>
/// Nothing is answered either. There is no server to client packet of this id in <c>op_codes.md</c>, and
/// the 7.3 client has no arm for an incoming frame 60 (its dispatcher falls through to the
/// "unprocessed message" case), so any answer would be invented.
/// </para>
/// <para>
/// See <c>docs/packet-specs/60-request.md</c>.
/// </para>
/// </remarks>
public static class GameRequestPackets
{
    private const int HeaderSize = 7;

    /// <summary>Offset of the one byte selector <c>t</c>, immediately after the 7 byte header.</summary>
    public const int SelectorOffset = HeaderSize;

    /// <summary>Offset of the first <c>command</c> byte: the header plus the selector.</summary>
    public const int CommandOffset = HeaderSize + 1;

    /// <summary>
    /// Smallest well formed frame — 9 bytes: header, <c>t</c> and the NUL terminator of an empty command.
    /// </summary>
    public const int MinPacketSize = HeaderSize + 2;

    /// <summary>
    /// Largest frame that can ever reach the receive loop. <c>Connection</c> reads into a 32768 byte
    /// buffer (<c>Connection.cs:27</c>) and the loop refuses to advance while <c>Length</c> exceeds what
    /// it holds, so a longer frame is never delivered — the client would simply wait.
    /// </summary>
    public const int MaxPacketSize = 32768;

    /// <summary>Largest command, in bytes, before its NUL terminator: 32759.</summary>
    public const int MaxCommandLength = MaxPacketSize - MinPacketSize;

    /// <summary>
    /// Reads the selector and the raw command bytes of a <c>TM_CS_REQUEST</c> frame.
    /// </summary>
    /// <param name="packet">The whole frame, header included.</param>
    /// <param name="selector">
    /// The <c>t</c> byte. It is handed back as it stands: no value is valid, rejected or enumerated —
    /// nothing in rzu, NGemity or the shipped client establishes the meaning or the list of its values.
    /// </param>
    /// <param name="command">
    /// A view of the caller's own bytes, <c>Length - 9</c> long, terminator excluded. Handed back unread:
    /// the encoding and the content are not established, and the reader never interprets them.
    /// </param>
    /// <returns>
    /// <c>false</c> — without handing anything back — when the frame is shorter than the smallest well
    /// formed one (below 9 bytes, which covers the header only frame) or when its last byte is not the
    /// NUL terminator the frame is defined with. Both are anomalies of a frame no client of this family
    /// writes by hand: rzu would silently drop one byte of a frame with no terminator
    /// (<c>MessageBuffer.cpp:131-132</c>) rather than refuse it, which is not worth reproducing.
    /// </returns>
    public static bool TryReadRequest(ReadOnlySpan<byte> packet, out byte selector,
        out ReadOnlySpan<byte> command)
    {
        selector = 0;
        command = ReadOnlySpan<byte>.Empty;

        if (packet.Length < MinPacketSize)
            return false;

        if (packet[^1] != 0)
            return false;

        selector = packet[SelectorOffset];
        command = packet.Slice(CommandOffset, packet.Length - MinPacketSize);
        return true;
    }
}
