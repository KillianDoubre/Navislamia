using System;
using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// The wire offsets of <c>TM_CS_PUTON_ITEM</c> (200), the equip request whose port requirements the
/// socle <c>docs/packet-specs/socle-exigences-equipement.md</c> gates: a 7 byte header, then the
/// position (<c>int8</c>), the item handle and the target handle — 16 bytes in all (sheet §3). The
/// position comes first at Epic 7.3; from <c>EPIC_9_6_7</c> rzu moves it after the two handles
/// (<c>TS_CS_PUTON_ITEM.h:8-12</c>), so a later epic must not read offset 7 back without re-reading
/// that header (sheet §4).
/// </summary>
[TestFixture]
public class PutonItemPacketsTests
{
    private const int HeaderSize = 7;
    private const int PositionOffset = 7;
    private const int ItemHandleOffset = 8;
    private const int TargetHandleOffset = 12;
    private const int TotalLength = 16;

    /// <summary>The frame SFrame.exe sends: header, position, item handle, target handle.</summary>
    private static byte[] ClientFrame(sbyte position, uint itemHandle, uint targetHandle)
    {
        var packet = new byte[TotalLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), TotalLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_PUTON_ITEM);
        Write(packet, position, itemHandle, targetHandle);

        byte checksum = 0;
        for (var index = 0; index < 6; index++)
        {
            checksum += packet[index];
        }

        packet[6] = checksum;
        return packet;
    }

    private static void Write(byte[] packet, sbyte position, uint itemHandle, uint targetHandle)
    {
        packet[PositionOffset] = unchecked((byte)position);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(ItemHandleOffset, 4), itemHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(TargetHandleOffset, 4), targetHandle);
    }

    [Test]
    public void PutonItemId_MatchesTheEpic73Protocol()
    {
        ((ushort)GamePackets.TM_CS_PUTON_ITEM).Should().Be(200);
        Enum.IsDefined(typeof(GamePackets), (ushort)GamePackets.TM_CS_PUTON_ITEM).Should().BeTrue();
    }

    [Test]
    public void PutonItemRequest_IsSixteenBytesWithThePositionAtSeven()
    {
        // HeaderSize (7) + int8 position (1) + uint32 item handle (4) + uint32 target handle (4).
        TotalLength.Should().Be(HeaderSize + 1 + 4 + 4);
        PositionOffset.Should().Be(HeaderSize);
        ItemHandleOffset.Should().Be(PositionOffset + 1);
        TargetHandleOffset.Should().Be(ItemHandleOffset + 4);
    }

    [Test]
    public void TryReadPutonItem_ReadsEachFieldAtItsOwnOffset()
    {
        var packet = ClientFrame(5, 0x80000456u, 0x40000123u);

        GameActionPackets.TryReadPutonItem(packet, out var request).Should().BeTrue();

        request.Position.Should().Be(5, "position sits at {0} in an {1} byte frame", PositionOffset, TotalLength);
        request.ItemHandle.Should().Be(0x80000456u, "item_handle sits at {0}", ItemHandleOffset);
        request.TargetHandle.Should().Be(0x40000123u, "target_handle sits at {0}", TargetHandleOffset);
    }

    [Test]
    public void TryReadPutonItem_KeepsTheItemAndTargetHandlesApart()
    {
        // The two dwords are read at 8 and 12: a swap would equip a slot the client never asked for.
        var packet = new byte[TotalLength];
        Write(packet, 2, 0x11223344u, 0x55667788u);

        GameActionPackets.TryReadPutonItem(packet, out var request).Should().BeTrue();

        request.ItemHandle.Should().Be(0x11223344u);
        request.TargetHandle.Should().Be(0x55667788u);
    }

    [Test]
    public void TryReadPutonItem_LeavesTheHeaderChecksumOutOfThePayload()
    {
        // +6 is the header checksum (the sum of the first six bytes), not the position: whatever it
        // holds, the position stays at 7.
        var packet = ClientFrame(0, 0x80000001u, 0);
        packet[6] = 0xFF;

        GameActionPackets.TryReadPutonItem(packet, out var request).Should().BeTrue();

        request.Position.Should().Be(0);
        request.ItemHandle.Should().Be(0x80000001u);
    }

    [Test]
    public void TryReadPutonItem_ReadsThePositionAsASignedByte()
    {
        // -1 is ItemWearType.CantWear: the byte must survive as a signed value, not as 255.
        var packet = ClientFrame(-1, 0x80000001u, 0);

        GameActionPackets.TryReadPutonItem(packet, out var request).Should().BeTrue();

        request.Position.Should().Be(-1);
    }

    [Test]
    public void TryReadPutonItem_AcceptsAFrameLongerThanTheBody()
    {
        // The reader refuses only a frame shorter than the body it needs (packet.Length < 16): the
        // Epic 7.3 client's own frame is 16 bytes and the extra bytes are ignored, not refused.
        var packet = new byte[TotalLength + 4];
        Write(packet, 3, 0x80000007u, 0x80000008u);

        GameActionPackets.TryReadPutonItem(packet, out var request).Should().BeTrue();

        request.Position.Should().Be(3);
        request.ItemHandle.Should().Be(0x80000007u);
        request.TargetHandle.Should().Be(0x80000008u);
    }

    [Test]
    public void TryReadPutonItem_RejectsAFrameOneByteShort()
    {
        GameActionPackets.TryReadPutonItem(new byte[TotalLength - 1], out var request).Should().BeFalse();
        request.Should().Be(default(GameActionPackets.PutonItemRequest));
    }

    [Test]
    public void TryReadPutonItem_RejectsAHeaderOnlyFrame()
    {
        GameActionPackets.TryReadPutonItem(new byte[HeaderSize], out _).Should().BeFalse();
    }
}
