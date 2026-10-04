using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;

namespace Tests.Game;

public partial class GmCommandServiceTests
{
    [TestCase("block_chat")]
    [TestCase("check_auto_user")]
    [TestCase("force_warp")]
    [TestCase("invisible")]
    [TestCase("kick")]
    [TestCase("rebirth")]
    [TestCase("lv")]
    public async Task Official_gm_commands_are_unknown_at_permission_99(string command)
    {
        var (client, connection) = NewClient(99);
        await _service.HandleAsync(client, "/" + command, Array.Empty<GameClient>());
        Replies(connection).Single().Text.Should().Be($"Unknown command: /{command}. Type /help.");
        Fake.GetCalls(_warp).Should().BeEmpty(); Fake.GetCalls(_combat).Should().BeEmpty();
        Fake.GetCalls(_characters).Should().BeEmpty(); Fake.GetCalls(_resurrection).Should().BeEmpty();
        Fake.GetCalls(_leveling).Should().BeEmpty();
    }

    [Test]
    public async Task Change_name_updates_session_only_after_persistence_and_broadcasts_packet_30()
    {
        var (client, connection) = NewClient(0);
        var info = StorageTestHarness.Session(client); info.CharacterList.Add("Tester");
        A.CallTo(() => _characters.RenameCharacterAsync("Tester", "Renamed")).Returns(ResultCode.Success);
        await _service.HandleAsync(client, "/change_name Renamed", Array.Empty<GameClient>());
        info.CharacterName.Should().Be("Renamed"); info.CharacterList.Should().Contain("Renamed").And.NotContain("Tester");
        connection.Sent.Should().ContainSingle().Which.Should().Equal(GamePetPackets.BuildChangeName(CharacterHandle, "Renamed"));
        A.CallTo(() => _parties.OnNameChanged(client)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task A_refused_name_change_keeps_session_and_emits_no_name_packet()
    {
        var (client, connection) = NewClient(0);
        A.CallTo(() => _characters.RenameCharacterAsync("Tester", "Renamed")).Returns(ResultCode.AccessDenied);
        await _service.HandleAsync(client, "/change_name Renamed", Array.Empty<GameClient>());
        StorageTestHarness.Session(client).CharacterName.Should().Be("Tester");
        connection.Sent.Should().NotContain(p => Id(p) == 30);
        A.CallTo(() => _parties.OnNameChanged(client)).MustNotHaveHappened();
    }

    [Test]
    public async Task Block_chat_persists_seconds_and_zero_unblocks()
    {
        var (client, connection) = NewClient(100); var info = StorageTestHarness.Session(client);
        A.CallTo(() => _characters.SaveChatBlockTimeAsync("Tester", A<int>._)).Returns(true);
        await _service.HandleAsync(client, "/block_chat Tester 2", Array.Empty<GameClient>());
        A.CallTo(() => _characters.SaveChatBlockTimeAsync("Tester", 120)).MustHaveHappenedOnceExactly();
        info.ChatBlockRemaining(ServerClock.Now).Should().BeInRange(119, 120);
        await _service.HandleAsync(client, "/block_chat Tester", Array.Empty<GameClient>());
        Replies(connection).Last().Text.Should().StartWith("Chat block:").And.EndWith("seconds.");
        await _service.HandleAsync(client, "/block_chat Tester 0", Array.Empty<GameClient>());
        info.ChatBlockRemaining(ServerClock.Now).Should().Be(0); info.ChatBlockUntil.Should().Be(0);
    }

    [TestCase("-1")]
    [TestCase("144001")]
    [TestCase("nonsense")]
    public async Task Block_chat_rejects_out_of_bound_durations(string duration)
    {
        var (client, _) = NewClient(100);
        await _service.HandleAsync(client, "/block_chat Tester " + duration, Array.Empty<GameClient>());
        A.CallTo(() => _characters.SaveChatBlockTimeAsync(A<string>._, A<int>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task A_failed_chat_block_save_does_not_report_success_or_block_the_session()
    {
        var (client, connection) = NewClient(100);
        A.CallTo(() => _characters.SaveChatBlockTimeAsync("Tester", 60)).Returns(false);
        await _service.HandleAsync(client, "/block_chat Tester 1", Array.Empty<GameClient>());
        StorageTestHarness.Session(client).ChatBlockUntil.Should().Be(0);
        Replies(connection).Single().Text.Should().Be("Chat block save failed.");
    }

    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public async Task Check_auto_user_reads_the_trusted_session_flag(bool used, int expected)
    {
        var (client, connection) = NewClient(100);
        StorageTestHarness.Session(client).AutoUsed = used;
        await _service.HandleAsync(client, "/check_auto_user Tester", Array.Empty<GameClient>());
        Replies(connection).Single().Text.Should().Be($"auto_user: {expected}");
    }

    [Test]
    public async Task The_three_force_warp_forms_use_WarpService_and_the_named_players_layer()
    {
        var (gm, _) = NewClient(100); var (other, _) = NewClient(0);
        var info = StorageTestHarness.Session(other); info.CharacterName = "Other"; info.CharacterHandle++;
        info.X = info.DestinationX = 100; info.Y = info.DestinationY = 200; info.Layer = 7;
        await _service.HandleAsync(gm, "/force_warp Other", new[] { other });
        A.CallTo(() => _warp.Warp(gm, 100, 200, 7)).MustHaveHappenedOnceExactly();
        await _service.HandleAsync(gm, "/force_warp 300 400", new[] { other });
        A.CallTo(() => _warp.Warp(gm, 300, 400)).MustHaveHappenedOnceExactly();
        await _service.HandleAsync(gm, "/force_warp 300 400 Other", new[] { other });
        A.CallTo(() => _warp.Warp(other, 300, 400)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task Invisible_uses_official_one_two_modes_and_drops_monster_aggro()
    {
        var (client, connection) = NewClient(100); var info = StorageTestHarness.Session(client);
        await _service.HandleAsync(client, "/invisible 1", Array.Empty<GameClient>());
        info.IsInvisible.Should().BeTrue();
        (ActorStatus.ForPlayer(info) & Navislamia.Game.Network.Packets.Enums.CreatureStatus.Invisible).Should().NotBe(0);
        connection.Sent.Should().Contain(p => Id(p) == 500);
        A.CallTo(() => _combat.DropAggro(client)).MustHaveHappenedOnceExactly();
        await _service.HandleAsync(client, "/invisible 2", Array.Empty<GameClient>());
        info.IsInvisible.Should().BeFalse();
    }

    [Test]
    public async Task Rebirth_and_lv_delegate_to_the_existing_game_services()
    {
        var (client, _) = NewClient(100);
        A.CallTo(() => _leveling.MaxLevel).Returns(170); A.CallTo(() => _leveling.SetLevel(client, 3)).Returns(true);
        await _service.HandleAsync(client, "/rebirth", Array.Empty<GameClient>());
        A.CallTo(() => _resurrection.Rebirth(client)).MustHaveHappenedOnceExactly();
        await _service.HandleAsync(client, "/lv 3", Array.Empty<GameClient>());
        A.CallTo(() => _leveling.SetLevel(client, 3)).MustHaveHappenedOnceExactly();
        await _service.HandleAsync(client, "/lv 171", Array.Empty<GameClient>());
        A.CallTo(() => _leveling.SetLevel(client, 171)).MustNotHaveHappened();
    }

    [Test]
    public void A_chat_block_cannot_reappear_when_the_unsigned_server_clock_wraps()
    {
        var info = new ConnectionInfo();
        info.ChatBlockRemaining(uint.MaxValue - 100).Should().Be(0);
        info.ChatBlockUntil = 500;
        info.ChatBlockRemaining(uint.MaxValue - 499).Should().Be(10);
        info.ChatBlockRemaining(600).Should().Be(0);
    }
}
