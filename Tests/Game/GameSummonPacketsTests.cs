using System;
using System.Buffers.Binary;
using System.Linq;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// Offset tests for the Epic 7.3 summon socle packets (S→C). Every expected size comes from
/// <c>docs/packet-specs/socle-invocations.md</c> §3, itself derived from the pinned rzu headers.
/// </summary>
[TestFixture]
public class GameSummonPacketsTests
{
    [Test]
    public void SummonSocleIds_AreTheEpic73ThreeDigitIds()
    {
        ((ushort)GamePackets.TM_SC_ADD_SUMMON_INFO).Should().Be(301);
        ((ushort)GamePackets.TM_SC_REMOVE_SUMMON_INFO).Should().Be(302);
        ((ushort)GamePackets.TM_SC_UNSUMMON).Should().Be(305);
        ((ushort)GamePackets.TM_SC_UNSUMMON_NOTICE).Should().Be(306);
        ((ushort)GamePackets.TM_SC_SUMMON_EVOLUTION).Should().Be(307);
        ((ushort)GamePackets.TM_SC_MOUNT_SUMMON).Should().Be(320);
        ((ushort)GamePackets.TM_SC_UNMOUNT_SUMMON).Should().Be(321);
    }

    [Test]
    public void GamePackets_HasNoDuplicateValue()
    {
        // GameClient's receive loop casts every id to ushort and switches on it: two names sharing one
        // value would silently drop one of the arms, which is exactly the trap the socle sheet flags
        // for 303 (the same id is used in both directions).
        Enum.GetValues<GamePackets>().Select(value => (ushort)value).Should().OnlyHaveUniqueItems();
    }

    [Test]
    public void BuildAddSummonInfo_LaysOutTheEpic73Record()
    {
        var packet = GameSummonPackets.BuildAddSummonInfo(cardHandle: 0x80000111u, summonHandle: 0x40000222u,
            name: "Nimble", code: 4242, level: 42, sp: 7);

        packet.Length.Should().Be(46);
        AssertFrame(packet, GamePackets.TM_SC_ADD_SUMMON_INFO);

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x80000111u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(0x40000222u);
        packet.AsSpan(15, 6).ToArray().Should().Equal("Nimble"u8.ToArray());
        packet[21].Should().Be(0);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(34, 4)).Should().Be(4242);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(38, 4)).Should().Be(42);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(42, 4)).Should().Be(7);
    }

    [Test]
    public void BuildAddSummonInfo_UsesTheNineteenByteEpic73NameBuffer()
    {
        GameSummonPackets.NameSize.Should().Be(19);

        var name = new string('a', 30);
        var packet = GameSummonPackets.BuildAddSummonInfo(1, 2, name, 3, 4, 5);

        packet.Length.Should().Be(46);
        packet.AsSpan(15, 18).ToArray().Should().Equal("aaaaaaaaaaaaaaaaaa"u8.ToArray());
        packet[33].Should().Be(0);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(34, 4)).Should().Be(3);
    }

    [Test]
    public void BuildAddSummonInfo_FromASummonEntity_UsesTheRegisteredSummonFields()
    {
        var summon = new SummonEntity
        {
            CardItemId = 0x7ABCDEF0,
            Name = "Nimble",
            Lv = 42,
            Sp = 7
        };

        var packet = GameSummonPackets.BuildAddSummonInfo(summon, summonHandle: 0x40000222u, code: 4242);

        packet.Length.Should().Be(46);
        AssertFrame(packet, GamePackets.TM_SC_ADD_SUMMON_INFO);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x7ABCDEF0u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(0x40000222u);
        packet.AsSpan(15, 6).ToArray().Should().Equal("Nimble"u8.ToArray());
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(34, 4)).Should().Be(4242);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(38, 4)).Should().Be(42);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(42, 4)).Should().Be(7);
    }

    [Test]
    public void BuildAddSummonInfo_FromASummonEntity_KeepsOnlyTheLowThirtyTwoBitsOfTheCardItemId()
    {
        // ar_handle_t is 32 bits wide while CardItemId is a long, so the wire cannot carry more.
        var summon = new SummonEntity { CardItemId = 0x1_80000111, Name = "Nimble", Lv = 1 };

        var packet = GameSummonPackets.BuildAddSummonInfo(summon, 0, 0);

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x80000111u);
    }

    [Test]
    public void BuildAddSummonInfo_WritesAnEmptyNameBufferWhenTheNameIsMissing()
    {
        var packet = GameSummonPackets.BuildAddSummonInfo(1, 2, null!, 3, 4, 5);

        packet.AsSpan(15, 19).ToArray().Should().Equal(new byte[19]);
    }

    [Test]
    public void BuildRemoveSummonInfo_LaysOutTheEpic73Record()
    {
        var packet = GameSummonPackets.BuildRemoveSummonInfo(0x80000111u);

        packet.Length.Should().Be(11);
        AssertFrame(packet, GamePackets.TM_SC_REMOVE_SUMMON_INFO);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x80000111u);
    }

    [Test]
    public void BuildUnsummon_LaysOutTheEpic73Record()
    {
        var packet = GameSummonPackets.BuildUnsummon(0x40000222u);

        packet.Length.Should().Be(11);
        AssertFrame(packet, GamePackets.TM_SC_UNSUMMON);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x40000222u);
    }

    [Test]
    public void BuildUnsummonNotice_LaysOutTheEpic73Record()
    {
        // 3000 ar_time ticks is the 30 s the client itself uses for its sort button: one tick is 10 ms.
        var packet = GameSummonPackets.BuildUnsummonNotice(0x40000222u, unsummonDurationTicks: 3000);

        packet.Length.Should().Be(15);
        AssertFrame(packet, GamePackets.TM_SC_UNSUMMON_NOTICE);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x40000222u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(3000);
    }

    [Test]
    public void BuildSummonEvolution_LaysOutTheEpic73Record()
    {
        var packet = GameSummonPackets.BuildSummonEvolution(0x80000111u, 0x40000222u, "Nimble", 4242);

        packet.Length.Should().Be(38);
        AssertFrame(packet, GamePackets.TM_SC_SUMMON_EVOLUTION);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x80000111u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(0x40000222u);
        packet.AsSpan(15, 6).ToArray().Should().Equal("Nimble"u8.ToArray());
        packet[33].Should().Be(0);
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(34, 4)).Should().Be(4242);
    }

    [Test]
    public void BuildMountSummon_LaysOutTheEpic73Record()
    {
        var packet = GameSummonPackets.BuildMountSummon(0x40000222u, 0x40000333u, x: 1234.5f, y: -6789.25f,
            success: true);

        packet.Length.Should().Be(24);
        AssertFrame(packet, GamePackets.TM_SC_MOUNT_SUMMON);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x40000222u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(0x40000333u);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(15, 4)).Should().Be(1234.5f);
        BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(19, 4)).Should().Be(-6789.25f);
        packet[23].Should().Be(1);
    }

    [Test]
    public void BuildMountSummon_WritesAFailedMountAsZero()
    {
        var packet = GameSummonPackets.BuildMountSummon(0x40000222u, 0x40000333u, 1f, 2f, success: false);

        packet[23].Should().Be(0);
    }

    [Test]
    public void BuildUnmountSummon_LaysOutTheEpic73Record()
    {
        var packet = GameSummonPackets.BuildUnmountSummon(0x40000222u, 0x40000333u, flag: 1);

        packet.Length.Should().Be(16);
        AssertFrame(packet, GamePackets.TM_SC_UNMOUNT_SUMMON);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)).Should().Be(0x40000222u);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4)).Should().Be(0x40000333u);
        packet[15].Should().Be(1);
    }

    [Test]
    public void BuildUnmountSummon_WritesANegativeFlagAsOneByte()
    {
        var packet = GameSummonPackets.BuildUnmountSummon(1, 2, flag: -1);

        packet.Length.Should().Be(16);
        packet[15].Should().Be(0xFF);
    }

    private static void AssertFrame(byte[] packet, GamePackets id)
    {
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be((uint)packet.Length);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be((ushort)id);

        byte checksum = 0;
        for (var i = 0; i < 6; i++)
        {
            checksum += packet[i];
        }

        packet[6].Should().Be(checksum);
    }
}
