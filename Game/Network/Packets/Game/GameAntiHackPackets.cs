using System;
using System.Buffers.Binary;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// Frame layout of <c>TM_CS_ANTI_HACK</c> (54), the client anti-cheat datagram.
/// </summary>
/// <remarks>
/// The layout comes from rzu <c>TS_CS_ANTI_HACK.h</c> on the Epic 7.3 side of its version gating:
/// a <c>uint16 nLength</c> at offsets 7-8 followed by the fixed <c>uint8 byBuffer[400]</c> at offsets
/// 9-408, for 409 bytes on the wire. <c>byBuffer</c> is a fixed array in rzu, not a length prefixed
/// one, so a conforming client always sends the full 409 bytes.
/// <para>
/// The meaning of <c>nLength</c> is <strong>not established</strong> by rzu, NGemity/Chihiro or the
/// Epic 7.3 client: the reader below exposes the raw value for observation only. Nothing compares,
/// truncates or validates it, because doing so would encode one reading of an open question.
/// See <c>docs/packet-specs/socle-anti-triche.md</c>.
/// </para>
/// </remarks>
public static class GameAntiHackPackets
{
    private const int HeaderSize = 7;

    /// <summary>Total size of the datagram, header included: 409 bytes.</summary>
    public const int AntiHackPacketSize = 409;

    /// <summary>Size of the payload after the 7 byte header: 402 bytes.</summary>
    public const int AntiHackPayloadSize = AntiHackPacketSize - HeaderSize;

    /// <summary>
    /// Reads the declared length at offsets 7-8 without interpreting it. Returns false when the
    /// datagram is shorter than a complete anti-cheat frame.
    /// </summary>
    public static bool TryReadAntiHack(ReadOnlySpan<byte> packet, out ushort declaredLength)
    {
        declaredLength = 0;

        if (packet.Length < AntiHackPacketSize)
        {
            return false;
        }

        declaredLength = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(HeaderSize, sizeof(ushort)));
        return true;
    }
}
