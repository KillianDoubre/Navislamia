using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// The byte layout of a ground object's entry: <c>TM_SC_ENTER</c> with <c>objType = EOT_Item</c>. The
/// packet is 70 bytes and its <c>itemInfo</c> is the 44 bytes of <c>TS_ENTER::ItemInfo</c> the official PDB
/// carries — <c>code</c> (8 on the wire), <c>cnt</c> (8) and <c>pick_up_order</c> (28, the
/// <c>TS_ENTER::TS_ITEM_PICK_UP_ORDER</c> of the PDB). Compared against rzu
/// (<c>TS_SC_ENTER.h:29-44,145-169</c>, <c>librzu/src/lib/Packet/EncodingRandomized.h:16-32</c>) and the
/// 7.3 client, whose <c>SGameItem::SetPickUpOrder</c> (0x6CA200) reads <c>hPlayer[0]</c> at 46 and
/// <c>nPartyID[0]</c> at 58 — see <c>docs/packet-specs/socle-partage-objets-sol.md</c> §2.
/// </summary>
[TestFixture]
public class GroundItemEnterItemTests
{
    private const uint Handle = 0x11223344;
    private const uint OwnerHandle = 0x55667788;
    private const uint PartyId = 4242;

    /// <summary>An <c>ar_time</c> tick since the server started, never a wall clock.</summary>
    private const uint DropTime = 987654;

    private static byte[] Build(int itemCode = 603002, long count = 7, uint partyId = PartyId) =>
        GameSpawnPackets.BuildEnterItem(Handle, 1000.5f, 2000.25f, 30.125f, 4, itemCode, count, DropTime,
            OwnerHandle, partyId);

    [Test]
    public void EnterItem_IsSeventyBytesOnTheEpic73Id()
    {
        var packet = Build();

        ((ushort)GamePackets.TM_SC_ENTER).Should().Be(3, "1003 only exists from EPIC_9_6_3 on");
        packet.Should().HaveCount(70);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(70, "the total length");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(3, "the packet id");
        packet[6].Should().Be(StorageTestHarness.Checksum(packet),
            "the checksum sums the first six header bytes");
    }

    [Test]
    public void EnterItem_LaysOutTheObjectHeaderAtItsReferenceOffsets()
    {
        var packet = Build();

        packet[7].Should().Be(2, "type = ET_StaticObject");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8, 4)).Should().Be(Handle, "handle");
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(12, 4)).Should().Be(1000.5f, "x");
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(16, 4)).Should().Be(2000.25f, "y");
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(20, 4)).Should().Be(30.125f, "z");
        packet[24].Should().Be(4, "layer sits before the object, not after it");
        packet[25].Should().Be(2, "objType = EOT_Item");
    }

    /// <summary>
    /// <c>itemInfo.code</c> is an <c>EncodedInt&lt;EncodingRandomized&gt;</c>: eight bytes, the two
    /// randomized words zero and the value split high half at +2, low half at +6. A scrambled id here would
    /// be read straight by the client, and the wrong half order would swap the two halves.
    /// </summary>
    [Test]
    public void EnterItem_WritesTheItemCodeAsAnEightByteEncodedInt()
    {
        var packet = Build(itemCode: 0x0001ABCD);

        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(26, 2)).Should().Be(0, "first random word");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(28, 2)).Should().Be(0x0001, "high half of the code");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(30, 2)).Should().Be(0, "second random word");
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(32, 2)).Should().Be(0xABCD, "low half of the code");
    }

    /// <summary>
    /// <c>itemInfo.count</c> is an eight-byte <c>__int64</c> since <c>EPIC_4_1</c>, which 7.3 is well past;
    /// a <c>uint32</c> would have made the packet 66 bytes and moved every offset after it.
    /// </summary>
    [Test]
    public void EnterItem_WritesTheCountAsAUint64AndNeverZero()
    {
        var packet = Build(count: 7);

        BinaryPrimitives.ReadUInt64LittleEndian(packet.AsSpan(34, 8)).Should().Be(7, "count");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(42, 4)).Should().Be(DropTime,
            "drop_time follows the eight-byte count");
        BinaryPrimitives.ReadUInt64LittleEndian(Build(count: 0).AsSpan(34, 8)).Should().Be(1,
            "a zero count is clamped to one");
    }

    /// <summary>
    /// The 28 bytes of <c>pick_up_order</c>: <c>drop_time</c> at 42, then the three player handles at
    /// 46/50/54 and the three party ids at 58/62/66. This repository fills slot 0 and leaves the nine other
    /// bytes zero — the three contributing parties of the official server are a separate card
    /// (<c>socle-partage-objets-sol.md</c> §7.2).
    /// </summary>
    [Test]
    public void EnterItem_FillsOnlyTheFirstSlotOfThePickUpOrder()
    {
        var packet = Build();

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(46, 4)).Should().Be(OwnerHandle, "hPlayer[0]");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(50, 4)).Should().Be(0, "hPlayer[1] is left zero");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(54, 4)).Should().Be(0, "hPlayer[2] is left zero");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(58, 4)).Should().Be((int)PartyId, "nPartyID[0]");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(62, 4)).Should().Be(0, "nPartyID[1] is left zero");
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(66, 4)).Should().Be(0, "nPartyID[2] is left zero");
    }

    /// <summary>A dropped inventory item and a quest item are nobody's party's: <c>nPartyID[0]</c> is zero.</summary>
    [Test]
    public void EnterItem_LeavesThePartySlotEmptyForANonPartyDrop()
    {
        var packet = Build(partyId: 0);

        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(58, 4)).Should().Be(0, "nPartyID[0]");
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(46, 4)).Should().Be(OwnerHandle,
            "hPlayer[0] still names the character who dropped it");
    }
}
