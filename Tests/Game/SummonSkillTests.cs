using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Creatures;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

/// <summary>A summon's skills: its trees, learning (402), list (403, 452) and passives.</summary>
[TestFixture]
public class SummonSkillTests
{
    private const int Bite = 40621;
    private const int Thick = 40700;
    private const int Ride = CreatureService.CreatureRidingSkill;

    private static SkillUnlockRule Rule(int min, int max, int jobLevel = 1, int minEnhance = 0, int maxEnhance = 5) => new()
    {
        MinSkillLevel = min, MaxSkillLevel = max, RequiredJobLevel = jobLevel, MinCardEnhance = minEnhance,
        MaxCardEnhance = maxEnhance
    };

    private static SkillCatalog Trees() => new(new SkillCatalogOptions
    {
        Jobs =
        {
            new JobSkillCatalog { JobId = 2101, Skills =
            {
                new LearnableSkill { SkillId = Bite, JpCosts = { 3, 5 }, Rules = { Rule(1, 2) } },
                new LearnableSkill { SkillId = Ride, JpCosts = { 1 }, Rules = { Rule(1, 1) } },
                new LearnableSkill { SkillId = Thick, JpCosts = { 2 }, Rules = { Rule(1, 1, minEnhance: 1) } }
            } },
            new JobSkillCatalog { JobId = 2102, Skills =
            {
                new LearnableSkill { SkillId = 40800, JpCosts = { 2 }, Rules = { Rule(1, 1, jobLevel: 55) } }
            } }
        }
    });

    private sealed class Harness
    {
        public readonly ICharacterService Characters = A.Fake<ICharacterService>();
        public readonly ISkillPassiveCatalog Passives = A.Fake<ISkillPassiveCatalog>();
        public readonly CreatureService Service;
        public readonly GameClient Client;
        public readonly ConnectionInfo Info;
        public readonly CreatureCard Card;

        public Harness(int code = 2101, int level = 10, int jp = 10)
        {
            var players = A.Fake<IPlayerVisibilityService>();
            A.CallTo(() => players.Registry).Returns(new PlayerRegistry());
            A.CallTo(() => players.Observers(A<GameClient>._)).Returns(Array.Empty<GameClient>());
            var repository = A.Fake<IMonsterResourceRepository>();
            A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._)).Returns(Array.Empty<MonsterResourceEntity>());
            var catalog = new CreatureCatalog(Options.Create(new CreatureCatalogOptions
            {
                Summons =
                {
                    new SummonResourceOptions { Id = 2101, Form = 1, CardId = 540014, StatId = 2101, RunSpeed = 100,
                        AttackRange = 0.2f, Size = 2.4f, Scale = 1f, Stats = new float[] { 14, 7, 16, 5, 10, 8, 10 } },
                    new SummonResourceOptions { Id = 2102, Form = 2, CardId = 540014, StatId = 2102, RunSpeed = 100,
                        AttackRange = 0.2f, Size = 2.4f, Scale = 1f, Stats = new float[] { 24, 17, 26, 15, 20, 18, 20 } }
                }
            }));
            A.CallTo(() => Characters.SaveSummonSkillAsync(A<string>._, A<long>._, A<int>._, A<byte>._, A<int>._))
                .Returns(true);
            A.CallTo(() => Passives.Resolve(A<int>._, A<int>._, A<ItemType?>._)).Returns(Array.Empty<StatEffect>());
            Service = new CreatureService(catalog, Characters,
                new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions())), A.Fake<ICombatService>(),
                new SummonWorldService(players), players, runTicks: false, skillTrees: Trees(), passives: Passives);
            Client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
            Info = StorageTestHarness.Session(Client);
            Info.CharacterHandle = 7;
            Info.CharacterName = "Ana";
            Info.CharacterLevel = 60;
            Card = new CreatureCard
            {
                ItemId = 60, Code = 540014, Amount = 1, Flag = CreatureRules.WithSummonFlag(ItemFlag.None),
                SummonId = 9, SummonCode = code, SummonName = "RossParr", Level = level, MaxReachedLevel = level,
                Jp = jp, SummonHandle = 0x50000001, InfoSent = true
            };
            Info.CreatureCards[60] = Card;
            Info.SummonSlots = new long[] { 60, 0, 0, 0, 0, 0 };
        }

        public List<byte[]> Sent => ((StorageTestHarness.FrameConnection)Client.Connection).Sent;

        public byte[] Last(GamePackets id) => Sent.Last(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2)) == (ushort)id);

        public (ushort Request, ushort Result) Result()
        {
            var frame = Last(GamePackets.TM_SC_RESULT);
            return (BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(7, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(9, 2)));
        }
    }

    [Test]
    public async Task A_summon_learns_from_its_tree_with_its_own_jp()
    {
        var h = new Harness(jp: 10);
        (await h.Service.TryLearnSkillAsync(h.Client, new GameActionPackets.LearnSkillRequest(0x50000001, Bite, 1)))
            .Should().BeTrue();

        h.Result().Should().Be(((ushort)402, (ushort)ResultCode.Success));
        h.Card.Jp.Should().Be(7);
        h.Card.Skills[Bite].Should().Be(1);
        BinaryPrimitives.ReadUInt32LittleEndian(h.Last(GamePackets.TM_SC_SKILL_LIST).AsSpan(7, 4))
            .Should().Be(0x50000001u, "the list belongs to the summon");
        A.CallTo(() => h.Characters.SaveSummonSkillAsync("Ana", 9, Bite, 1, 7)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task The_official_refusals_apply_to_a_summon()
    {
        var poor = new Harness(jp: 1);
        await poor.Service.TryLearnSkillAsync(poor.Client, new GameActionPackets.LearnSkillRequest(0x50000001, Bite, 1));
        poor.Result().Should().Be(((ushort)402, (ushort)ResultCode.NotEnoughJP));

        var plain = new Harness();
        await plain.Service.TryLearnSkillAsync(plain.Client, new GameActionPackets.LearnSkillRequest(0x50000001, Thick, 1));
        plain.Result().Should().Be(((ushort)402, (ushort)ResultCode.EnhanceLimit), "the rule needs an enhanced card");

        var other = new Harness();
        await other.Service.TryLearnSkillAsync(other.Client, new GameActionPackets.LearnSkillRequest(0x50000001, 99999, 1));
        other.Result().Should().Be(((ushort)402, (ushort)ResultCode.LimitJob));
    }

    [Test]
    public async Task An_evolved_summon_still_learns_from_its_former_tree()
    {
        var h = new Harness(code: 2102, level: 52);
        h.Card.PreviousSummonIds[0] = 2101;
        h.Card.PreviousLevels[0] = 50;

        await h.Service.TryLearnSkillAsync(h.Client, new GameActionPackets.LearnSkillRequest(0x50000001, Bite, 1));
        h.Result().Should().Be(((ushort)402, (ushort)ResultCode.Success));

        await h.Service.TryLearnSkillAsync(h.Client, new GameActionPackets.LearnSkillRequest(0x50000001, 40800, 1));
        h.Result().Should().Be(((ushort)402, (ushort)ResultCode.NotEnoughJobLevel), "the form's own tree needs 55");
    }

    [Test]
    public async Task A_handle_that_is_not_a_summon_goes_to_the_player()
    {
        var h = new Harness();
        (await h.Service.TryLearnSkillAsync(h.Client, new GameActionPackets.LearnSkillRequest(7, Bite, 1))).Should().BeFalse();
        (await h.Service.TryLearnSkillAsync(h.Client, new GameActionPackets.LearnSkillRequest(1234, Bite, 1))).Should().BeFalse();
    }

    [Test]
    public void The_card_flip_lists_the_summon_skills()
    {
        var h = new Harness();
        h.Card.Skills[Bite] = 2;
        h.Service.SendCardSkillList(h.Client, 60);
        var list = h.Last(GamePackets.TM_SC_SKILL_LEVEL_LIST);
        list.Should().HaveCount(14);
        BinaryPrimitives.ReadUInt16LittleEndian(list.AsSpan(7, 2)).Should().Be(1);
        BinaryPrimitives.ReadInt32LittleEndian(list.AsSpan(9, 4)).Should().Be(Bite);
        list[13].Should().Be(2);
    }

    [Test]
    public async Task A_passive_moves_the_summon_stats_and_riding_makes_it_ridable()
    {
        var h = new Harness();
        A.CallTo(() => h.Passives.Resolve(Ride, 1, null))
            .Returns(new[] { new StatEffect(StatTarget.Defence, 100, false) });
        CreatureService.IsRidable(h.Card).Should().BeFalse();

        await h.Service.TryLearnSkillAsync(h.Client, new GameActionPackets.LearnSkillRequest(0x50000001, Ride, 1));

        CreatureService.IsRidable(h.Card).Should().BeTrue();
        A.CallTo(() => h.Passives.Resolve(Ride, 1, null)).MustHaveHappened();
        h.Last(GamePackets.TM_SC_STAT_INFO).Should().NotBeNull();
    }

    [Test]
    public void The_skill_catalogue_bounds_a_rule_by_the_card_enhance()
    {
        var trees = Trees();
        trees.Evaluate(2101, 10, 10, Thick, 0, 1, new Dictionary<int, byte>(), 10, cardEnhance: 0).Result
            .Should().Be(ResultCode.EnhanceLimit);
        trees.Evaluate(2101, 10, 10, Thick, 0, 1, new Dictionary<int, byte>(), 10, cardEnhance: 1).Result
            .Should().Be(ResultCode.Success);
        trees.Evaluate(2101, 10, 10, Bite, 0, 1, new Dictionary<int, byte>(), 10).Result
            .Should().Be(ResultCode.Success, "a player counts as enhance 0");
    }

    [Test]
    public async Task Summon_skills_are_written_raised_in_place_and_read_back()
    {
        var options = new DbContextOptionsBuilder<TelecasterContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), o => o.EnableNullChecks(false)).Options;
        await using (var db = new TelecasterContext(options))
        {
            db.Characters.Add(new CharacterEntity { Id = 1, AccountId = 4, AccountName = "A", CharacterName = "Ana",
                Items = new List<ItemEntity>(), Skills = new List<CharacterSkillEntity>() });
            db.Summons.Add(new SummonEntity { Id = 9, AccountId = 4, CharacterId = 1, SummonResourceId = 2101,
                CardItemId = 60, Name = "RossParr", Lv = 10, Jp = 10, PreviousLevel = new int[2],
                PreviousSummonResourceIds = new long[2] });
            await db.SaveChangesAsync();
        }

        var service = new CharacterService(A.Fake<IStarterItemsRepository>(), new CharacterRepositoryFactory(options),
            new CharacterGate(), NullLogger<CharacterService>.Instance);
        (await service.SaveSummonSkillAsync("Ana", 9, Bite, 1, 7)).Should().BeTrue();
        (await service.SaveSummonSkillAsync("Ana", 9, Bite, 2, 2)).Should().BeTrue();
        (await service.SaveSummonSkillAsync("Ana", 99, Bite, 1, 0)).Should().BeFalse("not the character's summon");

        (await service.GetSummonSkillsAsync("Ana")).Should().ContainSingle()
            .Which.Should().Be(new SummonSkillRecord(9, Bite, 2));
        await using var check = new TelecasterContext(options);
        (await check.Summons.SingleAsync()).Jp.Should().Be(2);
    }
}
