using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>The seasonal events of the NPCs (docs/packet-specs/socle-pnj-evenements.md).</summary>
[TestFixture]
public class NpcEventsTests
{
    private static List<GameEventWindow> Halloween(string from = "10-20", string to = "11-05") =>
        new() { new GameEventWindow { Name = "Halloween", From = from, To = to } };

    [Test]
    public void An_event_is_open_inside_its_window_only_and_a_window_can_span_the_new_year()
    {
        NpcEvents.IsOpen("Halloween", Halloween(), new DateTime(2026, 10, 31)).Should().BeTrue();
        NpcEvents.IsOpen("halloween", Halloween(), new DateTime(2026, 11, 5, 23, 0, 0)).Should().BeTrue("both ends included");
        NpcEvents.IsOpen("Halloween", Halloween(), new DateTime(2026, 11, 6)).Should().BeFalse();
        NpcEvents.IsOpen("Christmas", Halloween(), new DateTime(2026, 10, 31)).Should().BeFalse();
        NpcEvents.IsOpen("Halloween", null, new DateTime(2026, 10, 31)).Should().BeFalse("no event runs by default");

        var christmas = new List<GameEventWindow> { new() { Name = "Christmas", From = "12-20", To = "01-05" } };
        NpcEvents.IsOpen("Christmas", christmas, new DateTime(2026, 12, 25)).Should().BeTrue();
        NpcEvents.IsOpen("Christmas", christmas, new DateTime(2027, 1, 3)).Should().BeTrue();
        NpcEvents.IsOpen("Christmas", christmas, new DateTime(2027, 1, 6)).Should().BeFalse();
        NpcEvents.IsOpen("Halloween", Halloween("31-10", "11-05"), new DateTime(2026, 10, 31)).Should().BeFalse(
            "a window that is not MM-dd opens nothing");
    }

    [Test]
    public void The_embedded_list_knows_the_town_candy_entry_and_the_event_npcs_that_stand_all_year()
    {
        NpcEvents.EventOfMenu("Trick_or_treat_2011()").Should().Be("Halloween");
        NpcEvents.EventOfMenu("open_market( 'foodshop_etc' )").Should().BeNull();
        NpcEvents.EventOfNpc(9982).Should().Be("Halloween", "a pumpkin spirit, npc_wonderland_teleport_contact");
        NpcEvents.EventOfNpc(11477).Should().Be("Easter", "NPC_dragonservicer_easter_contact");
        NpcEvents.EventOfNpc(1090).Should().BeNull("the food merchant stands all year");
        NpcEvents.IsNpcShown(11477, null, new DateTime(2026, 4, 5)).Should().BeFalse();
    }

    [Test]
    public void A_town_npc_offers_halloween_candy_only_while_halloween_runs()
    {
        var options = new NpcDialogOptions
        {
            Npcs = { [1090] = "NPC_food_shop_contact()" }
        };
        options.Dialogs["NPC_food_shop_contact"] = new NpcDialogDefinition
        {
            Title = "@90109001", Text = "@90109002",
            Menu =
            {
                new NpcDialogMenuEntry { Label = "@90604959", Trigger = "Trick_or_treat_2011()" },
                new NpcDialogMenuEntry { Label = "@90010187", Trigger = "open_market( 'foodshop_etc' )" },
                new NpcDialogMenuEntry { Label = "@90010002", Trigger = "" }
            }
        };

        IReadOnlyCollection<string> Advertised(DateTime now)
        {
            var dialogs = new NpcDialogService(Options.Create(options), A.Fake<IWarpService>(), A.Fake<IStorageService>(),
                A.Fake<IMarketService>(), rules: Options.Create(new GameRuleOptions { Events = Halloween() }),
                localNow: () => now);
            var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
            var info = StorageTestHarness.Session(client);
            info.SpawnedNpcIdsByHandle[50] = 1090;
            var contact = new byte[11];
            BinaryPrimitives.WriteUInt32LittleEndian(contact.AsSpan(7), 50);
            dialogs.Contact(client, contact);
            return info.NpcDialogTriggers;
        }

        Advertised(new DateTime(2026, 7, 14)).Should().BeEquivalentTo("open_market( 'foodshop_etc' )");
        Advertised(new DateTime(2026, 10, 31)).Should().BeEquivalentTo("Trick_or_treat_2011()",
            "open_market( 'foodshop_etc' )");
    }
}
