using FluentAssertions;
using Navislamia.Configuration.Options;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Services;

namespace Tests.Game;

[TestFixture]
public class SkillCatalogTests
{
    private const int JobId = 300;
    private const int SkillId = 1003;
    private readonly Dictionary<int, byte> _prerequisites = new() { [1004] = 1 };

    [Test]
    public void Evaluate_AcceptsTheNextLevelAndReturnsItsJpCost()
    {
        var result = Catalog().Evaluate(JobId, 1, 3, SkillId, 0, 1, _prerequisites, 4);

        result.Result.Should().Be(ResultCode.Success);
        result.Cost.Should().Be(4);
    }

    [TestCase(0, 2, ResultCode.InvalidArgument)]
    [TestCase(2, 2, ResultCode.InvalidArgument)]
    [TestCase(2, 3, ResultCode.LimitMax)]
    public void Evaluate_RejectsSkippedOrUnsupportedLevels(byte currentLevel, byte targetLevel,
        ResultCode expected)
    {
        Catalog().Evaluate(JobId, 1, 3, SkillId, currentLevel, targetLevel, _prerequisites, 100)
            .Result.Should().Be(expected);
    }

    [Test]
    public void Evaluate_EnforcesJobLevelPrerequisiteAndJp()
    {
        var catalog = Catalog();

        catalog.Evaluate(JobId, 1, 2, SkillId, 0, 1, _prerequisites, 100).Result
            .Should().Be(ResultCode.NotEnoughJobLevel);
        catalog.Evaluate(JobId, 1, 3, SkillId, 0, 1, new Dictionary<int, byte>(), 100).Result
            .Should().Be(ResultCode.NotEnoughSkill);
        catalog.Evaluate(JobId, 1, 3, SkillId, 0, 1, _prerequisites, 3).Result
            .Should().Be(ResultCode.NotEnoughJP);
        catalog.Evaluate(999, 1, 3, SkillId, 0, 1, _prerequisites, 100).Result
            .Should().Be(ResultCode.LimitJob);
    }

    private static SkillCatalog Catalog()
    {
        return new SkillCatalog(new SkillCatalogOptions
        {
            Jobs =
            {
                new JobSkillCatalog
                {
                    JobId = JobId,
                    Skills =
                    {
                        new LearnableSkill
                        {
                            SkillId = SkillId,
                            JpCosts = new List<int> { 4, 10 },
                            Rules =
                            {
                                new SkillUnlockRule
                                {
                                    MinSkillLevel = 1,
                                    MaxSkillLevel = 2,
                                    RequiredLevel = 1,
                                    RequiredJobLevel = 3,
                                    Prerequisites =
                                    {
                                        new SkillPrerequisite { SkillId = 1004, Level = 1 }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });
    }
}
