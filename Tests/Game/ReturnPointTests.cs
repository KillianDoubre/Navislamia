using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Props;
using Navislamia.Game.Services.ReturnPoints;

namespace Tests.Game;

/// <summary>docs/packet-specs/socle-point-de-retour.md.</summary>
[TestFixture]
public class ReturnPointTests
{
    private const int Gaia = 3, Deva = 4, Asura = 5;

    [TestCase(Deva, 164474, 52932)]
    [TestCase(Asura, 168356, 55399)]
    [TestCase(Gaia, 164335, 49510)]
    public void each_race_starts_at_its_own_point_of_the_island_of_trainees(int race, int x, int y)
    {
        ReturnPointRules.Start(race).Should().Be(new ReturnPoint(x, y));
    }

    [Test]
    public void the_first_return_point_is_the_start_within_thirty_units()
    {
        var start = ReturnPointRules.Start(Deva);
        var random = new Random(7);
        for (var i = 0; i < 500; i++)
        {
            var point = ReturnPointRules.FirstReturnPoint(start, random);
            point.X.Should().BeInRange(start.X - 30, start.X + 30);
            point.Y.Should().BeInRange(start.Y - 30, start.Y + 30);
        }
    }

    [TestCase(Deva, 7250, 6959)]
    [TestCase(Asura, 116542, 58190)]
    [TestCase(Gaia, 152742, 77401)]
    public void a_character_without_a_return_point_gets_its_race_town_at_login(int race, int x, int y)
    {
        var random = new Random(3);
        for (var i = 0; i < 200; i++)
        {
            var point = ReturnPointRules.LoginTown(race, random);
            point.X.Should().BeInRange(x, x + 100);
            point.Y.Should().BeInRange(y, y + 100);
        }
    }

    [Test]
    public void the_flags_round_trip_and_keep_the_other_flags()
    {
        var flags = ReturnPointRules.Write(new[] { "hx:10", "rx:1", "Vul1:3" }, new ReturnPoint(6650, 7001));

        flags.Should().Equal("hx:10", "Vul1:3", "rx:6650", "ry:7001");
        ReturnPointRules.TryRead(flags, out var point).Should().BeTrue();
        point.Should().Be(new ReturnPoint(6650, 7001));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("rx:6650")]
    [TestCase("rx:0|ry:7001")]
    [TestCase("rx:abc|ry:7001")]
    public void a_missing_or_zero_coordinate_is_no_return_point(string joined)
    {
        var flags = joined is null ? null : joined.Split('|', StringSplitOptions.RemoveEmptyEntries);

        // GetLastTownPosition: atoi, then "!x || !y" falls back.
        ReturnPointRules.TryRead(flags, out _).Should().BeFalse();
    }

    [TestCase("Binding_Deva_001", 6625, 6980, 100, "@90100508")]
    [TestCase("Binding_Asura_001", 116799, 58205, 100, "@90200508")]
    [TestCase("Binding_Gaia_001", 153513, 77203, 100, "@90400509")]
    [TestCase("Binding_Beginner_001", 172185, 52095, 10, "@90300508")]
    [TestCase("Binding_Rondoh_001", 135466, 104917, 100, "@90600509")]
    [TestCase("Binding_Rondoh_002", 140019, 106038, 100, "@90600509")]
    [TestCase("Binding_Ancient_relic_001", 152634, 151508, 100, "@90703308")]
    public void the_teleporters_bindings_are_the_epic7_lua(string function, int x, int y, int spread, string message)
    {
        ReturnPointRules.TryGetBinding(function, out var binding).Should().BeTrue();
        binding.Should().Be(new ReturnPointBinding(x, y, spread, message));
    }

    [Test]
    public void binding_sets_the_session_return_point_saves_it_and_says_so()
    {
        var characters = A.Fake<ICharacterService>();
        var service = new ReturnPointService(characters, new Random(1));
        var (client, sent) = Player();

        service.TryBind(client, "Binding_Gaia_001").Should().BeTrue();

        var info = StorageTestHarness.Session(client);
        info.RespawnX.Should().BeInRange(153513, 153613);
        info.RespawnY.Should().BeInRange(77203, 77303);
        info.RespawnLayer.Should().Be(0);
        A.CallTo(() => characters.SaveReturnPointAsync("Player",
            new ReturnPoint((int)info.RespawnX, (int)info.RespawnY))).MustHaveHappenedOnceExactly();
        sent.Should().ContainSingle();
        Encoding.ASCII.GetString(sent[0]).Should().Contain("@90400509");
    }

    [Test]
    public void an_unknown_function_binds_nothing()
    {
        var characters = A.Fake<ICharacterService>();
        var (client, sent) = Player();

        new ReturnPointService(characters, new Random(1)).TryBind(client, "Binding_Moon_001").Should().BeFalse();

        sent.Should().BeEmpty();
        A.CallTo(() => characters.SaveReturnPointAsync(A<string>._, A<ReturnPoint>._)).MustNotHaveHappened();
    }

    [TestCase(5, true)]
    [TestCase(4, false)]
    [TestCase(6, false)]
    public void reaching_level_five_moves_the_return_point_to_the_camp(int level, bool moved)
    {
        var characters = A.Fake<ICharacterService>();
        var (client, _) = Player();

        new ReturnPointService(characters, new Random(1)).OnLevelUp(client, level);

        var info = StorageTestHarness.Session(client);
        if (moved)
        {
            info.RespawnX.Should().BeInRange(172543, 172643);
            info.RespawnY.Should().BeInRange(51847, 51947);
        }
        else
        {
            info.RespawnX.Should().Be(1000);
        }
    }

    [Test]
    public void the_three_teleports_carry_their_cost()
    {
        PropScript.Parse("RunTeleport( 48000 , 139982 , 85162 )").Should()
            .Be(new PropAction(PropActionKind.RunTeleport, 139982, 85162, 0, Cost: 48000));
        PropScript.Parse("RunTeleport_Begin_TO_City( 10 , 6625 , 6980 )").Should()
            .Be(new PropAction(PropActionKind.RunTeleportBeginToCity, 6625, 6980, 0, Cost: 10));
        PropScript.Parse("RunTeleport_City_To_Camp( 0 , 172543 , 51847 )").Should()
            .Be(new PropAction(PropActionKind.RunTeleportCityToCamp, 172543, 51847, 0));
        PropScript.Parse("RunTeleport( -1 , 1 , 1 )").Should().Be(PropAction.None);
    }

    [Test]
    public void the_island_teleporter_offers_what_the_character_has_earned()
    {
        Triggers(TownTeleportRules.BeginnerTeleporter(Deva, 0, 1))
            .Should().Equal("Binding_Beginner_001()", "");
        Triggers(TownTeleportRules.BeginnerTeleporter(Deva, 1, 255)).Should().Equal(
            "Binding_Beginner_001()", "RunTeleport( 0 , 175711 ,56887 )",
            "RunTeleport_Begin_TO_City( 10 , 6625 , 6980 )", "");
        Triggers(TownTeleportRules.BeginnerTeleporter(Asura, 1, 0))[1]
            .Should().Be("RunTeleport_Begin_TO_City( 10 , 116799 , 58205 )");
        Triggers(TownTeleportRules.BeginnerTeleporter(Gaia, 2, 0))[1]
            .Should().Be("RunTeleport_Begin_TO_City( 10 , 153506 , 77175 )");
    }

    [Test]
    public void the_catalogue_links_every_town_teleporter_to_a_page_whose_actions_are_all_handled()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "npc-dialogs.73.json")));
        var catalog = document.RootElement.GetProperty("NpcDialogCatalog").Deserialize<NpcDialogOptions>()!;

        foreach (var (npc, contact) in new[]
                 {
                     (1005, "NPC_TeleportTown_Deva_contact"), (1014, "NPC_TeleportTown_2_Deva_contact"),
                     (2005, "NPC_TeleportTown_Asura_contact"), (2017, "NPC_TeleportTown_2_Asura_contact"),
                     (4005, "NPC_TeleportTown_Gaia_contact"), (4098, "NPC_TeleportTown_2_Gaia_contact"),
                     (6005, "NPC_TeleportTown_Rondoh_contact"), (6014, "NPC_TeleportTown_2_Rondoh_contact"),
                     (7033, "NPC_TeleportTown_Ancient_relic_contact")
                 })
        {
            catalog.Npcs[npc].Should().Be(contact + "()");
            var page = catalog.Dialogs[contact];
            page.Menu[0].Trigger.Should().StartWith("Binding_", "the return point comes first on every teleporter");
            foreach (var trigger in page.Menu.Select(entry => entry.Trigger).Where(trigger => trigger.Length > 0))
            {
                var name = trigger[..trigger.IndexOf('(')];
                var handled = ReturnPointRules.TryGetBinding(name, out _)
                              || PropScript.Parse(trigger).Kind is PropActionKind.RunTeleport
                                  or PropActionKind.RunTeleportBeginToCity
                              || catalog.Dialogs.ContainsKey(name);
                handled.Should().BeTrue($"{contact} advertises {trigger}");
            }
        }

        catalog.Npcs[3005].Should().Be(TownTeleportRules.BeginnerContact + "()");
    }

    [Test]
    public void a_teleport_is_paid_before_the_warp()
    {
        var warp = A.Fake<IWarpService>();
        var (client, dialogs) = Teleporter(warp, "RunTeleport( 500 , 6625 , 6980 )");
        var info = StorageTestHarness.Session(client);
        info.CharacterGold = 600;

        dialogs.Select(client, Select("RunTeleport( 500 , 6625 , 6980 )"));

        info.CharacterGold.Should().Be(100);
        A.CallTo(() => warp.Warp(client, A<float>.That.Matches(x => x >= 6625f && x <= 6635f), A<float>.That.Matches(y => y >= 6980f && y <= 6990f)))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public void a_teleport_without_enough_gold_says_so_and_stays()
    {
        var warp = A.Fake<IWarpService>();
        var (client, dialogs) = Teleporter(warp, "RunTeleport( 500 , 6625 , 6980 )");
        var info = StorageTestHarness.Session(client);
        info.CharacterGold = 499;

        dialogs.Select(client, Select("RunTeleport( 500 , 6625 , 6980 )"));

        info.CharacterGold.Should().Be(499);
        A.CallTo(() => warp.Warp(A<GameClient>._, A<float>._, A<float>._)).MustNotHaveHappened();
        Sent(client).Should().Contain(frame => Encoding.ASCII.GetString(frame).Contains("@90010008"));
    }

    [Test]
    public void a_binding_selected_from_the_dialog_sets_the_return_point()
    {
        var returnPoints = A.Fake<IReturnPointService>();
        A.CallTo(() => returnPoints.TryBind(A<GameClient>._, "Binding_Deva_001")).Returns(true);
        var (client, dialogs) = Teleporter(A.Fake<IWarpService>(), "Binding_Deva_001()", returnPoints);

        dialogs.Select(client, Select("Binding_Deva_001()"));

        A.CallTo(() => returnPoints.TryBind(client, "Binding_Deva_001")).MustHaveHappenedOnceExactly();
    }

    private static string[] Triggers(NpcDialogDefinition dialog) => dialog.Menu.Select(entry => entry.Trigger).ToArray();

    private static (GameClient Client, List<byte[]> Sent) Player()
    {
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterName = "Player";
        info.CharacterHandle = 1;
        info.RespawnX = 1000;
        info.RespawnY = 1000;
        return (client, connection.Sent);
    }

    private static List<byte[]> Sent(GameClient client) => ((StorageTestHarness.FrameConnection)client.Connection).Sent;

    private static (GameClient, NpcDialogService) Teleporter(IWarpService warp, string trigger,
        IReturnPointService returnPoints = null)
    {
        var options = new NpcDialogOptions();
        options.Npcs[4005] = "teleporter_contact()";
        options.Dialogs["teleporter_contact"] = new NpcDialogDefinition
        {
            Title = "@90400501", Text = "@90400502",
            Menu = { new NpcDialogMenuEntry { Label = "@90400506", Trigger = trigger } }
        };
        var dialogs = new NpcDialogService(Options.Create(options), warp, A.Fake<IStorageService>(),
            A.Fake<IMarketService>(), returnPoints: returnPoints);
        var (client, _) = Player();
        StorageTestHarness.Session(client).SpawnedNpcIdsByHandle[50] = 4005;
        dialogs.Contact(client, Contact(50));
        return (client, dialogs);
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
}
