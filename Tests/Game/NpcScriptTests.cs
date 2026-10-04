using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Navislamia.Configuration.Options;
using Microsoft.Extensions.Options;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Auction;
using Navislamia.Game.Services.Interfaces;
using NUnit.Framework;

namespace Tests.Game;

[TestFixture]
public class NpcScriptTests
{
    private sealed class Items : IItemMatchCatalog
    {
        public bool TryGetFields(long code, out ItemMatchFields fields)
        {
            fields = new ItemMatchFields((int)code, ItemGroup.Weapon, ItemType.OnehandSword, 3, ItemWearType.Weapon)
                { Mix = ItemMixFields.Empty with { Grade = 1, MaxEtherealDurability = 100_000, Price = 1000 } };
            return true;
        }
    }
    private sealed class Harness
    {
        public readonly DbContextOptions<TelecasterContext> Options = new DbContextOptionsBuilder<TelecasterContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        public readonly GameClient Client;
        public readonly ConnectionInfo Info;
        public readonly NpcScriptService Service;
        public readonly StorageTestHarness.FrameConnection Connection = new(Array.Empty<byte>());
        public Harness(params int[] knownCodes)
        {
            Client = StorageTestHarness.NewGameClient(Connection); Info = StorageTestHarness.Session(Client);
            Info.CharacterName = "Ana"; Info.CharacterHandle = 42; Info.CharacterLevel = 30; Info.CharacterJob = 201;
            Info.NpcDialogHandle = 123; Info.SpawnedNpcIdsByHandle[123] = 1003;
            var names = new AuctionCatalog(Microsoft.Extensions.Options.Options.Create(new AuctionCatalogOptions
                { Items = knownCodes.Select(c => new AuctionItemRow { Code = c, NameId = c + 1 }).ToList() }));
            Service = new NpcScriptService(new NpcScriptCatalog(), new CharacterRepositoryFactory(Options), new CharacterGate(), new Items(), names);
            using var db = new TelecasterContext(Options);
            db.Characters.Add(new CharacterEntity { CharacterName = "Ana", Items = new List<ItemEntity>(),
                CurrentJob = (Job)201, JobDepth = JobDepth.First, Lv = 30, FlagList = new[] { "event_code:1" } });
            db.SaveChanges();
        }
        public Task<NpcScriptPage> Run(string trigger) => Service.RunAsync(Client, 123, Info.NpcDialogRevision, trigger);
        public async Task<CharacterEntity> Reload()
        {
            await using var db = new TelecasterContext(Options);
            return await db.Characters.Include(c => c.Items).SingleAsync();
        }
    }

    [Test]
    public void All_official_functions_compile_inside_the_sandbox()
    {
        var catalog = new NpcScriptCatalog();
        catalog.Functions.Should().ContainKey("max_item_durability");
        catalog.Functions.Should().ContainKey("question_stamp");
        catalog.Create().Globals.Get("second_present").Type.Should().Be(MoonSharp.Interpreter.DataType.Function);
        catalog.Create().Globals.Get("io").IsNil().Should().BeTrue();
    }

    [Test]
    public async Task Stamps_and_talent_information_render_the_official_follow_up_menus()
    {
        var h = new Harness();
        var stamps = await h.Run("question_stamp_master()");
        stamps.Should().NotBeNull();
        stamps.Dialog.Menu.Should().Contain(m => m.Trigger == "question_stamp_master_katan()");
        var tp = await h.Run("tp_skill()");
        tp.Should().NotBeNull();
        tp.Dialog.Menu.Should().Contain(m => m.Trigger == "upper_tp_skill()");
        (await h.Run("upper_tp_skill()")).Dialog.Text.Should().Be("@91002557");
    }

    [Test]
    public async Task A_gift_is_persisted_once_with_its_flag_and_obtained_notification()
    {
        var h = new Harness(3000301);
        (await h.Run("second_present()")).Dialog.Menu.Should().Contain(m => m.Trigger == "second_present_weapon()");
        await Task.WhenAll(h.Run("second_present_weapon()"), h.Run("second_present_weapon()"));
        var character = await h.Reload();
        character.Items.Should().ContainSingle().Which.ItemResourceId.Should().Be(3000301);
        character.Items.Single().EtherealDurability.Should().Be(100_000);
        character.FlagList.Should().Contain("q18:1");
        // SCRIPT_InsertItem sends no "item obtained" line: the script's own message() does the talking.
        h.Connection.Sent.Should().NotContain(p => System.Text.Encoding.ASCII.GetString(p).Contains("@253"));
        (await h.Run("second_present()")).Dialog.Menu.Should().NotContain(m => m.Trigger == "second_present_weapon()");
    }

    [Test]
    public async Task An_unknown_client_reward_commits_neither_items_nor_the_one_time_flag()
    {
        var h = new Harness();
        await h.Run("second_present_weapon()");
        var character = await h.Reload();
        character.Items.Should().BeEmpty(); character.FlagList.Should().NotContain("q18:1");
    }

    [TestCase(1000, 946, 100_000)]
    [TestCase(10, 10, 80_000)]
    public async Task Repair_checks_gold_and_persists_the_official_rank_cost(long gold, long expectedGold, int expectedDurability)
    {
        var h = new Harness(); h.Info.CharacterGold = gold;
        await using (var db = new TelecasterContext(h.Options))
        {
            var character = await db.Characters.SingleAsync(); character.Gold = gold;
            db.Items.Add(new ItemEntity { CharacterId = character.Id, ItemResourceId = 1, Amount = 1,
                WearInfo = ItemWearType.Weapon, EtherealDurability = 80_000 });
            await db.SaveChangesAsync();
        }
        await h.Run("max_item_durability()");
        var saved = await h.Reload();
        saved.Items.Single().EtherealDurability.Should().Be(expectedDurability);
        h.Info.CharacterGold.Should().Be(expectedGold);
        saved.Gold.Should().Be(expectedGold);
    }

    [Test]
    public async Task Hectors_card_exchange_takes_the_card_then_advances_quest_3335()
    {
        var h = new Harness();
        var quests = A.Fake<IQuestService>();
        A.CallTo(() => quests.SetQuestStatusAsync(A<GameClient>._, A<int>._, A<int>._, A<int>._)).Returns(true);
        var service = new NpcScriptService(new NpcScriptCatalog(), new CharacterRepositoryFactory(h.Options), new CharacterGate(),
            new Items(), new AuctionCatalog(Options.Create(new AuctionCatalogOptions())), quests);
        await using (var db = new TelecasterContext(h.Options))
        {
            var character = await db.Characters.SingleAsync();
            db.Items.Add(new ItemEntity { CharacterId = character.Id, ItemResourceId = 700001, Amount = 2,
                WearInfo = ItemWearType.None });
            await db.SaveChangesAsync();
        }

        // delete_item answers 1 like SCRIPT_DeleteItem: the script's "success == 1" branch runs.
        var page = await service.RunAsync(h.Client, 123, h.Info.NpcDialogRevision, "card_delete(700001)");

        page.Dialog.Text.Should().Be("@91002482");
        (await h.Reload()).Items.Single().Amount.Should().Be(1);
        A.CallTo(() => quests.SetQuestStatusAsync(h.Client, 3335, 1, 1)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task A_rejected_exchange_advances_no_quest()
    {
        var h = new Harness();
        var quests = A.Fake<IQuestService>();
        var service = new NpcScriptService(new NpcScriptCatalog(), new CharacterRepositoryFactory(h.Options), new CharacterGate(),
            new Items(), new AuctionCatalog(Options.Create(new AuctionCatalogOptions())), quests);

        (await service.RunAsync(h.Client, 123, h.Info.NpcDialogRevision, "card_delete(700001)")).Should().BeNull();

        A.CallTo(() => quests.SetQuestStatusAsync(A<GameClient>._, A<int>._, A<int>._, A<int>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task A_stale_dialogue_does_not_give_a_gift()
    {
        var h = new Harness(3000301);
        await h.Service.RunAsync(h.Client, 123, h.Info.NpcDialogRevision + 1, "second_present_weapon()");
        (await h.Reload()).Items.Should().BeEmpty();
    }

    [Test]
    public async Task Random_conversion_preserves_instance_data_and_updates_both_options()
    {
        var h = new Harness(601100354);
        long handle;
        await using (var db = new TelecasterContext(h.Options))
        {
            var character = await db.Characters.SingleAsync();
            var item = new ItemEntity { CharacterId = character.Id, ItemResourceId = 601100300, Amount = 1,
                WearInfo = ItemWearType.Weapon, Level = 4, Enhance = 5, EtherealDurability = 50_000,
                RandomOptionTypes = new[] { 96, 96 }, RandomOptionVars = new[] { 512, 1024 }, RandomOptionValues = new[] { 3m, 4m },
                SocketItemIds = new long[] { 7, 8, 0, 0 } };
            db.Items.Add(item); await db.SaveChangesAsync(); handle = item.Id;
        }
        var menu = await h.Run("random_item_change_menu()");
        menu.Dialog.Menu.Should().Contain(m => m.Trigger.Contains($"{handle}"));
        await h.Run($"random_item_change({handle})");
        var saved = (await h.Reload()).Items.Single();
        saved.ItemResourceId.Should().Be(601100354);
        saved.RandomOptionValues.Should().Equal(6m, 8m);
        saved.Level.Should().Be(4); saved.Enhance.Should().Be(5); saved.EtherealDurability.Should().Be(50_000);
        saved.SocketItemIds.Should().Equal(7, 8, 0, 0);
    }
}
