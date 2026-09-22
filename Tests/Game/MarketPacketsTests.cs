using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// Offsets of <c>TM_SC_MARKET</c> (250), the frame that opens the merchant window, and of
/// <c>TM_SC_NPC_TRADE_INFO</c> (240), the single-transaction echo.
/// <para>
/// 250 is <c>13 + 16 × n</c>: a 7-byte header, <c>npc_handle</c> (4) at 7, a UInt16 line count at 11,
/// then per line <c>code</c> (4), <c>price</c> (8) and <c>huntaholic_point</c> (4) at 13, 17 and 25 of
/// the line, with no padding at the end (a 7.3 client reads <c>count × 16</c> contiguous bytes).
/// </para>
/// <para>
/// 240 is 36 bytes: <c>is_sell</c> (1) at 7, <c>code</c> (4) at 8, <c>count</c> (8) at 12,
/// <c>price</c> (8) at 20, <c>huntaholic_point</c> (4) at 28, <c>target</c> (4) at 32. There is no
/// <c>arena_point</c>, which only exists from <c>EPIC_9</c>.
/// </para>
/// </summary>
[TestFixture]
public class MarketPacketsTests
{
    private static readonly MarketLine[] TwoLines =
    {
        new(12345, 678_000L, 0),
        new(54321, 1L, 7)
    };

    [Test]
    public void MarketIds_MatchTheEpic73Protocol()
    {
        ((ushort)GamePackets.TM_SC_NPC_TRADE_INFO).Should().Be(240);
        ((ushort)GamePackets.TM_SC_MARKET).Should().Be(250);
    }

    [Test]
    public void BuildMarketInfo_LaysOutTheThirteenByteHeader()
    {
        var packet = GameTradePackets.BuildMarketInfo(0x80000123u, TwoLines);

        packet.Length.Should().Be(45);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be((uint)packet.Length);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_MARKET);
        packet[6].Should().Be(Checksum(packet));
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x80000123u);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(11, 2)).Should().Be((ushort)2);
    }

    [Test]
    public void BuildMarketInfo_WritesEveryLineOnSixteenBytes()
    {
        var packet = GameTradePackets.BuildMarketInfo(0x80000123u, TwoLines);

        // First line, at 13: code (4), price (8), huntaholic_point (4).
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(13, 4)).Should().Be(12345);
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(17, 8)).Should().Be(678_000L);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(25, 4)).Should().Be(0);

        // Second line, exactly 16 bytes further, at 29.
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(29, 4)).Should().Be(54321);
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(33, 8)).Should().Be(1L);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(41, 4)).Should().Be(7);
    }

    [Test]
    public void BuildMarketInfo_EmitsOneLineAndNoPadding()
    {
        var packet = GameTradePackets.BuildMarketInfo(1u, new[] { new MarketLine(7, 9L, 0) });

        packet.Length.Should().Be(29);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(11, 2)).Should().Be(1);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(25, 4)).Should().Be(0, "the line ends at 29");
    }

    [Test]
    public void BuildMarketInfo_KeepsAPriceBeyondInt32()
    {
        var packet = GameTradePackets.BuildMarketInfo(1u, new[] { new MarketLine(7, 0x1_0000_0002L, 0) });

        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(17, 8)).Should().Be(0x1_0000_0002L);
    }

    [Test]
    public void BuildNpcTradeInfo_LaysOutTheThirtySixByteEcho()
    {
        var packet = GameTradePackets.BuildNpcTradeInfo(true, 12345, 0x1_0000_0002L, 678_000L, 0,
            0x80000123u);

        packet.Length.Should().Be(36);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(36u);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_NPC_TRADE_INFO);
        packet[6].Should().Be(Checksum(packet));
        packet[7].Should().Be(1, "is_sell is a single int8 byte");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(8, 4)).Should().Be(12345);
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(12, 8)).Should().Be(0x1_0000_0002L);
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(20, 8)).Should().Be(678_000L);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(28, 4)).Should().Be(0);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(32, 4)).Should().Be(0x80000123u);
    }

    [Test]
    public void BuildNpcTradeInfo_WritesZeroForABuy()
    {
        var bought = GameTradePackets.BuildNpcTradeInfo(false, 1, 1, 1, 0, 1u);

        bought[7].Should().Be(0);
        bought.Length.Should().Be(36, "is_sell shifts nothing: the remaining fields are aligned");
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
}
