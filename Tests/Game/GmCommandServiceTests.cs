using System.Buffers.Binary;
using System.Text;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
using Navislamia.Game.Services.GmCommands;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

[TestFixture]
public class GmCommandServiceTests
{
    private const uint CharacterHandle = 0x80000001;
    private const long MonsterInstanceId = 0;
    private const uint MonsterHandle = 0x40000001;

    private IWarpService _warp = null!;
    private ICombatService _combat = null!;
    private ILevelingService _leveling = null!;
    private IStatService _stats = null!;
    private ICharacterService _characters = null!;
    private IItemSortCatalog _items = null!;
    private MonsterWorldState _monsters = null!;
    private GmCommandService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _warp = A.Fake<IWarpService>();
        _combat = A.Fake<ICombatService>();
        _leveling = A.Fake<ILevelingService>();
        _stats = A.Fake<IStatService>();
        _characters = A.Fake<ICharacterService>();
        _items = A.Fake<IItemSortCatalog>();
        _monsters = BuildMonsters(hp: 500);
        _service = new GmCommandService(_warp, _combat, _leveling, _stats, _characters, _items, _monsters);
    }

    [Test]
    public async Task Position_AnswersAnyPlayerOnTheSystemLine()
    {
        var (client, connection) = NewClient(permission: 0);
        var info = StorageTestHarness.Session(client);
        info.X = 94454.5f;
        info.Y = 126040f;

        await _service.HandleAsync(client, "/position", Array.Empty<GameClient>());

        var reply = Replies(connection).Should().ContainSingle().Subject;
        reply.Sender.Should().Be(GmCommandService.SystemSender);
        reply.Type.Should().Be((byte)ChatType.System);
        reply.Text.Should().StartWith("X: 94454.5 Y: 126040");
    }

    [Test]
    public async Task PrivilegedCommand_WithoutPermission_LooksUnknownAndDoesNothing()
    {
        var (client, connection) = NewClient(permission: 0);

        await _service.HandleAsync(client, "/warp 100 200", Array.Empty<GameClient>());

        A.CallTo(() => _warp.Warp(A<GameClient>._, A<float>._, A<float>._)).MustNotHaveHappened();
        Replies(connection).Single().Text.Should().Be("Unknown command: /warp. Type /help.");
    }

    [Test]
    public async Task UnknownCommand_IsAnsweredAndNotRun()
    {
        var (client, connection) = NewClient(permission: GmCommandRules.GmPermission);

        await _service.HandleAsync(client, "/run warp(1, 2)", Array.Empty<GameClient>());

        Replies(connection).Single().Text.Should().Be("Unknown command: /run. Type /help.");
    }

    [Test]
    public async Task Command_OutsideTheWorld_IsIgnored()
    {
        var (client, connection) = NewClient(permission: GmCommandRules.GmPermission);
        StorageTestHarness.Session(client).CharacterHandle = 0;

        await _service.HandleAsync(client, "/position", Array.Empty<GameClient>());

        connection.Sent.Should().BeEmpty();
    }

    [Test]
    public async Task Warp_GoesThroughTheWarpService()
    {
        var (client, _) = NewClient(permission: GmCommandRules.GmPermission);

        await _service.HandleAsync(client, "/warp 94454 126040", Array.Empty<GameClient>());

        A.CallTo(() => _warp.Warp(client, 94454f, 126040f)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task Warp_WithBadArguments_AnswersTheUsage()
    {
        var (client, connection) = NewClient(permission: GmCommandRules.GmPermission);

        await _service.HandleAsync(client, "/warp here", Array.Empty<GameClient>());

        A.CallTo(() => _warp.Warp(A<GameClient>._, A<float>._, A<float>._)).MustNotHaveHappened();
        Replies(connection).Single().Text.Should().Be("Usage: /warp <x> <y>");
    }

    [Test]
    public async Task Sitdown_StopsTheAttackAndPublishesTheSitBitWithThePkBit()
    {
        var (client, connection) = NewClient(permission: 0);
        StorageTestHarness.Session(client).PkMode = true;

        await _service.HandleAsync(client, "/sitdown", Array.Empty<GameClient>());

        A.CallTo(() => _combat.StopAttack(client)).MustHaveHappenedOnceExactly();
        var status = StatusChanges(connection).Should().ContainSingle().Subject;
        status.Handle.Should().Be(CharacterHandle);
        status.Status.Should().Be(CreatureStatus.PlayerSitdown | CreatureStatus.PlayerPkOn);
        StorageTestHarness.Session(client).IsSitting.Should().BeTrue();
    }

    [Test]
    public async Task Sitdown_WhileDead_IsRefused()
    {
        var (client, connection) = NewClient(permission: 0);
        StorageTestHarness.Session(client).CharacterHp = 0;

        await _service.HandleAsync(client, "/sitdown", Array.Empty<GameClient>());

        StatusChanges(connection).Should().BeEmpty();
        Replies(connection).Single().Text.Should().Contain("dead");
    }

    [Test]
    public async Task Standup_ClearsTheSitBit()
    {
        var (client, connection) = NewClient(permission: 0);
        StorageTestHarness.Session(client).IsSitting = true;

        await _service.HandleAsync(client, "/standup", Array.Empty<GameClient>());

        StatusChanges(connection).Single().Status.Should().Be(0u);
    }

    [Test]
    public async Task Walk_TogglesWithoutArgument()
    {
        var (client, connection) = NewClient(permission: 0);

        await _service.HandleAsync(client, "/walk", Array.Empty<GameClient>());
        await _service.HandleAsync(client, "/walk", Array.Empty<GameClient>());

        StatusChanges(connection).Select(change => change.Status).Should()
            .Equal(CreatureStatus.PlayerWalking, 0u);
    }

    [Test]
    public async Task Battle_DefaultsToOn()
    {
        var (client, connection) = NewClient(permission: 0);

        await _service.HandleAsync(client, "/battle", Array.Empty<GameClient>());

        StatusChanges(connection).Single().Status.Should().Be(CreatureStatus.BattleMode);
    }

    [Test]
    public async Task Item_WithAnUnknownCode_IsRefusedBeforeTheDatabase()
    {
        var (client, connection) = NewClient(permission: GmCommandRules.GmPermission);
        A.CallTo(() => _items.Contains(999)).Returns(false);

        await _service.HandleAsync(client, "/item 999", Array.Empty<GameClient>());

        A.CallTo(() => _characters.AddItemAsync(A<string>._, A<int>._, A<long>._)).MustNotHaveHappened();
        Replies(connection).Single().Text.Should().Be("Unknown item code 999.");
    }

    [Test]
    public async Task Item_AddsTheStackAndSendsItsInventoryRecord()
    {
        var (client, connection) = NewClient(permission: GmCommandRules.GmPermission);
        A.CallTo(() => _items.Contains(101221)).Returns(true);
        A.CallTo(() => _characters.AddItemAsync("Tester", 101221, 3))
            .Returns(new ItemEntity { Id = 42, ItemResourceId = 101221, Amount = 3, Idx = 7 });

        await _service.HandleAsync(client, "/item 101221 3", Array.Empty<GameClient>());

        connection.Sent.Should().Contain(packet => Id(packet) == (ushort)GamePackets.TM_SC_INVENTORY);
        Replies(connection).Single().Text.Should().Be("Item 101221 x3 added.");
    }

    [Test]
    public async Task Gold_AddsAndSendsTheGoldUpdate()
    {
        var (client, connection) = NewClient(permission: GmCommandRules.GmPermission);
        StorageTestHarness.Session(client).CharacterGold = 1_000;

        await _service.HandleAsync(client, "/gold 250", Array.Empty<GameClient>());

        StorageTestHarness.Session(client).CharacterGold.Should().Be(1_250);
        var update = connection.Sent.Single(packet => Id(packet) == (ushort)GamePackets.TM_SC_GOLD_UPDATE);
        BinaryPrimitives.ReadInt64LittleEndian(update.AsSpan(7, 8)).Should().Be(1_250);
    }

    [Test]
    public async Task Level_RaisesTheExperienceThenRunsTheOrdinaryLevelUp()
    {
        var (client, connection) = NewClient(permission: GmCommandRules.GmPermission);
        var info = StorageTestHarness.Session(client);
        info.CharacterLevel = 10;
        info.CharacterExp = 5_000;
        A.CallTo(() => _leveling.MaxLevel).Returns(300);
        long threshold = 90_000;
        A.CallTo(() => _leveling.TryGetExperienceFor(50, out threshold)).Returns(true)
            .AssignsOutAndRefParameters(threshold);

        await _service.HandleAsync(client, "/level 50", Array.Empty<GameClient>());

        info.CharacterExp.Should().Be(90_000);
        connection.Sent.Should().Contain(packet => Id(packet) == (ushort)GamePackets.TM_SC_EXP_UPDATE);
        A.CallTo(() => _leveling.ApplyExperience(client)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task Level_BelowTheCurrentOne_IsRefused()
    {
        var (client, connection) = NewClient(permission: GmCommandRules.GmPermission);
        StorageTestHarness.Session(client).CharacterLevel = 10;
        A.CallTo(() => _leveling.MaxLevel).Returns(300);

        await _service.HandleAsync(client, "/level 5", Array.Empty<GameClient>());

        A.CallTo(() => _leveling.ApplyExperience(A<GameClient>._)).MustNotHaveHappened();
        Replies(connection).Single().Text.Should().Contain("above 10");
    }

    [Test]
    public async Task Heal_RestoresTheComputedMaxima()
    {
        var (client, connection) = NewClient(permission: GmCommandRules.GmPermission);
        var info = StorageTestHarness.Session(client);
        info.CharacterHp = 10;
        A.CallTo(() => _stats.Compute(info))
            .Returns(new CharacterStatResult(new StatBlock { MaxHp = 800, MaxMp = 300 }, new StatBlock()));

        await _service.HandleAsync(client, "/heal", Array.Empty<GameClient>());

        info.CharacterHp.Should().Be(800);
        info.CharacterMp.Should().Be(300);
        Properties(connection).Should().Contain(("hp", 800)).And.Contain(("mp", 300));
    }

    [Test]
    public async Task Die_DropsHpToZeroAndStopsTheAttack()
    {
        var (client, connection) = NewClient(permission: GmCommandRules.GmPermission);

        await _service.HandleAsync(client, "/die", Array.Empty<GameClient>());

        StorageTestHarness.Session(client).CharacterHp.Should().Be(0);
        A.CallTo(() => _combat.StopAttack(client)).MustHaveHappenedOnceExactly();
        Properties(connection).Should().ContainSingle().Which.Should().Be(("hp", 0));
    }

    [Test]
    public async Task Doit_KillsEveryVisibleLivingMonsterThroughTheCombatPath()
    {
        var (client, connection) = NewClient(permission: GmCommandRules.GmPermission);
        var info = StorageTestHarness.Session(client);
        lock (info.MonsterVisibilityLock)
        {
            info.SpawnedMonsters[MonsterInstanceId] = MonsterHandle;
        }

        A.CallTo(() => _combat.ApplyDamage(client, MonsterInstanceId, MonsterHandle, 500)).Returns(0);

        await _service.HandleAsync(client, "/doit", Array.Empty<GameClient>());

        A.CallTo(() => _combat.ApplyDamage(client, MonsterInstanceId, MonsterHandle, 500))
            .MustHaveHappenedOnceExactly();
        var attack = connection.Sent.Single(packet => Id(packet) == (ushort)GamePackets.TM_SC_ATTACK_EVENT);
        BinaryPrimitives.ReadUInt32LittleEndian(attack.AsSpan(7, 4)).Should().Be(CharacterHandle);
        Replies(connection).Single().Text.Should().Be("1 monster(s) killed.");
    }

    [Test]
    public async Task Doit_SkipsACorpse()
    {
        var (client, connection) = NewClient(permission: GmCommandRules.GmPermission);
        var info = StorageTestHarness.Session(client);
        lock (info.MonsterVisibilityLock)
        {
            info.SpawnedMonsters[MonsterInstanceId] = MonsterHandle;
        }

        _monsters.ApplyDamage(MonsterInstanceId, 500);
        _monsters.Kill(MonsterInstanceId, DateTime.UtcNow.AddMinutes(1));

        await _service.HandleAsync(client, "/doit", Array.Empty<GameClient>());

        A.CallTo(() => _combat.ApplyDamage(A<GameClient>._, A<long>._, A<uint>._, A<int>._)).MustNotHaveHappened();
        Replies(connection).Single().Text.Should().Be("0 monster(s) killed.");
    }

    [Test]
    public async Task Notice_ReachesEveryPlayerInTheWorld()
    {
        var (gm, gmConnection) = NewClient(permission: GmCommandRules.GmPermission);
        var (other, otherConnection) = NewClient(permission: 0);
        var (lobby, lobbyConnection) = NewClient(permission: 0);
        StorageTestHarness.Session(lobby).CharacterHandle = 0;

        await _service.HandleAsync(gm, "/notice Server restart", new[] { gm, other, lobby });

        foreach (var connection in new[] { gmConnection, otherConnection })
        {
            var notice = Replies(connection).Single();
            notice.Type.Should().Be((byte)ChatType.Notice);
            notice.Text.Should().Be("Server restart");
        }

        lobbyConnection.Sent.Should().BeEmpty("a client still in the lobby has no chat window");
    }

    [Test]
    public async Task Help_ListsOnlyWhatTheCallerMayRun()
    {
        var (player, playerConnection) = NewClient(permission: 0);
        var (gm, gmConnection) = NewClient(permission: GmCommandRules.GmPermission);

        await _service.HandleAsync(player, "/help", Array.Empty<GameClient>());
        await _service.HandleAsync(gm, "/help", Array.Empty<GameClient>());

        Replies(playerConnection).Should().HaveCount(GmCommandCatalog.AvailableTo(0).Count());
        Replies(playerConnection).Should().NotContain(reply => reply.Text.StartsWith("/warp"));
        Replies(gmConnection).Should().HaveCount(GmCommandCatalog.All.Count);
    }

    [Test]
    public void ChatRequest_StartingWithASlash_GoesToTheCommandServiceAndIsNotEchoed()
    {
        var commands = A.Fake<IGmCommandService>();
        var frame = ChatRequest((byte)ChatType.Normal, "/position");
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection, gmCommandService: commands);

        client.OnDataReceived(frame.Length);

        A.CallTo(() => commands.HandleAsync(client, A<string>.That.StartsWith("/position"),
            A<IEnumerable<GameClient>>._)).MustHaveHappenedOnceExactly();
        connection.Sent.Should().BeEmpty("a command is never relayed as chat");
    }

    [Test]
    public void ChatRequest_WithoutASlash_IsStillEchoed()
    {
        var commands = A.Fake<IGmCommandService>();
        var frame = ChatRequest((byte)ChatType.Normal, "hello");
        var connection = new StorageTestHarness.FrameConnection(frame);
        var client = StorageTestHarness.NewGameClient(connection, gmCommandService: commands);

        client.OnDataReceived(frame.Length);

        A.CallTo(() => commands.HandleAsync(A<GameClient>._, A<string>._, A<IEnumerable<GameClient>>._))
            .MustNotHaveHappened();
        connection.Sent.Should().ContainSingle();
    }

    private static MonsterWorldState BuildMonsters(int hp)
    {
        var options = new MonsterSpawnOptions
        {
            Spawns = { new MonsterSpawnPoint { MonsterId = 2101, X = 1000, Y = 2000, Count = 1, Radius = 0 } }
        };
        var repository = A.Fake<IMonsterResourceRepository>();
        A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._))
            .Returns(new[] { new MonsterResourceEntity { Id = 2101, Level = 5, Hp = hp, Race = 1 } });

        return new MonsterWorldState(repository, Options.Create(options));
    }

    private static (GameClient Client, StorageTestHarness.FrameConnection Connection) NewClient(int permission)
    {
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = CharacterHandle;
        info.CharacterName = "Tester";
        info.CharacterPermission = permission;
        info.CharacterHp = 100;
        info.CharacterMaxHp = 100;
        return (client, connection);
    }

    /// <summary>A <c>TM_CS_CHAT_REQUEST</c> as the receive loop reads it: 22 unread bytes, count, type, text.</summary>
    private static byte[] ChatRequest(byte type, string text)
    {
        var message = Encoding.ASCII.GetBytes(text + "\0");
        var frame = new byte[7 + 22 + 2 + message.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)frame.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), (ushort)GamePackets.TM_CS_CHAT_REQUEST);
        frame[6] = StorageTestHarness.Checksum(frame);
        frame[29] = (byte)message.Length;
        frame[30] = type;
        message.CopyTo(frame, 31);
        return frame;
    }

    private static ushort Id(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));

    private static List<(string Sender, byte Type, string Text)> Replies(StorageTestHarness.FrameConnection connection) =>
        connection.Sent.Where(packet => Id(packet) == (ushort)GamePackets.TM_SC_CHAT)
            .Select(packet =>
            {
                var sender = Encoding.ASCII.GetString(packet, 7, 21).TrimEnd('\0');
                var count = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(28, 2));
                var text = Encoding.ASCII.GetString(packet, 31, count - 1);
                return (sender, packet[30], text);
            })
            .ToList();

    private static List<(uint Handle, uint Status)> StatusChanges(StorageTestHarness.FrameConnection connection) =>
        connection.Sent.Where(packet => Id(packet) == (ushort)GamePackets.TM_SC_STATUS_CHANGE)
            .Select(packet => (BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(11, 4))))
            .ToList();

    private static List<(string Name, long Value)> Properties(StorageTestHarness.FrameConnection connection) =>
        connection.Sent.Where(packet => Id(packet) == (ushort)GamePackets.TM_SC_PROPERTY)
            .Select(packet => (Encoding.ASCII.GetString(packet, 12, 16).TrimEnd('\0'),
                BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(28, 8))))
            .ToList();
}
