using System;
using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// TM_SC_WEATHER_INFO (902) is 13 bytes on the wire (7-byte header + uint32 region_id at offset 7 and
/// uint16 weather_id at offset 11) — the two offsets the 7.3 client reads its fields from. The answer to
/// TM_CS_GET_WEATHER_INFO (903) is that same 902; the request itself is 11 bytes (7-byte header + uint32
/// region_id at offset 7).
/// See docs/packet-specs/902-weather-info.md.
/// </summary>
[TestFixture]
public class WeatherInfoPacketsTests
{
    private const int WeatherInfoLength = 13;
    private const int GetWeatherInfoLength = 11;

    /// <summary>A client frame as the 7.3 client would build one: header, then the single field.</summary>
    private static byte[] ClientFrame(uint regionId)
    {
        var packet = new byte[GetWeatherInfoLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), GetWeatherInfoLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_GET_WEATHER_INFO);

        packet[6] = Checksum(packet);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), regionId);
        return packet;
    }

    private static byte Checksum(byte[] packet)
    {
        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        return checksum;
    }

    [Test]
    public void WeatherInfoIds_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_SC_WEATHER_INFO).Should().Be(902);
        ((ushort)GamePackets.TM_CS_GET_WEATHER_INFO).Should().Be(903);

        // rzu remaps the pair to 1902/1903 from EPIC_9_6_3 on (EPIC_7_3 = 0x070300 is below it); 7.3 stays
        // on the low branch and the remapped ids must not be declared.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_SC_WEATHER_INFO).Should().BeTrue();
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_GET_WEATHER_INFO).Should().BeTrue();
        Enum.IsDefined(typeof(GamePackets), (ushort)1902).Should().BeFalse();
        Enum.IsDefined(typeof(GamePackets), (ushort)1903).Should().BeFalse();
    }

    [Test]
    public void BuildWeatherInfo_LaysOutHeaderAndBothFields()
    {
        // 60600 is a real 7.3 location id: x = 6, y = 6, n = 0.
        var packet = GameWeatherPackets.BuildWeatherInfo(regionId: 60600u, weatherId: 0);

        packet.Length.Should().Be(WeatherInfoLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(WeatherInfoLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(902);
        packet[6].Should().Be(Checksum(packet));
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(60600u);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(11, 2)).Should().Be(0);
    }

    [Test]
    public void BuildWeatherInfo_WritesTheWeatherIdAtElevenAndNothingElse()
    {
        var packet = GameWeatherPackets.BuildWeatherInfo(regionId: 110901u, weatherId: 4);

        packet.Length.Should().Be(WeatherInfoLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(110901u);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(11, 2)).Should().Be(4);
    }

    [Test]
    public void TryReadGetWeatherInfo_ReadsTheRegionIdAtSeven()
    {
        GameWeatherPackets.TryReadGetWeatherInfo(ClientFrame(110901u), out var regionId).Should().BeTrue();

        regionId.Should().Be(110901u);
    }

    [Test]
    public void TryReadGetWeatherInfo_ReadsRegionIdAsUnsigned()
    {
        // The id is a uint32 on the wire: the top bit must not turn it negative.
        GameWeatherPackets.TryReadGetWeatherInfo(ClientFrame(uint.MaxValue), out var regionId).Should().BeTrue();

        regionId.Should().Be(uint.MaxValue);
    }

    [Test]
    public void TryReadGetWeatherInfo_RejectsAnyLengthOtherThanEleven()
    {
        var exact = ClientFrame(10u);

        foreach (var length in new[] { 0, 7, 10, 12, 15, 19 })
        {
            var packet = new byte[length];
            Array.Copy(exact, packet, Math.Min(length, exact.Length));

            // The header of a shorter frame is rewritten so the length byte is not the only thing wrong.
            if (length >= 7)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)length);
                BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_GET_WEATHER_INFO);
                packet[6] = Checksum(packet);
            }

            GameWeatherPackets.TryReadGetWeatherInfo(packet, out var regionId)
                .Should().BeFalse($"a {length}-byte frame is not the 11-byte TM_CS_GET_WEATHER_INFO");
            regionId.Should().Be(0u);
        }
    }
}
