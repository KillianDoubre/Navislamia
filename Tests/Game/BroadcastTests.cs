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
using Navislamia.Game.Services.Pets;
using Navislamia.Game.Services.Rates;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

/// <summary>
/// What the other players see (docs/packet-specs/socle-diffusion-combat.md and
/// socle-diffusion-compagnons.md): fights under each observer's own monster handle, and companions that
/// come and go with their master.
/// </summary>
[TestFixture]
public class BroadcastTests
{
    private const uint HandleA = 0x10000001;
    private const uint HandleB = 0x10000002;
    private const uint HandleC = 0x10000003;
    private const uint PetHandle = 0x7000_0001;
    private const uint SummonHandle = 0x7000_0002;

    [Test]
    public void A_fight_frame_is_rebuilt_with_each_observers_handle_for_the_monster()
    {
        var visibility = NewService();
        var a = NewPlayer(HandleA, 0f, 0f);
        var b = NewPlayer(HandleB, 100f, 0f);
        var c = NewPlayer(HandleC, 0f, 100f);
        visibility.EnterWorld(a);
        visibility.EnterWorld(b);
        visibility.EnterWorld(c);
        Spawn(b, 7, 0x40000099);
        Clear(a, b, c);

        ObserverFrames.SendMonsterFrame(visibility, a, 7, (_, handle) => BitConverter.GetBytes(handle));

        SentOf(a).Should().BeEmpty("the subject's own frame is the caller's");
        SentOf(b).Should().ContainSingle().Which.Should().Equal(BitConverter.GetBytes(0x40000099u));
        SentOf(c).Should().BeEmpty("c does not see the monster");
    }

    [Test]
    public void A_kill_drops_the_monster_on_the_watchers_screens_too()
    {
        var visibility = NewService();
        var killer = NewPlayer(HandleA, 0f, 0f);
        var watcher = NewPlayer(HandleB, 100f, 0f);
        visibility.EnterWorld(killer);
        visibility.EnterWorld(watcher);
        Spawn(killer, 0, 0x40000001);
        Spawn(watcher, 0, 0x40000055);
        Clear(killer, watcher);

        var combat = new CombatService(World(), A.Fake<IMonsterSpawnService>(), A.Fake<ILevelingService>(),
            A.Fake<IGroundItemService>(), A.Fake<IRateService>(), A.Fake<IStatService>(), A.Fake<IStateCatalog>(),
            A.Fake<Navislamia.Game.Services.Party.IPartyService>(), players: visibility);

        combat.ApplyDamage(killer, 0, 0x40000001, 100_000).Should().Be(0);

        SentOf(watcher).Where(frame => Id(frame) == (ushort)GamePackets.TM_SC_STATUS_CHANGE)
            .Select(frame => BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(7, 4)))
            .Should().ContainSingle().Which.Should().Be(0x40000055u);
    }

    [Test]
    public void Companions_come_into_view_and_leave_it_with_their_master()
    {
        var visibility = NewService();
        var a = NewPlayer(HandleA, 0f, 0f);
        var b = NewPlayer(HandleB, 100f, 0f);
        var infoA = StorageTestHarness.Session(a);
        infoA.ActivePet = new ActivePet(PetHandle, 55, PetEntry());
        infoA.Summons = new[] { new SummonPresence(SummonHandle, SummonEntry(), 5f, 5f, 0) };

        visibility.EnterWorld(a);
        visibility.EnterWorld(b);

        var shown = SentOf(b).Where(frame => Id(frame) == (ushort)GamePackets.TM_SC_ENTER)
            .Select(frame => (Handle: BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8, 4)), frame.Length))
            .ToList();
        shown.Should().Equal((HandleA, 118), (PetHandle, 95), (SummonHandle, 96));

        Clear(a, b);
        StorageTestHarness.Session(b).X = 40000f;
        visibility.Sync(b);

        LeaveOrder(SentOf(b)).Should().Equal(PetHandle, SummonHandle, HandleA);
    }

    [Test]
    public void A_pet_called_and_dismissed_is_shown_to_the_observers_without_its_window()
    {
        var visibility = NewService();
        var a = NewPlayer(HandleA, 0f, 0f);
        var b = NewPlayer(HandleB, 100f, 0f);
        visibility.EnterWorld(a);
        visibility.EnterWorld(b);
        Clear(a, b);
        var world = new PetWorldService(visibility);
        var infoA = StorageTestHarness.Session(a);

        var handle = world.Enter(infoA, "a", a.Connection, PetEntry(), a);

        SentOf(a).Select(Id).Should().Equal((ushort)GamePackets.TM_SC_ENTER, (ushort)GamePackets.TM_SC_ADD_PET_INFO);
        SentOf(b).Select(Id).Should().Equal((ushort)GamePackets.TM_SC_ENTER);

        Clear(a, b);
        world.Leave(infoA, "a", a.Connection, handle, a);
        LeaveOrder(SentOf(b)).Should().Equal(handle);
    }

    [Test]
    public void A_summon_is_recorded_on_its_master_and_shown_to_the_observers()
    {
        var visibility = NewService();
        var a = NewPlayer(HandleA, 0f, 0f);
        var b = NewPlayer(HandleB, 100f, 0f);
        visibility.EnterWorld(a);
        visibility.EnterWorld(b);
        Clear(a, b);
        var world = new SummonWorldService(visibility);
        var infoA = StorageTestHarness.Session(a);

        var handle = world.Enter(infoA, "a", a.Connection, SummonEntry(), a);

        infoA.Summons.Should().ContainSingle().Which.Handle.Should().Be(handle);
        SentOf(b).Select(Id).Should().Equal((ushort)GamePackets.TM_SC_ENTER);

        Clear(a, b);
        world.Leave(infoA, "a", a.Connection, handle, a);
        infoA.Summons.Should().BeEmpty();
        LeaveOrder(SentOf(b)).Should().Equal(handle);
    }

    private static PetWorldEntry PetEntry() => new()
    {
        CageHandle = 55, PetCode = 1, Name = "Pet", Level = 1, Hp = 100, MaxHp = 100, X = 1f, Y = 1f
    };

    private static SummonWorldEntry SummonEntry() => new()
    {
        CardHandle = 66, Code = 1001, Name = "Summon", Level = 1, Hp = 100, MaxHp = 100
    };

    private static void Spawn(GameClient client, long instanceId, uint handle)
    {
        var info = StorageTestHarness.Session(client);
        lock (info.MonsterVisibilityLock)
        {
            info.SpawnedMonsters[instanceId] = handle;
        }
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

    private static PlayerVisibilityService NewService() =>
        new(A.Fake<Microsoft.Extensions.Logging.ILogger<PlayerVisibilityService>>());

    private static GameClient NewPlayer(uint handle, float x, float y)
    {
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = handle;
        info.CharacterName = $"player{handle}";
        info.CharacterHp = 1000;
        info.CharacterMaxHp = 1000;
        info.CharacterLevel = 10;
        info.X = x;
        info.Y = y;
        info.Appearance = new PlayerAppearance { Race = 2, Sex = 1 };
        return client;
    }

    private static List<byte[]> SentOf(GameClient client) =>
        ((StorageTestHarness.FrameConnection)client.Connection).Sent;

    private static void Clear(params GameClient[] clients)
    {
        foreach (var client in clients)
        {
            SentOf(client).Clear();
        }
    }

    private static ushort Id(byte[] frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2));

    private static List<uint> LeaveOrder(List<byte[]> frames) => frames
        .Where(frame => Id(frame) == (ushort)GamePackets.TM_SC_LEAVE)
        .Select(frame => BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(7, 4)))
        .ToList();
}
