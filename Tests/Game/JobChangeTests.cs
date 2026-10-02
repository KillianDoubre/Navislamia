using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Interfaces;
using Navislamia.Game.Services.Jobs;
using Navislamia.Game.Services.Party;
using Navislamia.Game.Services.Rates;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

/// <summary>The job change of the official Epic 7 Lua (docs/packet-specs/socle-changement-metier.md).</summary>
[TestFixture]
public class JobChangeTests
{
    private const int DevaJobNpc = 1004;

    [Test]
    public void The_menus_are_the_lua_ones()
    {
        JobChangeRules.NextJobs(4, 200, 0).Should().Equal(201, 202, 203);
        JobChangeRules.NextJobs(5, 300, 0).Should().Equal(301, 302, 303);
        JobChangeRules.NextJobs(3, 100, 0).Should().Equal(101, 102, 103);
        JobChangeRules.NextJobs(4, 201, 1).Should().Equal(210, 211);
        JobChangeRules.NextJobs(4, 203, 1).Should().Equal(214);
        JobChangeRules.NextJobs(5, 302, 1).Should().Equal(312, 313);
        JobChangeRules.NextJobs(3, 113, 2).Should().Equal(123);
        JobChangeRules.NextJobs(4, 214, 2).Should().Equal(224);
        JobChangeRules.NextJobs(4, 224, 3).Should().BeEmpty("a master class goes no further");
        JobChangeRules.NextJobs(4, 200, 1).Should().BeEmpty("a base job at depth 1 is not a state the Lua knows");
    }

    [Test]
    public void The_requirements_are_10_10_then_50_40_then_147_49()
    {
        JobChangeRules.MeetsRequirement(0, 10, 10).Should().BeTrue();
        JobChangeRules.MeetsRequirement(0, 10, 9).Should().BeFalse();
        JobChangeRules.MeetsRequirement(1, 50, 40).Should().BeTrue();
        JobChangeRules.MeetsRequirement(1, 49, 50).Should().BeFalse();
        JobChangeRules.MeetsRequirement(2, 147, 49).Should().BeTrue();
        JobChangeRules.MeetsRequirement(2, 146, 50).Should().BeFalse();
        JobChangeRules.MeetsRequirement(3, 300, 60).Should().BeFalse();
        JobChangeRules.IsAbleToJobChange(2, 145, 49).Should().BeTrue("is_able_to_jobchange says 145");
    }

    [Test]
    public void The_texts_and_triggers_match_the_lua()
    {
        JobChangeRules.Title(4004).Should().Be("@90400401");
        JobChangeRules.NpcString(7032, 6).Should().Be("@90703206");
        JobChangeRules.Title(JobChangeRules.MasterNpcId).Should().Be("@91002405");
        JobChangeRules.ConfirmText(210).Should().Be("@90700411");
        JobChangeRules.ConfirmText(314).Should().Be("@90700420");
        JobChangeRules.ConfirmText(114).Should().Be("@90700425");
        JobChangeRules.ConfirmText(101).Should().Be("@90301919");
        JobChangeRules.ConfirmText(203).Should().Be("@90301915");
        JobChangeRules.ConfirmText(301).Should().Be("@90301916");
        JobChangeRules.ConfirmText(124).Should().Be("@91002414");
        JobChangeRules.ConfirmText(220).Should().Be("@91002415");
        JobChangeRules.ConfirmText(324).Should().Be("@91002424");
        JobChangeRules.ShortJobName(201).Should().Be("@1357");
        JobChangeRules.ShortJobName(303).Should().Be("@1375");

        var trigger = JobChangeRules.Trigger(JobChangeRules.CheckCommon, 201);
        trigger.Should().Be("Run_JobChange_check_common( '@10201' , 201 )");
        JobChangeRules.TryReadJobTrigger(trigger, out var function, out var job).Should().BeTrue();
        (function, job).Should().Be((JobChangeRules.CheckCommon, 201));
        JobChangeRules.TryReadJobTrigger("Run_JobChange_common( '@10210' , 201 )", out _, out _)
            .Should().BeFalse("the name must be the job's own");
        JobChangeRules.Sconv("@90010017", "#@job_name@#", "@10201").Should().Be("@90010017\v#@job_name@#\v@10201");
    }

    [Test]
    public void A_change_keeps_the_left_job_and_grants_talent_points_to_the_master_class()
    {
        var first = JobChangeRules.Apply(Array.Empty<(int, int)>(), 200, 12, 201);
        first.Should().Match<JobChangeResult>(r => r.Job == 201 && r.JobLevel == 1 && r.Depth == 1 && r.TalentPoints == 0);
        first.PreviousJobs.Should().Equal((200, 12));

        var master = JobChangeRules.Apply(new[] { (200, 10), (201, 45) }, 210, 50, 220);
        master.Depth.Should().Be(3);
        master.TalentPoints.Should().Be(JobChangeRules.MasterClassTalentPoints);
    }

    private sealed class Harness
    {
        public readonly ICharacterService Characters = A.Fake<ICharacterService>();
        public readonly IStatService Stats = A.Fake<IStatService>();
        public readonly IQuestService Quests = A.Fake<IQuestService>();
        public readonly IPartyService Parties = A.Fake<IPartyService>();
        public readonly IWarpService Warp = A.Fake<IWarpService>();
        public readonly JobChangeService Service;
        public readonly GameClient Client;
        public readonly ConnectionInfo Info;

        public Harness(int race = 4, int job = 200, int level = 10, int jobLevel = 10)
        {
            A.CallTo(() => Stats.Compute(A<ConnectionInfo>._)).Returns(new CharacterStatResult(
                new StatBlock { MaxHp = 500, MaxMp = 300 }, new StatBlock()));
            A.CallTo(() => Characters.ChangeJobAsync(A<string>._, A<int>._, A<IReadOnlyList<(int, int)>>._, A<int>._))
                .ReturnsLazily((string _, int _, IReadOnlyList<(int, int)> _, int tp) => Task.FromResult<int?>(tp));
            Service = new JobChangeService(Characters, Stats, Warp, Quests, Parties);
            Client = StorageTestHarness.NewGameClient(new StorageTestHarness.FrameConnection(Array.Empty<byte>()));
            Info = StorageTestHarness.Session(Client);
            Info.CharacterHandle = 7;
            Info.CharacterName = "Ana";
            Info.CharacterRace = race;
            Info.CharacterJob = job;
            Info.CharacterLevel = level;
            Info.CharacterJobLevel = jobLevel;
            Info.CharacterHp = 900;
        }

        public List<byte[]> Sent => ((StorageTestHarness.FrameConnection)Client.Connection).Sent;

        public IEnumerable<(string Name, long Value)> Properties => Sent
            .Where(p => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4, 2)) == (ushort)GamePackets.TM_SC_PROPERTY)
            .Select(p => (Encoding.ASCII.GetString(p, 12, 16).TrimEnd('\0'),
                BinaryPrimitives.ReadInt64LittleEndian(p.AsSpan(28, 8))));
    }

    [Test]
    public async Task The_job_npc_offers_the_race_jobs_once_the_requirement_is_met()
    {
        var h = new Harness();
        var step = await h.Service.SelectAsync(h.Client, DevaJobNpc, JobChangeRules.ChangeJob, "NPC_JobChange_change_job()");

        step.Page.Text.Should().Be("@90100404");
        step.Page.Menu.Select(m => m.Trigger).Should().StartWith(new[]
        {
            "Run_JobChange_check_common( '@10201' , 201 )",
            "Run_JobChange_check_common( '@10202' , 202 )",
            "Run_JobChange_check_common( '@10203' , 203 )"
        });

        var low = new Harness(jobLevel: 9);
        (await low.Service.SelectAsync(low.Client, DevaJobNpc, JobChangeRules.ChangeJob, string.Empty))
            .Page.Text.Should().Be("@90100403", "the NPC's own 'not yet' text");
    }

    [Test]
    public async Task Confirming_changes_the_job_and_sends_the_properties()
    {
        var h = new Harness();
        var step = await h.Service.SelectAsync(h.Client, DevaJobNpc, JobChangeRules.Common,
            JobChangeRules.Trigger(JobChangeRules.Common, 202));
        step.CommitJob.Should().Be(202);
        step.Page.Text.Should().Be("@90100406\v#@job_name@#\v@10202");

        (await h.Service.CommitAsync(h.Client, DevaJobNpc, 202, false)).Should().BeTrue();

        h.Info.CharacterJob.Should().Be(202);
        h.Info.CharacterJobLevel.Should().Be(1);
        h.Info.PreviousJobs.Should().Equal((200, 10));
        h.Info.CharacterHp.Should().Be(500, "clamped to the new maximum");
        A.CallTo(() => h.Characters.ChangeJobAsync("Ana", 202,
            A<IReadOnlyList<(int, int)>>.That.Matches(l => l.Count == 1 && l[0].Item1 == 200 && l[0].Item2 == 10), 0))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => h.Parties.OnJobChanged(h.Client)).MustHaveHappenedOnceExactly();

        var properties = h.Properties.ToList();
        properties.Take(2).Should().Equal(("job_0", 200), ("jlv_0", 10));
        properties.Should().Contain(("job", 202)).And.Contain(("job_depth", 1)).And.Contain(("job_level", 1));
        h.Sent.Select(Id).Should().Contain((ushort)GamePackets.TM_SC_STAT_INFO);
        Id(h.Sent.Last()).Should().Be((ushort)GamePackets.TM_SC_CHAT);
    }

    private static ushort Id(byte[] packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));

    [Test]
    public async Task A_change_is_refused_when_the_character_no_longer_qualifies_or_picks_another_race_job()
    {
        var h = new Harness(jobLevel: 9);
        (await h.Service.CommitAsync(h.Client, DevaJobNpc, 201, false)).Should().BeFalse();

        h = new Harness();
        (await h.Service.CommitAsync(h.Client, DevaJobNpc, 301, false)).Should().BeFalse("an Asura job for a Deva");
        (await h.Service.SelectAsync(h.Client, DevaJobNpc, JobChangeRules.Common,
            JobChangeRules.Trigger(JobChangeRules.Common, 301))).Should().Be(JobChangeStep.Nothing);
        A.CallTo(() => h.Characters.ChangeJobAsync(A<string>._, A<int>._, A<IReadOnlyList<(int, int)>>._, A<int>._))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task The_master_class_needs_its_npc_and_quest_3322()
    {
        var h = new Harness(job: 210, level: 150, jobLevel: 50);
        h.Info.PreviousJobs.Add((200, 10));
        h.Info.PreviousJobs.Add((201, 45));

        (await h.Service.CommitAsync(h.Client, JobChangeRules.MasterNpcId, 220, false)).Should().BeFalse("quest 3322 not done");

        A.CallTo(() => h.Quests.GetQuestProgressAsync(h.Client, JobChangeRules.MasterClassQuest)).Returns(255);
        (await h.Service.CommitAsync(h.Client, DevaJobNpc, 220, false)).Should().BeFalse("only the master NPC does it");
        var page = await h.Service.SelectAsync(h.Client, JobChangeRules.MasterNpcId, JobChangeRules.MasterContact, string.Empty);
        page.Page.Text.Should().Be("@91002409");
        page.Page.Menu[0].Trigger.Should().Be("Run_JobChange_check_common( '@10220' , 220 )");

        (await h.Service.CommitAsync(h.Client, JobChangeRules.MasterNpcId, 220, false)).Should().BeTrue();
        h.Properties.Should().Contain(("job_depth", 3)).And.Contain(("tp", 2));
    }

    [Test]
    public async Task The_tutorial_npc_changes_the_base_job_only_after_quest_6_3()
    {
        var h = new Harness();
        var page = await h.Service.SelectAsync(h.Client, JobChangeRules.TutorialNpcId, JobChangeRules.TutorialChangeJob, string.Empty);
        page.Page.Menu.Should().NotContain(m => m.Trigger == "Quest_Link_6_3()");

        A.CallTo(() => h.Quests.GetQuestProgressAsync(h.Client, JobChangeRules.TutorialQuest63)).Returns(255);
        page = await h.Service.SelectAsync(h.Client, JobChangeRules.TutorialNpcId, JobChangeRules.TutorialChangeJob, string.Empty);
        page.Page.Menu.Should().Contain(m => m.Trigger == "Quest_Link_6_3()");

        (await h.Service.CommitAsync(h.Client, JobChangeRules.TutorialNpcId, 203, tutorial: true)).Should().BeTrue();
        (await h.Service.CommitAsync(h.Client, JobChangeRules.TutorialNpcId, 214, tutorial: true))
            .Should().BeFalse("the tutorial only changes the base job");
    }

    [Test]
    public void A_skill_of_a_job_left_behind_is_learned_with_that_job_level()
    {
        var catalog = new SkillCatalog(new SkillCatalogOptions
        {
            Jobs =
            {
                Job(200, 1004, requiredJobLevel: 8),
                Job(201, 1105, requiredJobLevel: 1)
            }
        });
        var learned = new Dictionary<int, byte>();
        var history = new[] { (200, 10) };

        catalog.EvaluateAcrossJobs(history, 201, 20, 1, 1004, 0, 1, learned, 100).Result
            .Should().Be(ResultCode.Success, "the base job reached JLv 10");
        catalog.Evaluate(201, 20, 1, 1004, 0, 1, learned, 100).Result
            .Should().Be(ResultCode.LimitJob, "the current tree alone does not hold it");
        catalog.EvaluateAcrossJobs(new[] { (200, 5) }, 201, 20, 1, 1004, 0, 1, learned, 100).Result
            .Should().Be(ResultCode.LimitJob, "the official loop answers with the last tree it tried, the current one");
        catalog.EvaluateAcrossJobs(history, 201, 20, 1, 1105, 0, 1, learned, 100).Result
            .Should().Be(ResultCode.Success, "then the current tree");
    }

    private static JobSkillCatalog Job(int jobId, int skillId, int requiredJobLevel) => new()
    {
        JobId = jobId,
        Skills =
        {
            new LearnableSkill
            {
                SkillId = skillId,
                JpCosts = new List<int> { 4 },
                Rules = { new SkillUnlockRule { MinSkillLevel = 1, MaxSkillLevel = 1, RequiredJobLevel = requiredJobLevel } }
            }
        }
    };

    [Test]
    public void A_job_level_costs_the_tier_of_its_depth()
    {
        var repository = A.Fake<ILevelResourceRepository>();
        A.CallTo(() => repository.GetAll()).Returns(new[]
        {
            new Navislamia.Game.DataAccess.Entities.Arcadia.LevelResourceEntity { Level = 1, NormalExp = 0, JLvs = new[] { 3 } }
        });
        var costs = new JobLevelCostOptions
        {
            Depths = new[]
            {
                new long[] { 0, 3, 0 },
                new long[] { 0, 20, 30 },
                new long[] { 0, 210, 300 },
                new long[] { 0, 8200, 24_293_159_660 }
            }
        };
        var leveling = new LevelingService(repository, A.Fake<IStatService>(),
            new RateService(new StaticOptionsMonitor<RatesOptions>(new RatesOptions { EventStatePath = "" })),
            jobLevelCosts: Options.Create(costs));

        leveling.TryGetNextJobLevelCost(0, 2, out _).Should().BeFalse("the base tier caps here");
        leveling.TryGetNextJobLevelCost(1, 2, out var cost).Should().BeTrue();
        cost.Should().Be(30);
        leveling.TryGetNextJobLevelCost(3, 2, out cost).Should().BeTrue();
        cost.Should().Be(24_293_159_660, "the master tier needs 64 bits");
    }
}
