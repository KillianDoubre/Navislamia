using System;
using System.Buffers.Binary;
using System.Linq;
using System.Runtime.InteropServices;
using FluentAssertions;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;

namespace Tests.Game;

/// <summary>
/// TM_SC_TAMING_INFO (310) is 16 bytes on the wire: the 7-byte header, the signed <c>mode</c> byte at
/// offset 7, <c>tamer_handle</c> at 8-11 and <c>target_handle</c> at 12-15. The id and the layout are
/// settled by rzu (<c>TS_SC_TAMING_INFO.h:5-12</c>): the two handles are 32-bit <c>ar_handle_t</c> with no
/// version gate, and the id is 310 below <c>EPIC_9_6_3</c>, 1310 from there on — out of this repository's
/// scope.
/// <para>
/// The frame is server to client only. Nothing emits it yet: the attempt that would — the tamer state, the
/// card flags and the regional broadcast — is étape 1 of the socle, so this lot delivers the encoder and
/// the receive-loop arm that keeps a declared id away from the throwing switch, nothing more
/// (docs/packet-specs/socle-apprivoisement-invocation.md §3.1, §11).
/// </para>
/// </summary>
[TestFixture]
public class TamingInfoPacketsTests
{
    private const int PacketLength = 16;
    private const int ModeOffset = 7;
    private const int TamerHandleOffset = 8;
    private const int TargetHandleOffset = 12;
    private const uint TamerHandle = 0x40000111u;
    private const uint TargetHandle = 0x00000222u;

    /// <summary>A frame the way the server would build it, since the 7.3 client builds none of it.</summary>
    private static byte[] ServerFrame(sbyte mode)
    {
        return GameSummonPackets.BuildTamingInfo(mode, TamerHandle, TargetHandle);
    }

    [Test]
    public void Ids_AreTheEpic73Ones()
    {
        ((ushort)GamePackets.TM_SC_TAMING_INFO).Should().Be(310);

        // rzu gates the id: 1310 from EPIC_9_6_3 (0x090603, dated 20200713) on, above EPIC_7_3 = 0x070300.
        // That id is out of this repository's scope and must stay undeclared here — a second member would
        // be a second client contract nothing can honour.
        Enum.IsDefined(typeof(GamePackets), (ushort)1310).Should().BeFalse(
            "1310 is the 9.6.3 remap of the taming info and is above EPIC_7_3");
    }

    [Test]
    public void LayoutConstants_NameTheHeaderAndThePayloadSeparately()
    {
        // 7 + 1 + 4 + 4: naming the parts apart means a change of the 7-byte header breaks this test even
        // when the total happens to stay at 16.
        Marshal.SizeOf<Header>().Should().Be(7);
        GameSummonPackets.TamingInfoPacketSize.Should().Be(PacketLength);
        GameSummonPackets.TamingInfoModeOffset.Should().Be(ModeOffset);
        GameSummonPackets.TamingInfoTamerHandleOffset.Should().Be(TamerHandleOffset);
        GameSummonPackets.TamingInfoTargetHandleOffset.Should().Be(TargetHandleOffset);
    }

    [Test]
    public void BuildTamingInfo_LaysOutTheEpic73Record()
    {
        var packet = ServerFrame(GameSummonPackets.TamingModeStart);

        packet.Should().HaveCount(PacketLength);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4)).Should().Be(PacketLength);
        BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2)).Should().Be(310);
        packet[6].Should().Be(StorageTestHarness.Checksum(packet),
            "the receive loop refuses a bad checksum");
        packet[ModeOffset].Should().Be(0);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(TamerHandleOffset, 4)).Should().Be(TamerHandle);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(TargetHandleOffset, 4)).Should().Be(TargetHandle);
    }

    [Test]
    public void ModeConstants_AreTheDecidedOnes()
    {
        // One emitter per mode in the reference: SetTamer broadcasts 0 (World.cpp:698-712), the patrol timer
        // broadcasts 1 (Monster.cpp:1156), the successful draw broadcasts 2 (World.cpp:650-656) and the
        // failed one 3 (World.cpp:670-674).
        GameSummonPackets.TamingModeStart.Should().Be(0);
        GameSummonPackets.TamingModeAbandon.Should().Be(1);
        GameSummonPackets.TamingModeSuccess.Should().Be(2);
        GameSummonPackets.TamingModeFailed.Should().Be(3);
    }

    [TestCase(GameSummonPackets.TamingModeStart, TestName = "BuildTamingInfo_WritesModeZeroOnStart")]
    [TestCase(GameSummonPackets.TamingModeAbandon, TestName = "BuildTamingInfo_WritesModeOneOnAbandon")]
    [TestCase(GameSummonPackets.TamingModeSuccess, TestName = "BuildTamingInfo_WritesModeTwoOnSuccess")]
    [TestCase(GameSummonPackets.TamingModeFailed, TestName = "BuildTamingInfo_WritesModeThreeOnFailure")]
    public void BuildTamingInfo_WritesTheModeInItsSignedByte(sbyte mode)
    {
        var packet = ServerFrame(mode);

        packet[ModeOffset].Should().Be((byte)mode);
        packet.Should().HaveCount(PacketLength, "the mode does not change the size");
    }

    [Test]
    public void BuildTamingInfo_KeepsTheWholeUnsignedRangeOfTheHandles()
    {
        // ar_handle_t is 32 bits unsigned: a handle above 0x7FFFFFFF must not be written sign extended, and
        // the two fields must not overlap.
        var packet = GameSummonPackets.BuildTamingInfo(GameSummonPackets.TamingModeSuccess,
            tamerHandle: 0xFFFFFFFFu, targetHandle: 0x80000000u);

        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(TamerHandleOffset, 4)).Should().Be(0xFFFFFFFFu);
        BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(TargetHandleOffset, 4)).Should().Be(0x80000000u);
    }

    [Test]
    public void OnDataReceived_DropsTheServerToClientFrameWithoutThrowing()
    {
        // The 7.3 client routes 310 as an incoming frame and builds none of it, so an incoming one is a
        // protocol anomaly, not a request. It must not reach the final switch, whose only arm throws
        // "Unknown Packet Type".
        var frame = ServerFrame(GameSummonPackets.TamingModeStart);
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(frame.Length);

        receive.Should().NotThrow("an id defined in GamePackets must not reach the throwing switch");
        connection.BytesAvailable.Should().Be(0, "the whole frame was consumed");
        connection.Sent.Should().BeEmpty("a server to client frame is answered nothing");
    }

    [Test]
    public void OnDataReceived_KeepsTheLoopOnAFrameCoalescedWithAnotherOne()
    {
        // The dropped frame must not swallow or desynchronise the one behind it: the keepalive is consumed
        // too.
        var keepalive = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(keepalive.AsSpan(0, 4), 7u);
        BinaryPrimitives.WriteUInt16LittleEndian(keepalive.AsSpan(4, 2), (ushort)GamePackets.TM_NONE);
        keepalive[6] = StorageTestHarness.Checksum(keepalive);

        var stream = ServerFrame(GameSummonPackets.TamingModeFailed).Concat(keepalive).ToArray();
        var connection = new StorageTestHarness.FrameConnection(stream);
        var client = StorageTestHarness.NewGameClient(connection);

        var receive = () => client.OnDataReceived(stream.Length);

        receive.Should().NotThrow();
        connection.Sent.Should().BeEmpty();
        connection.BytesAvailable.Should().Be(0, "both frames were consumed");
    }
}
