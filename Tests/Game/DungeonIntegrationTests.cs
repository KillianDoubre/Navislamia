using System.Buffers.Binary;
using System.Text;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Dungeons;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Pets;
using Navislamia.Game.Services.Props;

namespace Tests.Game;

[TestFixture]
public class DungeonIntegrationTests
{
    private static GameClient Player(uint id = 1)
    {
        var player = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(player);
        info.CharacterHandle = id; info.CharacterName = "Dungeon" + id; info.CharacterHp = 100;
        info.CharacterLevel = 170; info.X = 100000; info.Y = 120000;
        return player;
    }

    private static byte[] Contact(uint handle)
    {
        var packet = new byte[11];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7), handle);
        return packet;
    }

    private static byte[] Select(string trigger)
    {
        var packet = new byte[9 + trigger.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(7), (ushort)trigger.Length);
        Encoding.ASCII.GetBytes(trigger).CopyTo(packet, 9);
        return packet;
    }

    [Test]
    public void Instance_NPC_routes_advertised_actions_and_rejects_forged_ones()
    {
        var options = new NpcDialogOptions();
        options.Npcs[777] = "instance_contact()";
        options.Dialogs["instance_contact"] = new NpcDialogDefinition
        {
            Title = "Instance", Text = "Enter", Menu = { new NpcDialogMenuEntry
                { Label = "Enter", Trigger = "warp_to_instance_dungeon(40000,0)" } }
        };
        var service = A.Fake<IDungeonService>();
        var dialogs = new NpcDialogService(Options.Create(options), A.Fake<IWarpService>(),
            A.Fake<IStorageService>(), A.Fake<IMarketService>(), dungeons: service);
        var player = Player();
        var info = StorageTestHarness.Session(player);
        info.SpawnedNpcIdsByHandle[50] = 777;
        dialogs.Contact(player, Contact(50));
        dialogs.Select(player, Select("warp_to_instance_dungeon(40000,3)"));
        A.CallTo(() => service.ExecuteAsync(A<GameClient>._, A<PropAction>._)).MustNotHaveHappened();
        dialogs.Select(player, Select("warp_to_instance_dungeon(40000,0)"));
        // SCRIPT_WarpToInstanceDungeon warps at once: only enter_instance_dungeon opens the native window.
        A.CallTo(() => service.ExecuteAsync(player, PropScript.Parse("warp_to_instance_dungeon(40000,0)")))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => service.ConfirmAsync(A<GameClient>._, A<PropAction>._)).MustNotHaveHappened();
    }

    [TestCase("enter_instance_dungeon(40000)", true)]
    [TestCase("enter_secret_dungeon(70101)", true)]
    [TestCase("leave_instance_dungeon(40000)", true)]
    [TestCase("enter_dungeon(130300)", true)]
    [TestCase("warp_to_instance_dungeon(40000,0)", false)]
    [TestCase("warp_to_secret_dungeon(70101)", false)]
    [TestCase("scf_teleport_to_owned_secret_dungeon()", false)]
    public void Only_the_official_window_entries_confirm_a_menu_choice(string trigger, bool confirms) =>
        NpcDialogService.OpensConfirmation(trigger, PropScript.Parse(trigger)).Should().Be(confirms);


    private static MonsterWorldState World()
    {
        var repository = A.Fake<IMonsterResourceRepository>();
        A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._)).Returns(
            new[] { new MonsterResourceEntity { Id = 2101, Level = 5, Hp = 100, Race = 1 } });
        return new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions
        {
            Spawns = { new MonsterSpawnPoint { MonsterId = 2101, Count = 1, X = 40000, Y = 20000 } }
        }));
    }

    [Test]
    public void Room_monsters_and_reinforcements_do_not_survive_layer_reuse()
    {
        var world = World();
        var items = A.Fake<IGroundItemService>();
        var rooms = new DungeonRooms(world, items);
        var key = new DungeonRoomKey(DungeonRoomKind.Instance, 40000, 10);
        var room = rooms.Create(key, 0, 40000, 20000, new[]
        {
            new MonsterSpawnPoint { MonsterId = 2101, Count = 2, X = 40000, Y = 20000, RespawnSeconds = 10 }
        });
        var player = Player();
        rooms.Join(player, room!);
        var reinforcement = world.RespawnNearMonster(room!.Monsters[0], 2101, 1).Single().Instance;
        reinforcement.Layer.Should().Be(room.Layer);
        world.WithinRange(40000, 20000, 1000).Count(m => m.Layer == room.Layer).Should().Be(3);
        rooms.OnWorldExit(player);
        rooms.Find(key).Should().BeNull();
        world.WithinRange(40000, 20000, 1000).Should().ContainSingle().Which.Layer.Should().Be(0);
        world.TryGetInstance(reinforcement.InstanceId, out _).Should().BeFalse();
        A.CallTo(() => items.RemoveDungeonItems(room.Layer, room.CellX, room.CellY)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public void Instance_respawn_retains_layer_and_resets_damage_and_states()
    {
        var world = World();
        var id = world.SpawnDungeonMonsters(new[]
        {
            new MonsterSpawnPoint { MonsterId = 2101, Count = 1, X = 40000, Y = 20000, Layer = 2, RespawnSeconds = 10 }
        }).Single();
        world.AddState(id, 5, 1, 1, 0, uint.MaxValue);
        world.ApplyDamage(id, 200);
        world.TryKill(id, DateTime.UtcNow.AddHours(1)).Should().BeTrue();
        world.CollectRespawns(DateTime.UtcNow.AddSeconds(11)).Should().Contain(id);
        world.TryGetInstance(id, out var monster).Should().BeTrue();
        monster.Layer.Should().Be(2);
        world.GetHp(id).Should().Be(monster.Hp);
        world.GetStates(id).Should().BeEmpty();
    }

    [Test]
    public void One_time_instance_monster_is_removed_after_death()
    {
        var world = World();
        var id = world.SpawnDungeonMonsters(new[]
        {
            new MonsterSpawnPoint { MonsterId = 2101, Count = 1, X = 40000, Y = 20000, Layer = 2, RespawnSeconds = 0 }
        }).Single();
        world.TryKill(id, DateTime.UtcNow.AddSeconds(1));
        world.CollectRespawns(DateTime.UtcNow.AddSeconds(2));
        world.TryGetInstance(id, out _).Should().BeFalse();
    }

    [Test]
    public void Warp_removes_old_visibility_before_changing_layer_and_escape_releases_room()
    {
        var player = Player();
        var info = StorageTestHarness.Session(player);
        var players = A.Fake<IPlayerVisibilityService>();
        var layersAtDeparture = new List<byte>();
        A.CallTo(() => players.LeaveWorld(player, true)).Invokes(() => layersAtDeparture.Add(info.Layer));
        var rooms = new DungeonRooms();
        var warp = new WarpService(A.Fake<INpcSpawnService>(), A.Fake<IMonsterSpawnService>(),
            A.Fake<IFieldPropService>(), A.Fake<ICombatService>(), A.Fake<IPetSummonService>(), players,
            A.Fake<IGroundItemService>(), dungeons: rooms);
        var key = new DungeonRoomKey(DungeonRoomKind.Instance, 40000, -1);
        var room = rooms.Create(key, 0, 40000, 20000, Array.Empty<MonsterSpawnPoint>());
        rooms.Join(player, room!);
        warp.Warp(player, 40000, 20000, room!.Layer);
        layersAtDeparture.Should().Equal(0);
        info.Layer.Should().Be(room.Layer);
        warp.Warp(player, 100000, 120000);
        layersAtDeparture.Should().Equal(0, room.Layer);
        info.Layer.Should().Be(0);
        rooms.Find(key).Should().BeNull();
    }

    [TestCase("warp_to_instance_dungeon(40000,0); other()")]
    [TestCase("warp_to_instance_dungeon(40000,999999999999999999)")]
    [TestCase("warp_to_instance_dungeon(40000,foo)")]
    [TestCase("enter_to_secret_dungeon(12345)")]
    public void Malformed_and_unknown_dungeon_scripts_are_refused(string script)
        => PropScript.Parse(script).Should().Be(PropAction.None);
}
