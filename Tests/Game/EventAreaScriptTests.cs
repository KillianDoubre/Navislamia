using System.Buffers.Binary;
using FakeItEasy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Maps;
using Navislamia.Game.Maps.Entities;
using Navislamia.Game.Maps.X2D;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Auction;
using Navislamia.Game.Services.Casting;

namespace Tests.Game;

[TestFixture]
public class EventAreaScriptTests
{
    [Test]
    public void Nfe_loader_keeps_every_polygon_of_the_same_event_id_and_checks_indices()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var writer = new BinaryWriter(File.OpenWrite(path)))
            {
                writer.Write(1); writer.Write(7); writer.Write(2);
                foreach (var offset in new[] { 0,1000 })
                {
                    writer.Write(4);
                    foreach (var (x,y) in new[] { (0,0),(100,0),(100,100),(0,100) })
                    { writer.Write(x + offset); writer.Write(y + offset); }
                }
            }
            var map = new MapService(Microsoft.Extensions.Options.Options.Create(new Navislamia.Configuration.Options.MapOptions { Width = 2000, Height = 2000 }),
                A.Fake<Microsoft.Extensions.Logging.ILogger<MapService>>(), A.Fake<Navislamia.Game.Scripting.IScriptService>());
            typeof(MapService).GetMethod("LoadEventAreaFile", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(map, new object[] { path,0,0,1f,0f });
            map.GetEventAreas().Should().HaveCount(2);
            map.TryGetEventArea(7, 0, out var first).Should().BeTrue(); first.Area.IsIncluded(50,50).Should().BeTrue();
            map.TryGetEventArea(7, 1, out var second).Should().BeTrue(); second.Area.IsIncluded(1050,1050).Should().BeTrue();
            map.TryGetEventArea(7, -1, out _).Should().BeFalse(); map.TryGetEventArea(7, 2, out _).Should().BeFalse();
        }
        finally { File.Delete(path); }
    }

    [Test]
    public async Task Overlapping_areas_leave_independently_and_logout_blocks_new_scripts_until_world_entry()
    {
        var area = new EventAreaInfo(1, new[] { new PointF(0,0),new PointF(100,0),new PointF(100,100),new PointF(0,100) });
        var second = new EventAreaInfo(2, new[] { new PointF(50,0),new PointF(150,0),new PointF(150,100),new PointF(50,100) });
        var map = A.Fake<IMapService>(); A.CallTo(() => map.GetEventAreas()).Returns(new[] { area,second });
        var calls = new List<(long,bool)>(); var scripts = A.Fake<INpcScriptService>();
        A.CallTo(() => scripts.RunEventAreaAsync(A<Navislamia.Game.Network.Clients.GameClient>._, A<EventAreaResourceEntity>._, A<bool>._))
            .ReturnsLazily((Navislamia.Game.Network.Clients.GameClient c, EventAreaResourceEntity a, bool enter) => { calls.Add((a.Id,enter)); return Task.FromResult(true); });
        var service = new EventAreaService(map, new EventAreaCatalog(new[] { Row(1,""),Row(2,"") }), scripts);
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client); info.CharacterHandle = 1; info.CharacterName = "Zones"; info.X = 75; info.Y = 50;
        service.EnterWorld(client); await service.FlushAsync(client); calls.Should().Equal((1L,true),(2L,true));
        info.X = 125; service.Refresh(client); await service.FlushAsync(client); calls.Last().Should().Be((1L,false));
        await service.LeaveWorldAsync(client); calls.Last().Should().Be((2L,false));
        service.Refresh(client).Should().BeFalse(); calls.Should().HaveCount(4);
        service.EnterWorld(client); await service.FlushAsync(client); calls.Last().Should().Be((2L,true));
    }
    private static DbContextOptions<TelecasterContext> Options() => new DbContextOptionsBuilder<TelecasterContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString(), o => o.EnableNullChecks(false)).Options;
    private static EventAreaResourceEntity Row(int id, string enter, string leave = "") => new() {
        Id = id, EnterHandler = enter, LeaveHandler = leave, MinLevel = 1, MaxLevel = 300 };
    private static NpcScriptService Script(DbContextOptions<TelecasterContext> options, CharacterGate gate,
        IQuestService quests = null, ICastInterrupts states = null, IEventAreaWorldEffects world = null) => new(new NpcScriptCatalog(),
        new CharacterRepositoryFactory(options), gate, A.Fake<IItemMatchCatalog>(), A.Fake<IAuctionCatalog>(),
        quests: quests, states: states, worldEffects: world);

    [Test]
    public void Event_functions_compile_in_an_isolated_hard_sandbox()
    {
        var sandbox = new NpcScriptCatalog().Create(true);
        sandbox.Globals.Get("mainquest2_region_espoir_level_10101").Type.Should().Be(MoonSharp.Interpreter.DataType.Function);
        sandbox.Globals.Get("io").IsNil().Should().BeTrue(); sandbox.Globals.Get("os").IsNil().Should().BeTrue();
    }

    [Test]
    public async Task Server_geometry_runs_ordered_enter_leave_once_and_rejects_forged_claims()
    {
        var options = Options(); var gate = new CharacterGate();
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client); info.CharacterHandle = 1; info.CharacterName = "Zones"; info.CharacterLevel = 20; info.CharacterHp = 100;
        await using (var db = new TelecasterContext(options))
        { db.Characters.Add(new CharacterEntity { Id = 1, CharacterName = "Zones" }); await db.SaveChangesAsync(); }
        var area = new EventAreaInfo(7, new[] { new PointF(0,0), new PointF(100,0), new PointF(100,100), new PointF(0,100) });
        var map = A.Fake<IMapService>(); A.CallTo(() => map.GetEventAreas()).Returns(new[] { area });
        EventAreaInfo ignored;
        A.CallTo(() => map.TryGetEventArea(7, out ignored)).Returns(true).AssignsOutAndRefParameters(area);
        A.CallTo(() => map.TryGetEventArea(7, 0, out ignored)).Returns(true).AssignsOutAndRefParameters(area);
        var row = Row(7, "set_flag('order', 'enter')", "set_flag('order', get_flag('order') .. '_leave')"); row.CountLimit = 1;
        var service = new EventAreaService(map, new EventAreaCatalog(new[] { row }), Script(options, gate));
        var claim = GameGuildPacket(15, 7);
        info.X = 500; info.Y = 500; service.HandlePacket(client, claim, true).Should().BeFalse();
        info.X = 50; info.Y = 50; service.Refresh(client).Should().BeTrue();
        service.HandlePacket(client, claim, true).Should().BeFalse();
        await service.FlushAsync(client);
        info.X = 500; service.Refresh(client).Should().BeTrue(); await service.FlushAsync(client);
        await using (var db = new TelecasterContext(options))
        { var character = await db.Characters.SingleAsync(); character.FlagList.Should().Contain("order:enter_leave"); character.FlagList.Should().Contain("event_area_7_count:1"); }
        // Activation counter survives a new service and rejects another entry; no leave for a refused entry.
        service = new EventAreaService(map, new EventAreaCatalog(new[] { row }), Script(options, gate));
        info.X = 50; service.Refresh(client); await service.FlushAsync(client); await service.LeaveWorldAsync(client);
        await using var check = new TelecasterContext(options); (await check.Characters.SingleAsync()).FlagList.Should().Contain("order:enter_leave");
    }

    [Test]
    public async Task Official_quest_objective_and_activation_count_commit_together_and_failure_consumes_neither()
    {
        var options = Options(); var gate = new CharacterGate();
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client); info.CharacterName = "Zones"; info.CharacterHandle = 1; info.CharacterLevel = 20; info.CharacterHp = 100;
        await using (var db = new TelecasterContext(options))
        {
            db.Characters.Add(new CharacterEntity { Id = 1, CharacterName = "Zones" });
            db.CharacterQuests.Add(new CharacterQuestEntity { CharacterId = 1, Code = 3217,
                Value = new int[6], Status = new int[6], Progress = QuestRules.InProgress });
            await db.SaveChangesAsync();
        }
        var resource = new QuestResourceEntity { Id = 3217, Type = 701, Value2 = 1 };
        var catalog = A.Fake<IQuestCatalogueRepository>(); A.CallTo(() => catalog.GetResources()).Returns(new[] { resource });
        using var quests = new QuestService(A.Fake<ICharacterService>(), catalog, options, gate);
        var script = Script(options, gate, quests);
        var row = Row(10101, "mainquest2_region_espoir_level_10101()"); row.CountLimit = 1;
        (await script.RunEventAreaAsync(client, row, true)).Should().BeTrue();
        await using (var db = new TelecasterContext(options))
        {
            var quest = await db.CharacterQuests.SingleAsync(); quest.Status[0].Should().Be(1); quest.Progress.Should().Be(QuestRules.Finishable);
            (await db.Characters.SingleAsync()).FlagList.Should().Contain("event_area_10101_count:1");
        }
        var failing = Row(9, "set_flag('never','saved'); set_quest_status(99999, 1, 1)"); failing.CountLimit = 1;
        (await script.RunEventAreaAsync(client, failing, true)).Should().BeFalse();
        await using var check = new TelecasterContext(options);
        (await check.Characters.SingleAsync()).FlagList.Should().NotContain(s => s.StartsWith("never:") || s.StartsWith("event_area_9_count:"));
    }

    [Test]
    public async Task Official_buff_and_spawn_callbacks_are_invoked_after_successful_script()
    {
        var options = Options(); var gate = new CharacterGate();
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client); info.CharacterName = "Zones"; info.CharacterHandle = 1; info.CharacterLevel = 20; info.CharacterHp = 100;
        await using (var db = new TelecasterContext(options))
        { db.Characters.Add(new CharacterEntity { Id = 1, CharacterName = "Zones" }); await db.SaveChangesAsync(); }
        var states = A.Fake<ICastInterrupts>(); var world = A.Fake<IEventAreaWorldEffects>();
        var script = Script(options, gate, states: states, world: world);
        (await script.RunEventAreaAsync(client, Row(20, "Quest_Link_18_1()"), true)).Should().BeTrue();
        A.CallTo(() => states.ApplyState(client, 1011, 3, 60000)).MustHaveHappenedOnceExactly();
        (await script.RunEventAreaAsync(client, Row(21, "add_npc(10,20,123008,2,60000); add_field_prop(1,28,10,20,0)"), true)).Should().BeTrue();
        A.CallTo(() => world.SpawnMonsters(123008, 2, 10, 20, 0, 60000)).MustHaveHappenedOnceExactly();
        A.CallTo(() => world.SpawnProp(1, 28, 10, 20, 0)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public void Activation_enforces_time_level_race_job_and_conditions()
    {
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
        var info = StorageTestHarness.Session(client); info.CharacterLevel = 20; info.CharacterHp = 100; info.CharacterRace = 3; info.CharacterJob = 101;
        var character = new CharacterEntity { Items = new List<ItemEntity>(), Skills = new List<CharacterSkillEntity>() };
        var row = Row(1, ""); row.RaceJobLimit = 1L | 1L << 4;
        var flags = new Dictionary<string,string>(); var now = DateTimeOffset.Now;
        EventAreaActivation.Allows(row, client, character, flags, now, null).Should().BeTrue();
        info.CharacterRace = 4; EventAreaActivation.Allows(row, client, character, flags, now, null).Should().BeFalse();
        info.CharacterRace = 3; info.CharacterJob = 102; EventAreaActivation.Allows(row, client, character, flags, now, null).Should().BeFalse();
        row.RaceJobLimit = 0; row.MinLevel = 21; EventAreaActivation.Allows(row, client, character, flags, now, null).Should().BeFalse();
        row.MinLevel = 1; row.Conditions = new[] { 1 }; row.Values = new[] { 7, 2 };
        EventAreaActivation.Allows(row, client, character, flags, now, null).Should().BeFalse();
        character.Items.Add(new ItemEntity { ItemResourceId = 7, Amount = 2 });
        EventAreaActivation.Allows(row, client, character, flags, now, null).Should().BeTrue();
        row.CountLimit = 1; flags[EventAreaActivation.CountKey(1)] = "1";
        EventAreaActivation.Allows(row, client, character, flags, now, null).Should().BeFalse();
    }
    private static byte[] GameGuildPacket(ushort id, int area)
    { var frame = Navislamia.Game.Network.Packets.Game.GameGuildPackets.Frame(id, 15); BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(7), area); return frame; }
}
