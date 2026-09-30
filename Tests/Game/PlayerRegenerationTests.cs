using System;
using System.Buffers.Binary;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Stats;
using Navislamia.Game.Network.Packets.Enums;

namespace Tests.Game;

[TestFixture]
public class PlayerRegenerationTests
{
    [Test]
    public void TickRestoresVitalsFasterWhileSittingAndLeavesDeadPlayersDead()
    {
        var visibility = new PlayerVisibilityService(A.Fake<ILogger<PlayerVisibilityService>>());
        var stats = A.Fake<IStatService>();
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection, playerVisibilityService: visibility);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 1;
        info.CharacterHp = 50;
        info.CharacterMp = 50;
        visibility.Registry.Register(1, client);
        A.CallTo(() => stats.Compute(info)).Returns(new CharacterStatResult(
            new StatBlock { MaxHp = 100, MaxMp = 100, HpRegenPoint = 100, MpRegenPoint = 100,
                HpRegenPercentage = 5, MpRegenPercentage = 5 }, new StatBlock()));
        var service = new PlayerRegenerationService(visibility, stats,
            A.Fake<ILogger<PlayerRegenerationService>>());

        service.Tick();
        info.CharacterHp.Should().Be(55);
        info.CharacterMp.Should().Be(55);
        var first = connection.Sent[0];
        BinaryPrimitives.ReadUInt16LittleEndian(first.AsSpan(4, 2))
            .Should().Be((ushort)GamePackets.TM_SC_REGEN_HPMP);
        BinaryPrimitives.ReadInt32LittleEndian(first.AsSpan(11, 4)).Should().Be(5);
        BinaryPrimitives.ReadInt32LittleEndian(first.AsSpan(19, 4)).Should().Be(55);

        info.IsSitting = true;
        service.Tick();
        info.CharacterHp.Should().Be(65);
        info.CharacterMp.Should().Be(65);

        info.CharacterHp = 0;
        service.Tick();
        info.CharacterHp.Should().Be(0);
        info.CharacterMp.Should().Be(65);
    }
}
