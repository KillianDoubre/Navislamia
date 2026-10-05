using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Auction;
using Navislamia.Game.Services.Creatures;
using Navislamia.Game.Services.Interfaces;
using NUnit.Framework;

namespace Tests.Game;

public partial class NpcScriptTests
{
    [Test]
    public async Task Official_lua_general_and_special_dialogs_send_the_measured_frames()
    {
        var h = new Harness();
        await h.Run("dlg_general('Été')");
        h.Connection.Sent.Single().Should().Equal(GameSmallPackets.GeneralMessageBox("Été"));
        h.Connection.Sent.Clear();
        await h.Run("dlg_special('confirm_window', 'set_flag(\"done\",1)', 'Été')");
        h.Connection.Sent.Single().Should().Equal(GameSmallPackets.ShowWindow("confirm_window", "Été", "set_flag(\"done\",1)"));
        h.Info.ScriptWindowTrigger.Should().Be("set_flag(\"done\",1)");
    }

    [Test]
    public async Task Failed_scripts_send_no_ui_and_grant_no_window_callback()
    {
        var h = new Harness();
        await h.Run("dlg_general('Hello'); dlg_special('confirm_window','set_flag(\"done\",1)','Hello'); error('rollback')");
        h.Connection.Sent.Should().NotContain(p => Id(p) == 3003 || Id(p) == 3004);
        h.Info.ScriptWindowTrigger.Should().BeEmpty();
        await h.Run("dlg_general('Hello', 'Another'); dlg_special('confirm_window','go','Hello','Another')");
        h.Connection.Sent.Should().NotContain(p => Id(p) == 3003 || Id(p) == 3004);
    }

    [Test]
    public async Task Oversized_scripts_and_stale_contacts_grant_no_callback()
    {
        var h = new Harness();
        await h.Run("dlg_special('confirm_window','go',string.rep('a',1024)); dlg_general(string.rep('a',1024))");
        h.Connection.Sent.Should().BeEmpty(); h.Info.ScriptWindowTrigger.Should().BeEmpty();
        h.Info.NpcDialogHandle = 0;
        await h.Run("dlg_general('Hello')"); h.Connection.Sent.Should().BeEmpty();
    }

    private static ushort Id(byte[] p) => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4));
    private static byte[] WindowReply(string trigger)
    {
        var bytes = Encoding.ASCII.GetBytes(trigger); var frame = new byte[9 + bytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)frame.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), 3001);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(7), (ushort)bytes.Length);
        bytes.CopyTo(frame, 9); return frame;
    }

    [Test]
    public async Task A_window_callback_is_exact_single_use_and_works_without_an_npc()
    {
        var h = new Harness();
        await h.Run("dlg_special('confirm_window', 'set_flag(\"done\",1)', 'Hello')");
        h.Info.ClearNpcDialog();
        var scripts = A.Fake<INpcScriptService>();
        var dialogs = new NpcDialogService(Options.Create(new NpcDialogOptions()), A.Fake<IWarpService>(),
            A.Fake<IStorageService>(), A.Fake<IMarketService>(), npcScripts: scripts);
        dialogs.Select(h.Client, WindowReply("set_flag(\"done\",2)"));
        h.Info.ScriptWindowTrigger.Should().Be("set_flag(\"done\",1)");
        A.CallTo(() => scripts.RunWindowScriptAsync(A<GameClient>._, A<string>._)).MustNotHaveHappened();
        dialogs.Select(h.Client, WindowReply("set_flag(\"done\",1)"));
        h.Info.ScriptWindowTrigger.Should().BeEmpty();
        dialogs.Select(h.Client, WindowReply("set_flag(\"done\",1)"));
        A.CallTo(() => scripts.RunWindowScriptAsync(h.Client, "set_flag(\"done\",1)")).MustHaveHappenedOnceExactly();
        h.Info.ScriptWindowTrigger = "tp_skill";
        dialogs.Select(h.Client, WindowReply("tp_skill()"));
        A.CallTo(() => scripts.RunWindowScriptAsync(h.Client, "tp_skill()")).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task Window_callback_runs_the_existing_sandbox_and_persists_its_effect()
    {
        var h = new Harness(); h.Info.ClearNpcDialog();
        await h.Service.RunWindowScriptAsync(h.Client, "set_flag('from_window',1)");
        (await h.Reload()).FlagList.Should().Contain("from_window:1");
    }

    [Test]
    public async Task The_official_creature_name_menu_uses_formed_handles_and_checks_the_gold_before_opening()
    {
        var h = new Harness(); var summons = A.Fake<ICreatureService>();
        var card = new CreatureCard { ItemId = 88, SummonId = 7, SummonCode = 2201, SummonHandle = 0x50000001,
            Level = 5, SummonName = "Original", Amount = 1 };
        h.Info.CreatureCards[88] = card; h.Info.SummonSlots = new long[6]; h.Info.SummonSlots[0] = 88; h.Info.CharacterGold = 5000;
        h.Info.SpawnedNpcIdsByHandle[123] = 1001;
        A.CallTo(() => summons.NameIdOf(card)).Returns(1234);
        var service = new NpcScriptService(new NpcScriptCatalog(), new CharacterRepositoryFactory(h.Options), new CharacterGate(),
            new Items(), new AuctionCatalog(Options.Create(new AuctionCatalogOptions())), summonService: summons);
        var menu = await service.RunAsync(h.Client, 123, h.Info.NpcDialogRevision, "Creature_name_change_Menu()");
        var choice = menu.Dialog.Menu.Single(m => m.Trigger.Contains("Creature_name_change_gold"));
        choice.Trigger.Should().Contain(card.SummonHandle.ToString()); choice.Label.Should().Contain("5000");
        await service.RunAsync(h.Client, 123, h.Info.NpcDialogRevision, choice.Trigger);
        A.CallTo(() => summons.ShowNameChange(h.Client, card.SummonHandle)).MustHaveHappenedOnceExactly();
        h.Info.CharacterGold = 4999;
        await service.RunAsync(h.Client, 123, h.Info.NpcDialogRevision, choice.Trigger);
        A.CallTo(() => summons.ShowNameChange(h.Client, card.SummonHandle)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task Lua_SP_changes_persist_before_the_owner_notification()
    {
        var h = new Harness(); var summons = A.Fake<ICreatureService>();
        var card = new CreatureCard { ItemId = 88, SummonId = 7, SummonCode = 2201, SummonHandle = 0x50000001, Amount = 1 };
        h.Info.CreatureCards[88] = card; h.Info.SummonSlots = new long[6]; h.Info.SummonSlots[0] = 88;
        await using (var db = new Navislamia.Game.DataAccess.Contexts.TelecasterContext(h.Options))
        {
            var character = await db.Characters.SingleAsync();
            db.Items.Add(new ItemEntity { Id = 88, CharacterId = character.Id, ItemResourceId = 540015, Amount = 1 });
            db.Summons.Add(new SummonEntity { Id = 7, CharacterId = character.Id, CardItemId = 88, Name = "Original", Sp = 100 });
            await db.SaveChangesAsync();
        }
        var service = new NpcScriptService(new NpcScriptCatalog(), new CharacterRepositoryFactory(h.Options), new CharacterGate(),
            new Items(), new AuctionCatalog(Options.Create(new AuctionCatalogOptions())), summonService: summons);
        await service.RunAsync(h.Client, 123, h.Info.NpcDialogRevision, "set_creature_value(0,'sp',5000)");
        await using var verify = new Navislamia.Game.DataAccess.Contexts.TelecasterContext(h.Options);
        (await verify.Summons.SingleAsync()).Sp.Should().Be(1000);
        A.CallTo(() => summons.SetSp(h.Client, card, 1000)).MustHaveHappenedOnceExactly();
    }
}
