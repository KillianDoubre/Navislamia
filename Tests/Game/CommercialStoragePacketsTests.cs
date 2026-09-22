using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// The commercial storage socle, measured on the 7.3 client (SFrame.exe):
/// TM_SC_COMMERCIAL_STORAGE_INFO (10003) is 11 bytes (total_item_count u16 @7, new_item_count u16 @9),
/// TM_SC_COMMERCIAL_STORAGE_LIST (10004) is 9 + 10 x n bytes (count u16 @7, then 10-byte lines from @9:
/// commercial_item_uid u32 @0, code i32 @4, count u16 @8) and TM_CS_TAKEOUT_COMMERCIAL_ITEM (10005) is
/// 13 bytes (commercial_item_uid u32 @7, count u16 @11). None of the three has a version-gated field in
/// 7.3. See docs/packet-specs/socle-stockage-commercial.md.
/// </summary>
[TestFixture]
public class CommercialStoragePacketsTests
{
    private const int HeaderSize = 7;
    private const int StorageInfoLength = 11;
    private const int StorageListHeaderLength = 9;
    private const int StorageItemLength = 10;
    private const int TakeoutLength = 13;

    private static byte[] TakeoutFrame(uint uid, ushort count)
    {
        var packet = new byte[TakeoutLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), TakeoutLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2),
            (ushort)GamePackets.TM_CS_TAKEOUT_COMMERCIAL_ITEM);

        packet[6] = Checksum(packet);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), uid);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(11, 2), count);
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

    private static IReadOnlyList<(uint Uid, int Code, ushort Count)> Items(params (uint Uid, int Code, ushort Count)[] items)
    {
        return items;
    }

    [Test]
    public void Ids_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_SC_COMMERCIAL_STORAGE_INFO).Should().Be(10003);
        ((ushort)GamePackets.TM_SC_COMMERCIAL_STORAGE_LIST).Should().Be(10004);
        ((ushort)GamePackets.TM_CS_TAKEOUT_COMMERCIAL_ITEM).Should().Be(10005);

        // rzu moves this family to 9003/9004/9005 from EPIC_9_6_3 on (EPIC_7_3 = 0x070300 is below it), and
        // in 7.3 those three numbers do not belong to this family at all: 9004/9005 are the security number
        // pair. The low branch is therefore the only usable one. All three ids must be defined, otherwise
        // OnDataReceived drops the frame as "Undefined packet ID" before any dispatch.
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_SC_COMMERCIAL_STORAGE_INFO).Should().BeTrue();
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_SC_COMMERCIAL_STORAGE_LIST).Should().BeTrue();
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_TAKEOUT_COMMERCIAL_ITEM).Should().BeTrue();
    }

    [Test]
    public void StorageInfo_IsElevenBytesWithTheTwoCountersAtSevenAndNine()
    {
        var packet = GameCommercialStoragePackets.BuildCommercialStorageInfo(4321, 12);

        packet.Length.Should().Be(StorageInfoLength);
        GameCommercialStoragePackets.CommercialStorageInfoSize.Should().Be(StorageInfoLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(StorageInfoLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(10003);
        packet[6].Should().Be(Checksum(packet));
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(HeaderSize, 2)).Should().Be(4321);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(HeaderSize + 2, 2)).Should().Be(12);
    }

    [Test]
    public void StorageInfo_WritesTotalCountBeforeNewCount()
    {
        // Asymmetric values: swapping the two u16 would fail here. The order is rzu's declaration order and
        // the order the client reads (total at frame +7, new at frame +9).
        var packet = GameCommercialStoragePackets.BuildCommercialStorageInfo(1, 2);

        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(7, 2)).Should().Be(1);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(9, 2)).Should().Be(2);
    }

    [Test]
    public void StorageInfo_ReachesNoByteBeyondTheTwoCounters()
    {
        // 7 + 2 + 2: an extra field would push the total past 11 and desynchronise the client, which reads
        // nothing after offset 10.
        GameCommercialStoragePackets.BuildCommercialStorageInfo(ushort.MaxValue, ushort.MaxValue)
            .Length.Should().Be(StorageInfoLength);
    }

    [Test]
    public void StorageInfo_SendsTheZeroZeroOfRzu()
    {
        // The value rzu sends at world entry (Character.cpp:308-311). Nothing in this repository can feed the
        // container, so 0/0 is the only exact value - not an estimate.
        var packet = GameCommercialStoragePackets.BuildCommercialStorageInfo(0, 0);

        packet.Length.Should().Be(StorageInfoLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(10003);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(7, 2)).Should().Be(0);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(9, 2)).Should().Be(0);
    }

    [Test]
    public void EmptyList_IsNineBytesWithCountZero()
    {
        var packet = GameCommercialStoragePackets.BuildCommercialStorageList(Items());

        packet.Length.Should().Be(StorageListHeaderLength);
        GameCommercialStoragePackets.CommercialStorageListHeaderSize.Should().Be(StorageListHeaderLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(StorageListHeaderLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(10004);
        packet[6].Should().Be(Checksum(packet));
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(HeaderSize, 2)).Should().Be(0);
    }

    [Test]
    public void List_LaysOutTwoEntriesAtNineAndNineteen()
    {
        var packet = GameCommercialStoragePackets.BuildCommercialStorageList(
            Items((0x11223344u, -5, 3), (0xAABBCCDDu, 4001, 65535)));

        packet.Length.Should().Be(29);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(29);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(10004);
        packet[6].Should().Be(Checksum(packet));
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(7, 2)).Should().Be(2);

        // First entry: uid @9, code @13, count @17.
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(9, 4)).Should().Be(0x11223344u);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(13, 4)).Should().Be(-5);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(17, 2)).Should().Be(3);

        // Second entry: uid @19, code @23, count @27.
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(19, 4)).Should().Be(0xAABBCCDDu);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(23, 4)).Should().Be(4001);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(27, 2)).Should().Be(65535);
    }

    [TestCase(0, TestName = "List_AnnouncesAndSizesAnEmptyContainer")]
    [TestCase(1, TestName = "List_AnnouncesAndSizesOneEntry")]
    [TestCase(3, TestName = "List_AnnouncesAndSizesThreeEntries")]
    [TestCase(5, TestName = "List_AnnouncesAndSizesFiveEntries")]
    public void List_CountMatchesTheAnnouncedLength(int count)
    {
        var items = Enumerable.Range(0, count)
            .Select(index => ((uint)index, index, (ushort)index))
            .ToArray();

        var packet = GameCommercialStoragePackets.BuildCommercialStorageList(items);

        // The client trusts count and never compares it with Length: it walks count lines of 10 bytes from
        // offset 9. The frame must therefore be exactly 9 + 10 x count, with count in the field.
        packet.Length.Should().Be(StorageListHeaderLength + (StorageItemLength * count));
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(7, 2)).Should().Be((ushort)count);
    }

    [Test]
    public void List_KeepsTheUidAheadOfTheCode()
    {
        // Both are 4 bytes wide, so only the values can catch a swap. The client hands the uid back verbatim
        // in 10005, which makes this ordering part of the contract.
        var packet = GameCommercialStoragePackets.BuildCommercialStorageList(Items((7u, 7, 0)));

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(9, 4)).Should().Be(7u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(13, 4)).Should().Be(7u);

        var distinct = GameCommercialStoragePackets.BuildCommercialStorageList(Items((1u, 2, 0)));
        BinaryPrimitives.ReadUInt32LittleEndian(distinct.AsSpan(9, 4)).Should().Be(1u);
        BinaryPrimitives.ReadInt32LittleEndian(distinct.AsSpan(13, 4)).Should().Be(2);
    }

    [Test]
    public void List_RefusesAMissingLineList()
    {
        var act = () => GameCommercialStoragePackets.BuildCommercialStorageList(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void TryReadTakeout_ReadsUidAtSevenAndCountAtEleven()
    {
        GameActionPackets.TryReadTakeoutCommercialItem(TakeoutFrame(0x48CE60u, 2), out var request)
            .Should().BeTrue();

        request.Uid.Should().Be(0x48CE60u);
        request.Count.Should().Be(2);
    }

    [Test]
    public void TryReadTakeout_KeepsFieldOrder()
    {
        // 4 bytes then 2: the frame is 13 bytes and nothing else is read, so the uid must not be taken from
        // the count field or vice versa.
        GameActionPackets.TryReadTakeoutCommercialItem(TakeoutFrame(0x00010002u, 0x0003), out var request)
            .Should().BeTrue();

        request.Uid.Should().Be(0x00010002u);
        request.Count.Should().Be(0x0003);
    }

    [Test]
    public void TryReadTakeout_AcceptsTheClientFrameLength()
    {
        var packet = TakeoutFrame(1u, 1);

        packet.Length.Should().Be(TakeoutLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(TakeoutLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(10005);
        packet[6].Should().Be(Checksum(packet));
    }

    [TestCase(0, TestName = "TryReadTakeout_RejectsAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadTakeout_RejectsAHeaderOnlyFrame")]
    [TestCase(12, TestName = "TryReadTakeout_RejectsATruncatedFrame")]
    [TestCase(14, TestName = "TryReadTakeout_RejectsAPaddedFrame")]
    [TestCase(16, TestName = "TryReadTakeout_RejectsALongerFrame")]
    public void TryReadTakeout_RejectsAnyLengthOtherThanThirteen(int length)
    {
        var packet = new byte[length];
        if (length >= TakeoutLength)
        {
            TakeoutFrame(9u, 9).CopyTo(packet, 0);
        }

        GameActionPackets.TryReadTakeoutCommercialItem(packet, out var request).Should().BeFalse();
        request.Should().Be(default(GameActionPackets.TakeoutCommercialItemRequest));
    }

    [Test]
    public void ServerHasNoBuilderForTheTakeoutPacket()
    {
        // The server must never emit TM_CS_TAKEOUT_COMMERCIAL_ITEM (10005): the only 10005 frame the client
        // owns is an outgoing one, and the only handling of an internal 0x2715 message identified in
        // SFrame.exe answers with TM_CS_LOGOUT. Locked by reflection: this type may only expose the two
        // server to client builders, and the ids they write are 10003 and 10004.
        var builders = typeof(GameCommercialStoragePackets)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.ReturnType == typeof(byte[]))
            .Select(method => method.Name)
            .ToArray();

        builders.Should().BeEquivalentTo("BuildCommercialStorageInfo", "BuildCommercialStorageList");

        var emittedIds = new[]
        {
            BinaryPrimitives.ReadUInt16LittleEndian(
                GameCommercialStoragePackets.BuildCommercialStorageInfo(0, 0).AsSpan(4, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(
                GameCommercialStoragePackets.BuildCommercialStorageList(Items()).AsSpan(4, 2))
        };

        emittedIds.Should().Equal(10003, 10004);
        emittedIds.Should().NotContain((ushort)GamePackets.TM_CS_TAKEOUT_COMMERCIAL_ITEM);
    }

    [Test]
    public void TakeoutIsReadOnlyInTheActionPackets()
    {
        // The 10005 surface outside this builder set is the parser alone: one Try* reader, taking the frame
        // and returning a request. A builder added there would break the "never emitted" rule above.
        var takeoutMembers = typeof(GameActionPackets)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name.Contains("Takeout", StringComparison.Ordinal))
            .ToArray();

        takeoutMembers.Should().HaveCount(1);

        var reader = takeoutMembers[0];
        reader.Name.Should().Be("TryReadTakeoutCommercialItem");
        reader.ReturnType.Should().Be(typeof(bool));

        var parameters = reader.GetParameters();
        parameters.Should().HaveCount(2);
        parameters[0].ParameterType.Should().Be(typeof(ReadOnlySpan<byte>));
        parameters[1].ParameterType.IsByRef.Should().BeTrue();
        parameters[1].ParameterType.GetElementType().Should()
            .Be(typeof(GameActionPackets.TakeoutCommercialItemRequest));
    }
}
