using System;
using System.Buffers.Binary;
using Navislamia.Game.Network.Packets.Enums;

namespace Navislamia.Game.Network.Packets.Game;

/// <summary>
/// TM_SC_WEATHER_INFO (902) and TM_CS_GET_WEATHER_INFO (903): the weather of one world location.
/// <para>
/// <c>region_id</c> is <b>not</b> a visibility region index (the 550/11 pair, divided by 180): it is the
/// <c>WorldLocation.id</c> of the location, packed as <c>x * 10000 + y * 100 + n</c> over that table's
/// <c>x</c>/<c>y</c> columns. NGemity puts the location id there, rzu puts 0.
/// </para>
/// <para>
/// The ids 1902/1903 exist from EPIC_9_6_3 on; Epic 7.3 (0x070300 &lt; 0x090603) uses 902/903.
/// </para>
/// </summary>
public static class GameWeatherPackets
{
    private const int HeaderSize = 7;

    /// <summary>
    /// TM_SC_WEATHER_INFO (902): 13 bytes, <c>region_id</c> (uint32) at offset 7 and <c>weather_id</c>
    /// (uint16) at offset 11 — the two offsets the 7.3 client reads its fields from. The server owns this
    /// push: the client never asks for it.
    /// </summary>
    public static byte[] BuildWeatherInfo(uint regionId, ushort weatherId)
    {
        var packet = new byte[HeaderSize + 6];
        var payload = packet.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(4, 2), (ushort)GamePackets.TM_SC_WEATHER_INFO);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(HeaderSize, 4), regionId);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(HeaderSize + 4, 2), weatherId);
        WriteChecksum(packet);

        return packet;
    }

    /// <summary>
    /// TM_CS_GET_WEATHER_INFO (903) carries the location id the client asks about. Only the exact 11-byte
    /// form is accepted: that size comes from the rzu definition alone — no 7.3 client build emits this
    /// packet, so no reader attests the offset — and a short or padded frame is refused rather than
    /// partially read, exactly as TM_CS_GET_REGION_INFO (550) does for its own size.
    /// </summary>
    public static bool TryReadGetWeatherInfo(ReadOnlySpan<byte> packet, out uint regionId)
    {
        const int packetLength = HeaderSize + 4;
        if (packet.Length != packetLength)
        {
            regionId = 0;
            return false;
        }

        regionId = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(HeaderSize, 4));
        return true;
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
