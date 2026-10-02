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
using Navislamia.Game.Services;
using Navislamia.Game.Services.Creatures;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

/// <summary>A summon's stats, exp and levels (docs/packet-specs/socle-invocations-progression.md).</summary>
[TestFixture]
public class SummonProgressionTests
{
    // SummonLevelResource.normal_exp: the first levels of the 9.4 table, then a steep tail up to level 170 (the
    // rules read the threshold of the form's last level).
    private static readonly long[] Exp = new long[] { 5, 17, 43, 100, 228, 480, 950, 1700, 2900, 4700 }
        .Concat(Enumerable.Range(11, 160).Select(level => 4700L * level * level)).ToArray();

    private static long Need(int level) => level >= 1 && level <= Exp.Length ? Exp[level - 1] : 0;

    private static SummonResourceInfo Poultry(int form = 1) => new(2101, "Bloody Poultry", 1, 0, form, 540014, 100,
        0.2f, 2.4f, 1f, new StatBaseStats(2101, 14, 7, 16, 5, 10, 8, 10), form == 1 ? 2102 : 0,
        new[] { 0.34f, 0.17f, 0.38f, 0.12f, 0.24f, 0.19f, 0f });

    [Test]
    public void The_penalties_and_limits_follow_the_official_rules()
    {
        SummonProgression.LevelPenalty(50, 50).Should().Be(0);
        SummonProgression.LevelPenalty(50, 60).Should().Be((int)(10 * 0.25f * (2.34f - 10 / 30.0f) * 10f) / 10f);
        SummonProgression.LevelPenalty(10, 45).Should().Be(10);
        SummonProgression.StatPenalty(50, 50).Should().Be(1);
        SummonProgression.StatPenalty(50, 60).Should().Be(((int)(40 * 0.6f) + 70f) / 100f);
        SummonProgression.StatPenalty(1, 80).Should().Be(0.7f);
        SummonProgression.ExpLimit(10).Should().Be(20000);
        SummonProgression.PlayerExpLimit(10).Should().Be((long)(Math.Pow(10, 1.8) * 30) + 240);
        SummonProgression.CreatureCoefficient(2).Should().BeApproximately(0.76f, 1e-6f);
        SummonProgression.MaxLevel(1).Should().Be(60);
        SummonProgression.MaxLevel(2).Should().Be(115);
        SummonProgression.MaxLevel(3).Should().Be(170);
    }

    [Test]
    public void Stats_use_the_creature_coefficient_and_the_battle_level()
    {
        var stats = SummonProgression.Stats(Poultry(), 10, new SummonStatContext(10));
        // Base 14 str + (int)(0.34 × 10) = 3 → 17; FCM 0.7, battle level 10.
        stats.Strength.Should().Be(17);
        stats.AttackPointRight.Should().BeApproximately(17 * 2.8f * 0.7f + 10, 1e-3f);
        stats.Vitality.Should().Be(7 + 1);
        stats.MaxHp.Should().Be((int)(8 * 0.7f * 33 + 10 * 20));
        stats.MoveSpeed.Should().Be(100, "a summon walks at its run_speed");
        // (2 × master − level) / level, integer: (20 − 10) / 10 = 1.
        stats.AccuracyRight.Should().BeApproximately((16 + 3) * 0.5f * 0.7f + 10 + 1, 1e-3f);

        var mastered = SummonProgression.Stats(Poultry(), 10, new SummonStatContext(10, CreatureMasteryLevel: 5));
        mastered.AttackPointRight.Should().BeGreaterThan(stats.AttackPointRight, "Creature Mastery raises the FCM");

        var enhanced = SummonProgression.Stats(Poultry(), 10, new SummonStatContext(10, StatAmplify: 0.1f));
        enhanced.Strength.Should().BeApproximately(14 * 1.1f + 3, 1e-3f, "the amplifier acts on the base stats only");

        var above = SummonProgression.Stats(Poultry(), 10, new SummonStatContext(5));
        above.Strength.Should().BeLessThan(stats.Strength, "a summon above its master loses stats");
    }

    [Test]
    public void The_level_bonus_counts_the_levels_of_the_current_form()
    {
        SummonProgression.LevelBonus(0.34f, 1, 10).Should().Be(3);
        SummonProgression.LevelBonus(0.34f, 2, 60).Should().Be((int)(0.34f * (60 + 1 - 50)));
        SummonProgression.LevelBonus(0.34f, 3, 110).Should().Be((int)(0.34f * (110 + 1 - 100)));
    }

    [Test]
    public void Levels_come_from_the_cumulative_table_and_give_jp_once()
    {
        var up = SummonProgression.ResolveLevel(100, 1, 1, 1, Need);
        up.Level.Should().Be(5, "100 passes levels 1 to 4 (5, 17, 43, 100)");
        up.JpGained.Should().Be(4);
        up.MaxReachedLevel.Should().Be(5);

        // After a death penalty, the levels regained give no JP.
        SummonProgression.ResolveLevel(43, 1, 5, 5, Need).Should().Be(new SummonLevelChange(4, 5, 0));
        SummonProgression.ResolveLevel(100, 1, 5, 3, Need).JpGained.Should().Be(0);

        // Past the evolvable level a level gives two JP (overbreed).
        long Flat(int level) => level * 10L;
        SummonProgression.ResolveLevel(Flat(51), 1, 50, 50, Flat).Should().Be(new SummonLevelChange(52, 52, 4));
        SummonProgression.ResolveLevel(long.MaxValue / 2, 1, 1, 1, Flat).Level.Should().Be(60, "a first form stops at 60");
    }

    [Test]
    public void A_gain_is_capped_by_the_level_limit_and_the_form_maximum()
    {
        SummonProgression.CapGain(0, 1_000_000, 2, 1, Need).Should().Be(800, "2² × 200");
        SummonProgression.CapGain(0, 1_000_000, 2, 1, Need, force: true).Should().Be(1_000_000);
        SummonProgression.CapGain(0, -5, 2, 1, Need).Should().Be(0);
        SummonProgression.DeathPenalty(5, Need).Should().Be(0, "nothing up to level 5");
        SummonProgression.DeathPenalty(6, Need).Should().Be((long)(480 * (0.15 / 5 + 0.0005)));
    }

    private static CreatureCatalog Catalog() => new(Options.Create(new CreatureCatalogOptions
    {
        Summons =
        {
            new SummonResourceOptions { Id = 2101, Form = 1, CardId = 540014, StatId = 2101, RunSpeed = 100,
                AttackRange = 0.2f, Size = 2.4f, Scale = 1f, Stats = new float[] { 14, 7, 16, 5, 10, 8, 10 },
                LevelBonus = new[] { 0.34f, 0.17f, 0.38f, 0.12f, 0.24f, 0.19f, 0f } }
        },
        SummonExp = Exp.ToList(),
        Enhance = { new CreatureEnhanceOptions { Level = 0, SlotAmount = 2 } }
    }));

    private sealed class Harness
    {
        public readonly ICharacterService Characters = A.Fake<ICharacterService>();
        public readonly IPlayerVisibilityService Players = A.Fake<IPlayerVisibilityService>();
        public readonly CreatureService Service;
        public readonly GameClient Client;
        public readonly ConnectionInfo Info;
        public readonly CreatureCard Card;

        public Harness(int masterLevel, int summonLevel = 1, long exp = 0)
        {
            A.CallTo(() => Players.Registry).Returns(new PlayerRegistry());
            A.CallTo(() => Players.Observers(A<GameClient>._)).Returns(Array.Empty<GameClient>());
            var repository = A.Fake<IMonsterResourceRepository>();
            A.CallTo(() => repository.GetByIds(A<IReadOnlyCollection<int>>._)).Returns(Array.Empty<MonsterResourceEntity>());
            var world = new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions()));
            Service = new CreatureService(Catalog(), Characters, world, A.Fake<ICombatService>(),
                new SummonWorldService(Players), Players, runTicks: false);
            Client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
            Info = StorageTestHarness.Session(Client);
            Info.CharacterHandle = 7;
            Info.CharacterName = "Ana";
            Info.CharacterLevel = masterLevel;
            Info.X = Info.DestinationX = 100;
            Info.Y = Info.DestinationY = 100;
            Card = new CreatureCard
            {
                ItemId = 60, Code = 540014, Amount = 1, Flag = CreatureRules.WithSummonFlag(ItemFlag.None),
                SummonId = 9, SummonCode = 2101, SummonName = "RossParr", Level = summonLevel, Exp = exp,
                MaxReachedLevel = summonLevel
            };
            Info.CreatureCards[60] = Card;
            Info.SummonSlots = new long[] { 60, 0, 0, 0, 0, 0 };
        }

        public IEnumerable<ushort> Ids => ((StorageTestHarness.FrameConnection)Client.Connection).Sent
            .Select(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2)));
    }

    [Test]
    public void A_summon_out_and_below_its_master_takes_the_hunting_exp_and_levels()
    {
        var h = new Harness(masterLevel: 10);
        h.Service.Summon(h.Client, 60).Should().BeTrue();

        h.Service.OnExperienceGained(h.Client, 100);

        h.Card.Exp.Should().Be(100);
        h.Card.Level.Should().Be(5);
        h.Card.Jp.Should().Be(4);
        h.Ids.Should().Contain((ushort)GamePackets.TM_SC_LEVEL_UPDATE).And.Contain((ushort)GamePackets.TM_SC_EXP_UPDATE);
        h.Info.Summons[0].Entry.Level.Should().Be(5);
        h.Info.Summons[0].Hp.Should().Be((int)h.Info.Summons[0].Stats.MaxHp, "a level-up fills the summon");
        A.CallTo(() => h.Characters.SaveSummonProgressAsync("Ana",
            A<IReadOnlyList<SummonProgress>>.That.Matches(p => p.Single().Level == 5 && p.Single().Jp == 4)))
            .MustHaveHappened();
    }

    [Test]
    public void A_summon_kept_in_its_card_or_not_below_its_master_takes_nothing()
    {
        var kept = new Harness(masterLevel: 10);
        kept.Service.OnExperienceGained(kept.Client, 100);
        kept.Card.Exp.Should().Be(0, "a summon in its card gets m_fDeactiveSummonExpAmp = 0");

        var equal = new Harness(masterLevel: 5, summonLevel: 5, exp: 100);
        equal.Service.Summon(equal.Client, 60);
        equal.Service.OnExperienceGained(equal.Client, 100);
        equal.Card.Exp.Should().Be(100);
    }

    [Test]
    public void A_summon_above_its_master_caps_the_masters_gain()
    {
        var h = new Harness(masterLevel: 5, summonLevel: 8, exp: 1700);
        h.Service.OnLimitPlayerExperience(h.Client, 1_000_000).Should().Be(1_000_000, "the summon is not out");
        h.Service.Summon(h.Client, 60);
        h.Service.OnLimitPlayerExperience(h.Client, 1_000_000).Should().Be(SummonProgression.PlayerExpLimit(5));
    }

    [Test]
    public async Task The_summon_progress_is_written_to_its_row()
    {
        var options = new DbContextOptionsBuilder<TelecasterContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), o => o.EnableNullChecks(false)).Options;
        await using (var db = new TelecasterContext(options))
        {
            db.Characters.Add(new CharacterEntity { Id = 1, AccountId = 4, AccountName = "A", CharacterName = "Ana",
                Items = new List<ItemEntity>(), Skills = new List<CharacterSkillEntity>() });
            db.Summons.Add(new SummonEntity { Id = 9, AccountId = 4, CharacterId = 1, SummonResourceId = 2101,
                CardItemId = 60, Name = "RossParr", Lv = 1, Jlv = 1, MaxLevel = 1,
                PreviousLevel = new int[2], PreviousSummonResourceIds = new long[2] });
            await db.SaveChangesAsync();
        }

        var service = new CharacterService(A.Fake<IStarterItemsRepository>(), new CharacterRepositoryFactory(options),
            new CharacterGate(), NullLogger<CharacterService>.Instance);
        (await service.SaveSummonProgressAsync("Ana", new[]
        {
            new SummonProgress(9, 2102, 51, 7777268, 12, 51, 400, 300, 0, new long[] { 2101, 0 }, new[] { 50, 0 }, "RossParr")
        })).Should().BeTrue();

        await using var check = new TelecasterContext(options);
        var row = await check.Summons.SingleAsync();
        row.Lv.Should().Be(51);
        row.SummonResourceId.Should().Be(2102);
        row.Exp.Should().Be(7777268);
        row.Jp.Should().Be(12);
        row.MaxLevel.Should().Be(51);
        row.PreviousSummonResourceIds.Should().Equal(2101, 0);
        row.PreviousLevel.Should().Equal(50, 0);
    }
}
