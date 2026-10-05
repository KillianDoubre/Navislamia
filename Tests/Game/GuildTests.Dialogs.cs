using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services.Guilds;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// The guild officers' and siege managers' official Lua (docs/packet-specs/socle-dialogues-guilde-siege.md):
/// NPC_CreateGuild.lua and NPC_QuestClient.lua of Epic 7 Part 4, with their engine functions.
/// </summary>
public partial class GuildTests
{
    private const uint Npc = 77;

    /// <summary>Runs <paramref name="call"/> like the dialog service: a contact opens a dialog, a trigger must be advertised.</summary>
    private static async Task<GuildDialogResult> Step(Harness h, GameClient client, string call, bool contact = false)
    {
        var info = Info(client);
        if (contact) { info.ClearNpcDialog(); info.NpcDialogHandle = Npc; }
        else info.NpcDialogTriggers.Should().Contain(call, "only an advertised trigger reaches the script");
        var result = await h.Guilds.RunDialogAsync(client, Npc, info.NpcDialogRevision, call);
        Render(client, result);
        return result;
    }

    private static void Render(GameClient client, GuildDialogResult result)
    {
        if (result?.Page is not { } page) return;
        var info = Info(client);
        lock (info.NpcVisibilityLock)
        {
            info.NpcDialogRevision++;
            info.NpcDialogTriggers.Clear();
            foreach (var entry in page.Dialog.Menu.Where(m => m.Trigger.Length > 0)) info.NpcDialogTriggers.Add(entry.Trigger);
        }
    }

    private static TaskCompletionSource AttachRenderer(Harness h)
    {
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Guilds.AttachDialogs(async (client, handle, revision, call) =>
        {
            Render(client, await h.Guilds.RunDialogAsync(client, handle, revision, call));
            rendered.TrySetResult();
        });
        return rendered;
    }

    private static async Task<GameClient> AtNpc(Harness h, uint id, int npcId, long? guild = null, byte rank = 0, long gold = 500000)
    {
        var player = await h.Player(id, guild, rank, gold);
        Info(player).SpawnedNpcIdsByHandle[Npc] = npcId;
        return player;
    }

    [Test]
    public async Task An_officer_offers_the_official_menu_of_its_race()
    {
        var h = new Harness(); var player = await AtNpc(h, 1, 1012);
        var result = await Step(h, player, "NPC_CreateGuild_Deva_contact()", contact: true);

        result.Page.Dialog.Title.Should().Be("@90101201");
        result.Page.Dialog.Text.Should().Be("@90101202");
        Info(player).NpcDialogTriggers.Should().BeEquivalentTo("create_guild_Deva()", "guild_alliance()");
    }

    [Test]
    public async Task A_guild_is_founded_through_the_official_window_flow_and_the_script_charges_the_fee()
    {
        var h = new Harness(); var leader = await AtNpc(h, 1, 1012); var rendered = AttachRenderer(h);
        await Step(h, leader, "NPC_CreateGuild_Deva_contact()", contact: true);
        var offer = await Step(h, leader, "create_guild_Deva()");
        offer.Page.Dialog.Text.Should().Be("@90101205", "level 20 and no guild");
        await Step(h, leader, "show_guild_create()");
        h.Frames[1].Sent.Should().Contain(p => BitConverter.ToUInt16(p, 4) == 650 && p.Length == 7);

        (await h.Guilds.ExecuteCommandAsync(leader, "/gcreate FirstGuild")).Should().BeTrue();
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Info(leader).NpcDialogTriggers.Should().Contain("create_guild_main( 'FirstGuild' )");
        await Step(h, leader, "create_guild_main( 'FirstGuild' )");

        Info(leader).CharacterGold.Should().Be(400000);
        Info(leader).GuildPermission.Should().Be(7);
        await using var db = h.Db();
        (await db.Guilds.SingleAsync()).LeaderId.Should().Be(1);
        (await db.Characters.SingleAsync()).Gold.Should().Be(400000);
        h.Frames[1].Sent.Should().Contain(p => Encoding.ASCII.GetString(p).Contains("@90019002"), "the script's message()");
    }

    [Test]
    public async Task An_invalid_name_or_a_low_level_founds_nothing_and_charges_nothing()
    {
        var h = new Harness(); var leader = await AtNpc(h, 1, 1012);
        Info(leader).NpcDialogTriggers.Add("create_guild_main( 'Bad Name' )");
        Info(leader).NpcDialogHandle = Npc;
        var refused = await Step(h, leader, "create_guild_main( 'Bad Name' )");
        refused.Page.Dialog.Text.Should().Be("@90101212", "GUILD_CREATE_INVALID_GUILD_NAME: a space is not a letter");
        Info(leader).CharacterGold.Should().Be(500000);
        (await h.Guilds.ExecuteCommandAsync(leader, "/gcreate Bad'Name")).Should().BeFalse("a quote would leave the Lua string");

        var low = await AtNpc(h, 2, 1012); Info(low).CharacterLevel = 19;
        await Step(h, low, "NPC_CreateGuild_Deva_contact()", contact: true);
        var page = await Step(h, low, "create_guild_Deva()");
        page.Page.Dialog.Text.Should().Be("@90101203");
        Info(low).NpcDialogTriggers.Should().NotContain("show_guild_create()");
        await using var db = h.Db(); (await db.Guilds.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task An_alliance_is_created_and_destroyed_by_the_officer_script()
    {
        var h = new Harness(); var leader = await AtNpc(h, 1, 1012); await h.SeedGuild(leader, "Wolves");
        (await h.Create(leader, "Union", alliance: true)).Should().BeTrue();
        await using (var db = h.Db()) (await db.Alliances.SingleAsync()).MaxAllianceCount.Should().Be(3);

        await Step(h, leader, "guild_alliance()", contact: true);
        Info(leader).NpcDialogTriggers.Should().Contain("show_destroy_alliance()", "the alliance leader's menu");
        Info(leader).NpcDialogTriggers.Add("on_destroy_alliance()");
        var destroyed = await Step(h, leader, "on_destroy_alliance()");
        destroyed.Page.Dialog.Text.Should().Be("@90010234");
        await using (var db = h.Db()) (await db.Alliances.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task A_stranger_reads_the_siege_manager_pages_with_their_dungeon_and_is_never_warped()
    {
        var h = new Harness(); var stranger = await AtNpc(h, 1, 4089);
        var contact = await Step(h, stranger, "NPC_dungeon_siege_manager_contact( 130300 )", contact: true);
        contact.Page.Dialog.Title.Should().Be("@90408501");
        Info(stranger).NpcDialogTriggers.Should().BeEquivalentTo("dungeon_information( 130300 )", "secret_dungeon_information( 130300 )");

        var secret = await Step(h, stranger, "secret_dungeon_information( 130300 )");
        Info(stranger).NpcDialogTriggers.Should().Contain("question_secret_dungeon_enter(130300)");
        secret.Warp.Should().BeNull();
        Info(stranger).NpcDialogTriggers.Should().Contain("NPC_dungeon_siege_manager_contact( 130300 )", "the way back");
    }

    private static async Task<long> OwnDungeon(Harness h, GameClient leader, int dungeon, long gold)
    {
        var guild = await h.SeedGuild(leader, "Owners");
        await using var db = h.Db();
        var row = await db.Guilds.SingleAsync(g => g.Id == guild);
        row.DungeonId = dungeon; row.Gold = gold;
        db.Dungeons.Add(new DungeonEntity { Id = dungeon, OwnerGuildId = guild, TaxRate = 1 });
        await db.SaveChangesAsync();
        return guild;
    }

    [Test]
    public async Task The_owners_master_adjusts_the_tax_draws_it_and_gives_the_dungeon_up()
    {
        var h = new Harness(); var master = await AtNpc(h, 1, 4085);
        var guild = await OwnDungeon(h, master, 130000, 5000);
        await Step(h, master, "NPC_dungeon_siege_manager_contact( 130000 )", contact: true);
        Info(master).NpcDialogTriggers.Should().Contain(new[] { "management_menu( 130000 )", "dungeon_drop( 130000 )" },
            "relation 1, the owning guild's master");

        await Step(h, master, "management_menu( 130000 )");
        await Step(h, master, "tax_rate_adjust( 130000 )");
        await Step(h, master, "tax_rate_increase(130000)");
        await using (var db = h.Db()) (await db.Dungeons.SingleAsync()).TaxRate.Should().Be(2);

        await Step(h, master, Info(master).NpcDialogTriggers.First(t => t.StartsWith("management_menu")));
        await Step(h, master, "tax_collection( 130000 )");
        await Step(h, master, "tax_collection_click( 130000 )");
        Info(master).CharacterGold.Should().Be(505000);
        await using (var db = h.Db()) (await db.Guilds.SingleAsync(g => g.Id == guild)).Gold.Should().Be(0);

        await Step(h, master, "NPC_dungeon_siege_manager_contact( 130000 )", contact: true);
        await Step(h, master, "dungeon_drop( 130000 )");
        var dropped = await Step(h, master, "click_dungeon_drop( 130000 )");
        dropped.Page.Dialog.Text.Should().Be("@90408520");
        dropped.Warp.Should().NotBeNull().And.Match<(float X, float Y, byte Layer)?>(w => w!.Value.Layer == 0);
        await using (var db = h.Db()) (await db.Dungeons.SingleAsync()).OwnerGuildId.Should().BeNull();
    }

    [Test]
    public async Task A_plain_member_of_the_owners_sees_no_management()
    {
        var h = new Harness(); var master = await AtNpc(h, 1, 4085);
        var guild = await OwnDungeon(h, master, 130000, 5000);
        var member = await AtNpc(h, 2, 4085, guild);
        await Step(h, member, "NPC_dungeon_siege_manager_contact( 130000 )", contact: true);
        Info(member).NpcDialogTriggers.Should().BeEquivalentTo(new[] { "dungeon_information( 130000 )" }, "relation 2");
    }
}
