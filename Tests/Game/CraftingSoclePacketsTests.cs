using System.Buffers.Binary;
using FluentAssertions;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// Offset tests for the crafting and item-enchantment family, all Epic 7.3:
/// <c>TM_CS_MIX</c> (256) is 15 + 6N bytes (handle at 7, count at 11, slot count at 13, then N 6-byte
/// records), <c>TM_CS_SOULSTONE_CRAFT</c> (260) is 27 (handle at 7, four slot handles from 11),
/// <c>TM_CS_REPAIR_SOULSTONE</c> (262) is 31 (six handles from 7),
/// <c>TM_CS_TRANSMIT_ETHEREAL_DURABILITY</c> (263) is 11 (one handle at 7) and
/// <c>TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT</c> (264) is 11 (one float at 7, no
/// <c>target</c> byte: that field is gated EPIC_8_1).
/// See docs/packet-specs/socle-artisanat-objets.md §3 and §4.
/// </summary>
[TestFixture]
public class CraftingSoclePacketsTests
{
    private const int HeaderSize = 7;

    [Test]
    public void CraftingFamily_CarriesItsEpic73Ids()
    {
        ((ushort)GamePackets.TM_CS_MIX).Should().Be(256);
        ((ushort)GamePackets.TM_SC_MIX_RESULT).Should().Be(257);
        ((ushort)GamePackets.TM_CS_SOULSTONE_CRAFT).Should().Be(260);
        ((ushort)GamePackets.TM_SC_SHOW_SOULSTONE_REPAIR_WINDOW).Should().Be(261);
        ((ushort)GamePackets.TM_CS_REPAIR_SOULSTONE).Should().Be(262);
        ((ushort)GamePackets.TM_CS_TRANSMIT_ETHEREAL_DURABILITY).Should().Be(263);
        ((ushort)GamePackets.TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT).Should().Be(264);
    }

    [Test]
    public void CraftingFamily_IsDefinedSoTheReceiveLoopDispatchesEveryMember()
    {
        // A member of GamePackets without a receive arm reaches the "Unknown Packet Type" throw and kills
        // the receive loop, so every id of the family has to be defined and dispatched together.
        foreach (var id in new ushort[] { 256, 257, 260, 261, 262, 263, 264 })
        {
            Enum.IsDefined(typeof(GamePackets), id).Should().BeTrue($"{id} belongs to the family");
        }
    }

    [Test]
    public void SoulstoneCraftWindow_HasNoEstablishedId()
    {
        // rzu and NGemity both declare TS_SC_SHOW_SOULSTONE_CRAFT_WINDOW on 259, where op_codes.md:86
        // declares TM_CS_DONATE_REWARD: no 7.3 id is established, so 259 must stay out of the enum.
        Enum.IsDefined(typeof(GamePackets), (ushort)259).Should().BeFalse();
    }

    // ---------------------------------------------------------------- 256, TM_CS_MIX

    private static byte[] MixFrame(ushort declaredCount, params (uint Handle, ushort Count)[] subItems)
    {
        var packet = new byte[HeaderSize + 8 + subItems.Length * 6];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_MIX);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), 0x80000001u);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(11, 2), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(13, 2), declaredCount);

        for (var i = 0; i < subItems.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(15 + i * 6, 4), subItems[i].Handle);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(19 + i * 6, 2), subItems[i].Count);
        }

        return packet;
    }

    [Test]
    public void TryReadMix_LaysOutTheTargetSlotAndNoMaterialSlot()
    {
        var packet = MixFrame(0);

        GameActionPackets.TryReadMix(packet, out var request).Should().BeTrue();

        packet.Length.Should().Be(15);
        request.MainItemHandle.Should().Be(0x80000001u);
        request.MainItemCount.Should().Be(3);
        request.DeclaredSubItemCount.Should().Be(0);
        request.SubItems.Should().BeEmpty();
    }

    [Test]
    public void TryReadMix_LaysOutEachMaterialSlotSixBytesApart()
    {
        var packet = MixFrame(2, (0x80000010u, 1), (0x80000011u, 2));

        GameActionPackets.TryReadMix(packet, out var request).Should().BeTrue();

        // Cube in Material Slot 1, powder in Material Slot 2 — the two-record frame is 27 bytes.
        packet.Length.Should().Be(27);
        request.DeclaredSubItemCount.Should().Be(2);
        request.SubItems.Should().HaveCount(2);
        request.SubItems[0].Handle.Should().Be(0x80000010u);
        request.SubItems[0].Count.Should().Be(1);
        request.SubItems[1].Handle.Should().Be(0x80000011u);
        request.SubItems[1].Count.Should().Be(2);
    }

    [Test]
    public void TryReadMix_KeepsTheSlotOrderOfTheFrame()
    {
        var packet = MixFrame(2, (1u, 10), (2u, 20));

        GameActionPackets.TryReadMix(packet, out var request).Should().BeTrue();

        // Asymmetric values: reading the count before the handle would swap them here.
        request.SubItems[0].Should().Be(new GameActionPackets.MixItemInfo(1u, 10));
        request.SubItems[1].Should().Be(new GameActionPackets.MixItemInfo(2u, 20));
    }

    [Test]
    public void TryReadMix_AcceptsTheNineSlotMaximum()
    {
        var slots = new (uint, ushort)[GameActionPackets.MaxSubItems];
        for (var i = 0; i < slots.Length; i++)
        {
            slots[i] = ((uint)(0x80000100 + i), (ushort)(i + 1));
        }

        var packet = MixFrame(GameActionPackets.MaxSubItems, slots);

        GameActionPackets.MaxSubItems.Should().Be(9);
        packet.Length.Should().Be(69);
        GameActionPackets.TryReadMix(packet, out var request).Should().BeTrue();
        request.SubItems.Should().HaveCount(9);
        request.SubItems[8].Handle.Should().Be(0x80000108u);
    }

    [Test]
    public void TryReadMix_RefusesMoreMaterialSlotsThanTheReferenceTableHolds()
    {
        var slots = new (uint, ushort)[GameActionPackets.MaxSubItems + 1];
        for (var i = 0; i < slots.Length; i++)
        {
            slots[i] = ((uint)(0x80000100 + i), 1);
        }

        var packet = MixFrame(GameActionPackets.MaxSubItems + 1, slots);

        packet.Length.Should().Be(75);
        GameActionPackets.TryReadMix(packet, out var request).Should().BeFalse();
        request.SubItems.Should().BeNull();
    }

    [TestCase(0, TestName = "TryReadMix_RefusesAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadMix_RefusesAHeaderOnlyFrame")]
    [TestCase(14, TestName = "TryReadMix_RefusesATruncatedFrame")]
    [TestCase(16, TestName = "TryReadMix_RefusesAFrameWhoseTailIsNotAWholeSlot")]
    [TestCase(17, TestName = "TryReadMix_RefusesAFrameOneByteOverASlot")]
    public void TryReadMix_RefusesAnyFrameShorterThanTheTargetSlot(int length)
    {
        var packet = new byte[length];

        GameActionPackets.TryReadMix(packet, out var request).Should().BeFalse();
        request.SubItems.Should().BeNull();
    }

    [Test]
    public void TryReadMix_RefusesADeclaredSlotCountThatDisagreesWithTheFrameLength()
    {
        // rzu writes the real array length into the count field (_(count)(uint16_t, sub_items)), so a
        // frame whose two sizes disagree is malformed rather than partly usable.
        var packet = MixFrame(1, (0x80000010u, 1), (0x80000011u, 2));

        packet.Length.Should().Be(27);
        GameActionPackets.TryReadMix(packet, out var request).Should().BeFalse();
        request.SubItems.Should().BeNull();
    }

    [Test]
    public void TryReadMix_AcceptsAZeroTargetHandle()
    {
        // A mix with no target slot is a case the reference server handles explicitly
        // (WorldSession.cpp:1451 tests handle != 0), so zero must not be read as malformed.
        var packet = MixFrame(0);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), 0u);

        GameActionPackets.TryReadMix(packet, out var request).Should().BeTrue();
        request.MainItemHandle.Should().Be(0u);
    }

    // ------------------------------------------------------- 260, TM_CS_SOULSTONE_CRAFT

    private static byte[] SoulstoneCraftFrame(uint craftItemHandle, params uint[] slots)
    {
        var packet = new byte[HeaderSize + 4 + 4 * 4];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_SOULSTONE_CRAFT);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), craftItemHandle);

        for (var i = 0; i < slots.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(11 + i * 4, 4), slots[i]);
        }

        return packet;
    }

    [Test]
    public void TryReadSoulstoneCraft_LaysOutTheItemThenFourSlots()
    {
        var packet = SoulstoneCraftFrame(0x80000020u, 0x80000030u, 0u, 0x80000031u, 0u);

        GameActionPackets.TryReadSoulstoneCraft(packet, out var request).Should().BeTrue();

        packet.Length.Should().Be(27);
        request.CraftItemHandle.Should().Be(0x80000020u);
        request.SoulstoneHandles.Should().Equal(0x80000030u, 0u, 0x80000031u, 0u);
    }

    [Test]
    public void TryReadSoulstoneCraft_KeepsAnEmptySlotAsZero()
    {
        // An always-27 frame writes a zero handle for a socket left empty; it must never be renumbered
        // or dropped, or the server would socket the stone into the wrong chassis.
        var packet = SoulstoneCraftFrame(0x80000020u, 0u, 0x80000030u, 0u, 0u);

        GameActionPackets.TryReadSoulstoneCraft(packet, out var request).Should().BeTrue();

        request.SoulstoneHandles[0].Should().Be(0u);
        request.SoulstoneHandles[1].Should().Be(0x80000030u);
        request.SoulstoneHandles[3].Should().Be(0u);
    }

    [TestCase(0, TestName = "TryReadSoulstoneCraft_RefusesAnEmptyFrame")]
    [TestCase(26, TestName = "TryReadSoulstoneCraft_RefusesATruncatedFrame")]
    [TestCase(28, TestName = "TryReadSoulstoneCraft_RefusesAPaddedFrame")]
    public void TryReadSoulstoneCraft_AcceptsOnlyTwentySevenBytes(int length)
    {
        var packet = new byte[length];
        if (length >= 27)
        {
            SoulstoneCraftFrame(1u, 2u, 0u, 0u, 0u).CopyTo(packet, 0);
        }

        GameActionPackets.TryReadSoulstoneCraft(packet, out var request).Should().BeFalse();
        request.SoulstoneHandles.Should().BeNull();
    }

    // -------------------------------------------------------- 262, TM_CS_REPAIR_SOULSTONE

    [Test]
    public void TryReadRepairSoulstone_LaysOutSixHandlesFromOffsetSeven()
    {
        var packet = new byte[31];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), 31u);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_REPAIR_SOULSTONE);
        for (var i = 0; i < 6; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7 + i * 4, 4), (uint)(0x80000200 + i));
        }

        GameActionPackets.TryReadRepairSoulstone(packet, out var request).Should().BeTrue();

        packet.Length.Should().Be(31);
        request.ItemHandles.Should().HaveCount(6);
        request.ItemHandles.Should().Equal(0x80000200u, 0x80000201u, 0x80000202u, 0x80000203u, 0x80000204u,
            0x80000205u);
    }

    [TestCase(0, TestName = "TryReadRepairSoulstone_RefusesAnEmptyFrame")]
    [TestCase(30, TestName = "TryReadRepairSoulstone_RefusesATruncatedFrame")]
    [TestCase(32, TestName = "TryReadRepairSoulstone_RefusesAPaddedFrame")]
    public void TryReadRepairSoulstone_AcceptsOnlyThirtyOneBytes(int length)
    {
        var packet = new byte[length];

        GameActionPackets.TryReadRepairSoulstone(packet, out var request).Should().BeFalse();
        request.ItemHandles.Should().BeNull();
    }

    // --------------------------------- 263 and 264, the ethereal durability frames

    [Test]
    public void TryReadTransmitEtherealDurability_ReadsTheHandleAtSeven()
    {
        var packet = new byte[11];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), 11u);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2),
            (ushort)GamePackets.TM_CS_TRANSMIT_ETHEREAL_DURABILITY);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), 0x80000040u);

        GameActionPackets.TryReadTransmitEtherealDurability(packet, out var request).Should().BeTrue();

        packet.Length.Should().Be(11);
        request.Handle.Should().Be(0x80000040u);
    }

    [TestCase(0, TestName = "TryReadTransmitEtherealDurability_RefusesAnEmptyFrame")]
    [TestCase(7, TestName = "TryReadTransmitEtherealDurability_RefusesAHeaderOnlyFrame")]
    [TestCase(10, TestName = "TryReadTransmitEtherealDurability_RefusesATruncatedFrame")]
    [TestCase(12, TestName = "TryReadTransmitEtherealDurability_RefusesAPaddedFrame")]
    public void TryReadTransmitEtherealDurability_AcceptsOnlyElevenBytes(int length)
    {
        var packet = new byte[length];

        GameActionPackets.TryReadTransmitEtherealDurability(packet, out var request).Should().BeFalse();
        request.Handle.Should().Be(0u);
    }

    [Test]
    public void TryReadTransmitEtherealDurabilityToEquipment_ReadsTheRateAsAFloatAtSeven()
    {
        var packet = new byte[11];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), 11u);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2),
            (ushort)GamePackets.TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT);
        BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(7, 4), 0.5f);

        GameActionPackets.TryReadTransmitEtherealDurabilityToEquipment(packet, out var request).Should().BeTrue();

        packet.Length.Should().Be(11);
        request.Rate.Should().Be(0.5f);
    }

    [TestCase(0f, TestName = "TryReadTransmitEtherealDurabilityToEquipment_KeepsAZeroRate")]
    [TestCase(1f, TestName = "TryReadTransmitEtherealDurabilityToEquipment_KeepsAFullRate")]
    [TestCase(100f, TestName = "TryReadTransmitEtherealDurabilityToEquipment_DoesNotBoundTheRate")]
    [TestCase(-1.5f, TestName = "TryReadTransmitEtherealDurabilityToEquipment_KeepsANegativeRate")]
    public void TryReadTransmitEtherealDurabilityToEquipment_DoesNotInterpretTheRate(float rate)
    {
        // The rate is a float of unknown unit (spec §7 NON ÉTABLI 7) and NGemity has no handler to copy:
        // the reader must not turn it into a percentage or refuse it.
        var packet = new byte[11];
        BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(7, 4), rate);

        GameActionPackets.TryReadTransmitEtherealDurabilityToEquipment(packet, out var request).Should().BeTrue();
        request.Rate.Should().Be(rate);
    }

    [Test]
    public void TryReadTransmitEtherealDurabilityToEquipment_RefusesTheTwelveByteEightOneForm()
    {
        // From EPIC_8_1 the frame gains a trailing 'target' byte (0 for the player, 1 to 6 for a summon).
        // 7.3 has no such field, so the 12-byte form is refused rather than read with a stray byte.
        var packet = new byte[12];
        BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(7, 4), 1f);
        packet[11] = 0;

        GameActionPackets.TryReadTransmitEtherealDurabilityToEquipment(packet, out var request).Should().BeFalse();
        request.Rate.Should().Be(0f);
    }

    [TestCase(0, TestName = "TryReadTransmitEtherealDurabilityToEquipment_RefusesAnEmptyFrame")]
    [TestCase(10, TestName = "TryReadTransmitEtherealDurabilityToEquipment_RefusesATruncatedFrame")]
    public void TryReadTransmitEtherealDurabilityToEquipment_RefusesAFrameShorterThanElevenBytes(int length)
    {
        var packet = new byte[length];

        GameActionPackets.TryReadTransmitEtherealDurabilityToEquipment(packet, out _).Should().BeFalse();
    }

    // --------------------------------------------------------- the handles actually named

    [Test]
    public void ReferencedHandles_SkipsTheZeroSentinelsOfAMix()
    {
        var request = new GameActionPackets.MixRequest(0u, 0,
            3,
            new[]
            {
                new GameActionPackets.MixItemInfo(0x80000010u, 1),
                new GameActionPackets.MixItemInfo(0u, 0),
                new GameActionPackets.MixItemInfo(0x80000011u, 1)
            });

        CraftingSocleRules.ReferencedHandles(request).Should().Equal(0x80000010u, 0x80000011u);
    }

    [Test]
    public void ReferencedHandles_KeepsTheTargetSlotFirst()
    {
        var request = new GameActionPackets.MixRequest(0x80000001u, 1, 1,
            new[] { new GameActionPackets.MixItemInfo(0x80000010u, 1) });

        CraftingSocleRules.ReferencedHandles(request).Should().Equal(0x80000001u, 0x80000010u);
    }

    [Test]
    public void ReferencedHandles_SkipsTheEmptySocketsOfASoulstoneCraft()
    {
        var request = new GameActionPackets.SoulstoneCraftRequest(0x80000020u,
            new[] { 0u, 0x80000030u, 0u, 0u });

        // The item being socketed is resolved too, and the three empty sockets are not.
        CraftingSocleRules.ReferencedHandles(request).Should().Equal(0x80000020u, 0x80000030u);
    }

    [Test]
    public void ReferencedHandles_ListsTheSixRepairHandlesAndSkipsTheZeroes()
    {
        var request = new GameActionPackets.RepairSoulstoneRequest(new[]
        {
            0u, 0x80000201u, 0u, 0x80000203u, 0u, 0x80000205u
        });

        CraftingSocleRules.ReferencedHandles(request).Should().Equal(0x80000201u, 0x80000203u, 0x80000205u);
    }

    [Test]
    public void ReferencedHandles_ReturnsNothingForAZeroEtherealHandle()
    {
        CraftingSocleRules.ReferencedHandles(new GameActionPackets.TransmitEtherealDurabilityRequest(0u))
            .Should().BeEmpty();
        CraftingSocleRules.ReferencedHandles(new GameActionPackets.TransmitEtherealDurabilityRequest(0x80000040u))
            .Should().Equal(0x80000040u);
    }
}
