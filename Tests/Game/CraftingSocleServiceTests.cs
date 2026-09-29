using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Tests.Game;

/// <summary>
/// The conduct of the crafting socle, which no test covered until now: <c>CraftingSocleService.HandleAsync</c>
/// reads a <c>TM_CS_MIX</c> (256) frame, resolves its handles against the character's own items, walks the
/// <c>MixResource</c> table and answers. What is locked here is the service layer the matcher tests cannot
/// reach — the zero-target sentinel is never resolved, an unknown handle is <c>NotExist</c>, a failed read is
/// <c>DBError</c>, an item resource absent from the item table is <c>InvalidArgument</c> rather than judged on
/// a zeroed row, and the two journal cases are distinct and at distinct levels: « no rule accepts this frame »
/// at Debug and « resolves to mix rule N » at Warning (spec §8, L1b).
///
/// The frames are the ones of <c>Tests/Game/CraftingSoclePacketsTests.cs</c>, built the same way, so an
/// offset change breaks both files. See docs/packet-specs/socle-artisanat-ressources.md §8 (L1b) and §13.
/// </summary>
[TestFixture]
public class CraftingSocleServiceTests
{
    private const string Character = "Killian";
    private const int HeaderSize = 7;

    private const uint TargetHandle = 0x80000001u;
    private const uint MaterialHandle = 0x80000010u;
    private const uint SecondHandle = 0x80000011u;

    private const long TargetResource = 700201;
    private const long MaterialResource = 700204;

    private const int ItemGroupWanted = 3;

    private CapturingSink _sink;
    private ILogger _previousLogger;

    /// <summary>
    /// <c>CraftingSocleService</c> logs through the static Serilog logger, so the fixture swaps it for a sink
    /// that keeps the events of one test and puts it back afterwards.
    /// </summary>
    [SetUp]
    public void CaptureLogs()
    {
        _sink = new CapturingSink();
        _previousLogger = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(_sink).CreateLogger();
    }

    [TearDown]
    public void RestoreLogs()
    {
        Log.Logger = _previousLogger;
    }

    private sealed record Harness(CraftingSocleService Service, ICharacterService CharacterService,
        GameClient Client, StorageTestHarness.FrameConnection Connection);

    private sealed class FakeMixCatalog : IMixResourceCatalog
    {
        public FakeMixCatalog(IReadOnlyList<MixResourceEntity> rules)
        {
            Rules = rules;
        }

        public IReadOnlyList<MixResourceEntity> Rules { get; }

        public bool TryGetRule(long mixId, out MixResourceEntity rule)
        {
            rule = Rules.FirstOrDefault(candidate => candidate.Id == mixId)!;
            return rule is not null;
        }
    }

    private sealed class FakeItemCatalog : IItemMatchCatalog
    {
        private readonly IReadOnlyDictionary<long, ItemMatchFields> _fields;

        public FakeItemCatalog(IReadOnlyDictionary<long, ItemMatchFields> fields)
        {
            _fields = fields;
        }

        public bool TryGetFields(long resourceId, out ItemMatchFields fields)
        {
            return _fields.TryGetValue(resourceId, out fields);
        }
    }

    private static Harness Build(IReadOnlyList<MixResourceEntity> rules,
        IReadOnlyDictionary<uint, ItemEntity> items,
        IReadOnlyDictionary<long, ItemMatchFields> fields,
        ushort characterHandle = 42)
    {
        var characterService = A.Fake<ICharacterService>();
        A.CallTo(() => characterService.GetItemByHandleAsync(Character, A<uint>._))
            .ReturnsLazily((string _, uint handle) =>
                Task.FromResult(items.TryGetValue(handle, out var item) ? item : null));

        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var session = StorageTestHarness.Session(client);
        session.CharacterName = Character;
        session.CharacterHandle = characterHandle;

        var service = new CraftingSocleService(characterService, new FakeMixCatalog(rules),
            new FakeItemCatalog(fields));

        return new Harness(service, characterService, client, connection);
    }

    /// <summary>A recipe row whose conditions are written out by the test, pair by pair.</summary>
    private static MixResourceEntity Rule(long id, int subMaterialCount, int mixType = 101)
    {
        return new MixResourceEntity { Id = id, MixType = mixType, SubMaterialCount = subMaterialCount };
    }

    private static ItemEntity Item(long resourceId, long amount = 1, long level = 1, long enhance = 0,
        int flag = 0)
    {
        return new ItemEntity
        {
            Id = 1,
            ItemResourceId = resourceId,
            Amount = amount,
            Level = (uint)level,
            Enhance = (uint)enhance,
            Flag = (ItemFlag)flag
        };
    }

    private static ItemMatchFields Fields(long resourceId, int group = ItemGroupWanted, int rank = 4,
        int wear = 0)
    {
        return new ItemMatchFields((int)resourceId, (ItemGroup)group, (ItemType)0, rank, (ItemWearType)wear);
    }

    private static byte[] MixFrame(uint mainHandle, ushort mainCount,
        params (uint Handle, ushort Count)[] subItems)
    {
        var packet = new byte[HeaderSize + 8 + subItems.Length * 6];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)GamePackets.TM_CS_MIX);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), mainHandle);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(11, 2), mainCount);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(13, 2), (ushort)subItems.Length);

        for (var i = 0; i < subItems.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(15 + i * 6, 4), subItems[i].Handle);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(19 + i * 6, 2), subItems[i].Count);
        }

        return packet;
    }

    private static byte[] RepairFrame(params uint[] handles)
    {
        var packet = new byte[31];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), 31u);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2),
            (ushort)GamePackets.TM_CS_REPAIR_SOULSTONE);

        for (var i = 0; i < 6; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7 + i * 4, 4), i < handles.Length ? handles[i] : 0u);
        }

        return packet;
    }

    private static byte[] SoulstoneCraftFrame(uint craftItemHandle, params uint[] slots)
    {
        var packet = new byte[27];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), 27u);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2),
            (ushort)GamePackets.TM_CS_SOULSTONE_CRAFT);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7, 4), craftItemHandle);

        for (var i = 0; i < 4; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(11 + i * 4, 4), i < slots.Length ? slots[i] : 0u);
        }

        return packet;
    }

    private static TS_SC_RESULT ResultOf(byte[] packet)
    {
        return new Packet<TS_SC_RESULT>(packet).GetDataStruct<TS_SC_RESULT>();
    }

    private IReadOnlyList<string> RenderedMessages()
    {
        return _sink.Events.Select(logEvent => logEvent.RenderMessage()).ToList();
    }

    /// <summary>Every event of one test as (level, rendered message), so a level can be asserted on.</summary>
    private IReadOnlyList<(LogEventLevel Level, string Message)> Logged()
    {
        return _sink.Events.Select(logEvent => (logEvent.Level, logEvent.RenderMessage())).ToList();
    }

    /// <summary>The rule of the fixtures: target of group 3, one material of item code 700204.</summary>
    private static MixResourceEntity MaterialRule(long id = 1154)
    {
        var rule = Rule(id, 1);
        rule.MainType01 = MixResourceMatcher.CheckItemGroup;
        rule.MainValue01 = ItemGroupWanted;
        rule.Sub01Type01 = MixResourceMatcher.CheckItemId;
        rule.Sub01Value01 = (int)MaterialResource;
        return rule;
    }

    private static Harness MixHarness(IReadOnlyList<MixResourceEntity> rules)
    {
        return Build(rules,
            new Dictionary<uint, ItemEntity>
            {
                [TargetHandle] = Item(TargetResource),
                [MaterialHandle] = Item(MaterialResource, amount: 2)
            },
            new Dictionary<long, ItemMatchFields>
            {
                [TargetResource] = Fields(TargetResource),
                [MaterialResource] = Fields(MaterialResource)
            });
    }

    // ------------------------------------------------------------------ the resolution of 256

    [Test]
    public async Task Mix_ResolvesTheRuleAndRefusesWithInvalidArgument()
    {
        var harness = MixHarness(new[] { MaterialRule() });

        await harness.Service.HandleAsync(harness.Client, (ushort)GamePackets.TM_CS_MIX,
            MixFrame(TargetHandle, 1, (MaterialHandle, 1)));

        harness.Connection.Sent.Should().ContainSingle("the socle answers the frame it just read");
        var result = ResultOf(harness.Connection.Sent[0]);
        result.RequestMsgID.Should().Be(256);
        result.Result.Should().Be((ushort)ResultCode.InvalidArgument,
            "a resolved mix still has no effects: the answer is the refusal the socle already sends");
        result.Value.Should().Be(0, "the reference sends no value on this refusal");
    }

    [Test]
    public async Task Mix_LogsTheResolvedRuleAtWarning()
    {
        var harness = MixHarness(new[] { MaterialRule(1154) });

        await harness.Service.HandleAsync(harness.Client, (ushort)GamePackets.TM_CS_MIX,
            MixFrame(TargetHandle, 1, (MaterialHandle, 1)));

        RenderedMessages().Should().Contain(message => message.Contains("resolves to mix rule 1154"),
            "the rule that was resolved is the fact the next lobe needs");
        Logged().Should().Contain(entry =>
            entry.Level == LogEventLevel.Warning && entry.Message.Contains("resolves to mix rule 1154"),
            "the resolved rule is the case worth a Warning");
    }

    [Test]
    public async Task Mix_LogsAFrameNoRuleAcceptsAtDebug()
    {
        // Two distinct cases in the journal, at two distinct levels (spec §8, L1b): a frame no rule accepts is
        // a routine event of a player experimenting in the combination window, so it must not raise a warning
        // for every keystroke, while the resolved rule is the one worth a warning. Both get the same refusal.
        var rule = Rule(1154, 1);
        rule.MainType01 = MixResourceMatcher.CheckItemGroup;
        rule.MainValue01 = ItemGroupWanted;
        rule.Sub01Type01 = MixResourceMatcher.CheckItemId;
        rule.Sub01Value01 = 700999;
        var harness = MixHarness(new[] { rule });

        await harness.Service.HandleAsync(harness.Client, (ushort)GamePackets.TM_CS_MIX,
            MixFrame(TargetHandle, 1, (MaterialHandle, 1)));

        harness.Connection.Sent.Should().ContainSingle();
        ResultOf(harness.Connection.Sent[0]).Result.Should().Be((ushort)ResultCode.InvalidArgument);
        Logged().Should().Contain(entry =>
            entry.Level == LogEventLevel.Debug && entry.Message.Contains("is not accepted by any of the 1 mix rules"),
            "an unmatched frame is answered at Debug");
        Logged().Should().NotContain(entry =>
            entry.Level == LogEventLevel.Warning && entry.Message.Contains("is not accepted"),
            "an unmatched frame is not an anomaly");
    }

    [Test]
    public async Task Mix_DoesNotResolveTheZeroTargetSentinel()
    {
        // handle 0 is an empty target slot, not an item (WorldSession.cpp:1451): a row that asks nothing of its
        // target is the only one that can accept such a frame, and the inventory must never be asked for 0.
        var rule = Rule(601, 1);
        rule.Sub01Type01 = MixResourceMatcher.CheckItemId;
        rule.Sub01Value01 = (int)MaterialResource;
        var harness = MixHarness(new[] { rule });

        await harness.Service.HandleAsync(harness.Client, (ushort)GamePackets.TM_CS_MIX,
            MixFrame(0u, 0, (MaterialHandle, 1)));

        harness.Connection.Sent.Should().ContainSingle();
        ResultOf(harness.Connection.Sent[0]).Result.Should().Be((ushort)ResultCode.InvalidArgument);
        RenderedMessages().Should().Contain(message => message.Contains("resolves to mix rule 601"));
        A.CallTo(() => harness.CharacterService.GetItemByHandleAsync(A<string>._, 0u)).MustNotHaveHappened();
    }

    [Test]
    public async Task Mix_RefusesAHandleThatIsNotOneOfTheCharactersItems()
    {
        var harness = MixHarness(new[] { MaterialRule() });

        await harness.Service.HandleAsync(harness.Client, (ushort)GamePackets.TM_CS_MIX,
            MixFrame(TargetHandle, 1, (SecondHandle, 1)));

        harness.Connection.Sent.Should().ContainSingle();
        var result = ResultOf(harness.Connection.Sent[0]);
        result.RequestMsgID.Should().Be(256);
        result.Result.Should().Be((ushort)ResultCode.NotExist);
        result.Value.Should().Be(unchecked((int)SecondHandle),
            "the handle that resolved to no item is copied into the result, as the 203 drop path does");
        RenderedMessages().Should().NotContain(message => message.Contains("resolves to mix rule"),
            "the frame never reached the rule table");
    }

    [Test]
    public async Task Mix_RefusesAMaterialWhoseResourceIsNotInTheItemTable()
    {
        // Judging conditions on a zeroed row would accept a recipe the client never meant, so an item resource
        // the catalog does not know is a refusal of its own (IItemMatchCatalog).
        var harness = Build(new[] { MaterialRule() },
            new Dictionary<uint, ItemEntity>
            {
                [TargetHandle] = Item(TargetResource),
                [MaterialHandle] = Item(MaterialResource)
            },
            new Dictionary<long, ItemMatchFields> { [TargetResource] = Fields(TargetResource) });

        await harness.Service.HandleAsync(harness.Client, (ushort)GamePackets.TM_CS_MIX,
            MixFrame(TargetHandle, 1, (MaterialHandle, 1)));

        harness.Connection.Sent.Should().ContainSingle();
        ResultOf(harness.Connection.Sent[0]).Result.Should().Be((ushort)ResultCode.InvalidArgument);
        RenderedMessages().Should().Contain(message => message.Contains("is not in ItemResource"));
        RenderedMessages().Should().NotContain(message => message.Contains("resolves to mix rule"));
    }

    [Test]
    public async Task Mix_AnswersDatabaseErrorWhenTheInventoryReadFails()
    {
        var harness = MixHarness(new[] { MaterialRule() });
        A.CallTo(() => harness.CharacterService.GetItemByHandleAsync(Character, MaterialHandle))
            .ThrowsAsync(new InvalidOperationException("the item table is gone"));

        await harness.Service.HandleAsync(harness.Client, (ushort)GamePackets.TM_CS_MIX,
            MixFrame(TargetHandle, 1, (MaterialHandle, 1)));

        harness.Connection.Sent.Should().ContainSingle();
        var result = ResultOf(harness.Connection.Sent[0]);
        result.Result.Should().Be((ushort)ResultCode.DBError);
        result.Value.Should().Be(unchecked((int)MaterialHandle));
    }

    [Test]
    public async Task Mix_RefusesAMalformedFrameWithoutReadingTheInventory()
    {
        var harness = MixHarness(new[] { MaterialRule() });

        await harness.Service.HandleAsync(harness.Client, (ushort)GamePackets.TM_CS_MIX, new byte[14]);

        harness.Connection.Sent.Should().ContainSingle();
        ResultOf(harness.Connection.Sent[0]).Result.Should().Be((ushort)ResultCode.InvalidArgument);
        // A frame that is not 15 + 6N bytes is refused before anything is resolved.
        A.CallTo(() => harness.CharacterService.GetItemByHandleAsync(A<string>._, A<uint>._))
            .MustNotHaveHappened();
    }

    // ------------------------------------------------------- the rest of the family and the guard

    [Test]
    public async Task Socle_DropsEveryFrameOfAConnectionOutsideTheWorld()
    {
        var harness = Build(new[] { MaterialRule() },
            new Dictionary<uint, ItemEntity> { [MaterialHandle] = Item(MaterialResource) },
            new Dictionary<long, ItemMatchFields> { [MaterialResource] = Fields(MaterialResource) },
            characterHandle: 0);

        await harness.Service.HandleAsync(harness.Client, (ushort)GamePackets.TM_CS_MIX,
            MixFrame(TargetHandle, 1, (MaterialHandle, 1)));
        await harness.Service.HandleAsync(harness.Client, (ushort)GamePackets.TM_CS_REPAIR_SOULSTONE,
            RepairFrame(MaterialHandle));

        harness.Connection.Sent.Should().BeEmpty("there is no inventory to resolve against and no handle to name");
        A.CallTo(() => harness.CharacterService.GetItemByHandleAsync(A<string>._, A<uint>._))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task Repair_ResolvesTheSixHandlesAndRefusesTheUnknownOne()
    {
        var harness = MixHarness(Array.Empty<MixResourceEntity>());

        await harness.Service.HandleAsync(harness.Client, (ushort)GamePackets.TM_CS_REPAIR_SOULSTONE,
            RepairFrame(MaterialHandle, 0u, SecondHandle, 0u, 0u, 0u));

        harness.Connection.Sent.Should().ContainSingle();
        var result = ResultOf(harness.Connection.Sent[0]);
        result.RequestMsgID.Should().Be(262);
        result.Result.Should().Be((ushort)ResultCode.NotExist);
        result.Value.Should().Be(unchecked((int)SecondHandle));
        A.CallTo(() => harness.CharacterService.GetItemByHandleAsync(A<string>._, 0u)).MustNotHaveHappened();
    }

    [Test]
    public async Task SoulstoneCraft_KeepsTheEmptySlotsOutOfTheResolution()
    {
        var harness = MixHarness(Array.Empty<MixResourceEntity>());

        await harness.Service.HandleAsync(harness.Client, (ushort)GamePackets.TM_CS_SOULSTONE_CRAFT,
            SoulstoneCraftFrame(TargetHandle, MaterialHandle, 0u, 0u, 0u));

        harness.Connection.Sent.Should().ContainSingle();
        var result = ResultOf(harness.Connection.Sent[0]);
        result.RequestMsgID.Should().Be(260);
        result.Result.Should().Be((ushort)ResultCode.InvalidArgument,
            "the frame is well formed and every handle it names exists, but the crafting engine is not written");
        A.CallTo(() => harness.CharacterService.GetItemByHandleAsync(A<string>._, A<uint>._))
            .MustHaveHappened(2, Times.Exactly);
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent)
        {
            Events.Add(logEvent);
        }
    }
}
