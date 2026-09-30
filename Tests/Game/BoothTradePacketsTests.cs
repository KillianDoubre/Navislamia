using System;
using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// The Epic 7.3 layouts of the booth trade family, each measured in the 7.3 client
/// (docs/packet-specs/705-buy-from-booth.md §3): 705 = 13 + 75×N, 706 = 19, 707 = 11 + 4×H,
/// 708 = 11 + 53×N, 709 = 11, 710 = 14 + 83×N.
/// </summary>
[TestFixture]
public class BoothTradePacketsTests
{
    [Test]
    public void TradeIds_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_CS_BUY_FROM_BOOTH).Should().Be(705);
        ((ushort)GamePackets.TM_CS_SELL_TO_BOOTH).Should().Be(706);
        ((ushort)GamePackets.TM_CS_GET_BOOTHS_NAME).Should().Be(707);
        ((ushort)GamePackets.TM_SC_GET_BOOTHS_NAME).Should().Be(708);
        ((ushort)GamePackets.TM_SC_BOOTH_CLOSED).Should().Be(709);
        ((ushort)GamePackets.TM_SC_BOOTH_TRADE_INFO).Should().Be(710);
    }

    [Test]
    public void BuyFromBooth_ReadsTargetCountAndEachMotifAt75ByteSteps()
    {
        var packet = BuildBuy(0x80000001, (0x0A000001, 240100, 3), (0x0A000002, 240200, 1));

        packet.Length.Should().Be(13 + 75 * 2, "the client builder sizes it 13 + 0x4B × count");
        BoothPackets.TryReadBuyFromBooth(packet, out var request).Should().BeTrue();

        request.Target.Should().Be(0x80000001);
        request.Lines.Should().Equal(
            new BoothBuyLine(0x0A000001, 240100, 3),
            new BoothBuyLine(0x0A000002, 240200, 1));
    }

    [Test]
    public void BuyFromBooth_RefusesALengthThatIsNotTheDeclaredCount()
    {
        var packet = BuildBuy(0x80000001, (0x0A000001, 240100, 3));

        BoothPackets.TryReadBuyFromBooth(packet.AsSpan(0, packet.Length - 1), out _).Should().BeFalse();

        var longer = new byte[packet.Length + 10];
        packet.CopyTo(longer, 0);
        BoothPackets.TryReadBuyFromBooth(longer, out _).Should().BeFalse("the 85 byte reading of rzu is refused");
    }

    [TestCase((short)0)]
    [TestCase((short)9)]
    [TestCase((short)-1)]
    public void BuyFromBooth_RefusesACountOutsideOneToEight(short count)
    {
        var packet = new byte[13 + 75 * Math.Max(0, (int)count)];
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(11, 2), count);

        BoothPackets.TryReadBuyFromBooth(packet, out _).Should().BeFalse();
    }

    [Test]
    public void SellToBooth_IsNineteenBytes_TargetHandleCount()
    {
        var packet = new byte[19];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), 0x80000001);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(11, 4), 0x0A000009);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(15, 4), 7);

        BoothPackets.TryReadSellToBooth(packet, out var request).Should().BeTrue();
        request.Should().Be(new SellToBoothRequest(0x80000001, 0x0A000009, 7));

        BoothPackets.TryReadSellToBooth(new byte[18], out _).Should().BeFalse();
        BoothPackets.TryReadSellToBooth(new byte[20], out _).Should().BeFalse();
    }

    [Test]
    public void GetBoothsName_ReadsItsHandleList()
    {
        var packet = new byte[11 + 4 * 2];
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(7, 4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(11, 4), 5);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(15, 4), 6);

        BoothPackets.TryReadGetBoothsName(packet, out var handles).Should().BeTrue();
        handles.Should().Equal(5u, 6u);

        BoothPackets.TryReadGetBoothsName(packet.AsSpan(0, 15), out _).Should().BeFalse();
    }

    [Test]
    public void BoothsName_IsElevenPlusFiftyThreePerNameWithANulKeptInEachField()
    {
        var longName = new byte[60];
        Array.Fill(longName, (byte)'x');

        var packet = BoothPackets.BuildGetBoothsName(new[]
        {
            new BoothName(7, "Boutique"u8.ToArray()),
            new BoothName(8, longName)
        });

        packet.Length.Should().Be(11 + 53 * 2);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be((uint)packet.Length);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(708);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(2, "count is a uint32 at +7");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(7);
        packet.AsSpan(15, 8).ToArray().Should().Equal("Boutique"u8.ToArray());
        packet[15 + 8].Should().Be(0);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(64, 4)).Should().Be(8, "the second record starts 53 bytes on");
        packet[68 + 48].Should().Be(0, "a name is cut at 48 bytes so its field keeps a nul");
        packet[6].Should().Be(StorageTestHarness.Checksum(packet));
    }

    [Test]
    public void BoothClosed_IsElevenBytesWithTheBoothHandle()
    {
        var packet = BoothPackets.BuildBoothClosed(0x80000001);

        packet.Length.Should().Be(11);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(709);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x80000001);
        packet[6].Should().Be(StorageTestHarness.Checksum(packet));
    }

    [Test]
    public void BoothTradeInfo_HasThe703RecordStrideAfterTargetIsSellAndCount()
    {
        var item = new ItemFixedInfo(0x0A000001, 240100, 0, 3, 0, 0, 0, 0, 0, new long[4], 0, 0, 0, 0, 0, 0);
        var packet = BoothPackets.BuildBoothTradeInfo(0x80000002, isSell: true,
            new[] { new BoothWatchItem(item, 1500), new BoothWatchItem(item, 20) });

        packet.Length.Should().Be(14 + 83 * 2);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(710);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x80000002);
        packet[11].Should().Be(1, "is_sell at +11");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(12, 2)).Should().Be(2, "count at +12");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(14 + 4, 4)).Should().Be(240100);
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(14 + 75, 8)).Should().Be(1500);
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(14 + 83 + 75, 8)).Should().Be(20);
        packet[6].Should().Be(StorageTestHarness.Checksum(packet));
    }

    internal static byte[] BuildBuy(uint target, params (uint Handle, int Code, long Count)[] items)
    {
        var packet = new byte[13 + 75 * items.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_BUY_FROM_BOOTH);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), target);
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(11, 2), (short)items.Length);
        for (var i = 0; i < items.Length; i++)
        {
            var motif = packet.AsSpan(13 + 75 * i, 75);
            BinaryPrimitives.WriteUInt32LittleEndian(motif.Slice(0, 4), items[i].Handle);
            BinaryPrimitives.WriteInt32LittleEndian(motif.Slice(4, 4), items[i].Code);
            BinaryPrimitives.WriteInt64LittleEndian(motif.Slice(16, 8), items[i].Count);
        }

        packet[6] = StorageTestHarness.Checksum(packet);
        return packet;
    }
}
