using Navislamia.Game.Network.Packets.Enums;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using FakeItEasy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;
using NUnit.Framework;
using Npgsql;

namespace Tests.Game;

[TestFixture]
public class QuestLifecycleTests
{
    private DbContextOptions<TelecasterContext> _options = null!;
    private QuestService _service = null!;
    private GameClient _client = null!;
    private StorageTestHarness.FrameConnection _connection = null!;
    private QuestResourceEntity _resource = null!;
    private ManualTime _time = null!;
    private FailSave _failure = null!;
    private IGroundItemService _ground = null!;
    private ILevelingService _leveling = null!;

    [SetUp]
    public async Task Setup()
    {
        _time = new ManualTime();
        _failure = new FailSave();
        _options = new DbContextOptionsBuilder<TelecasterContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), o => o.EnableNullChecks(false))
            .AddInterceptors(_failure).Options;
        await using (var db = Db())
        {
            db.Characters.Add(new CharacterEntity { Id = 1, CharacterName = "Questor", Items = new List<ItemEntity>() });
            await db.SaveChangesAsync();
        }
        _resource = new QuestResourceEntity
        {
            Id = 1005, TextIdQuest = 91005, Type = 102, Value1 = 1003, Value2 = 2,
            LimitGaia = "1", LimitDeva = "1", LimitAsura = "1", LimitFighter = "1",
            LimitHunter = "1", LimitMagician = "1", LimitSummoner = "1", LimitJobDepth = 15,
            Exp = 30, Jp = 12, Gold = 50, HolicPoint = 2,
            DefaultRewardId = 603001, DefaultRewardLevel = 1, DefaultRewardQuantity = 3,
            OptionalRewardId6 = 101001, OptionalRewardLevel6 = 7, OptionalRewardQuantity6 = 1
        };
        var catalogue = A.Fake<IQuestCatalogueRepository>();
        A.CallTo(() => catalogue.GetResources()).Returns(new[] { _resource });
        A.CallTo(() => catalogue.GetLinks()).Returns(new[]
        {
            new QuestLinkResourceEntity { NpcId = 3011, QuestId = 1005, FlagStart = "1", FlagProgress = "1", FlagEnd = "1",
                TextIdStart = 101, TextIdInProgress = 102, TextIdEnd = 103 }
        });
        A.CallTo(() => catalogue.GetJobs()).Returns(new[] { new JobResourceEntity { Id = 200, JobClass = 1, JobDepth = 1 } });
        A.CallTo(() => catalogue.GetRandomPools()).Returns(new[]
        {
            new RandomPoolResourceEntity { GroupId = -10, QuestTargetId = 1003, TargetLevel = 10 },
            new RandomPoolResourceEntity { GroupId = 101, QuestTargetId = 1003, TargetLevel = 10 },
            new RandomPoolResourceEntity { GroupId = 101, QuestTargetId = 1004, TargetLevel = 10 },
            new RandomPoolResourceEntity { GroupId = 101, QuestTargetId = 1005, TargetLevel = 10 }
        });
        var characters = A.Fake<ICharacterService>();
        A.CallTo(() => characters.GetQuestsAsync("Questor")).ReturnsLazily(async () =>
        {
            await using var db = Db();
            return await db.CharacterQuests.AsNoTracking().Where(q => q.CharacterId == 1).ToArrayAsync();
        });
        A.CallTo(() => characters.DropQuestAsync("Questor", A<int>._)).ReturnsLazily(async (string _, int code) =>
        {
            await using var db = Db();
            var quest = await db.CharacterQuests.SingleOrDefaultAsync(q => q.CharacterId == 1 && q.Code == code);
            if (quest is null) return false;
            db.CharacterQuests.Remove(quest);
            await db.SaveChangesAsync();
            return true;
        });
        _connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        _client = StorageTestHarness.NewGameClient(_connection, characterService: characters);
        var info = StorageTestHarness.Session(_client);
        info.CharacterName = "Questor"; info.CharacterHandle = 1; info.CharacterRace = 4;
        info.CharacterLevel = 10; info.CharacterJob = 200; info.CharacterJobLevel = 5;
        info.SpawnedNpcIdsByHandle[90] = 3011;
        info.NpcDialogHandle = 90; info.NpcQuestCode = 1005;
        _ground = A.Fake<IGroundItemService>();
        _leveling = A.Fake<ILevelingService>();
        var drops = A.Fake<IMonsterDropCatalog>();
        A.CallTo(() => drops.Groups).Returns(new Dictionary<int, DropGroupEntry[]> { [-1] = new[] { new DropGroupEntry(1000000, 1, 1, 1) } });
        _service = new QuestService(characters, catalogue, _options, new CharacterGate(), _leveling,
            ground: _ground, drops: drops, timeProvider: _time,
            scripts: new Navislamia.Game.Scripting.ScriptService(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Navislamia.Game.Scripting.ScriptService>.Instance));
    }

    [TearDown] public void Teardown() => _service.Dispose();
    private TelecasterContext Db() => new(_options);
    private Task Start() => _service.StartQuestAsync(_client, 3011, 1005, 101);

    [Test]
    public async Task Lua_controlled_quest_is_offered_progresses_reconnects_and_rewards_once()
    {
        _resource.Type = 701; _resource.Value1 = 90010095; _resource.Value2 = 2;
        (await _service.GetNpcOffersAsync(_client, 3011)).Should().ContainSingle();
        (await _service.SetQuestStatusAsync(_client, 1005, 1, 2)).Should().BeFalse("not accepted");
        await Start();
        var scripts = new Navislamia.Game.Scripting.ScriptService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Navislamia.Game.Scripting.ScriptService>.Instance);
        var context = new Navislamia.Game.Scripting.QuestScriptContext
        {
            PlayerHandle = 1,
            GetProgress = code => _service.GetQuestProgressAsync(_client, code).GetAwaiter().GetResult(),
            SetStatus = (code, index, value) => _service.SetQuestStatusAsync(_client, code, index, value).GetAwaiter().GetResult()
        };
        scripts.RunQuestScript("assert(get_quest_progress(1005)==1); assert(set_quest_status(1005,1,1)==1)", context).Should().Be(1);
        await Kill(); await _service.RefreshAsync(_client);
        (await Quest()).Status[0].Should().Be(1, "ordinary kills do not alter external counters");
        (await _service.SetQuestStatusAsync(_client, 1005, 0, 3)).Should().BeFalse();
        (await _service.SetQuestStatusAsync(_client, 1005, 7, 3)).Should().BeFalse();
        scripts.RunQuestScript("assert(set_quest_status(1005,1,2,999)==-1); assert(set_quest_status(1005,1,2)==1)", context).Should().Be(1);
        scripts.RunString("assert(set_quest_status(1005,1,999)==-1)").Should().Be(1, "Lua context was cleared");
        await _service.LeaveWorldAsync(_client); await _service.SendQuestListAsync(_client);
        (await Quest()).Progress.Should().Be(QuestRules.Finishable);
        await End(); await End();
        EndResults().Should().Equal(ResultCode.Success, ResultCode.NotActable);
        StorageTestHarness.Session(_client).CharacterExp.Should().Be(30);
    }

    [Test]
    public async Task Expired_Lua_quest_rejects_objective_updates()
    {
        _resource.Type = 701; _resource.TimeLimit = 1; _resource.TimeLimitType = "2";
        await Start(); _time.Advance(2);
        (await _service.SetQuestStatusAsync(_client, 1005, 1, 2)).Should().BeFalse();
    }

    [Test]
    public async Task Server_authored_Npc_Lua_action_updates_only_an_advertised_quest_objective()
    {
        _resource.Type = 701; _resource.Value2 = 2; await Start();
        StorageTestHarness.Session(_client).NpcDialogTriggers.Add("set_quest_status(1005,1,2)");
        var dialogs = new NpcDialogService(Options.Create(new NpcDialogOptions()), A.Fake<IWarpService>(),
            A.Fake<IStorageService>(), A.Fake<IMarketService>(), _service);
        dialogs.Select(_client, Selection("set_quest_status(1005,1,999)"));
        (await Quest()).Status[0].Should().Be(0);
        dialogs.Select(_client, Selection("set_quest_status(1005,1,2)"));
        (await Quest()).Progress.Should().Be(QuestRules.Finishable);
        (await _service.RunScriptAsync(_client, "assert(get_quest_progress(1005)==2)")).Should().Be(1);
    }
    [Test]
    public async Task The_mark_over_the_npc_follows_the_quest_from_offer_to_hand_in()
    {
        // SendNPCStatusInVisibleRange: a 500 for the NPC in view each time its mark changes for this player.
        StorageTestHarness.Session(_client).SpawnedNpcs[3011] = 90;
        uint? Mark()
        {
            var frame = _connection.Sent.LastOrDefault(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4)) == 500
                && BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(7)) == 90);
            return frame is null ? null : BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(11));
        }

        await _service.SendQuestListAsync(_client);
        Mark().Should().Be(CreatureStatus.NpcHasStartableQuest, "the quest can be taken: \"!\"");
        StorageTestHarness.Session(_client).NpcQuestMarks[3011].Should().Be(CreatureStatus.NpcHasStartableQuest,
            "what a later TS_SC_ENTER of the NPC carries");

        await Start();
        Mark().Should().Be(CreatureStatus.NpcHasInProgressQuest);

        await Kill(); await Kill();
        await WaitFor(() => Mark() == CreatureStatus.NpcHasFinishableQuest);
        Mark().Should().Be(CreatureStatus.NpcHasFinishableQuest, "the quest can be handed in: \"?\"");

        await End();
        await WaitFor(() => Mark() == 0u);
        Mark().Should().Be(0u, "done and not repeatable: no mark");
        StorageTestHarness.Session(_client).NpcQuestMarks.Should().BeEmpty();
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(10);
    }

    private Task Kill(int id = 1003) => _service.OnMonsterKilledAsync(_client, id, 10, 20, 0);
    private Task End(sbyte slot = -1) => _service.EndQuestAsync(_client, new GameActionPackets.EndQuestRequest(1005, slot));
    private IEnumerable<ResultCode> EndResults() => _connection.Sent
        .Where(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4)) == 0
            && BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(7)) == 605)
        .Select(p => (ResultCode)BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(9)));
    private async Task<CharacterQuestEntity> Quest()
    {
        await using var db = Db();
        return await db.CharacterQuests.SingleAsync();
    }

    [Test]
    public async Task DialogueOffersThenAcceptsTheQuestUsingOnlyItsAdvertisedTrigger()
    {
        var options = new NpcDialogOptions
        {
            Npcs = new() { [3011] = "contact()" },
            Dialogs = new() { ["contact"] = new NpcDialogDefinition { Title = "@90301101", Text = "@90301102" } }
        };
        var dialogs = new NpcDialogService(Options.Create(options), A.Fake<IWarpService>(), A.Fake<IStorageService>(), A.Fake<IMarketService>(), _service);
        var contact = new byte[11]; BinaryPrimitives.WriteUInt32LittleEndian(contact.AsSpan(7), 90);
        dialogs.Contact(_client, contact);
        await Until(() => StorageTestHarness.Session(_client).NpcDialogTriggers.Contains("quest_info(1005)"));
        dialogs.Select(_client, Selection("quest_info(1005)"));
        await Until(() => StorageTestHarness.Session(_client).NpcDialogTriggers.Contains("start_quest( 1005, 101 )"));
        Encoding.ASCII.GetString(_connection.Sent.Last()).Should().Contain("@90301101").And.Contain("QUEST|1005|101").And.Contain("START").And.Contain("REJECT");
        dialogs.Select(_client, Selection("start_quest( 1005, 999 )"));
        await using (var db = Db()) (await db.CharacterQuests.CountAsync()).Should().Be(0);
        dialogs.Select(_client, Selection("start_quest( 1005, 101 )"));
        await Until(() => _connection.Sent.Any(p => Encoding.ASCII.GetString(p).Contains("START|SUCCESS|1005")));
        (await Quest()).Progress.Should().Be(QuestRules.InProgress);
        (await Quest()).StartId.Should().Be(101, "startID is the accepted NPC text id, not the NPC resource id");
    }

    [Test]
    public async Task KillsPersistAndConcurrentHandInsRewardExactlyOnce()
    {
        await Start(); await Kill(999); (await Quest()).Status[0].Should().Be(0);
        await Kill(); await Kill(); await Kill();
        var quest = await Quest(); quest.Status[0].Should().Be(2); quest.Progress.Should().Be(QuestRules.Finishable);
        await Task.WhenAll(End(5), End(5));
        EndResults().Should().BeEquivalentTo(new[] { ResultCode.Success, ResultCode.NotActable });
        await using var db = Db();
        (await db.CharacterQuests.CountAsync()).Should().Be(0);
        (await db.CharacterQuestCompletions.CountAsync()).Should().Be(1);
        var character = await db.Characters.SingleAsync();
        character.Exp.Should().Be(30); character.Jp.Should().Be(12); character.Gold.Should().Be(50); character.HuntaholicPoint.Should().Be(2);
        var chosen = await db.Items.SingleAsync(i => i.ItemResourceId == 101001);
        chosen.Level.Should().Be(7); chosen.GenerateBySource.Should().Be(ItemGenerateSource.Quest);
        (await db.Items.SingleAsync(i => i.ItemResourceId == 603001)).Amount.Should().Be(3);
        StorageTestHarness.Session(_client).CharacterGold.Should().Be(50);
        A.CallTo(() => _leveling.ApplyExperience(_client)).MustHaveHappenedOnceExactly();
        (await _service.GetNpcOffersAsync(_client, 3011)).Should().BeEmpty();
    }

    [Test]
    public async Task RewardsIntoAnEmptyBagStartAtTheFirstSlot()
    {
        await Start(); await Kill(); await Kill(); await End(5);

        await using var db = Db();
        var indices = await db.Items.Select(i => i.Idx).OrderBy(i => i).ToArrayAsync();
        indices.Should().Equal(InventoryArrange.FirstIndex, InventoryArrange.FirstIndex + 1);
    }

    [Test]
    public async Task UnfinishedQuestAndEmptyRewardSlotAndWrongNpcCannotPay()
    {
        await Start(); await End();
        EndResults().Last().Should().Be(ResultCode.NotActable);
        await Kill(); await Kill(); await End(0);
        EndResults().Last().Should().Be(ResultCode.NotActable);
        StorageTestHarness.Session(_client).SpawnedNpcIdsByHandle[91] = 3012; StorageTestHarness.Session(_client).NpcDialogHandle = 91;
        await End(5); EndResults().Last().Should().Be(ResultCode.NotActable);
        await using var db = Db(); (await db.Items.CountAsync()).Should().Be(0); (await db.CharacterQuests.CountAsync()).Should().Be(1);
    }

    [Test]
    public async Task CollectedItemsAcrossStacksAreConsumedAndRemainingItemsArePreserved()
    {
        _resource.Type = 103; _resource.Value1 = 1000000; _resource.Value2 = 3;
        await Start(); await AddItem(1000000, 2); await AddItem(1000000, 2);
        await _service.RefreshAsync(_client);
        (await Quest()).Status[0].Should().Be(3);
        await End(); EndResults().Last().Should().Be(ResultCode.Success);
        await using var db = Db(); (await db.Items.Where(i => i.ItemResourceId == 1000000).SumAsync(i => i.Amount)).Should().Be(1);
    }

    [Test]
    public async Task LosingAnObjectRevokesCompletionAndWornItemsDoNotCount()
    {
        _resource.Type = 103; _resource.Value1 = 1000000; _resource.Value2 = 3;
        await AddItem(1000000, 3); await Start(); (await Quest()).Progress.Should().Be(QuestRules.Finishable);
        await using (var db = Db()) { var item = await db.Items.SingleAsync(); item.WearInfo = ItemWearType.Weapon; await db.SaveChangesAsync(); }
        await _service.RefreshAsync(_client); (await Quest()).Progress.Should().Be(QuestRules.InProgress);
        await End(); EndResults().Last().Should().Be(ResultCode.NotActable);
    }

    [Test]
    public async Task HuntingDropsOnlyForTheRequiredMonsterAndStopsWhenTheBagHasEnough()
    {
        _resource.Type = 106; _resource.Value1 = 1000000; _resource.Value2 = 1; _resource.Value7 = 1003; _resource.DropGroupId = -1;
        await Start(); await Kill(999); await Kill();
        A.CallTo(() => _ground.DropQuestItem(_client, 1000000, 10, 20, 0)).MustHaveHappenedOnceExactly();
        await AddItem(1000000, 1); await Kill();
        A.CallTo(() => _ground.DropQuestItem(_client, 1000000, 10, 20, 0)).MustHaveHappenedOnceExactly();
        (await Quest()).Progress.Should().Be(QuestRules.Finishable);
    }

    [Test]
    public async Task CompletionHistoryUnlocksPrerequisitesAndRepeatableCooldownIsEnforced()
    {
        _resource.Type = 401; _resource.ForeQuest1 = 999;
        await Start(); await using (var db = Db()) (await db.CharacterQuests.CountAsync()).Should().Be(0);
        await using (var db = Db()) { db.CharacterQuestCompletions.Add(new() { CharacterId = 1, Code = 999, CompletedAt = _time.GetUtcNow().UtcDateTime }); await db.SaveChangesAsync(); }
        await Start(); await End();
        _resource.Repeatable = "1"; _resource.CoolTime = 60;
        await Start(); await using (var db = Db()) (await db.CharacterQuests.CountAsync()).Should().Be(0);
        _time.Advance(61); await Start(); (await Quest()).Progress.Should().Be(QuestRules.Finishable);
        await End(); await using (var db = Db()) (await db.CharacterQuestCompletions.CountAsync(q => q.Code == 1005)).Should().Be(1);
        StorageTestHarness.Session(_client).CharacterGold.Should().Be(100);
    }

    [Test]
    public async Task AbortAllowsReacceptanceButAcceptCooldownStillApplies()
    {
        _resource.AcceptCoolTime = 60;
        await Start(); await _service.DropQuestAsync(_client, new GameActionPackets.DropQuestRequest(1005));
        await Start(); await using (var db = Db()) (await db.CharacterQuests.CountAsync()).Should().Be(0);
        // CreatedOn uses the database entity's real UTC creation time.
        _time.Advance(61); await Start(); (await Quest()).Code.Should().Be(1005);
    }

    [Test]
    public async Task DeadlinePreventsALateKillFromCompletingTheQuest()
    {
        _resource.TimeLimitType = "2"; _resource.TimeLimit = 10;
        await Start(); _time.Advance(11); await Kill();
        (await Quest()).Progress.Should().Be(100); (await Quest()).Status[0].Should().Be(0);
        await End(); EndResults().Last().Should().Be(ResultCode.NotActable);
    }

    [Test]
    public async Task OnlineCountdownPausesWhenTheCharacterLeaves()
    {
        _resource.TimeLimitType = "1"; _resource.TimeLimit = 60;
        await Start(); _time.Advance(10); await _service.LeaveWorldAsync(_client);
        (await Quest()).RemainingSeconds.Should().Be(50);
        _time.Advance(600); await _service.SendQuestListAsync(_client);
        (await Quest()).RemainingSeconds.Should().Be(50);
        _time.Advance(51); await _service.RefreshAsync(_client); (await Quest()).Progress.Should().Be(100);
    }

    [Test]
    public async Task FailedCommitLeavesObjectsQuestAndSessionRewardsUntouched()
    {
        _resource.Type = 103; _resource.Value1 = 1000000; _resource.Value2 = 1;
        await AddItem(1000000, 1); await Start();
        // Refresh does a SaveChanges first; fail only the save carrying a completion record.
        _failure.RejectCompletion = true;
        await End(); EndResults().Last().Should().Be(ResultCode.DBError);
        await using var db = Db();
        (await db.CharacterQuests.CountAsync()).Should().Be(1); (await db.CharacterQuestCompletions.CountAsync()).Should().Be(0);
        (await db.Items.SingleAsync()).Amount.Should().Be(1); StorageTestHarness.Session(_client).CharacterGold.Should().Be(0);
    }

    [Test]
    public async Task RewardDialogAdvertisesOnlyFilledSlotsAndUnsupportedTypesAreNotOffered()
    {
        _resource.Type = 401; await Start();
        var dialog = await _service.GetQuestDialogAsync(_client, 3011, 1005, "@90301101");
        // ShowQuestInfo stops at the first empty optional reward: slot 5 alone offers no choice, and REWARD carries the code.
        dialog.Menu.Select(m => m.Trigger).Should().Equal("end_quest( 1005, -1 )", "1005");
        _resource.Type = 999;
        (await _service.GetNpcOffersAsync(_client, 3011)).Should().BeEmpty();
    }

    [TestCase(201)]
    [TestCase(301)]
    [TestCase(302)]
    [TestCase(501)]
    [TestCase(601)]
    public async Task TutorialObjectivesFollowSkillsEquipmentJobLevelAndChaos(int type)
    {
        _resource.Type = type;
        _resource.Value1 = type switch { 201 => 1004, 301 => 0, 302 => 101001, 501 => 0, _ => 99 };
        _resource.Value2 = type == 601 ? 1 : type == 501 ? 6 : 2;
        _resource.Value3 = type == 601 ? 3 : 0;
        await Start(); (await Quest()).Progress.Should().Be(QuestRules.InProgress);
        var info = StorageTestHarness.Session(_client);
        info.LearnedSkills[1004] = 2; info.CharacterJobLevel = 6; info.CharacterChaos = 3;
        await using (var db = Db())
        {
            db.Items.Add(new ItemEntity { CharacterId = 1, ItemResourceId = 101001, Amount = 1,
                Level = 2, Enhance = 2, WearInfo = ItemWearType.Weapon });
            await db.SaveChangesAsync();
        }
        await _service.RefreshAsync(_client); (await Quest()).Progress.Should().Be(QuestRules.Finishable);
        await End(); EndResults().Last().Should().Be(ResultCode.Success);
    }

    [Test]
    public async Task RandomContractPersistsDistinctTargetsAndKeepsTheDrawAfterAbandonment()
    {
        _resource.Type = 901; _resource.LimitLevel = 10;
        _resource.Value1 = _resource.Value5 = _resource.Value9 = 101;
        _resource.Value2 = _resource.Value3 = _resource.Value6 = _resource.Value7 = _resource.Value10 = _resource.Value11 = 1;
        _resource.Value4 = _resource.Value8 = _resource.Value12 = 100;
        await Start(); var draw = (await Quest()).Value.ToArray();
        new[] { draw[0], draw[2], draw[4] }.Should().OnlyHaveUniqueItems();
        await _service.DropQuestAsync(_client, new GameActionPackets.DropQuestRequest(1005));
        await Start(); (await Quest()).Value.Should().Equal(draw);
        await Kill(draw[0]); await Kill(draw[2]); await Kill(draw[4]);
        (await Quest()).Progress.Should().Be(QuestRules.Finishable);
        await End(); EndResults().Last().Should().Be(ResultCode.Success);
        StorageTestHarness.Session(_client).CharacterGold.Should().Be(150, "three objectives each contribute one reward multiplier");
    }

    [Test]
    public async Task NegativePoolTargetCountsEveryMemberMonster()
    {
        _resource.Value1 = -10; await Start(); await Kill();
        (await Quest()).Status[0].Should().Be(1);
    }

    [TestCase("level")]
    [TestCase("max_level")]
    [TestCase("job_level")]
    [TestCase("race")]
    [TestCase("class")]
    [TestCase("depth")]
    [TestCase("job")]
    public async Task AcceptanceChecksTheImportedPlayerRestrictions(string restriction)
    {
        switch (restriction)
        {
            case "level": _resource.LimitLevel = 11; break;
            case "max_level": _resource.LimitMaxLevel = 9; break;
            case "job_level": _resource.LimitJobLevel = 6; break;
            case "race": _resource.LimitDeva = "0"; break;
            case "class": _resource.LimitFighter = "0"; break;
            case "depth": _resource.LimitJobDepth = 2; break;
            case "job": _resource.LimitJob = 201; break;
        }
        (await _service.GetNpcOffersAsync(_client, 3011)).Should().BeEmpty();
        await Start(); await using var db = Db(); (await db.CharacterQuests.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task QuestLimitAndOrPrerequisitesAreEnforced()
    {
        _resource.ForeQuest1 = 998; _resource.ForeQuest2 = 999; _resource.OrFlag = "1";
        await using (var db = Db()) { db.CharacterQuestCompletions.Add(new() { CharacterId = 1, Code = 999 }); await db.SaveChangesAsync(); }
        (await _service.GetNpcOffersAsync(_client, 3011)).Should().ContainSingle();
        _resource.OrFlag = "0"; (await _service.GetNpcOffersAsync(_client, 3011)).Should().BeEmpty();
        _resource.OrFlag = "1";
        await using (var db = Db())
        {
            db.CharacterQuests.AddRange(Enumerable.Range(5000, 20).Select(code => new CharacterQuestEntity { CharacterId = 1, Code = code }));
            await db.SaveChangesAsync();
        }
        await Start(); await using (var db = Db()) (await db.CharacterQuests.CountAsync(q => q.Code == 1005)).Should().Be(0);
    }

    [Test]
    public async Task HandInCreditsTheNpcFavorAndTakesTheHateGroup()
    {
        _resource.FavorGroupId = QuestRules.NpcFavorGroup; _resource.HateGroupId = 77; _resource.Favor = 5;
        await Start(); await Kill(); await Kill(); await End(5);

        EndResults().Last().Should().Be(ResultCode.Success);
        await using var db = Db();
        var favors = await db.CharacterFavors.OrderBy(f => f.FavorId).Select(f => new { f.FavorId, f.Value }).ToArrayAsync();
        favors.Should().BeEquivalentTo(new[] { new { FavorId = 77, Value = -5 }, new { FavorId = 3011, Value = 5 } },
            "group 999 is the NPC handing the quest in");
    }

    [Test]
    public async Task LimitFavorGatesTheStartOnTheNpcFavor()
    {
        _resource.LimitFavorGroupId = QuestRules.NpcFavorGroup; _resource.LimitFavor = 10;
        await Start();
        await using (var db = Db()) (await db.CharacterQuests.CountAsync()).Should().Be(0);

        await using (var db = Db())
        {
            db.CharacterFavors.Add(new CharacterFavorEntity { CharacterId = 1, FavorId = 3011, Value = 10 });
            await db.SaveChangesAsync();
        }
        await Start();
        (await Quest()).Code.Should().Be(1005);
    }

    [Test]
    public async Task HandInPassingTheCarriedGoldCeilingIsRefusedAndConsumesNothing()
    {
        await Start(); await Kill(); await Kill();
        StorageTestHarness.Session(_client).CharacterGold = GoldRules.MaxCarried - 10;

        await End(5);

        EndResults().Last().Should().Be(ResultCode.TooMuchMoney);
        _connection.Sent.Should().Contain(p => Encoding.ASCII.GetString(p).Contains("END|TOO_MUCH_MONEY|1005"));
        await using var db = Db();
        (await db.CharacterQuests.CountAsync()).Should().Be(1);
        (await db.Items.CountAsync()).Should().Be(0);
        StorageTestHarness.Session(_client).CharacterGold.Should().Be(GoldRules.MaxCarried - 10);
    }

    [Test, Explicit("Runs migrations and rollback checks in an isolated schema on the configured local PostgreSQL server.")]
    public async Task PostgreSqlMigrationsAndFailedRewardInsertRollBackTheWholeHandIn()
    {
        var settingsPath = Path.GetFullPath("../../../../DevConsole/appsettings.json", TestContext.CurrentContext.TestDirectory);
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        var options = settings.RootElement.GetProperty("Database").Deserialize<DatabaseOptions>()!;
        var schema = "quest_test_" + Guid.NewGuid().ToString("N");
        var arcadiaSchema = schema + "_arcadia";
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = options.DataSource, Port = options.Port, Username = options.User, Password = options.Password,
            Database = options.TelecasterCatalog, SearchPath = schema, Pooling = false
        };
        var testConnection = Environment.GetEnvironmentVariable("NAVIS_QUEST_TEST_CONNECTION");
        if (!string.IsNullOrEmpty(testConnection)) builder = new NpgsqlConnectionStringBuilder(testConnection) { SearchPath = schema, Pooling = false };
        var connectionString = builder.ConnectionString;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection)) await create.ExecuteNonQueryAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{arcadiaSchema}\"", connection)) await create.ExecuteNonQueryAsync();
        try
        {
            _service.Dispose();
            var arcadiaConnection = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = arcadiaSchema }.ConnectionString;
            var arcadiaOptions = new DbContextOptionsBuilder<ArcadiaContext>()
                .UseNpgsql(arcadiaConnection, o => o.MigrationsHistoryTable("__EFMigrationsHistory", arcadiaSchema)).Options;
            await using (var arcadia = new ArcadiaContext(arcadiaOptions))
            {
                // Seed an existing quest before the new gold migration, to check its backfill too.
                await arcadia.GetService<IMigrator>().MigrateAsync("20260930082125_AllowTwentyFiveEnhancePercentages");
                var required = arcadia.Model.FindEntityType(typeof(QuestResourceEntity))!.GetProperties()
                    .Where(p => !p.IsNullable && p.Name != "Gold").ToArray();
                var columns = string.Join(",", required.Select(p => $"\"{p.Name}\""));
                var values = string.Join(",", required.Select(p => p.Name == "Id" ? "1005" : p.ClrType == typeof(string) ? "'0'" : "0"));
                await arcadia.Database.ExecuteSqlRawAsync($"INSERT INTO \"QuestResources\" ({columns}) VALUES ({values})");
                await arcadia.Database.MigrateAsync();
                (await arcadia.RandomPoolResources.CountAsync()).Should().Be(1637);
                (await arcadia.QuestResources.SingleAsync()).Gold.Should().Be(300);
            }
            _options = new DbContextOptionsBuilder<TelecasterContext>()
                .UseNpgsql(connectionString, o => o.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options;
            await using (var db = Db())
            {
                await db.Database.MigrateAsync();
                (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(id => id.EndsWith("_QuestLifecycle"));
                var character = new CharacterEntity { Id = 1, CharacterName = "Questor" };
                foreach (var property in typeof(CharacterEntity).GetProperties().Where(p => p.CanWrite))
                {
                    if (property.PropertyType == typeof(string) && property.GetValue(character) is null) property.SetValue(character, "");
                    if (property.PropertyType.IsArray) property.SetValue(character, Array.CreateInstance(property.PropertyType.GetElementType()!, 4));
                }
                db.Characters.Add(character); await db.SaveChangesAsync();
            }
            _resource.Type = 103; _resource.Value1 = 1000000; _resource.Value2 = 3;
            var catalogue = A.Fake<IQuestCatalogueRepository>();
            A.CallTo(() => catalogue.GetResources()).Returns(new[] { _resource });
            A.CallTo(() => catalogue.GetLinks()).Returns(new[] { new QuestLinkResourceEntity
                { NpcId = 3011, QuestId = 1005, FlagStart = "1", FlagEnd = "1", TextIdStart = 101 } });
            var characters = A.Fake<ICharacterService>();
            A.CallTo(() => characters.GetQuestsAsync("Questor")).ReturnsLazily(async () =>
                { await using var db = Db(); return await db.CharacterQuests.AsNoTracking().ToArrayAsync(); });
            _service = new QuestService(characters, catalogue, _options, new CharacterGate());
            await AddItem(1000000, 4); await Start();
            await using (var reject = new NpgsqlCommand("ALTER TABLE \"Items\" ADD CONSTRAINT reject_quest_reward CHECK (\"ItemResourceId\" <> 603001)", connection))
                await reject.ExecuteNonQueryAsync();
            await End(); EndResults().Last().Should().Be(ResultCode.DBError);
            await using (var db = Db())
            {
                (await db.Items.SingleAsync()).Amount.Should().Be(4);
                (await db.CharacterQuestCompletions.CountAsync()).Should().Be(0);
                (await db.CharacterQuests.CountAsync()).Should().Be(1);
                (await db.Characters.SingleAsync()).Gold.Should().Be(0);
            }
            await using (var allow = new NpgsqlCommand("ALTER TABLE \"Items\" DROP CONSTRAINT reject_quest_reward", connection)) await allow.ExecuteNonQueryAsync();
            await End(); await End();
            EndResults().TakeLast(2).Should().Equal(ResultCode.Success, ResultCode.NotActable);
            await using (var db = Db())
            {
                (await db.Items.SingleAsync(i => i.ItemResourceId == 1000000)).Amount.Should().Be(1);
                (await db.Items.SingleAsync(i => i.ItemResourceId == 603001)).Amount.Should().Be(3);
                (await db.CharacterQuestCompletions.CountAsync()).Should().Be(1);
            }
        }
        finally
        {
            // schema is generated above with a fixed prefix and GUID, and contains only this test's rows.
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA \"{schema}\", \"{arcadiaSchema}\" CASCADE", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private async Task AddItem(int id, long amount)
    {
        await using var db = Db(); db.Items.Add(new ItemEntity { CharacterId = 1, ItemResourceId = id, Amount = amount,
            Level = 1, SocketItemIds = new long[4], WearInfo = ItemWearType.None }); await db.SaveChangesAsync();
    }
    private static byte[] Selection(string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text); var frame = new byte[9 + bytes.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(7), (ushort)bytes.Length); bytes.CopyTo(frame, 9); return frame;
    }
    private static async Task Until(Func<bool> predicate)
    {
        for (var i = 0; i < 100 && !predicate(); i++) await Task.Delay(10);
        predicate().Should().BeTrue();
    }
    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }
    private sealed class FailSave : SaveChangesInterceptor
    {
        public bool RejectCompletion { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (RejectCompletion && eventData.Context!.ChangeTracker.Entries<CharacterQuestCompletionEntity>().Any(e => e.State == EntityState.Added))
                throw new InvalidOperationException("Simulated commit failure");
            return ValueTask.FromResult(result);
        }
    }
}
