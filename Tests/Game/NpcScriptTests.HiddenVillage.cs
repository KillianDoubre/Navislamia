using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// The Hidden Village teleporters, the towns' teleporters to it, the Flea Market and the auctioneers run their
/// official Lua (docs/packet-specs/socle-pnj-pays-periodes.md): before, their contact had no page and nothing opened.
/// </summary>
public partial class NpcScriptTests
{
    private static Harness AtNpc(int npcId)
    {
        var h = new Harness();
        h.Info.SpawnedNpcIdsByHandle[123] = npcId;
        return h;
    }

    [Test]
    public async Task The_hidden_village_teleporter_offers_the_towns_without_the_guild_dungeon_shortcut()
    {
        var h = AtNpc(7005);
        var page = await h.Run("NPC_TeleportTown_1_Secroute_contact()");

        page.Dialog.Title.Should().Be("@90700501");
        page.Dialog.Menu.Select(m => m.Trigger).Should().Contain("RunTeleport( 0 , 6625 , 6980 )")
            .And.NotContain("scf_teleport_to_owned_dungeon()");
    }

    [Test]
    public async Task The_hunting_ground_teleporter_needs_the_hidden_village_pass()
    {
        var h = AtNpc(7006);
        var page = await h.Run("NPC_TeleportTown_2_Secroute_contact()");
        page.Dialog.Text.Should().Be("@90700118", "is_premium(): no pass, no hunting grounds");
        page.Dialog.Menu.Should().ContainSingle(m => m.Trigger == string.Empty);

        h.Info.ActiveBuffs.Add(new ActiveBuff(1, NpcScriptService.HiddenVillagePassState, 0, 1, 0, uint.MaxValue));
        page = await h.Run("NPC_TeleportTown_2_Secroute_contact()");
        page.Dialog.Title.Should().Be("@90700601");
        page.Dialog.Menu.Select(m => m.Trigger).Should().Contain("NPC_TeleportTown_2_Secroute_Sub( 1 )");
    }

    [Test]
    public async Task A_town_teleporter_to_the_hidden_village_names_itself_by_its_npc_id()
    {
        var h = AtNpc(2016);
        h.Info.ActiveBuffs.Add(new ActiveBuff(1, NpcScriptService.HiddenVillagePassState, 0, 1, 0, uint.MaxValue));
        var page = await h.Run("NPC_TeleportSecroute_Town_contact()");

        page.Dialog.Title.Should().Be("@90201601");
        page.Dialog.Menu.Select(m => m.Trigger).Should().Contain("RunTeleport_Secroute( 0 , 222175 , 17949 )");
    }

    [Test]
    public async Task The_flea_market_ambassador_of_the_hidden_village_offers_the_market()
    {
        var h = AtNpc(11240);
        var page = await h.Run("NPC_maricat_market_teleport_contact()");

        page.Dialog.Title.Should().Be("@90999702");
        page.Dialog.Menu.Select(m => m.Trigger).Should().Contain("warp_to_market()");
    }

    [Test]
    public async Task An_auctioneer_opens_the_auction_window()
    {
        var h = AtNpc(9999);
        (await h.Run("NPC_Auction_Secroute_contact()")).Should().BeNull("show_auction_window shows no NPC page");

        var dialog = h.Connection.Sent.Single(f => BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(4)) == 3000);
        BinaryPrimitives.ReadInt32LittleEndian(dialog.AsSpan(7)).Should().Be(NpcScriptService.AuctionWindowDialogType);
        BinaryPrimitives.ReadUInt32LittleEndian(dialog.AsSpan(11)).Should().Be(123u, "the auctioneer's handle");
    }

    [Test]
    public void Every_hidden_village_and_auction_npc_is_linked_to_a_contact_the_sandbox_runs()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "npc-dialogs.73.json");
        if (!File.Exists(path)) path = Path.Combine(FindRepository(), "DevConsole", "npc-dialogs.73.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var links = document.RootElement.GetProperty("NpcDialogCatalog").GetProperty("Npcs");
        var catalog = new NpcScriptCatalog();

        // Teleporters 7005/7006/11237/11238, the towns' teleporters to the village, Flea Market, auctioneers.
        foreach (var npc in new[] { 7005, 7006, 11237, 11238, 1016, 2016, 3024, 4016, 6016, 7027, 7040, 11127, 11240,
                     9999, 10000, 10001 })
        {
            var contact = links.GetProperty(npc.ToString()).GetString();
            catalog.Handles(contact!.Split('(')[0].Trim()).Should().BeTrue($"NPC {npc} ({contact})");
        }
    }

    private static string FindRepository()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Navislamia.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("Navislamia.sln");
    }
}
