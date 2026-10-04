using System.Text;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Dungeons;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Props;

namespace Tests.Game;

/// <summary>The Vulcanus scenario of ETC_dungeon_prop.lua (docs/packet-specs/socle-donjons-instances-secrets.md).</summary>
[TestFixture]
public class VulcanusTests
{
    [TestCase(20001, 1, 1)]
    [TestCase(20004, 1, 8)]
    [TestCase(20006, 2, 2)]
    [TestCase(20012, 3, 8)]
    [TestCase(20013, 4, 0)]
    [TestCase(30001, 0, 0)]
    public void A_respawn_group_is_a_room_of_a_floor(int group, int floor, int bit)
    {
        VulcanusRules.Floor(group).Should().Be(floor);
        VulcanusRules.RoomBit(group).Should().Be(bit);
    }

    [Test]
    public void The_keys_and_gates_are_the_luas()
    {
        VulcanusRules.Keys(1).Should().Be((1000401, 20));
        VulcanusRules.Keys(2).Should().Be((1000402, 10));
        VulcanusRules.Keys(3).Should().Be((1000403, 5));
        VulcanusRules.Keys(4).Should().Be((1000404, 1));
        VulcanusRules.Gate(1).Should().Be(126024);
        VulcanusRules.Gate(4).Should().Be(126027);
        VulcanusRules.GateZOffset(4).Should().Be(35);
        VulcanusRules.GateZOffset(2).Should().Be(10);
        VulcanusRules.RoomPosition(4, 1).Should().Be((206474, 27625));
    }

    [Test]
    public void A_trip_goes_to_a_room_not_yet_cleared()
    {
        var random = new Random(3);
        VulcanusRules.ChooseRoom(1, 7, random).Should().Be(4);
        VulcanusRules.ChooseRoom(2, 15, random).Should().Be(0, "a cleared floor has no room left");
        VulcanusRules.ChooseRoom(4, 0, random).Should().Be(1);
        VulcanusRules.ChooseRoom(-1, 0, random).Should().Be(0);
        Enumerable.Range(0, 50).Select(_ => VulcanusRules.ChooseRoom(3, 5, random)).Distinct()
            .Should().BeEquivalentTo(new[] { 2, 4 });
    }

    [Test]
    public void The_floor_window_offers_another_room_until_the_floor_is_cleared()
    {
        VulcanusRules.FloorWindow(20000, 1, 2, 3, 20, 10).Select(m => (m.Label, m.Trigger)).Should().Equal(
            ("20", "warp_indun(20000,1)"), ("10", "warp_indun(20000,2)"));
        VulcanusRules.FloorWindow(20000, 1, 2, 15, 20, 10)[0].Trigger.Should().Be("warp_indun(20000,-1)");
    }

    [Test]
    public void The_gate_scripts_parse()
    {
        PropScript.Parse("enter_other_indun(20000,1,2,20,10)").Should().Be(
            new PropAction(PropActionKind.EnterOtherInstanceDungeon, 1, 2, 20000, Type: 20, Cost: 10));
        PropScript.Parse("warp_indun(20000,2)").Should().Be(new PropAction(PropActionKind.WarpInstanceFloor, 2, 0, 20000));
        PropScript.Parse("exit_instance_dungeon(126027)").Kind.Should().Be(PropActionKind.ExitInstanceDungeon);
    }

    [Test]
    public void The_catalogue_carries_rooms_gates_and_rewards()
    {
        var catalog = new DungeonCatalog(Options.Create(new DungeonOptions { TimeZone = "UTC" }));

        catalog.VulcanusRewards.Should().HaveCount(56, "14 difficulties by 4 floors");
        catalog.VulcanusRewards.Single(r => r.Difficulty == 0 && r.Floor == 1).Should()
            .Be(new VulcanusReward(0, 1, 19956, 3991, 31200));
        catalog.Respawns.Where(r => r.DungeonId == 20000 && r.Type == 0).Select(r => r.Group).Distinct()
            .Should().BeEquivalentTo(Enumerable.Range(20001, 13));
        catalog.InstanceProps.Where(p => p.DungeonId == 20000 && p.Type == 0).Should().HaveCount(12)
            .And.OnlyContain(p => p.PropId == 126023);
        foreach (var gate in new[] { 126023, 126024, 126025, 126026, 126027 })
            catalog.PropTemplates.Should().ContainKey(gate);
        PropScript.Parse(catalog.PropTemplates[126026].Script).Should().Be(
            new PropAction(PropActionKind.EnterOtherInstanceDungeon, 3, 4, 20000, Type: 5, Cost: 1));
    }

    private sealed class Scene
    {
        public readonly MonsterWorldState World;
        public readonly DungeonRooms Rooms;
        public readonly DynamicFieldProps Props = new();
        public readonly VulcanusScenario Scenario;
        public readonly DungeonRoom Room;
        public readonly GameClient Player;

        public Scene(int type = 0)
        {
            var repository = A.Fake<IMonsterResourceRepository>();
            A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._)).Returns(
                new[] { new MonsterResourceEntity { Id = 9400002, Level = 30, Hp = 100, Race = 1 } });
            World = new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions()));
            Rooms = new DungeonRooms(World, null, Props);
            var catalog = new DungeonCatalog(Options.Create(new DungeonOptions { TimeZone = "UTC" }));
            Scenario = new VulcanusScenario(catalog, Rooms, World, Props, null, null, new Random(1));
            Room = Rooms.Create(new DungeonRoomKey(DungeonRoomKind.Instance, 20000, -1), type, 197417, 28580, new[]
            {
                new MonsterSpawnPoint { MonsterId = 9400002, Count = 1, X = 197200, Y = 28500, Group = 20001, RespawnSeconds = 0 },
                new MonsterSpawnPoint { MonsterId = 9400002, Count = 1, X = 197250, Y = 28520, Group = 20001, RespawnSeconds = 0 },
                new MonsterSpawnPoint { MonsterId = 9400002, Count = 1, X = 198900, Y = 28450, Group = 20002, RespawnSeconds = 0 }
            })!;
            Scenario.OnCreate(Room);
            Player = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
            var info = StorageTestHarness.Session(Player);
            info.CharacterHandle = 1; info.CharacterName = "Vul"; info.CharacterHp = 100; info.CharacterLevel = 35;
            Rooms.Join(Player, Room);
        }

        public long[] Monsters(int group) => Room.GroupOf.Where(g => g.Value == group).Select(g => g.Key).ToArray();

        public void Kill(long id)
        {
            World.TryKill(id, DateTime.UtcNow.AddHours(1));
            Scenario.OnMonsterKilled(id);
        }

        public List<byte[]> Sent => ((StorageTestHarness.FrameConnection)Player.Connection).Sent;
    }

    [Test]
    public void A_new_room_keeps_its_floor_flags_and_difficulty_and_poses_its_gates()
    {
        var scene = new Scene(type: 2);

        scene.Room.Flags.Should().Contain(new KeyValuePair<string, int>("Vul1", 0))
            .And.Contain(new KeyValuePair<string, int>("difficulty", 2));
        scene.Props.Within(197460, 28579, scene.Room.Layer, 5).Should().ContainSingle(p => p.PropId == 126023);
        scene.Room.GroupOf.Values.Should().BeEquivalentTo(new[] { 20001, 20001, 20002 });
    }

    [Test]
    public void The_last_monster_of_a_room_clears_it_once_rewards_and_poses_the_floor_gate()
    {
        var scene = new Scene();
        var info = StorageTestHarness.Session(scene.Player);
        var room = scene.Monsters(20001);

        scene.Kill(room[0]);
        info.CharacterExp.Should().Be(0, "a monster of the room still lives");

        scene.Kill(room[1]);
        (info.CharacterExp, info.CharacterJp, info.CharacterGold).Should().Be((19956L, 3991L, 31200L));
        scene.Room.Flags["Vul1"].Should().Be(1);
        var (x, y) = scene.World.GetPosition(room[1]);
        scene.Props.Within(x, y, scene.Room.Layer, 1).Should().ContainSingle(p => p.PropId == 126024);
        scene.Sent.Should().Contain(frame => Encoding.ASCII.GetString(frame).Contains("@9813"));

        scene.Scenario.OnMonsterKilled(room[1]);
        info.CharacterExp.Should().Be(19956, "a room clears once");
    }

    [Test]
    public async Task A_trip_takes_the_keys_and_warps_on_the_instance_layer()
    {
        var scene = new Scene();
        scene.Room.Flags["Vul1"] = 7;
        var warp = A.Fake<IWarpService>();
        (int, int)? asked = null;

        var result = await scene.Scenario.WarpFloorAsync(scene.Player, 1,
            (item, count) => { asked = (item, count); return Task.FromResult(true); }, warp);

        result.Should().Be(ResultCode.Success);
        asked.Should().Be((1000401, 20));
        A.CallTo(() => warp.Warp(scene.Player, 202065, 28458, scene.Room.Layer)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task A_trip_without_the_keys_says_so_and_stays()
    {
        var scene = new Scene();
        var warp = A.Fake<IWarpService>();

        var result = await scene.Scenario.WarpFloorAsync(scene.Player, 2, (_, _) => Task.FromResult(false), warp);

        result.Should().Be(ResultCode.AccessDenied);
        scene.Sent.Should().Contain(frame => Encoding.ASCII.GetString(frame).Contains("@9810"));
        A.CallTo(() => warp.Warp(A<GameClient>._, A<float>._, A<float>._, A<byte>._)).MustNotHaveHappened();
    }

    [Test]
    public void The_floor_gate_opens_its_window_and_advertises_its_two_trips()
    {
        var scene = new Scene();

        scene.Scenario.ShowFloorWindow(scene.Player, 0x40000005,
            new PropAction(PropActionKind.EnterOtherInstanceDungeon, 1, 2, 20000, Type: 20, Cost: 10));

        var info = StorageTestHarness.Session(scene.Player);
        info.NpcDialogHandle.Should().Be(0x40000005u);
        info.NpcDialogTriggers.Should().BeEquivalentTo(new[] { "warp_indun(20000,1)", "warp_indun(20000,2)" });
    }
}
