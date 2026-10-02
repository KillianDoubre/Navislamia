using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;

namespace Tests.Game;

/// <summary>
/// The states of a monster entering a view (docs/packet-specs/socle-etats-monstre-entree.md §5.3): the
/// <c>ENTER</c> of a monster carrying active states is followed by one 505 frame per state, under the
/// handle that <c>ENTER</c> was just built with for this client; a monster without any state produces
/// its <c>ENTER</c> and nothing else.
/// </summary>
[TestFixture]
public class MonsterEnterStateTests
{
    private const uint PlayerHandle = 0x10000001;
    private const uint OtherHandle = 0x10000002;
    private const int Poison = 2622;
    private const int Slow = 2623;
    private const int PoisonSkill = 3005;
    private const int SlowSkill = 3006;

    [Test]
    public void A_monster_carrying_states_sends_one_state_frame_per_state_after_its_enter_frame()
    {
        var (world, client) = Scene(PlayerHandle, 0f, 0f);
        var monster = OnlyMonster(world);
        var now = ServerClock.Now;
        var poison = world.AddState(monster, Poison, PoisonSkill, 3, now, unchecked(now + 3000));
        var slow = world.AddState(monster, Slow, SlowSkill, 1, now, unchecked(now + 5000));

        new MonsterSpawnService(world).Sync(client);

        var frames = SentOf(client);
        var ids = frames.Select(Id).ToArray();
        ids.Should().Equal(new ushort[] { (ushort)GamePackets.TM_SC_ENTER, (ushort)GamePackets.TM_SC_STATE,
            (ushort)GamePackets.TM_SC_STATE }, "the enter frame comes first, its states follow it");

        var handle = BinaryPrimitives.ReadUInt32LittleEndian(frames[0].AsSpan(8, 4));
        handle.Should().NotBe(0);
        frames[0].Should().HaveCount(73, "the frame the states follow is the monster's ENTER");

        StateOf(frames[1]).Should().Be((handle, poison.StateHandle, (uint)Poison, (ushort)3, poison.EndTick,
            poison.StartTick));
        StateOf(frames[2]).Should().Be((handle, slow.StateHandle, (uint)Slow, (ushort)1, slow.EndTick,
            slow.StartTick));
    }

    [Test]
    public void The_state_frames_carry_the_505_offsets_of_the_entering_monster()
    {
        var (world, client) = Scene(PlayerHandle, 0f, 0f);
        var monster = OnlyMonster(world);
        var now = ServerClock.Now;
        // Distinct and close enough to now that the wrap-around comparison reads them as alive.
        var start = unchecked(now - 0x0123456);
        var end = unchecked(now + 0x02ABCDE);
        var state = world.AddState(monster, 2622, PoisonSkill, 0x0302, start, end);

        new MonsterSpawnService(world).Sync(client);

        var frame = SentOf(client).Single(f => Id(f) == (ushort)GamePackets.TM_SC_STATE);
        var s = frame.AsSpan();

        frame.Should().HaveCount(63, "TS_SC_STATE for Epic 7.3 is 63 bytes");
        BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(0, 4)).Should().Be(63);
        BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(4, 2)).Should().Be((ushort)GamePackets.TM_SC_STATE);
        frame[6].Should().Be(StorageTestHarness.Checksum(frame));

        // Every field is read back at its byte, so a swapped pair cannot pass: the four-byte state code
        // sits at 13 (bytes 3E 0A 00 00) and the two-byte level at 17 (bytes 02 03), level after code.
        s.Slice(13, 4).ToArray().Should().Equal(0x3E, 0x0A, 0x00, 0x00);
        s.Slice(17, 2).ToArray().Should().Equal(0x02, 0x03);
        // Little endian, proven on a value the test did not write byte by byte.
        s.Slice(19, 4).ToArray().Should().Equal(BitConverter.GetBytes(end));

        var handle = BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(7, 4));
        handle.Should().NotBe(0);
        BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(11, 2)).Should().Be(state.StateHandle);
        BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(13, 4)).Should().Be(2622u);
        BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(17, 2)).Should().Be(0x0302);
        BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(19, 4)).Should().Be(end);
        BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(23, 4)).Should().Be(start);
        BinaryPrimitives.ReadInt32LittleEndian(s.Slice(27, 4)).Should().Be(0, "no monster state carries a value");
        s.Slice(31, 32).ToArray().Should().OnlyContain(b => b == 0, "state_string_value is unused");
    }

    [Test]
    public void A_monster_without_any_state_only_sends_its_enter_frame()
    {
        var (world, client) = Scene(PlayerHandle, 0f, 0f);

        new MonsterSpawnService(world).Sync(client);

        var frames = SentOf(client);
        frames.Select(Id).Should().Equal((ushort)GamePackets.TM_SC_ENTER);
    }

    [Test]
    public void A_state_whose_deadline_has_passed_is_not_announced()
    {
        var (world, client) = Scene(PlayerHandle, 0f, 0f);
        var monster = OnlyMonster(world);
        var now = ServerClock.Now;
        // The expiry tick runs every 500 ms: between two ticks a state can be over and still be listed.
        world.AddState(monster, Poison, PoisonSkill, 1, unchecked(now - 1000), unchecked(now - 100));

        new MonsterSpawnService(world).Sync(client);

        SentOf(client).Select(Id).Should().Equal((ushort)GamePackets.TM_SC_ENTER);
    }

    [Test]
    public void A_monster_already_in_view_is_not_announced_again()
    {
        var (world, client) = Scene(PlayerHandle, 0f, 0f);
        var monster = OnlyMonster(world);
        var now = ServerClock.Now;
        world.AddState(monster, Poison, PoisonSkill, 1, now, unchecked(now + 3000));
        var service = new MonsterSpawnService(world);

        service.Sync(client);
        SentOf(client).Select(Id).Should().Equal((ushort)GamePackets.TM_SC_ENTER,
            (ushort)GamePackets.TM_SC_STATE);

        SentOf(client).Clear();
        service.Sync(client);

        SentOf(client).Should().BeEmpty("a monster already in view keeps its handle and gets no second state");
    }

    [Test]
    public void Each_client_gets_the_states_under_its_own_monster_handle()
    {
        var world = World();
        var state = world.AddState(OnlyMonster(world), Poison, PoisonSkill, 2, ServerClock.Now,
            unchecked(ServerClock.Now + 3000));
        var a = Client(PlayerHandle, 0f, 0f);
        var b = Client(OtherHandle, 100f, 0f);
        var service = new MonsterSpawnService(world);

        service.Sync(a);
        service.Sync(b);

        var aEnterHandle = BinaryPrimitives.ReadUInt32LittleEndian(SentOf(a)[0].AsSpan(8, 4));
        var bEnterHandle = BinaryPrimitives.ReadUInt32LittleEndian(SentOf(b)[0].AsSpan(8, 4));
        aEnterHandle.Should().NotBe(bEnterHandle, "a monster's handle is built per observer");

        var stateToA = SentOf(a).Single(f => Id(f) == (ushort)GamePackets.TM_SC_STATE);
        var stateToB = SentOf(b).Single(f => Id(f) == (ushort)GamePackets.TM_SC_STATE);
        stateToA.Should().HaveCount(63);
        stateToB.Should().HaveCount(63);
        StateOf(stateToA).Should().Be((aEnterHandle, state.StateHandle, (uint)Poison, (ushort)2,
            state.EndTick, state.StartTick));
        StateOf(stateToB).Should().Be((bEnterHandle, state.StateHandle, (uint)Poison, (ushort)2,
            state.EndTick, state.StartTick));
    }

    private static (uint Handle, ushort StateHandle, uint StateCode, ushort StateLevel, uint EndTick,
        uint StartTick) StateOf(byte[] frame) => (
            BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(7, 4)),
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(11, 2)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(13, 4)),
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(17, 2)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(19, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(23, 4)));

    private static long OnlyMonster(MonsterWorldState world) =>
        world.WithinRange(0f, 0f, WorldVisibility.ViewRange).Single().InstanceId;

    private static (MonsterWorldState World, GameClient Client) Scene(uint handle, float x, float y) =>
        (World(), Client(handle, x, y));

    private static GameClient Client(uint handle, float x, float y)
    {
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = handle;
        info.CharacterLevel = 10;
        info.X = x;
        info.Y = y;
        return client;
    }

    private static MonsterWorldState World()
    {
        var repository = A.Fake<IMonsterResourceRepository>();
        A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._))
            .Returns(new[] { new MonsterResourceEntity { Id = 2101, Level = 1, Hp = 80 } });
        return new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions
        {
            Spawns = { new MonsterSpawnPoint { MonsterId = 2101, X = 0, Y = 0, Count = 1, Radius = 0 } }
        }));
    }

    private static List<byte[]> SentOf(GameClient client) =>
        ((StorageTestHarness.FrameConnection)client.Connection).Sent;

    private static ushort Id(byte[] frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2));
}
