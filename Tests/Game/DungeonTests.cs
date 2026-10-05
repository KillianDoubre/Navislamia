using System.Text.Json;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Dungeons;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Party;
using Navislamia.Game.Services.Props;

namespace Tests.Game;

[TestFixture]
public partial class DungeonTests
{
    private DungeonOptions _options;
    private DungeonCatalog _catalog;
    private DungeonRooms _rooms;
    private DungeonService _service;
    private IWarpService _warp;
    private IPartyService _party;
    private IDungeonGuildRepository _guilds;
    private ICharacterService _characters;
    private Clock _clock;

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero); // Monday, open raid hours.
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [SetUp]
    public void Setup()
    {
        _options = new DungeonOptions { TimeZone = "UTC" };
        _catalog = new DungeonCatalog(Options.Create(_options));
        _rooms = new DungeonRooms();
        _warp = A.Fake<IWarpService>();
        A.CallTo(() => _warp.Warp(A<GameClient>._, A<float>._, A<float>._, A<byte>._)).Invokes(
            (GameClient c, float x, float y, byte layer) =>
            {
                StorageTestHarness.Session(c).Layer = _rooms.OnWarp(c, x, y, layer);
                StorageTestHarness.Session(c).X = x; StorageTestHarness.Session(c).Y = y;
                StorageTestHarness.Session(c).ClearNpcDialog();
            });
        _party = A.Fake<IPartyService>();
        A.CallTo(() => _party.DungeonParty(A<GameClient>._)).Returns(null);
        _guilds = A.Fake<IDungeonGuildRepository>();
        A.CallTo(() => _guilds.GetAsync(A<int>._)).Returns(new DungeonGuildState(null, null));
        _characters = A.Fake<ICharacterService>();
        _clock = new Clock();
        _service = new DungeonService(_catalog, _rooms, _warp, _party, _guilds, _characters,
            Options.Create(_options), time: _clock);
    }

    private static GameClient Player(uint id = 1, int level = 170, long? guild = null)
    {
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = id; info.CharacterName = "Dungeon" + id; info.CharacterHp = 100;
        info.CharacterLevel = level; info.GuildId = guild; info.X = 120000; info.Y = 100000;
        return client;
    }

    private void Group(long id, GameClient leader, params GameClient[] members)
    {
        var all = new[] { leader }.Concat(members).ToArray();
        var snapshot = new DungeonParty(id, StorageTestHarness.Session(leader).CharacterHandle,
            all.Select(c => StorageTestHarness.Session(c).CharacterHandle).ToArray(), all);
        foreach (var client in all)
            A.CallTo(() => _party.DungeonParty(client)).Returns(snapshot);
    }

    [Test]
    public void Imported_resources_preserve_regions_levels_and_all_difficulties()
    {
        _catalog.Dungeons.Should().HaveCount(21);
        _catalog.Instances.Should().HaveCount(9);
        _catalog.Types.Should().HaveCount(37);
        // 1 654 rows, of which 894 name a monster the 7.3 client does not know (filtre-ressources-73.md).
        _catalog.Respawns.Should().HaveCount(760);
        _catalog.Secrets.Should().HaveCount(6);
        _catalog.NpcDungeons.Should().HaveCount(12);
        _catalog.Dungeons[130000].LocalFlag.Should().Be(1);
        _catalog.Dungeons[70101].LocalFlag.Should().Be(1048575);
        new DungeonCatalog(Options.Create(new DungeonOptions { LocalFlag = 16 })).Dungeons[130000].LocalFlag.Should().Be(16);
    }

    [TestCase("2026-09-28T00:00:00Z", 0, 345600, true)]
    [TestCase("2026-10-02T00:00:00Z", 0, 345600, true)]
    [TestCase("2026-10-02T00:00:01Z", 0, 345600, false)]
    [TestCase("2026-10-04T23:59:59Z", 0, 345600, false)]
    [TestCase("2026-10-04T23:59:59Z", 600000, 3600, true)]
    [TestCase("2026-09-28T00:30:00Z", 600000, 3600, true)]
    [TestCase("2026-09-29T00:30:00Z", 600000, 3600, false)]
    public void Weekly_schedule_uses_Monday_and_inclusive_bounds(string instant, int open, int close, bool expected)
        => DungeonRules.IsOpen(DateTimeOffset.Parse(instant), TimeZoneInfo.Utc, open, close).Should().Be(expected);

    [Test]
    public void Schedule_converts_to_configured_time_zone()
    {
        var paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
        DungeonRules.IsOpen(DateTimeOffset.Parse("2026-09-27T22:00:00Z"), paris, 0, 0).Should().BeTrue();
        DungeonRules.IsOpen(DateTimeOffset.Parse("2026-09-27T21:59:59Z"), paris, 0, 0).Should().BeFalse();
    }

    [Test]
    public async Task Public_dungeon_remains_accessible_outside_guild_schedule()
    {
        _clock.Now = DateTimeOffset.Parse("2026-10-03T12:00:00Z");
        var player = Player(level: 30);
        (await _service.ExecuteAsync(player, PropScript.Parse("enter_dungeon(130000)"))).Should().Be(ResultCode.Success);
        StorageTestHarness.Session(player).Layer.Should().Be(0);
        StorageTestHarness.Session(player).X.Should().Be(_catalog.Dungeons[130000].X);
    }

    [TestCase(29, ResultCode.AccessDenied)]
    [TestCase(30, ResultCode.Success)]
    [TestCase(300, ResultCode.Success)]
    public async Task Dungeon_minimum_level_is_resource_level_minus_forty(int level, ResultCode expected)
        => (await _service.ExecuteAsync(Player(level: level), PropScript.Parse("enter_dungeon(130000)"))).Should().Be(expected);

    [Test]
    public async Task Closed_and_unknown_dungeons_never_warp()
    {
        _options.ClosedDungeons.Add(130000);
        (await _service.ExecuteAsync(Player(), PropScript.Parse("enter_dungeon(130000)"))).Should().Be(ResultCode.AccessDenied);
        (await _service.ExecuteAsync(Player(), PropScript.Parse("enter_dungeon(123456)"))).Should().Be(ResultCode.NotExist);
        A.CallTo(() => _warp.Warp(A<GameClient>._, A<float>._, A<float>._, A<byte>._)).MustNotHaveHappened();
    }

    [TestCase(149, 0, ResultCode.AccessDenied)]
    [TestCase(150, 0, ResultCode.Success)]
    [TestCase(159, 2, ResultCode.AccessDenied)]
    [TestCase(160, 2, ResultCode.Success)]
    [TestCase(300, 0, ResultCode.Success)]
    [TestCase(301, 0, ResultCode.AccessDenied)]
    [TestCase(170, 4, ResultCode.AccessDenied)]
    public async Task Instance_difficulty_checks_lower_inclusive_upper_exclusive(int level, int type, ResultCode expected)
        => (await _service.ExecuteAsync(Player(level: level), PropScript.Parse($"warp_to_instance_dungeon(40000,{type})"))).Should().Be(expected);

    [Test]
    public async Task Members_share_a_room_and_other_groups_get_another_layer()
    {
        var leader = Player(1); var member = Player(2); var other = Player(3);
        Group(10, leader, member); Group(11, other);
        var action = PropScript.Parse("warp_to_instance_dungeon(40000,0)");
        (await _service.ExecuteAsync(leader, action)).Should().Be(ResultCode.Success);
        (await _service.ExecuteAsync(member, action)).Should().Be(ResultCode.Success);
        (await _service.ExecuteAsync(other, action)).Should().Be(ResultCode.Success);
        StorageTestHarness.Session(member).Layer.Should().Be(StorageTestHarness.Session(leader).Layer).And.NotBe(0);
        StorageTestHarness.Session(other).Layer.Should().NotBe(StorageTestHarness.Session(leader).Layer);
    }

    [Test]
    public async Task Solo_instances_are_private_and_cannot_be_nested()
    {
        var one = Player(1); var two = Player(2);
        var action = PropScript.Parse("warp_to_instance_dungeon(40000,0)");
        (await _service.ExecuteAsync(one, action)).Should().Be(ResultCode.Success);
        (await _service.ExecuteAsync(two, action)).Should().Be(ResultCode.Success);
        StorageTestHarness.Session(one).Layer.Should().NotBe(StorageTestHarness.Session(two).Layer);
        (await _service.ExecuteAsync(one, PropScript.Parse("warp_to_instance_dungeon(41001,0)"))).Should().Be(ResultCode.NotActable);
    }

    [Test]
    public async Task Another_member_cannot_change_the_running_difficulty()
    {
        var leader = Player(1); var member = Player(2);
        Group(10, leader, member);
        await _service.ExecuteAsync(leader, PropScript.Parse("warp_to_instance_dungeon(40000,0)"));
        (await _service.ExecuteAsync(member, PropScript.Parse("warp_to_instance_dungeon(40000,1)"))).Should().Be(ResultCode.NotActable);
        StorageTestHarness.Session(member).Layer.Should().Be(0);
    }

    [Test]
    public async Task Leaving_restores_the_entry_point_and_last_member_destroys_room()
    {
        var player = Player();
        var key = new DungeonRoomKey(DungeonRoomKind.Instance, 40000, -1);
        await _service.ExecuteAsync(player, PropScript.Parse("warp_to_instance_dungeon(40000,0)"));
        _rooms.Find(key).Should().NotBeNull();
        (await _service.ExecuteAsync(player, PropScript.Parse("exit_instance_dungeon()"))).Should().Be(ResultCode.Success);
        StorageTestHarness.Session(player).Layer.Should().Be(0);
        StorageTestHarness.Session(player).X.Should().Be(120000);
        _rooms.Find(key).Should().BeNull();
    }

    [Test]
    public async Task Logout_restores_public_position_before_it_is_saved()
    {
        var player = Player();
        await _service.ExecuteAsync(player, PropScript.Parse("warp_to_instance_dungeon(40000,0)"));
        _rooms.OnWorldExit(player);
        StorageTestHarness.Session(player).Layer.Should().Be(0);
        StorageTestHarness.Session(player).X.Should().Be(120000);
        _rooms.Visits().Should().BeEmpty();
    }

    [Test]
    public async Task Leaving_party_ejects_member_but_preserves_other_members_room()
    {
        var leader = Player(1); var member = Player(2);
        Group(10, leader, member);
        var action = PropScript.Parse("warp_to_instance_dungeon(40000,0)");
        await _service.ExecuteAsync(leader, action); await _service.ExecuteAsync(member, action);
        A.CallTo(() => _party.DungeonParty(member)).Returns(null);
        await _service.SweepAsync();
        StorageTestHarness.Session(member).Layer.Should().Be(0);
        StorageTestHarness.Session(leader).Layer.Should().NotBe(0);
        _rooms.Visits().Should().ContainSingle();
    }

    [Test]
    public async Task Exit_dungeon_uses_outside_coordinates()
    {
        var player = Player();
        await _service.ExecuteAsync(player, PropScript.Parse("enter_dungeon(130000)"));
        await _service.ExecuteAsync(player, PropScript.Parse("exit_dungeon(130000)"));
        StorageTestHarness.Session(player).X.Should().Be(155817);
        StorageTestHarness.Session(player).Y.Should().Be(103724);
    }

    [Test]
    public async Task Raid_requires_guild_party_leader_and_current_schedule()
    {
        var leader = Player(1, guild: 7); var member = Player(2, guild: 7);
        var action = PropScript.Parse("begin_dungeon_raid(130000)");
        (await _service.ExecuteAsync(leader, action)).Should().Be(ResultCode.AccessDenied);
        Group(10, leader, member);
        (await _service.ExecuteAsync(member, action)).Should().Be(ResultCode.AccessDenied);
        StorageTestHarness.Session(member).GuildId = 8;
        (await _service.ExecuteAsync(leader, action)).Should().Be(ResultCode.AccessDenied);
        StorageTestHarness.Session(member).GuildId = 7;
        _clock.Now = DateTimeOffset.Parse("2026-10-03T12:00:00Z");
        (await _service.ExecuteAsync(leader, action)).Should().Be(ResultCode.NotActable);
    }

    [Test]
    public async Task Owner_cannot_raid_own_dungeon_and_other_guild_cannot_join_private_raid()
    {
        var leader = Player(1, guild: 7); Group(10, leader);
        A.CallTo(() => _guilds.GetAsync(130000)).Returns(new DungeonGuildState(7, null));
        (await _service.ExecuteAsync(leader, PropScript.Parse("begin_dungeon_raid(130000)"))).Should().Be(ResultCode.AccessDenied);
        A.CallTo(() => _guilds.GetAsync(130000)).Returns(new DungeonGuildState(8, null));
        (await _service.ExecuteAsync(leader, PropScript.Parse("begin_dungeon_raid(130000)"))).Should().Be(ResultCode.Success);
        var other = Player(2, guild: 9); Group(11, other);
        (await _service.ExecuteAsync(other, PropScript.Parse("enter_dungeon(130000)"))).Should().Be(ResultCode.Success);
        StorageTestHarness.Session(other).Layer.Should().Be(0);
        StorageTestHarness.Session(leader).Layer.Should().BeGreaterThan(1);
    }

    [Test]
    public async Task Raid_members_join_the_guild_layer_and_leave_when_hours_close()
    {
        var leader = Player(1, guild: 7); var member = Player(2, guild: 7);
        Group(10, leader, member);
        await _service.ExecuteAsync(leader, PropScript.Parse("begin_dungeon_raid(130000)"));
        await _service.ExecuteAsync(member, PropScript.Parse("enter_dungeon(130000)"));
        StorageTestHarness.Session(member).Layer.Should().Be(StorageTestHarness.Session(leader).Layer).And.BeGreaterThan(1);
        _clock.Now = DateTimeOffset.Parse("2026-10-03T00:00:00Z");
        await _service.SweepAsync();
        StorageTestHarness.Session(leader).Layer.Should().Be(0); StorageTestHarness.Session(member).Layer.Should().Be(0);
        _rooms.Visits().Should().BeEmpty();
    }

    [Test]
    public async Task Siege_accepts_only_owner_and_challenger_at_their_distinct_positions()
    {
        var dungeon = _catalog.Dungeons[130000];
        _clock.Now = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero).AddSeconds(dungeon.SiegeOpen);
        A.CallTo(() => _guilds.GetAsync(130000)).Returns(new DungeonGuildState(7, 8));
        var owner = Player(1, guild: 7); var challenger = Player(2, guild: 8); var other = Player(3, guild: 9);
        Group(10, owner); Group(11, challenger); Group(12, other);
        var action = PropScript.Parse("warp_to_siege_dungeon(130000)");
        (await _service.ExecuteAsync(owner, action)).Should().Be(ResultCode.Success);
        (await _service.ExecuteAsync(challenger, action)).Should().Be(ResultCode.Success);
        (await _service.ExecuteAsync(other, action)).Should().Be(ResultCode.AccessDenied);
        StorageTestHarness.Session(owner).Layer.Should().Be(1); StorageTestHarness.Session(challenger).Layer.Should().Be(1);
        StorageTestHarness.Session(owner).X.Should().Be(dungeon.DefenceX);
        StorageTestHarness.Session(challenger).X.Should().Be(dungeon.SiegeX);
    }

    [Test]
    public async Task Secret_portal_is_public_but_owned_shortcut_requires_real_guild_ownership()
    {
        var player = Player();
        (await _service.ExecuteAsync(player, PropScript.Parse("enter_to_secret_dungeon(70191)"))).Should().Be(ResultCode.Success);
        StorageTestHarness.Session(player).Layer.Should().Be(0);
        StorageTestHarness.Session(player).X.Should().Be(_catalog.Dungeons[70101].X);
        var shortcut = PropScript.Parse("scf_teleport_to_owned_secret_dungeon()");
        (await _service.ExecuteAsync(player, shortcut)).Should().Be(ResultCode.AccessDenied);
        StorageTestHarness.Session(player).GuildId = 7;
        A.CallTo(() => _guilds.OwnedDungeonAsync(7)).Returns(130300);
        (await _service.ExecuteAsync(player, shortcut)).Should().Be(ResultCode.Success);
        _options.ClosedDungeons.Add(70101);
        (await _service.ExecuteAsync(player, shortcut)).Should().Be(ResultCode.AccessDenied);
    }

    [Test]
    public async Task All_twenty_imported_instance_choices_dispatch_to_their_resources()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "../../../../DevConsole/npc-dialogs.73.json")));
        var options = json.RootElement.GetProperty("NpcDialogCatalog").Deserialize<NpcDialogOptions>()!;
        var menu = options.Dialogs["NPC_Teleport_instanceDuneGeon_contact"].Menu;
        var entries = menu.Where(m => m.Trigger.StartsWith("warp_to_instance_dungeon")).ToArray();
        entries.Should().HaveCount(20);
        foreach (var entry in entries)
        {
            var player = Player();
            var action = PropScript.Parse(entry.Trigger);
            (await _service.ExecuteAsync(player, action)).Should().Be(ResultCode.Success, entry.Trigger);
            StorageTestHarness.Session(player).X.Should().Be(_catalog.Instances[action.DungeonId].X);
            _rooms.OnWorldExit(player);
        }
    }

    [Test]
    public async Task Vulcanus_is_solo_and_requires_twenty_keys_before_room_creation()
    {
        var player = Player(level: 35);
        var action = PropScript.Parse("enter_vulcanus()");
        Group(10, player);
        (await _service.ExecuteAsync(player, action)).Should().Be(ResultCode.NotActable);
        A.CallTo(() => _party.DungeonParty(player)).Returns(null);
        A.CallTo(() => _characters.GetCarriedItemsAsync(StorageTestHarness.Session(player).CharacterName)).Returns(new[]
        {
            new ItemEntity { Id = 20, ItemResourceId = 1000401, Amount = 19, WearInfo = ItemWearType.None }
        });
        (await _service.ExecuteAsync(player, action)).Should().Be(ResultCode.AccessDenied);
        _rooms.Visits().Should().BeEmpty();
        A.CallTo(() => _characters.ApplyCraftAsync(A<string>._, A<IReadOnlyList<CraftConsumption>>._, null)).MustNotHaveHappened();
    }

    [Test]
    public async Task Vulcanus_charges_split_stacks_atomically_once()
    {
        var player = Player(level: 35);
        A.CallTo(() => _characters.GetCarriedItemsAsync(StorageTestHarness.Session(player).CharacterName)).Returns(new[]
        {
            new ItemEntity { Id = 20, ItemResourceId = 1000401, Amount = 12, WearInfo = ItemWearType.None },
            new ItemEntity { Id = 21, ItemResourceId = 1000401, Amount = 10, WearInfo = ItemWearType.None }
        });
        A.CallTo(() => _characters.ApplyCraftAsync(A<string>._, A<IReadOnlyList<CraftConsumption>>._, null))
            .Returns(new CraftCommitResult(CraftCommitOutcome.Success, new[] { (20u, 0L), (21u, 2L) }, null));
        (await _service.ExecuteAsync(player, PropScript.Parse("enter_vulcanus()"))).Should().Be(ResultCode.Success);
        A.CallTo(() => _characters.ApplyCraftAsync(StorageTestHarness.Session(player).CharacterName,
            A<IReadOnlyList<CraftConsumption>>.That.Matches(c => c.Sum(i => i.Count) == 20 && c.Count == 2), null)).MustHaveHappenedOnceExactly();
        (await _service.ExecuteAsync(player, PropScript.Parse("enter_vulcanus()"))).Should().Be(ResultCode.NotActable);
    }

    [Test]
    public async Task Stale_dialog_cannot_finish_warp_after_database_reply()
    {
        var player = Player(guild: 7);
        var pending = new TaskCompletionSource<int>();
        A.CallTo(() => _guilds.OwnedDungeonAsync(7)).Returns(pending.Task);
        var enter = _service.ExecuteAsync(player, PropScript.Parse("scf_teleport_to_owned_secret_dungeon()"));
        StorageTestHarness.Session(player).ClearNpcDialog();
        pending.SetResult(130300);
        (await enter).Should().Be(ResultCode.NotActable);
        A.CallTo(() => _warp.Warp(A<GameClient>._, A<float>._, A<float>._, A<byte>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task Guild_changed_during_ownership_read_cannot_enter_previous_guilds_secret()
    {
        var player = Player(guild: 7);
        var pending = new TaskCompletionSource<int>();
        A.CallTo(() => _guilds.OwnedDungeonAsync(7)).Returns(pending.Task);
        var enter = _service.ExecuteAsync(player, PropScript.Parse("scf_teleport_to_owned_secret_dungeon()"));
        StorageTestHarness.Session(player).GuildId = 8;
        pending.SetResult(130300);
        (await enter).Should().Be(ResultCode.NotActable);
        A.CallTo(() => _warp.Warp(A<GameClient>._, A<float>._, A<float>._, A<byte>._)).MustNotHaveHappened();
    }

    [Test]
    public void Private_layer_pool_refuses_exhaustion_without_reusing_an_active_layer()
    {
        var layers = new HashSet<byte>();
        for (var i = 0; i < 126; i++)
        {
            var room = _rooms.Create(new DungeonRoomKey(DungeonRoomKind.Instance, 40000, i + 1), 0, 40000, 20000,
                Array.Empty<MonsterSpawnPoint>());
            layers.Add(room!.Layer).Should().BeTrue();
        }
        _rooms.Create(new DungeonRoomKey(DungeonRoomKind.Instance, 40000, 200), 0, 40000, 20000,
            Array.Empty<MonsterSpawnPoint>()).Should().BeNull();
    }

    [Test]
    public async Task Cancelling_paid_entry_during_commit_refunds_keys_and_destroys_reserved_room()
    {
        var player = Player(level: 35);
        var info = StorageTestHarness.Session(player);
        var name = info.CharacterName;
        A.CallTo(() => _characters.GetCarriedItemsAsync(name)).Returns(new[]
        {
            new ItemEntity { Id = 20, ItemResourceId = 1000401, Amount = 20, WearInfo = ItemWearType.None }
        });
        var pending = new TaskCompletionSource<CraftCommitResult>();
        A.CallTo(() => _characters.ApplyCraftAsync(name, A<IReadOnlyList<CraftConsumption>>._, null)).Returns(pending.Task);
        var enter = _service.ExecuteAsync(player, PropScript.Parse("enter_vulcanus()"));
        info.ClearNpcDialog();
        pending.SetResult(new CraftCommitResult(CraftCommitOutcome.Success, new[] { (20u, 0L) }, null));
        (await enter).Should().Be(ResultCode.NotActable);
        A.CallTo(() => _characters.AddItemAsync(name, 1000401, 20)).MustHaveHappenedOnceExactly();
        _rooms.Find(new DungeonRoomKey(DungeonRoomKind.Instance, 20000, -1)).Should().BeNull();
        _rooms.Visits().Should().BeEmpty();
        info.Layer.Should().Be(0);
    }
}

