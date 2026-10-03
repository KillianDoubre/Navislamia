using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Huntaholic;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Party;

namespace Tests.Game;

/// <summary>HuntaHolic (Bear Road): rules, frames and the room/hunt flow (docs/packet-specs/socle-huntaholic.md).</summary>
[TestFixture]
public class HuntaholicTests
{
    private const int MonsterId = 9200003;
    private const int BossId = 9200099;

    private static HuntaholicRow Base() => new()
    {
        Id = 10000, NameId = 80010000, HuntingPeriodSeconds = 1500, ObjectivePoint = 3000, MaxPoint = 6000,
        LobbyX = 1800, LobbyY = 1800, LobbyArea = new HuntaholicArea { Left = 0, Top = 0, Right = 3600, Bottom = 3600 },
        DungeonX = 9000, DungeonY = 1800,
        DungeonArea = new HuntaholicArea { Left = 7200, Top = 0, Right = 10800, Bottom = 3600 },
        Tiers =
        {
            new HuntaholicTierRow
            {
                Id = 0, MinLevel = 20, MaxLevel = 35, PointAdvantage = 0.11, RewardExp = 1000, RewardJp = 1000,
                SuccessItemId = 3620020, SuccessItemCount = 1, FailItemId = 3620021, FailItemCount = 1,
                Respawns =
                {
                    new HuntaholicRespawnRow { Id = 1, Left = 8800, Top = 1600, Right = 9200, Bottom = 2000,
                        MonsterId = MonsterId, Count = 2, PeriodSeconds = 2, IsWandering = true },
                    new HuntaholicRespawnRow { Id = 2, Left = 8800, Top = 1600, Right = 9200, Bottom = 2000,
                        MonsterId = BossId, Count = 1, PeriodSeconds = 2, IsWandering = false }
                }
            },
            new HuntaholicTierRow { Id = 10, MinLevel = 0, MaxLevel = 15, PointAdvantage = 0.11 }
        }
    };

    private static IHuntaholicCatalog Catalog() =>
        new HuntaholicCatalog(Options.Create(new HuntaholicCatalogOptions { Huntaholics = { Base() } }));

    // ---- rules ----

    [Test]
    public void TheAreasAreTheChannelBoxesInRegionsWithTheOfficialPlusOne()
    {
        var catalog = Catalog();
        catalog.IsLobby(1800, 1800).Should().BeTrue();
        catalog.IsDungeon(9000, 1800).Should().BeTrue();
        catalog.IsLobby(9000, 1800).Should().BeFalse();
        catalog.GetHuntaholicId(3600 + 179, 100).Should().Be(10000, "the right region index is the box's plus one");
        catalog.GetHuntaholicId(50000, 50000).Should().Be(0);
    }

    [TestCase(10, 10)]
    [TestCase(20, 0)]
    [TestCase(34, 0)]
    [TestCase(35, 0xFF)]
    public void TheLobbyLayerIsTheFirstTierOfTheLevel(int level, int layer)
    {
        HuntaholicRules.ProperLobbyLayer(Base(), level).Should().Be((byte)layer);
    }

    [TestCase(1, 1)]
    [TestCase(2, 5)]
    [TestCase(3, 10)]
    [TestCase(4, 150)]
    [TestCase(0, 0)]
    public void AKillScoresByMonsterType(int type, int score) => HuntaholicRules.MonsterScore(type).Should().Be(score);

    [Test]
    public void TheGainIsTheCeilingOfTheAdvantageTimesTheRoomScore()
    {
        HuntaholicRules.GainPoint(0.11, 3001, true).Should().Be(331);
        HuntaholicRules.GainPoint(0.11, 3001, false).Should().Be(0);
        HuntaholicRules.GainPoint(0.13, 6000, true).Should().Be(780);
    }

    [Test]
    public void AQuitWithoutAResultIsJudgedLikeTheOfficialServer()
    {
        HuntaholicRules.ResolveResult(true, 5000, 3000, true).Should().Be(HuntingResult.FailedByDeath);
        HuntaholicRules.ResolveResult(false, 2999, 3000, true).Should().Be(HuntingResult.Retired);
        HuntaholicRules.ResolveResult(false, 3000, 3000, false).Should().Be(HuntingResult.Retired);
        HuntaholicRules.ResolveResult(false, 3000, 3000, true).Should().Be(HuntingResult.Success);
    }

    [Test]
    public void TheEntriesComeBackAtSixInTheMorning()
    {
        HuntaholicEntryRefill.RefillAfterLogout(new DateTime(2026, 10, 3, 5, 0, 0), new DateTime(2026, 10, 3, 5, 30, 0))
            .Should().Be(new DateTime(2026, 10, 3, 6, 0, 0));
        HuntaholicEntryRefill.RefillAfterLogout(new DateTime(2026, 10, 3, 7, 0, 0), new DateTime(2026, 10, 3, 8, 0, 0))
            .Should().Be(new DateTime(2026, 10, 4, 6, 0, 0));
        HuntaholicEntryRefill.RefillAfterLogout(null, DateTime.Now).Should().Be(DateTime.MinValue);
        HuntaholicEntryRefill.NextRefill(new DateTime(2026, 10, 3, 5, 0, 0)).Should().Be(new DateTime(2026, 10, 3, 6, 0, 0));
        HuntaholicEntryRefill.NextRefill(new DateTime(2026, 10, 3, 6, 0, 0)).Should().Be(new DateTime(2026, 10, 4, 6, 0, 0));
    }

    // ---- frames ----

    [Test]
    public void TheServerFramesHaveTheirSevenThreeSizes()
    {
        var list = GameHuntaholicServerPackets.BuildInstanceList(10000, 1, 1,
            new[] { new HuntaholicInstanceInfo(3, "Bears<0_h>", 2, 6, true) });
        list.Length.Should().Be(23 + 38);
        BinaryPrimitives.ReadInt32LittleEndian(list.AsSpan(7)).Should().Be(10000);
        BinaryPrimitives.ReadInt32LittleEndian(list.AsSpan(15)).Should().Be(1, "infos is the entry count");
        BinaryPrimitives.ReadInt32LittleEndian(list.AsSpan(23)).Should().Be(3);
        System.Text.Encoding.ASCII.GetString(list, 27, 10).Should().Be("Bears<0_h>");
        list[58].Should().Be(2);
        list[59].Should().Be(6);
        list[60].Should().Be(1);

        GameHuntaholicServerPackets.BuildInstanceInfo(default).Length.Should().Be(45);
        var score = GameHuntaholicServerPackets.BuildHuntingScore(10000, 4, 20, 9, 3001, 0.11, 1.0, 331, 0);
        score.Length.Should().Be(48);
        BinaryPrimitives.ReadDoubleLittleEndian(score.AsSpan(27)).Should().Be(0.11);
        BinaryPrimitives.ReadDoubleLittleEndian(score.AsSpan(35)).Should().Be(1.0);
        BinaryPrimitives.ReadInt32LittleEndian(score.AsSpan(43)).Should().Be(331);
        GameHuntaholicServerPackets.BuildUpdateScore(1, 2).Length.Should().Be(15);
        GameHuntaholicServerPackets.BuildBeginHunting(5).Length.Should().Be(11);
        GameHuntaholicServerPackets.BuildMaxPointAchieved().Length.Should().Be(7);
        GameHuntaholicServerPackets.BuildBeginCountdown().Length.Should().Be(7);
    }

    // ---- the flow ----

    private sealed class FakeWarp : IWarpService
    {
        public readonly List<(GameClient Client, float X, float Y, byte Layer)> Calls = new();
        public Action<GameClient, float, float> Before;

        public void Warp(GameClient client, float x, float y) => Warp(client, x, y, StorageTestHarness.Session(client).Layer);

        public void Warp(GameClient client, float x, float y, byte layer)
        {
            Before?.Invoke(client, x, y);
            Calls.Add((client, x, y, layer));
            StorageTestHarness.Session(client).X = x;
            StorageTestHarness.Session(client).Y = y;
            StorageTestHarness.Session(client).Layer = layer;
        }
    }

    private PlayerVisibilityService _visibility;
    private PartyService _parties;
    private FakeWarp _warp;
    private MonsterWorldState _world;
    private ISkillCastService _casts;
    private ICharacterService _characters;
    private HuntaholicEvents _events;
    private HuntaholicService _service;
    private uint _now;

    [SetUp]
    public void SetUp()
    {
        _visibility = new PlayerVisibilityService(A.Fake<ILogger<PlayerVisibilityService>>());
        var catalog = Catalog();
        _parties = new PartyService(_visibility, A.Fake<IStatService>(), A.Fake<IBannedWordsRepository>(), huntaholics: catalog);
        _warp = new FakeWarp();
        var repository = A.Fake<IMonsterResourceRepository>();
        A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._)).ReturnsLazily(call =>
            call.GetArgument<IReadOnlyCollection<int>>(0).Select(id => new MonsterResourceEntity
                { Id = id, Level = 20, Hp = 100, MonsterType = id == BossId ? 4 : 1 }).ToArray());
        _world = new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions()));
        _casts = A.Fake<ISkillCastService>();
        _characters = A.Fake<ICharacterService>();
        A.CallTo(() => _characters.AddItemAsync(A<string>._, A<int>._, A<long>._))
            .ReturnsLazily(call => Task.FromResult(new ItemEntity { Id = 77, ItemResourceId = call.GetArgument<int>(1), Amount = 1 }));
        _events = new HuntaholicEvents();
        _now = 1_000_000;
        _service = new HuntaholicService(catalog, _parties, _warp, _world, A.Fake<IMonsterSpawnService>(), _casts,
            _characters, _visibility, _events, clock: () => _now, localNow: () => new DateTime(2026, 10, 3, 12, 0, 0),
            random: new Random(7), runTicks: false);
        _warp.Before = (client, x, y) => _events.BeforeWarp(client, x, y);
    }

    private GameClient Player(uint handle, string name, int level = 25, float x = 1800, float y = 1800)
    {
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()),
            playerVisibilityService: _visibility);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = handle;
        info.CharacterName = name;
        info.CharacterLevel = level;
        info.CharacterHp = 100;
        info.CharacterMaxHp = 100;
        info.X = x;
        info.Y = y;
        info.DestinationX = x;
        info.DestinationY = y;
        info.HuntaholicEnterCount = 12;
        _visibility.Registry.Register(handle, client);
        return client;
    }

    private static List<byte[]> Frames(GameClient client, GamePackets id) =>
        ((StorageTestHarness.FrameConnection)client.Connection).Sent
            .Where(frame => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)) == (ushort)id).ToList();

    private static (ushort Request, ushort Result, int Value) LastResult(GameClient client)
    {
        var frame = Frames(client, GamePackets.TM_SC_RESULT).Last();
        return (BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(7)), BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(9)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(11)));
    }

    private (GameClient Leader, GameClient Member) Room(string password = "")
    {
        var leader = Player(1, "Ana");
        var member = Player(2, "Bo");
        _service.CreateInstance(leader, "Bears", 4, password);
        _service.JoinInstance(member, 1, password);
        return (leader, member);
    }

    [Test]
    public void CreatingARoomMakesAHuntaholicPartyAndAnswersWithTheHuntaholicId()
    {
        var leader = Player(1, "Ana");

        _service.CreateInstance(leader, "Bears", 6, "");

        LastResult(leader).Should().Be(((ushort)4003, (ushort)ResultCode.Success, 10000));
        _parties.PartyIdOf(leader).Should().NotBe(0);
        _parties.PartyName(_parties.PartyIdOf(leader)).Should().Be("Bears<0_h>");
        var info = Frames(leader, GamePackets.TM_SC_HUNTAHOLIC_INSTANCE_INFO).Single();
        BinaryPrimitives.ReadInt32LittleEndian(info.AsSpan(7)).Should().Be(1, "the first room is number 1");
        info[42].Should().Be(1);
        info[43].Should().Be(6);
    }

    [Test]
    public void CreateRefusesInTheOfficialOrder()
    {
        var outside = Player(1, "Ana", x: 50000, y: 50000);
        _service.CreateInstance(outside, "Bears", 4, "");
        LastResult(outside).Result.Should().Be((ushort)ResultCode.AccessDenied);

        var noEntry = Player(2, "Bo");
        StorageTestHarness.Session(noEntry).HuntaholicEnterCount = 0;
        _service.CreateInstance(noEntry, "Bears", 4, "");
        LastResult(noEntry).Result.Should().Be((ushort)ResultCode.NotEnoughBullet);

        var badCount = Player(3, "Cy");
        _service.CreateInstance(badCount, "Bears", 5, "");
        LastResult(badCount).Result.Should().Be((ushort)ResultCode.InvalidArgument);

        var badName = Player(4, "Di");
        _service.CreateInstance(badName, "B|ears", 4, "");
        LastResult(badName).Result.Should().Be((ushort)ResultCode.InvalidText);

        var tooHigh = Player(5, "Ed", level: 200);
        _service.CreateInstance(tooHigh, "Bears", 4, "");
        LastResult(tooHigh).Result.Should().Be((ushort)ResultCode.NotActable, "no tier takes the level");
    }

    [Test]
    public void JoinChecksThePasswordAndTheLevel()
    {
        var leader = Player(1, "Ana");
        _service.CreateInstance(leader, "Bears", 4, "secret");

        var wrong = Player(2, "Bo");
        _service.JoinInstance(wrong, 1, "nope");
        LastResult(wrong).Result.Should().Be((ushort)ResultCode.InvalidPassword);

        var low = Player(3, "Cy", level: 10);
        _service.JoinInstance(low, 1, "secret");
        LastResult(low).Result.Should().Be((ushort)ResultCode.LimitTarget);

        _service.JoinInstance(wrong, 1, "secret");
        LastResult(wrong).Should().Be(((ushort)4004, (ushort)ResultCode.Success, 10000));
        Frames(leader, GamePackets.TM_SC_HUNTAHOLIC_INSTANCE_INFO).Last()[42].Should().Be(2, "two members now");
    }

    [Test]
    public void TheListShowsTheRoomsOfTheLevelThatHaveNotStarted()
    {
        Room();
        var looker = Player(3, "Cy");

        _service.InstanceList(looker, 1);

        var list = Frames(looker, GamePackets.TM_SC_HUNTAHOLIC_INSTANCE_LIST).Single();
        BinaryPrimitives.ReadInt32LittleEndian(list.AsSpan(15)).Should().Be(1);
        BinaryPrimitives.ReadInt32LittleEndian(list.AsSpan(19)).Should().Be(1, "one page");

        var low = Player(4, "Di", level: 10);
        _service.InstanceList(low, 1);
        BinaryPrimitives.ReadInt32LittleEndian(Frames(low, GamePackets.TM_SC_HUNTAHOLIC_INSTANCE_LIST).Single().AsSpan(19))
            .Should().Be(0, "the room is not of the level");
    }

    [Test]
    public void TheLeaderStartsTheCountdownAndEveryMemberPaysAnEntry()
    {
        var (leader, member) = Room();

        _service.BeginHunting(member);
        Frames(member, GamePackets.TM_SC_HUNTAHOLIC_BEGIN_COUNTDOWN).Should().BeEmpty("only the leader starts");

        _service.BeginHunting(leader);

        foreach (var client in new[] { leader, member })
        {
            Frames(client, GamePackets.TM_SC_HUNTAHOLIC_BEGIN_COUNTDOWN).Should().ContainSingle();
            StorageTestHarness.Session(client).HuntaholicEnterCount.Should().Be(11);
        }

        var late = Player(3, "Cy");
        _service.JoinInstance(late, 1, "");
        LastResult(late).Result.Should().Be((ushort)ResultCode.CoolTime, "a room that pressed start is closed");
    }

    [Test]
    public void TheHuntBeginsTenSecondsLaterOnTheRoomsLayer()
    {
        var (leader, member) = Room();
        _service.BeginHunting(leader);

        _service.Process(_now + 999);
        _warp.Calls.Should().BeEmpty();

        _now += 1000;
        _service.Process(_now);

        _warp.Calls.Should().HaveCount(2);
        _warp.Calls.Should().OnlyContain(c => c.Layer == 1 && Math.Abs(c.X - 9000) <= 60 && Math.Abs(c.Y - 1800) <= 60);
        Frames(leader, GamePackets.TM_SC_HUNTAHOLIC_BEGIN_HUNTING).Should().ContainSingle();
        _world.WithinRange(9000, 1800, 1000).Where(m => m.Layer == 1).Should().HaveCount(3, "two monsters and a boss");
    }

    private (GameClient Leader, GameClient Member, MonsterInstance Boss) Hunting()
    {
        var (leader, member) = Room();
        _service.BeginHunting(leader);
        _now += 1000;
        _service.Process(_now);
        var boss = _world.WithinRange(9000, 1800, 1000).Single(m => m.MonsterId == BossId);
        return (leader, member, boss);
    }

    [Test]
    public void AKillScoresForItsDealerAndTheRoomAndRespawnsAfterItsPeriod()
    {
        var (leader, member, boss) = Hunting();

        _events.MonsterKilled(boss.InstanceId, leader);

        var update = Frames(leader, GamePackets.TM_SC_HUNTAHOLIC_UPDATE_SCORE).Single();
        BinaryPrimitives.ReadInt32LittleEndian(update.AsSpan(7)).Should().Be(1, "the leader's own kill count");
        BinaryPrimitives.ReadInt32LittleEndian(update.AsSpan(11)).Should().Be(150);
        BinaryPrimitives.ReadInt32LittleEndian(Frames(member, GamePackets.TM_SC_HUNTAHOLIC_UPDATE_SCORE).Single().AsSpan(7))
            .Should().Be(0, "the member killed nothing");

        _now += 200;
        _service.Process(_now);
        _world.WithinRange(9000, 1800, 1000).Count(m => m.MonsterId == BossId).Should().Be(2,
            "the dead boss is still a corpse in the table, and its respawn came after two seconds");
    }

    [Test]
    public void TheTimeRunningOutEndsTheHuntBelowTheObjectiveAsAFailureByDeath()
    {
        var (leader, member, _) = Hunting();

        _now += 1500 * 100 + 1;
        _service.Process(_now);

        foreach (var client in new[] { leader, member })
        {
            var score = Frames(client, GamePackets.TM_SC_HUNTAHOLIC_HUNTING_SCORE).Single();
            score[47].Should().Be((byte)HuntingResult.FailedByDeath, "endHunting gives no penalty below the objective");
            StorageTestHarness.Session(client).Layer.Should().Be(0, "back to the lobby of tier 0");
            StorageTestHarness.Session(client).HuntaholicEnterCount.Should().Be(11);
        }

        _parties.PartyIdOf(leader).Should().Be(0, "the room's party is destroyed");
        A.CallTo(() => _characters.AddItemAsync("Ana", 3620021, 1)).MustHaveHappenedOnceExactly();
        _world.WithinRange(9000, 1800, 1000).Should().BeEmpty("the hunt's monsters are cleared");
    }

    [Test]
    public void ReachingTheMaxPointClearsTheDungeonAndASuccessPaysThePoints()
    {
        var (leader, member, boss) = Hunting();
        // 40 boss kills reach 6000: kill the same boss repeatedly by respawning it.
        for (var i = 0; i < 40; i++)
        {
            var target = _world.WithinRange(9000, 1800, 1000).FirstOrDefault(m => m.MonsterId == BossId && _world.IsAlive(m.InstanceId));
            if (target.InstanceId == 0 && target.MonsterId == 0)
            {
                _now += 200;
                _service.Process(_now);
                target = _world.WithinRange(9000, 1800, 1000).First(m => m.MonsterId == BossId && _world.IsAlive(m.InstanceId));
            }

            _world.TryKill(target.InstanceId, DateTime.UtcNow.AddHours(1));
            _events.MonsterKilled(target.InstanceId, leader);
        }

        Frames(leader, GamePackets.TM_SC_HUNTAHOLIC_MAX_POINT_ACHIEVED).Should().ContainSingle();
        _service.LeaveInstance(member);

        var score = Frames(member, GamePackets.TM_SC_HUNTAHOLIC_HUNTING_SCORE).Single();
        score[47].Should().Be((byte)HuntingResult.Success);
        BinaryPrimitives.ReadInt32LittleEndian(score.AsSpan(43)).Should().Be(660, "ceil(0.11 × 6000)");
        StorageTestHarness.Session(member).HuntaholicPoint.Should().Be(660);
        StorageTestHarness.Session(member).CharacterExp.Should().Be(1000);
        LastResult(member).Should().Be(((ushort)4005, (ushort)ResultCode.Success, 10000));
    }

    [Test]
    public void RetiringBelowTheObjectiveCostsAnEntryAndTheSlowdown()
    {
        var (_, member, _) = Hunting();

        _service.LeaveInstance(member);

        Frames(member, GamePackets.TM_SC_HUNTAHOLIC_HUNTING_SCORE).Single()[47].Should().Be((byte)HuntingResult.Retired);
        StorageTestHarness.Session(member).HuntaholicEnterCount.Should().Be(10, "one for the start, one for the retirement");
        A.CallTo(() => _casts.ApplyState(member, HuntaholicRules.MoveSpeedSlowdownState, 1, HuntaholicRules.QuittingPenaltyTicks))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public void WarpingOutOfTheDungeonQuitsWithoutRewardButWithThePenalty()
    {
        var (_, member, _) = Hunting();

        _warp.Warp(member, 50000, 50000, 0);

        Frames(member, GamePackets.TM_SC_HUNTAHOLIC_HUNTING_SCORE).Should().BeEmpty("no reward, no scoreboard");
        StorageTestHarness.Session(member).HuntaholicEnterCount.Should().Be(10);
        _parties.PartyIdOf(member).Should().Be(0);
        LastResult(member).Request.Should().Be((ushort)4005, "the room window is closed by a 4005 result");
    }

    [Test]
    public void TheLastMemberLeavingTheLobbyRoomDeletesIt()
    {
        var (leader, member) = Room();
        _service.LeaveInstance(member);
        _service.LeaveInstance(leader);
        _parties.PartyIdOf(leader).Should().Be(0);

        var looker = Player(3, "Cy");
        _service.InstanceList(looker, 1);
        BinaryPrimitives.ReadInt32LittleEndian(Frames(looker, GamePackets.TM_SC_HUNTAHOLIC_INSTANCE_LIST).Single().AsSpan(15))
            .Should().Be(0);
    }

    [Test]
    public void ALoginInTheDungeonComesBackToTheLobbyOnItsTierLayer()
    {
        _service.PlaceAtLogin(25, 9000, 1800, 1).Should().Be((1800f, 1800f, (byte)0));
        _service.PlaceAtLogin(10, 1700, 1700, 0).Should().Be((1700f, 1700f, (byte)10));
        _service.PlaceAtLogin(25, 50000, 50000, 0).Should().Be((50000f, 50000f, (byte)0));
    }

    [Test]
    public void TheWarpSpellChecksThenWarpsToTheLobbyAndTheExitGoesBack()
    {
        var traveller = Player(9, "Fay", x: 50000, y: 50000);

        _events.CheckInstanceSkill(traveller, HuntaholicService.WarpSkill).Should().Be(ResultCode.Success);
        _events.FireInstanceSkill(traveller, HuntaholicService.WarpSkill).Should().Be(ResultCode.Success);
        StorageTestHarness.Session(traveller).X.Should().Be(1800);
        StorageTestHarness.Session(traveller).Layer.Should().Be(0);
        _events.CheckInstanceSkill(traveller, HuntaholicService.WarpSkill).Should().Be(ResultCode.NotActableInHuntaholic);

        _events.FireInstanceSkill(traveller, HuntaholicService.ExitSkill).Should().Be(ResultCode.Success);
        (StorageTestHarness.Session(traveller).X, StorageTestHarness.Session(traveller).Y).Should().Be((50000f, 50000f));

        var pk = Player(10, "Gus", x: 50000, y: 50000);
        StorageTestHarness.Session(pk).PkMode = true;
        _events.CheckInstanceSkill(pk, HuntaholicService.WarpSkill).Should().Be(ResultCode.PKLimit);
    }

    [Test]
    public void TheDailyRefillGivesTwelveEntriesBack()
    {
        var player = Player(1, "Ana");
        StorageTestHarness.Session(player).HuntaholicEnterCount = 3;
        StorageTestHarness.Session(player).NextHuntaholicRefill = new DateTime(2026, 10, 3, 6, 0, 0);

        _service.Process(_now);

        StorageTestHarness.Session(player).HuntaholicEnterCount.Should().Be(12);
        StorageTestHarness.Session(player).NextHuntaholicRefill.Should().Be(new DateTime(2026, 10, 4, 6, 0, 0));
    }

    [Test]
    public void AHuntaholicPartyIgnoresThePartyCommands()
    {
        var (leader, member) = Room();
        _parties.TryHandleCommand(leader, "/pdestroy").Should().BeTrue();
        _parties.PartyIdOf(member).Should().NotBe(0, "the command is ignored on a HuntaHolic party");

        var lone = Player(5, "Ed");
        _parties.TryHandleCommand(lone, "/pcreate Wolves").Should().BeTrue();
        _parties.PartyIdOf(lone).Should().Be(0, "no party is created inside HuntaHolic");
    }
}
