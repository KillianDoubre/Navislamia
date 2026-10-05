using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Jobs;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

/// <summary>Talent skills, the master class skill reset and the race change (socle-changement-metier.md §8).</summary>
[TestFixture]
public class JobResetTests
{
    private const int DevaJobNpc = 2004;
    private const int BaseSkill = 1004, MasterSkill = 50000, TalentSkill = 41101, CreatureControl = 1801;

    /// <summary>
    /// Deva (race 4) 200 → 201 → 210 → master 220. 1004 is learnable to 3 in the base tree and to 5 in the master's; 50000 is a
    /// master skill (100 + 200 JP); 41101 a talent skill (1 TP a level).
    /// </summary>
    private static SkillCatalog Catalog() => new(new SkillCatalogOptions
    {
        Jobs =
        {
            Tree(200, Skill(BaseSkill, 3, 4, 10, 62), Skill(CreatureControl, 1, 7)),
            Tree(201), Tree(210),
            Tree(220, Skill(BaseSkill, 5, 4, 10, 62, 80, 90), Skill(MasterSkill, 2, 100, 200), Skill(TalentSkill, 2, -1, -1))
        }
    });

    private static JobSkillCatalog Tree(int job, params LearnableSkill[] skills)
    {
        var tree = new JobSkillCatalog { JobId = job };
        tree.Skills.AddRange(skills);
        return tree;
    }

    private static LearnableSkill Skill(int id, int max, params int[] costs) => new()
    {
        SkillId = id,
        JpCosts = costs.ToList(),
        Rules = { new SkillUnlockRule { MinSkillLevel = 1, MaxSkillLevel = max } }
    };

    private static readonly (int, int)[] MasterHistory = { (200, 10), (201, 40), (210, 49) };

    [Test]
    public void A_master_reset_takes_the_master_skills_back_and_lowers_what_the_older_trees_cap()
    {
        var learned = new Dictionary<int, byte> { [BaseSkill] = 5, [MasterSkill] = 2, [TalentSkill] = 2, [CreatureControl] = 1 };

        var reset = SkillResetRules.Reset(Catalog(), MasterHistory, 220, learned, JobChangeRules.MasterDepth);

        reset.Removed.Should().BeEquivalentTo(new[] { MasterSkill, TalentSkill });
        reset.Lowered.Should().Equal((BaseSkill, (byte)3));
        reset.Remaining.Should().BeEquivalentTo(new Dictionary<int, byte> { [BaseSkill] = 3, [CreatureControl] = 1 });
        reset.JpBack.Should().Be(100 + 200 + 80 + 90, "the master skill's two levels and 1004's levels 4 and 5");
        reset.TpBack.Should().Be(2);
    }

    [Test]
    public void A_reset_to_the_base_job_takes_every_skill_back()
    {
        var learned = new Dictionary<int, byte> { [BaseSkill] = 3, [CreatureControl] = 1 };

        var reset = SkillResetRules.Reset(Catalog(), MasterHistory, 220, learned, 0);

        reset.Remaining.Should().BeEmpty();
        reset.JpBack.Should().Be(4 + 10 + 62 + 7);
        SkillResetRules.Reset(Catalog(), MasterHistory, 220, learned, 4).Should().BeNull("deeper than the character");
    }

    [Test]
    public void The_reset_prices_and_texts_follow_the_reset_count()
    {
        SkillResetRules.ResetCount(new Dictionary<string, string>()).Should().Be(0);
        SkillResetRules.ResetCount(new Dictionary<string, string> { ["reset_count"] = "12" }).Should().Be(9);
        SkillResetRules.GoldCosts[2].Should().Be(5_000_000);
        SkillResetRules.JpCosts[0].Should().Be(2_000);
        SkillResetRules.ResetText(0).Should().Be("@90604793");
        SkillResetRules.ResetText(9).Should().Be("@90604802");
    }

    [Test]
    public void A_talent_skill_costs_talent_points_and_no_jp()
    {
        var learned = new Dictionary<int, byte>();

        var refused = Catalog().Evaluate(220, 150, 1, TalentSkill, 0, 1, learned, 1_000, availableTp: 0);
        refused.Result.Should().Be(ResultCode.NotEnoughTP);

        var learnt = Catalog().Evaluate(220, 150, 1, TalentSkill, 0, 1, learned, 0, availableTp: 2);
        learnt.Should().Be(new SkillLearnEvaluation(ResultCode.Success, 0, 1));
    }

    private sealed class Harness
    {
        public readonly ICharacterService Characters = A.Fake<ICharacterService>();
        public readonly IStatService Stats = A.Fake<IStatService>();
        public readonly ILevelingService Leveling = A.Fake<ILevelingService>();
        public readonly ISkillCastService Casts = A.Fake<ISkillCastService>();
        public readonly JobChangeService Service;
        public readonly GameClient Client;
        public readonly ConnectionInfo Info;
        public SkillResetWrite Written;

        public Harness(int resetCount = 0, int stones = 1)
        {
            A.CallTo(() => Stats.Compute(A<ConnectionInfo>._)).Returns(new CharacterStatResult(
                new StatBlock { MaxHp = 500, MaxMp = 300 }, new StatBlock()));
            // Every job level costs 10 JP, whatever the depth.
            long cost;
            A.CallTo(() => Leveling.TryGetNextJobLevelCost(A<int>._, A<int>._, out cost)).Returns(true)
                .AssignsOutAndRefParameters(10L);
            A.CallTo(() => Characters.GetFlagsAsync("Ana")).Returns(new Dictionary<string, string> { ["reset_count"] = resetCount.ToString() });
            A.CallTo(() => Characters.GetCarriedItemsAsync("Ana")).Returns(Enumerable.Range(0, stones)
                .Select(i => new ItemEntity { Id = 90 + i, ItemResourceId = SkillResetRules.RaceChangeItem, Amount = 1,
                    WearInfo = Navislamia.Game.DataAccess.Entities.Enums.ItemWearType.None }).ToArray());
            A.CallTo(() => Characters.ApplySkillResetAsync("Ana", A<SkillResetWrite>._)).ReturnsLazily((string _, SkillResetWrite write) =>
            {
                Written = write;
                return Task.FromResult(new SkillResetCommit(true,
                    write.Race is not { StoneResourceId: not 0 } ? null : new ItemEntity { Id = 90, ItemResourceId = SkillResetRules.RaceChangeItem, Amount = 0 }));
            });
            Service = new JobChangeService(Characters, Stats, A.Fake<IWarpService>(), skills: Catalog(), leveling: Leveling,
                casts: Casts);
            Client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
            Info = StorageTestHarness.Session(Client);
            Info.CharacterHandle = 7;
            Info.CharacterName = "Ana";
            Info.CharacterRace = 4;
            Info.CharacterJob = 220;
            Info.CharacterJobLevel = 5;
            Info.PreviousJobs.AddRange(MasterHistory);
            Info.CharacterJp = 1_000;
            Info.CharacterGold = 6_000_000;
            Info.CharacterTalentPoint = 0;
            Info.LearnedSkills[BaseSkill] = 5;
            Info.LearnedSkills[MasterSkill] = 2;
            Info.LearnedSkills[TalentSkill] = 2;
        }

        public List<byte[]> Sent => ((StorageTestHarness.FrameConnection)Client.Connection).Sent;

        public IEnumerable<(string Name, long Value)> Properties => Sent
            .Where(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2)) == (ushort)GamePackets.TM_SC_PROPERTY)
            .Select(p => (Encoding.ASCII.GetString(p, 12, 16).TrimEnd('\0'), BinaryPrimitives.ReadInt64LittleEndian(p.AsSpan(28, 8))));
    }

    [Test]
    public async Task A_master_class_sees_the_resets_priced_by_its_count()
    {
        var h = new Harness(resetCount: 2);

        var page = await h.Service.SelectAsync(h.Client, DevaJobNpc, JobChangeRules.ChangeJob, "NPC_JobChange_change_job()");

        page.Page.Text.Should().Be("@90604795");
        page.Page.Menu.Select(m => m.Trigger).Should().StartWith(new[] { "gold_skill_reset_check(npc_id)", "jp_skill_reset_check(npc_id)" });
    }

    [Test]
    public async Task A_gold_reset_pays_takes_the_master_skills_back_and_restarts_the_job_level()
    {
        var h = new Harness(resetCount: 2);

        var page = await h.Service.SelectAsync(h.Client, DevaJobNpc, SkillResetRules.GoldReset, "gold_skill_reset_check(npc_id)");

        page.Page.Text.Should().Be("@90604805");
        h.Info.CharacterGold.Should().Be(1_000_000, "the third reset costs 5 000 000");
        h.Info.LearnedSkills.Should().BeEquivalentTo(new Dictionary<int, byte> { [BaseSkill] = 3 });
        h.Info.CharacterJp.Should().Be(1_000 + 100 + 200 + 80 + 90 + 4 * 10, "the skills, then JLv 1 to 5 of the master class");
        h.Info.CharacterTalentPoint.Should().Be(2);
        h.Info.CharacterJobLevel.Should().Be(1);
        h.Written.Should().Match<SkillResetWrite>(w => w.ResetCount == 3 && w.Gold == 1_000_000 && w.JobLevel == 1 && w.Race == null);
        A.CallTo(() => h.Casts.TurnOffAurasOf(h.Client, A<IReadOnlyCollection<int>>.That.Contains(MasterSkill))).MustHaveHappened();
        var list = h.Sent.Single(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2)) == (ushort)GamePackets.TM_SC_SKILL_LIST);
        list[13].Should().Be(1, "modification_type REFRESH: the client drops what is not listed");
        h.Properties.Should().Contain(("tp", 2)).And.Contain(("job_level", 1));
    }

    [Test]
    public async Task A_reset_without_the_price_changes_nothing()
    {
        var h = new Harness(resetCount: 2);
        h.Info.CharacterJp = 100;

        var page = await h.Service.SelectAsync(h.Client, DevaJobNpc, SkillResetRules.JpReset, "jp_skill_reset_check(npc_id)");

        page.Page.Text.Should().Be("@90604806");
        h.Info.LearnedSkills.Should().HaveCount(3);
        A.CallTo(() => h.Characters.ApplySkillResetAsync(A<string>._, A<SkillResetWrite>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task The_race_change_returns_to_the_new_race_base_job_with_everything_paid_back()
    {
        var h = new Harness();
        h.Info.CharacterTalentPoint = 0;

        var offer = await h.Service.SelectAsync(h.Client, DevaJobNpc, SkillResetRules.ChangeRace, "NPC_JobChange_change_race()");
        offer.Page.Menu.Select(m => m.Trigger).Should().Equal("NPC_JobChange_set_race(5)", "NPC_JobChange_set_race(3)");

        var page = await h.Service.SelectAsync(h.Client, DevaJobNpc, SkillResetRules.SetRace, "NPC_JobChange_set_race(5)");

        page.Page.Text.Should().Be("@90010258");
        h.Info.CharacterRace.Should().Be(5);
        h.Info.CharacterJob.Should().Be(300);
        h.Info.PreviousJobs.Should().BeEmpty();
        h.Info.LearnedSkills.Should().BeEmpty();
        // Skills (4+10+62+80+90 + 100+200), master JLv 1-5 (40), then the jobs left: JLv 10 (90), 40 (390), 49 (480).
        h.Info.CharacterJp.Should().Be(1_000 + 546 + 40 + 90 + 390 + 480);
        h.Info.CharacterTalentPoint.Should().Be(0, "the talent skills' 2 TP come back and the master class's 2 go");
        h.Written.Race.Should().Be(new RaceChangeWrite(5, 300, SkillResetRules.RaceChangeItem));
        h.Properties.Should().Contain(("race", 5)).And.Contain(("job", 300)).And.Contain(("job_depth", 0));
        h.Sent.Select(p => BitConverter.ToUInt16(p, 4)).Should().Contain((ushort)GamePackets.TM_SC_DESTROY_ITEM);
    }

    [Test]
    public async Task The_race_change_needs_the_stone_and_another_race()
    {
        var h = new Harness(stones: 0);
        (await h.Service.SelectAsync(h.Client, DevaJobNpc, SkillResetRules.SetRace, "NPC_JobChange_set_race(5)"))
            .Page.Text.Should().Be("@90010257");

        h = new Harness();
        (await h.Service.SelectAsync(h.Client, DevaJobNpc, SkillResetRules.SetRace, "NPC_JobChange_set_race(4)"))
            .Page.Text.Should().Be("@90010259", "already a Deva");
        A.CallTo(() => h.Characters.ApplySkillResetAsync(A<string>._, A<SkillResetWrite>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task The_gm_race_change_is_the_npc_one_without_the_stone()
    {
        var h = new Harness(stones: 0);

        (await h.Service.ForceRaceAsync(h.Client, 5)).Should().Be(GmJobChange.Done);

        h.Info.CharacterRace.Should().Be(5);
        h.Info.CharacterJob.Should().Be(300);
        h.Info.PreviousJobs.Should().BeEmpty();
        h.Info.LearnedSkills.Should().BeEmpty();
        h.Info.CharacterJp.Should().Be(1_000 + 546 + 40 + 90 + 390 + 480, "the same refund as the NPC's change");
        h.Written.Race.Should().Be(new RaceChangeWrite(5, 300, 0), "stone 0: none is taken");
        h.Sent.Select(p => BitConverter.ToUInt16(p, 4)).Should().NotContain((ushort)GamePackets.TM_SC_DESTROY_ITEM);
        (await h.Service.ForceRaceAsync(h.Client, 5)).Should().Be(GmJobChange.AlreadyThere);
        (await h.Service.ForceRaceAsync(h.Client, 7)).Should().Be(GmJobChange.UnknownRace);
    }

    [Test]
    public async Task The_gm_job_change_rebuilds_the_path_from_the_base_job()
    {
        var h = new Harness();
        h.Info.CharacterJob = 201;
        h.Info.CharacterJobLevel = 45;
        h.Info.PreviousJobs.Clear();
        h.Info.PreviousJobs.Add((200, 12));
        IReadOnlyList<(int Job, int JobLevel)> saved = null;
        var grant = -1;
        A.CallTo(() => h.Characters.ChangeJobAsync("Ana", 220, A<IReadOnlyList<(int, int)>>._, A<int>._))
            .ReturnsLazily((string _, int _, IReadOnlyList<(int Job, int JobLevel)> previous, int tp) =>
            {
                saved = previous;
                grant = tp;
                return Task.FromResult<int?>(tp);
            });

        (await h.Service.ForceJobAsync(h.Client, 220)).Should().Be(GmJobChange.Done);

        saved.Should().Equal((200, 12), (201, 45), (210, 49));
        grant.Should().Be(JobChangeRules.MasterClassTalentPoints, "reaching the master class grants its talent points");
        h.Info.CharacterJob.Should().Be(220);
        h.Info.CharacterJobLevel.Should().Be(1);
        h.Info.PreviousJobs.Should().Equal((200, 12), (201, 45), (210, 49));
        h.Info.LearnedSkills.Should().HaveCount(3, "the skills stay");
        h.Properties.Should().Contain(("job", 220)).And.Contain(("job_depth", 3)).And.Contain(("job_2", 210));
    }

    [Test]
    public async Task The_gm_job_change_can_go_back_to_the_base_job_and_refuses_another_race()
    {
        var h = new Harness();
        A.CallTo(() => h.Characters.ChangeJobAsync("Ana", 200, A<IReadOnlyList<(int, int)>>._, 0)).Returns(0);

        (await h.Service.ForceJobAsync(h.Client, 200)).Should().Be(GmJobChange.Done);
        h.Info.PreviousJobs.Should().BeEmpty();
        h.Properties.Should().Contain(("job_depth", 0));

        (await h.Service.ForceJobAsync(h.Client, 301)).Should().Be(GmJobChange.NotInRaceTree, "an Asura job for a Deva");
        (await h.Service.ForceJobAsync(h.Client, 200)).Should().Be(GmJobChange.AlreadyThere);
    }

    [Test]
    public void The_path_to_a_job_follows_the_official_tree()
    {
        JobChangeRules.PathTo(4, 200).Should().Equal(200);
        JobChangeRules.PathTo(4, 213).Should().Equal(200, 202, 213);
        JobChangeRules.PathTo(3, 124).Should().Equal(100, 103, 114, 124);
        JobChangeRules.PathTo(4, 110).Should().BeNull();
        JobChangeRules.PathTo(9, 100).Should().BeNull();
        JobChangeRules.LeftJobLevel(1, 12).Should().Be(40);
        JobChangeRules.LeftJobLevel(0, 25).Should().Be(25);
    }
}
