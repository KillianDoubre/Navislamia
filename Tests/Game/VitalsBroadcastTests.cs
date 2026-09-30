using System;
using System.Buffers.Binary;
using System.Linq;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;

namespace Tests.Game;

[TestFixture]
public class VitalsBroadcastTests
{
    [Test]
    public void HpMpFrameHasTheEpic73Layout()
    {
        var frame = GameStatPackets.BuildHpMp(0x01020304, -5, 95, 100, 3, 40, 50, display: true);

        frame.Length.Should().Be(36);
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)).Should().Be(509);
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(7, 4)).Should().Be(0x01020304u);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(11, 4)).Should().Be(-5);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(15, 4)).Should().Be(95);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(19, 4)).Should().Be(100);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(23, 4)).Should().Be(3);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(27, 4)).Should().Be(40);
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(31, 4)).Should().Be(50);
        frame[35].Should().Be(1);
    }

    [Test]
    public void AVitalPropertyReachesObserversAsHpMpAndThePlayerAsItsProperty()
    {
        var visibility = new PlayerVisibilityService(A.Fake<ILogger<PlayerVisibilityService>>());
        var selfConnection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var peerConnection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var self = StorageTestHarness.NewGameClient(selfConnection, playerVisibilityService: visibility);
        var peer = StorageTestHarness.NewGameClient(peerConnection, playerVisibilityService: visibility);
        var info = StorageTestHarness.Session(self);
        info.CharacterHandle = 1;
        info.CharacterHp = 70;
        info.CharacterMaxHp = 100;
        info.CharacterMp = 20;
        StorageTestHarness.Session(peer).CharacterHandle = 2;
        visibility.Registry.Register(1, self);
        visibility.Registry.Register(2, peer);
        visibility.Index.Add(new PlayerPresence(1, new PlayerAppearance(), 0, 0, 0, 0));
        visibility.Index.Add(new PlayerPresence(2, new PlayerAppearance(), 0, 10, 0, 0));
        StorageTestHarness.Session(peer).SpawnedPlayers[1] = 1;

        self.SendVitalProperty(GameStatPackets.BuildProperty(1, "hp", 70));

        selfConnection.Sent.Should().ContainSingle(frame => Id(frame) == (ushort)GamePackets.TM_SC_PROPERTY);
        var seen = peerConnection.Sent.Should().ContainSingle().Subject;
        Id(seen).Should().Be((ushort)GamePackets.TM_SC_HPMP);
        BinaryPrimitives.ReadUInt32LittleEndian(seen.AsSpan(7, 4)).Should().Be(1u);
        BinaryPrimitives.ReadInt32LittleEndian(seen.AsSpan(15, 4)).Should().Be(70);
        BinaryPrimitives.ReadInt32LittleEndian(seen.AsSpan(27, 4)).Should().Be(20);
    }

    private static ushort Id(byte[] frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2));
}
