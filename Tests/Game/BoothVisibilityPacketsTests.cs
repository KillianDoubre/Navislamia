using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FluentAssertions;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// Offset tests for the visibility half of the player booth: <c>TM_CS_WATCH_BOOTH</c> (702),
/// <c>TM_SC_WATCH_BOOTH</c> (703) and <c>TM_CS_STOP_WATCH_BOOTH</c> (704). Every size is measured in
/// the Epic 7.3 client, not deduced: its emitter <c>VA 0x48CC70</c> writes 11 for 702 and 704 (a 7 byte
/// header plus the booth handle at +7), and its 703 handler <c>VA 0x6732F0</c> reads <c>target</c> at
/// +7, <c>type</c> at +11, <c>count</c> at +12 and copies 83 byte records from +14 with a stride of
/// <c>0x53</c> (docs/packet-specs/socle-booths-visibilite.md §3.2 to §3.6).
/// </summary>
[TestFixture]
public class BoothVisibilityPacketsTests
{
    private const uint Target = 0x80000001u;

    [Test]
    public void TryReadWatchBooth_LaysOutElevenBytesWithTheHandleAtSeven()
    {
        var packet = ClientFrame(GamePackets.TM_CS_WATCH_BOOTH, Target);

        packet.Should().HaveCount(11, "the client's builder writes 11 in the length and one field");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(11);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_CS_WATCH_BOOTH);

        BoothPackets.WatchBoothLength.Should().Be(11, "règle mesurée au §3.2");
        BoothPackets.TryReadWatchBooth(packet, out var target).Should().BeTrue();
        target.Should().Be(Target, "the handle sits at +7 as a uint32");
    }

    [Test]
    public void TryReadWatchBooth_RejectsAFrameShorterThanElevenBytes()
    {
        // The handler refuses this with TS_SC_RESULT(702, InvalidArgument) rather than reading past the
        // buffer; the reader is the only place that can tell.
        for (var length = 0; length < 11; length++)
        {
            var packet = ClientFrame(GamePackets.TM_CS_WATCH_BOOTH, Target, length);

            BoothPackets.TryReadWatchBooth(packet, out var target).Should().BeFalse($"a {length} byte frame");

            target.Should().Be(0, "nothing is read before the length is judged");
        }
    }

    [Test]
    public void TryReadWatchBooth_IgnoresAnythingBeyondTheHandle()
    {
        var packet = ClientFrame(GamePackets.TM_CS_WATCH_BOOTH, Target, 15);

        BoothPackets.TryReadWatchBooth(packet, out var target).Should().BeTrue();
        target.Should().Be(Target, "trailing bytes are padding, as in every other reader of the family");
    }

    [Test]
    public void TryReadStopWatchBooth_SharesTheElevenByteLayoutOfSevenHundredTwo()
    {
        var packet = ClientFrame(GamePackets.TM_CS_STOP_WATCH_BOOTH, Target);

        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_CS_STOP_WATCH_BOOTH);

        BoothPackets.TryReadStopWatchBooth(packet, out var target).Should().BeTrue();
        target.Should().Be(Target, "704 is the same 11 byte frame as 702, id apart (§3.3)");

        BoothPackets.TryReadStopWatchBooth(packet.AsSpan(0, 10), out _)
            .Should().BeFalse("a shorter frame cannot come from the client");
    }

    [Test]
    public void BuildWatchBooth_LaysOutFourteenBytesForNoItem()
    {
        var packet = BoothPackets.BuildWatchBooth(Target, type: 1);

        packet.Should().HaveCount(14, "the empty 703 is header + target + type + count, 7 + 7");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(14, "length at +0");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_WATCH_BOOTH, "id at +4");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(Target, "target at +7");
        packet[11].Should().Be(1, "type at +11, copied verbatim");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(12, 2)).Should().Be(0, "count at +12");
        packet[6].Should().Be(Checksum(packet), "the header checksum is recomputed after the body");
    }

    [Test]
    public void BuildWatchBooth_LaysOutNinetySevenBytesForOneItem()
    {
        var packet = BoothPackets.BuildWatchBooth(Target, type: 2, Items(1));

        packet.Should().HaveCount(97, "14 + 83");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(97);
        packet[11].Should().Be(2);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(12, 2)).Should().Be(1);

        // The record starts at +14: the 75 byte motif, then the declared price as an int64 at +75.
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(14, 4)).Should().Be(0x80000001u,
            "item.handle at record offset 0");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(18, 4)).Should().Be(101, "item.code at 4");
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(22, 8)).Should().Be(0x80000002L, "uid at 8");
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(30, 8)).Should().Be(3, "count at 16");
        BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(89, 8)).Should().Be(1500,
            "gold at +14 + 75, the declared price verbatim");
    }

    [Test]
    public void BuildWatchBooth_LaysOutSixHundredSeventyEightBytesForEightItems()
    {
        var packet = BoothPackets.BuildWatchBooth(Target, type: 1, Items(8));

        packet.Should().HaveCount(678, "14 + 83 × 8");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(678);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(12, 2)).Should().Be(8);

        // The stride is 83, and every record carries its own item and its own price.
        for (var i = 0; i < 8; i++)
        {
            var record = 14 + i * 83;

            BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(record, 4))
                .Should().Be(0x80000001u + (uint)i, $"record {i} handle");
            BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(record + 75, 8))
                .Should().Be(1500 + i, $"record {i} gold at stride 83 + 75");
        }

        packet.Should().HaveCount(14 + 83 * 8,
            "the declared length must equal 14 + 83 × count: the client checks neither (§3.6)");
    }

    [Test]
    public void BuildWatchBooth_BoundsTheDeclaredCountAtEightRecords()
    {
        // The ceiling is a server rule: the client measures no upper bound, so a 703 with nine records
        // would make it read 83 bytes past what the server wrote.
        var packet = BoothPackets.BuildWatchBooth(Target, type: 1, Items(9));

        packet.Should().HaveCount(678, "nine items are cut to the eight item ceiling of the family");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(12, 2)).Should().Be(8,
            "the count on the wire is always the number of records actually written");
    }

    [Test]
    public void BuildWatchBooth_IsAcceptedByTheReceiveLoopChecksum()
    {
        var packet = BoothPackets.BuildWatchBooth(Target, type: 1, Items(3));

        packet[6].Should().Be(StorageTestHarness.Checksum(packet),
            "a 703 the client validates on reception must carry the sum of its six header bytes");
    }

    [Test]
    public void BoothPackets_PinEveryOffsetOfTheVisibilityLayout()
    {
        // One place that says the layout, so a silent drift of an offset breaks a test rather than a
        // client. Every value comes from the client's own measurements (fiche §3.2 to §3.6).
        BoothPackets.WatchBoothLength.Should().Be(11);
        BoothPackets.WatchBoothTargetOffset.Should().Be(7);
        BoothPackets.WatchBoothHeaderLength.Should().Be(14);
        BoothPackets.WatchBoothTypeOffset.Should().Be(11);
        BoothPackets.WatchBoothCountOffset.Should().Be(12);
        BoothPackets.WatchBoothItemsOffset.Should().Be(14);
        BoothPackets.WatchBoothItemSize.Should().Be(83, "75 bytes of motif then an int64 gold");
        BoothPackets.WatchBoothItemGoldOffset.Should().Be(75);
    }

    [Test]
    public void BoothVisibilityPackets_CarryTheirEpic73Ids()
    {
        ((ushort)GamePackets.TM_CS_WATCH_BOOTH).Should().Be(702);
        ((ushort)GamePackets.TM_SC_WATCH_BOOTH).Should().Be(703);
        ((ushort)GamePackets.TM_CS_STOP_WATCH_BOOTH).Should().Be(704);

        // rzu gates the trio below EPIC_9_6_3: the 1702/1703/1704 forms belong to 9.6.3 and later and
        // must not be declared here (fiche §4.1).
        foreach (var id in new ushort[] { 1702, 1703, 1704 })
        {
            Enum.IsDefined(typeof(GamePackets), id).Should().BeFalse();
        }
    }

    [Test]
    public void EveryMemberOfTheVisibilityTrio_IsDispatchedBeforeTheUnknownPacketThrow()
    {
        // GameClient's dispatch is a chain of ifs, so a member added to the enum without a branch reaches
        // the final switch and its `throw` kills the receive loop. 703 is a server to client frame, but a
        // declared member is a declared member: it needs its log and drop arm, like TM_SC_REGION_ACK.
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "Game", "Network", "Clients", "GameClient.cs"));

        var finalSwitch = source.IndexOf("throw new Exception($\"Unknown Packet Type", StringComparison.Ordinal);

        finalSwitch.Should().BeGreaterThan(-1, "the final switch is the guard this test is about");

        foreach (var member in new[] { "TM_CS_WATCH_BOOTH", "TM_SC_WATCH_BOOTH", "TM_CS_STOP_WATCH_BOOTH" })
        {
            var branch = source.IndexOf($"header.ID == (ushort)GamePackets.{member}", StringComparison.Ordinal);

            branch.Should().BeGreaterThan(-1, $"{member} needs a branch of its own in OnDataReceived");
            branch.Should().BeLessThan(finalSwitch, $"{member} must be handled before the final switch throws");
        }
    }

    [Test]
    public void WatchedBooth_IsForgottenBySevenHundredFourAndWithTheCharacterSession()
    {
        var info = new ConnectionInfo();

        info.WatchedBoothHandle.Should().BeNull("a fresh session watches nothing");

        info.BeginWatchingBooth(Target);
        info.WatchedBoothHandle.Should().Be(Target);

        // A second 702 replaces the observed booth instead of accumulating: the client holds one window.
        info.BeginWatchingBooth(0x80000009u);
        info.WatchedBoothHandle.Should().Be(0x80000009u);

        info.StopWatchingBooth().Should().BeTrue();
        info.WatchedBoothHandle.Should().BeNull();
        info.StopWatchingBooth().Should().BeFalse("closing an observation that is not held is idempotent");

        info.BeginWatchingBooth(Target);
        info.ClearCharacterSession();
        info.WatchedBoothHandle.Should().BeNull("an observation does not survive the character session");
    }

    private static BoothWatchItem[] Items(int count)
    {
        return Enumerable.Range(0, count).Select(i => new BoothWatchItem(
            new ItemFixedInfo(
                Handle: 0x80000001u + (uint)i,
                Code: 101,
                Uid: 0x80000002L,
                Count: 3,
                EtherealDurability: 7,
                Endurance: 12,
                Enhance: 1,
                Level: 2,
                Flag: 0,
                Sockets: new long[] { 0, 0, 0, 0 },
                RemainTime: 0,
                ElementalEffectType: 0,
                ElementalEffectRemainTime: 0,
                ElementalEffectAttackPoint: 0,
                ElementalEffectMagicPoint: 0,
                AppearanceCode: 0),
            Gold: 1500 + i)).ToArray();
    }

    /// <summary>
    /// The frame the 7.3 client builds for 702 and 704: the announced length, the id, the handle at +7
    /// and the header checksum the receive loop verifies before any dispatch.
    /// </summary>
    private static byte[] ClientFrame(GamePackets id, uint target, int length = 11)
    {
        var packet = new byte[length];

        if (length < 7)
        {
            // No complete header at all: such a frame never leaves the client, but the reader must still
            // refuse it without reading past the buffer.
            return packet;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)id);

        if (length >= 11)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), target);
        }

        packet[6] = Checksum(packet);
        return packet;
    }

    private static byte Checksum(byte[] packet)
    {
        byte sum = 0;
        for (var i = 0; i < 6; i++) sum += packet[i];
        return sum;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Navislamia.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the repository root is needed to check the dispatch chain");

        return directory!.FullName;
    }
}
