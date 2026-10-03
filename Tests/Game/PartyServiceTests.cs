using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Microsoft.Extensions.Logging;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Party;
using Navislamia.Game.Services.Stats;
using Navislamia.Game.Services.Rates;
using Microsoft.Extensions.Options;

namespace Tests.Game;

[TestFixture]
public class PartyServiceTests
{
    private PlayerVisibilityService _visibility;
    private IBannedWordsRepository _bannedWords;
    private PartyService _parties;

    [SetUp]
    public void SetUp()
    {
        _visibility = new PlayerVisibilityService(A.Fake<ILogger<PlayerVisibilityService>>());
        _bannedWords = A.Fake<IBannedWordsRepository>();
        _parties = new PartyService(_visibility, A.Fake<IStatService>(), _bannedWords);
    }

    [Test]
    public void MessagesHaveTheOfficialServerFormats()
    {
        PartyMessages.Create("Wolves", "Ana").Should().Be("CREATE|Wolves|Ana|0|");
        PartyMessages.Invite("Ana", "Wolves", 3, 777).Should().Be("INVITE|Ana|Wolves|3|777|");
        PartyMessages.Kick("Wolves", "Bo").Should().Be("KICK|Wolves|Bo|");
        PartyMessages.Mode(PartyShareMode.Linear).Should().Be("MODE|2|");
        PartyMessages.MemberInfo(new PartyMemberView(true, 9, "Ana", 3, 100, 12, 50, 7, 1000, 2000))
            .Should().Be("MINFO|9|Ana|3|100|50|7|1000|2000|2|");
        PartyMessages.Entry(new PartyMemberView(false, 0, "Bo", 5, 110, 20, 0, 0, 0, 0))
            .Should().Be("0|Bo|20|110|0|0|0|0|0|");
    }

    [TestCase(0, 100, 0)]
    [TestCase(1, 1000, 1)]
    [TestCase(50, 100, 50)]
    [TestCase(100, 100, 100)]
    public void PercentIsTruncatedAndNeverZeroWhileAlive(int value, int maximum, int expected)
    {
        PartyMessages.Percent(value, maximum).Should().Be(expected);
    }

    [Test]
    public void CreateAnswersCreateThenPartyInfo()
    {
        var ana = Player(1, "Ana");

        _parties.TryHandleCommand(ana, "/pcreate Wolves").Should().BeTrue();

        Lines(ana).Should().Equal("CREATE|Wolves|Ana|0|", "PINFO|1|Wolves|Ana|0|10|10|0|1|Ana|0|0|0|0|0|0|2|");
        Info(ana).PartyId.Should().Be(1);
    }

    [Test]
    public void Dungeon_party_snapshot_uses_membership_registry_instead_of_session_party_id()
    {
        var ana = Player(1, "Ana");
        Info(ana).PartyId = 999;
        _parties.DungeonParty(ana).Should().BeNull();
        Info(ana).PartyId = null;
        _parties.TryHandleCommand(ana, "/pcreate Wolves");
        var party = _parties.DungeonParty(ana);
        party!.Id.Should().Be(1);
        party.Leader.Should().Be(1);
        party.Members.Should().Equal(1u);
        party.Online.Should().ContainSingle().Which.Should().BeSameAs(ana);
    }

    [TestCase("/pcreate Bad|Name", "INVALID_PARTY_NAME")]
    [TestCase("/pcreate Bad-Name", "INVALID_PARTY_NAME")]
    public void CreateRefusesANameThatIsNotLettersAndDigits(string command, string error)
    {
        var ana = Player(1, "Ana");

        _parties.TryHandleCommand(ana, command);

        Lines(ana).Should().Equal(error);
        Info(ana).PartyId.Should().BeNull();
    }

    [Test]
    public void AnInvitationCarriesThePasswordThatJoinRequires()
    {
        var ana = Player(1, "Ana");
        var bo = Player(2, "Bo");
        _parties.TryHandleCommand(ana, "/pcreate Wolves");

        _parties.TryHandleCommand(ana, "/pinvite bo");

        var invite = Lines(bo).Single().Split('|');
        invite[0].Should().Be("INVITE");
        invite[1].Should().Be("Ana");
        invite[2].Should().Be("Wolves");
        invite[3].Should().Be("1");

        Clear(ana, bo);
        _parties.TryHandleCommand(bo, "/pjoin 1 12345");
        Lines(bo).Should().Equal("HAS_NO_AUTHORITY");
        Info(bo).PartyId.Should().BeNull();

        Clear(ana, bo);
        _parties.TryHandleCommand(bo, $"/pjoin 1 {invite[4]}");

        Lines(ana).Should().Equal("NEW|Bo|", "MINFO|2|Bo|0|0|0|0|0|0|2|");
        Lines(bo).Should().HaveCount(3);
        Lines(bo)[0].Should().Be("JOIN|Wolves|");
        Lines(bo)[1].Should().StartWith("PINFO|1|Wolves|Ana|");
        Lines(bo)[2].Should().Be("MINFO|2|Bo|0|0|0|0|0|0|2|");
        Info(bo).PartyId.Should().Be(1);
    }

    [Test]
    public void OnlyTheLeaderInvitesAndAFullPartyRefuses()
    {
        var members = Enumerable.Range(1, PartyService.MaxMembers + 1)
            .Select(i => Player((uint)i, $"P{i}")).ToArray();
        _parties.TryHandleCommand(members[0], "/pcreate Wolves");
        for (var i = 1; i < PartyService.MaxMembers; i++)
        {
            Join(members[0], members[i]);
        }

        Clear(members);
        _parties.TryHandleCommand(members[1], $"/pinvite {Info(members[8]).CharacterName}");
        Lines(members[8]).Should().BeEmpty();

        _parties.TryHandleCommand(members[0], $"/pinvite {Info(members[8]).CharacterName}");
        Lines(members[0]).Should().Equal("ERROR_MAX");
        Lines(members[8]).Should().BeEmpty();
    }

    [Test]
    public void KickTellsEveryoneThenRemovesTheMember()
    {
        var ana = Player(1, "Ana");
        var bo = Player(2, "Bo");
        _parties.TryHandleCommand(ana, "/pcreate Wolves");
        Join(ana, bo);
        Clear(ana, bo);

        _parties.TryHandleCommand(bo, "/pkick Ana");
        Lines(ana).Should().BeEmpty();

        _parties.TryHandleCommand(ana, "/pkick Bo");

        Lines(ana).Should().Equal("KICK|Wolves|Bo|");
        Lines(bo).Should().Equal("KICK|Wolves|Bo|");
        Info(bo).PartyId.Should().BeNull();
    }

    [Test]
    public void TheLeaderCannotLeaveButCanPromoteThenLeave()
    {
        var ana = Player(1, "Ana");
        var bo = Player(2, "Bo");
        _parties.TryHandleCommand(ana, "/pcreate Wolves");
        Join(ana, bo);
        Clear(ana, bo);

        _parties.TryHandleCommand(ana, "/pleave");
        Lines(ana).Should().BeEmpty();
        Info(ana).PartyId.Should().Be(1);

        _parties.TryHandleCommand(ana, "/ppromote Bo");
        _parties.TryHandleCommand(ana, "/pleave");

        Lines(bo).Should().Equal("PROMOTE|Bo|", "LEAVE|Ana|");
        Info(ana).PartyId.Should().BeNull();
        Info(bo).PartyId.Should().Be(1);
    }

    [Test]
    public void DestroyReachesEveryMemberAndEndsTheParty()
    {
        var ana = Player(1, "Ana");
        var bo = Player(2, "Bo");
        _parties.TryHandleCommand(ana, "/pcreate Wolves");
        Join(ana, bo);
        Clear(ana, bo);

        _parties.TryHandleCommand(ana, "/pdestroy");

        Lines(ana).Should().Equal("DESTROY|Wolves|");
        Lines(bo).Should().Equal("DESTROY|Wolves|");
        Info(ana).PartyId.Should().BeNull();
        Info(bo).PartyId.Should().BeNull();

        Clear(ana, bo);
        _parties.TryHandleCommand(bo, "/pcreate Wolves");
        Lines(bo).Should().StartWith("CREATE|Wolves|Bo|0|");
    }

    [Test]
    public void AMemberStaysInThePartyAcrossALogout()
    {
        var ana = Player(1, "Ana");
        var bo = Player(2, "Bo");
        _parties.TryHandleCommand(ana, "/pcreate Wolves");
        Join(ana, bo);
        Clear(ana, bo);

        _visibility.Registry.Unregister(2, bo);
        _parties.OnWorldExit(bo);
        Lines(ana).Should().Equal("LOGOUT|Bo|");

        Clear(ana);
        _parties.TryHandleCommand(ana, "/plist");
        Lines(ana).Single().Should().EndWith("0|Bo|10|0|0|0|0|0|0|");

        Clear(ana, bo);
        Info(bo).PartyId = null;
        _visibility.Registry.Register(2, bo);
        _parties.OnWorldEntry(bo);

        Lines(ana).Should().Equal("LOGIN|Wolves|Bo|", "MINFO|2|Bo|0|0|0|0|0|0|2|");
        Lines(bo).Should().HaveCount(3);
        Lines(bo)[2].Should().StartWith("PINFO|1|Wolves|Ana|");
        Info(bo).PartyId.Should().Be(1);
    }

    [Test]
    public void VitalsReachThePartyOnlyWhenAPercentageMoves()
    {
        var ana = Player(1, "Ana");
        var bo = Player(2, "Bo");
        _parties.TryHandleCommand(ana, "/pcreate Wolves");
        Join(ana, bo);
        Info(bo).CharacterMaxHp = 1000;
        Info(bo).CharacterHp = 995;
        _parties.OnVitalsChanged(bo);
        Clear(ana, bo);

        // 99.5 % then 99.1 %: both truncate to 99, nothing to tell.
        Info(bo).CharacterHp = 991;
        _parties.OnVitalsChanged(bo);
        Lines(ana).Should().BeEmpty();

        Info(bo).CharacterHp = 500;
        _parties.OnVitalsChanged(bo);
        Lines(ana).Should().Equal("MINFO|2|Bo|0|0|50|0|0|0|2|");
    }

    [Test]
    public void KillRewardsReachOnlyNearbyOnlineMembers()
    {
        var ana = Player(1, "Ana");
        var bo = Player(2, "Bo");
        var cy = Player(3, "Cy");
        _parties.TryHandleCommand(ana, "/pcreate Wolves");
        Join(ana, bo);
        Join(ana, cy);
        Info(bo).X = 300;
        Info(cy).X = 1000;

        _parties.RewardMembers(ana, 0, 0, 0).Should().Equal(ana, bo);
        _visibility.Registry.Unregister(2, bo);
        _parties.RewardMembers(ana, 0, 0, 0).Should().Equal(ana);
    }

    [Test]
    public void LootModeAndMembershipControlTheRecipient()
    {
        var ana = Player(1, "Ana");
        var bo = Player(2, "Bo");
        var outsider = Player(3, "Cy");
        _parties.TryHandleCommand(ana, "/pcreate Wolves");
        Join(ana, bo);

        _parties.CanTakeDrop(ana, bo, 1).Should().BeTrue();
        _parties.CanTakeDrop(ana, outsider, 1).Should().BeFalse();
        _parties.LootRecipient(bo, 1, 0, 0, 0).Should().BeSameAs(bo);

        _parties.TryHandleCommand(ana, "/pshare random");
        _parties.LootRecipient(bo, 1, 0, 0, 0).Should().BeOneOf(ana, bo);

        _parties.TryHandleCommand(ana, "/pkick Bo");
        _parties.CanTakeDrop(ana, bo, 1).Should().BeFalse();
    }

    [Test]
    public void MonsterKillSharesResourceExpJpWithoutLosingRemaindersAndDropsGoldOnce()
    {
        var ana = Player(1, "Ana");
        var bo = Player(2, "Bo");
        _parties.TryHandleCommand(ana, "/pcreate Wolves");
        Join(ana, bo);
        Info(ana).X = Info(bo).X = 1000;
        Info(ana).Y = Info(bo).Y = 2000;
        var options = new MonsterSpawnOptions
        {
            Spawns = { new MonsterSpawnPoint { MonsterId = 2101, X = 1000, Y = 2000, Count = 1, Radius = 0 } }
        };
        var repository = A.Fake<IMonsterResourceRepository>();
        A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._))
            .Returns(new[]
            {
                new MonsterResourceEntity
                {
                    Id = 2101, Level = 5, Hp = 100, Exp = 40, Jp = 21,
                    GoldDropPercentage = 100, GoldMin = 20, GoldMax = 20,
                    ChaosDropPercentage = 100, ChaosMin = 10, ChaosMax = 10
                }
            });
        var world = new MonsterWorldState(repository, Options.Create(options));
        var rates = new RateService(new StaticOptionsMonitor<RatesOptions>(new RatesOptions { EventStatePath = "" }));
        var quests = A.Fake<IQuestService>();
        var ground = A.Fake<IGroundItemService>();
        var stats = A.Fake<IStatService>();
        A.CallTo(() => stats.Compute(A<ConnectionInfo>._))
            .Returns(new CharacterStatResult(new StatBlock { MaxChaos = 100 }, new StatBlock()));
        var combat = new CombatService(world, A.Fake<IMonsterSpawnService>(), A.Fake<ILevelingService>(),
            ground, rates, stats, A.Fake<IStateCatalog>(), _parties, quests);

        // A level-5 monster has hp + 20 x level = 200 HP (MonsterCombatStats): deal more than that.
        // Both clients stream the monster, each under its own handle: the chaos gain is announced around the
        // corpse only to the clients that can place it.
        Info(ana).SpawnedMonsters[0] = 0x40000100;
        Info(bo).SpawnedMonsters[0] = 0x40000101;
        combat.ApplyDamage(ana, 0, 500, 1_000).Should().Be(0);
        combat.ApplyDamage(bo, 0, 500, 1_000).Should().Be(0);
        A.CallTo(() => quests.OnMonsterKilledAsync(ana, 2101, 1000, 2000, A<float>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => quests.OnMonsterKilledAsync(bo, 2101, 1000, 2000, A<float>._)).MustHaveHappenedOnceExactly();

        // The level-10 members outlevel the level-5 monster: the split happens first (40 / 2 and 21 / 2 with
        // the remainder to the first), then 1 - 0.05 x 5 = 0.75 for experience and JP, truncating.
        Info(ana).CharacterExp.Should().Be(15);
        Info(bo).CharacterExp.Should().Be(15);
        (Info(ana).CharacterJp + Info(bo).CharacterJp).Should().Be(15);
        Math.Abs(Info(ana).CharacterJp - Info(bo).CharacterJp).Should().Be(1, "21 JP leaves a remainder");

        // The gold falls on the ground once, announced from the corpse; no wallet moves at the kill.
        (Info(ana).CharacterGold + Info(bo).CharacterGold).Should().Be(0);
        A.CallTo(() => ground.DropGoldForMonster(ana, 20, 1000, 2000, A<float>._, 0)).MustHaveHappenedOnceExactly();
        Frames(ana, GamePackets.TM_SC_GOLD_UPDATE).Should().BeEmpty("the kill sends no 1001");

        // The chaos gain is announced twice: the region frame around the corpse and the property the client
        // keeps.
        (Info(ana).CharacterChaos + Info(bo).CharacterChaos).Should().Be(10);
        Frames(ana, GamePackets.TM_SC_GET_CHAOS).Should().ContainSingle("ana does not stream bo here");
        Frames(bo, GamePackets.TM_SC_GET_CHAOS).Should().ContainSingle();
        Frames(ana, GamePackets.TM_SC_PROPERTY).Should().ContainSingle();
        Encoding.ASCII.GetString(Frames(ana, GamePackets.TM_SC_PROPERTY)[0], 12, 16).TrimEnd('\0')
            .Should().Be("chaos");
        BinaryPrimitives.ReadInt64LittleEndian(Frames(ana, GamePackets.TM_SC_PROPERTY)[0].AsSpan(28, 8))
            .Should().Be(5);
    }

    [Test]
    public void KillRewardsFollowTheResourceRowTheLevelGapAndTheDistance()
    {
        var ana = Player(1, "Ana");
        var bo = Player(2, "Bo");
        _parties.TryHandleCommand(ana, "/pcreate Wolves");
        Join(ana, bo);
        Info(ana).CharacterLevel = 20;
        Info(ana).X = 1000;
        Info(ana).Y = 2000;
        // 520 units from the corpse: still a party member (the party range is 540), out of the 500 units the
        // official still grants experience and chaos in.
        Info(bo).X = 1000;
        Info(bo).Y = 2520;

        var options = new MonsterSpawnOptions
        {
            Spawns = { new MonsterSpawnPoint { MonsterId = 2101, X = 1000, Y = 2000, Count = 1, Radius = 0 } }
        };
        var repository = A.Fake<IMonsterResourceRepository>();
        A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._))
            .Returns(new[]
            {
                new MonsterResourceEntity
                {
                    Id = 2101, Level = 10, Hp = 100, Exp = 100, Jp = 100,
                    GoldDropPercentage = 0, ChaosDropPercentage = 0
                }
            });
        var world = new MonsterWorldState(repository, Options.Create(options));
        var rates = new RateService(new StaticOptionsMonitor<RatesOptions>(new RatesOptions { EventStatePath = "" }));
        var combat = new CombatService(world, A.Fake<IMonsterSpawnService>(), A.Fake<ILevelingService>(),
            A.Fake<IGroundItemService>(), rates, A.Fake<IStatService>(), A.Fake<IStateCatalog>(), _parties,
            A.Fake<IQuestService>());

        combat.ApplyDamage(ana, 0, 500, 1_000);

        // The level-20 leader outlevels the level-10 monster by ten: 1 - 0.05 x 10 = 0.5, so 50 of its 50.
        Info(ana).CharacterExp.Should().Be(25);
        Info(ana).CharacterJp.Should().Be(25);
        Info(bo).CharacterExp.Should().Be(0, "the corpse is 520 units away, beyond the 500 unit reward range");
        Info(bo).CharacterJp.Should().Be(0);
        (Info(ana).CharacterGold + Info(bo).CharacterGold).Should().Be(0, "the row drops no gold");
        (Info(ana).CharacterChaos + Info(bo).CharacterChaos).Should().Be(0, "the row drops no chaos");
        Frames(ana, GamePackets.TM_SC_GET_CHAOS).Should().BeEmpty();
    }

    [Test]
    public async System.Threading.Tasks.Task PartyMemberCanPickUpMonsterLootAndLinearModeAssignsRecipientsInTurn()
    {
        var ana = Player(1, "Ana");
        var bo = Player(2, "Bo");
        var outsider = Player(3, "Cy");
        _parties.TryHandleCommand(ana, "/pcreate Wolves");
        Join(ana, bo);
        _parties.TryHandleCommand(ana, "/pshare linear");
        var catalog = A.Fake<IMonsterDropCatalog>();
        A.CallTo(() => catalog.GetDrops(50)).Returns(new[] { new DropEntry(603002, 1, 1, 1) });
        A.CallTo(() => catalog.Groups).Returns(new Dictionary<int, DropGroupEntry[]>());
        var characters = A.Fake<ICharacterService>();
        A.CallTo(() => characters.AddItemAsync(A<string>._, 603002, 1))
            .Returns(new ItemEntity { Id = 10, ItemResourceId = 603002, Amount = 1 });
        var rates = new RateService(new StaticOptionsMonitor<RatesOptions>(new RatesOptions { EventStatePath = "" }));
        var ground = new GroundItemService(catalog, characters, A.Fake<IItemGroupCatalog>(), rates,
            _visibility, parties: _parties);

        for (var i = 0; i < 2; i++)
        {
            Clear(ana, bo, outsider);
            ground.DropForMonster(ana, 50, 0, 0, 0);
            var enter = ((StorageTestHarness.FrameConnection)bo.Connection).Sent.Single(frame =>
                BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)) == (ushort)GamePackets.TM_SC_ENTER);
            BinaryPrimitives.ReadUInt32LittleEndian(enter.AsSpan(58, 4)).Should().Be(1);
            var handle = BinaryPrimitives.ReadUInt32LittleEndian(enter.AsSpan(8, 4));
            Clear(ana, bo, outsider);

            await ground.TakeAsync(outsider, handle);
            await ground.TakeAsync(bo, handle);
        }

        A.CallTo(() => characters.AddItemAsync("Ana", 603002, 1)).MustHaveHappenedOnceExactly();
        A.CallTo(() => characters.AddItemAsync("Bo", 603002, 1)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public void AnythingElseIsNotAPartyCommand()
    {
        var ana = Player(1, "Ana");

        _parties.TryHandleCommand(ana, "/pk on").Should().BeFalse();
        _parties.TryHandleCommand(ana, "/position").Should().BeFalse();
        Lines(ana).Should().BeEmpty();
    }

    private void Join(GameClient leader, GameClient member)
    {
        Clear(member);
        _parties.TryHandleCommand(leader, $"/pinvite {Info(member).CharacterName}");
        var invite = Lines(member).Single().Split('|');
        _parties.TryHandleCommand(member, $"/pjoin {invite[3]} {invite[4]}");
    }

    private GameClient Player(uint handle, string name)
    {
        var client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()),
            playerVisibilityService: _visibility);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = handle;
        info.CharacterName = name;
        info.CharacterLevel = 10;
        _visibility.Registry.Register(handle, client);
        return client;
    }

    private static ConnectionInfo Info(GameClient client) => StorageTestHarness.Session(client);

    private static void Clear(params GameClient[] clients)
    {
        foreach (var client in clients)
        {
            ((StorageTestHarness.FrameConnection)client.Connection).Sent.Clear();
        }
    }

    /// <summary>The frames a client received with the given id.</summary>
    private static List<byte[]> Frames(GameClient client, GamePackets id) =>
        ((StorageTestHarness.FrameConnection)client.Connection).Sent
            .Where(frame => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)) == (ushort)id)
            .ToList();

    /// <summary>The <c>@PARTY</c> lines a client received: TS_SC_CHAT type 100, message from offset 31.</summary>
    private static List<string> Lines(GameClient client) =>
        ((StorageTestHarness.FrameConnection)client.Connection).Sent
            .Where(frame => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)) == (ushort)GamePackets.TM_SC_CHAT)
            .Select(frame =>
            {
                frame[30].Should().Be((byte)ChatType.PartySystem);
                Encoding.ASCII.GetString(frame, 7, 6).Should().Be("@PARTY");
                var length = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(28, 2));
                return Encoding.ASCII.GetString(frame, 31, length - 1);
            })
            .ToList();
}
