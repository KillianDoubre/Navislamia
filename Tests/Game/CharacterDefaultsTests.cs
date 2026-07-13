using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services;

namespace Tests.Game;

[TestFixture]
public class CharacterDefaultsTests
{
    [TestCase((int)Race.Gaia, Job.Rogue)]
    [TestCase((int)Race.Deva, Job.Guide)]
    [TestCase((int)Race.Asura, Job.Stepper)]
    public void GetStarterJob_MapsEachRace(int race, Job expected)
    {
        CharacterDefaults.GetStarterJob(race).Should().Be(expected);
    }

    [Test]
    public void Apply_RepairsAnOldUninitializedCharacter()
    {
        var character = new CharacterEntity { Race = (int)Race.Asura };

        CharacterDefaults.Apply(character).Should().BeTrue();

        character.Lv.Should().Be(1);
        character.MaxReachedLv.Should().Be(1);
        character.CurrentJob.Should().Be(Job.Stepper);
        character.Jlv.Should().Be(1);
        character.JobDepth.Should().Be(JobDepth.Base);
        character.PreviousJobs.Should().Equal((Job)0, (Job)0, (Job)0);
        character.JobLvs.Should().Equal(0, 0, 0);
        character.ClientInfo.Should().Be(CharacterDefaults.DefaultClientInfo);
    }

    [Test]
    public void Apply_DoesNotReplaceValidProgression()
    {
        var character = new CharacterEntity
        {
            Race = (int)Race.Gaia,
            Lv = 25,
            MaxReachedLv = 25,
            CurrentJob = Job.Fighter,
            Jlv = 18,
            JobDepth = JobDepth.First,
            PreviousJobs = new[] { Job.Rogue, (Job)0, (Job)0 },
            JobLvs = new[] { 10, 0, 0 },
            ClientInfo = CharacterDefaults.DefaultClientInfo
        };

        CharacterDefaults.Apply(character).Should().BeFalse();

        character.CurrentJob.Should().Be(Job.Fighter);
        character.Jlv.Should().Be(18);
    }

    [Test]
    public void DefaultClientInfo_ContainsTheCompleteEpic73KeyMap()
    {
        CharacterDefaults.DefaultClientInfo.Should().StartWith("QS2=0,2,0|QS2=1,2,2");
        CharacterDefaults.DefaultClientInfo.Should().Contain("KMT=0,0,0,0,192");
        CharacterDefaults.DefaultClientInfo.Should().Contain("KMT=128,0,0,0,76");
        CharacterDefaults.DefaultClientInfo.Should().EndWith("CLIENTVER=1");
    }

    [Test]
    public async Task CharacterService_PersistsRepairsWhenCharactersAreLoaded()
    {
        var character = new CharacterEntity { Race = (int)Race.Gaia };
        var repository = A.Fake<ICharacterRepository>();
        A.CallTo(() => repository.GetCharactersByAccountNameAsync("account", true))
            .Returns(Task.FromResult<IEnumerable<CharacterEntity>>(new[] { character }));

        var service = new CharacterService(
            A.Fake<IStarterItemsRepository>(),
            repository,
            A.Fake<ILogger<CharacterService>>());

        await service.GetCharactersByAccountNameAsync("account", true);

        character.CurrentJob.Should().Be(Job.Rogue);
        A.CallTo(() => repository.SaveChangesAsync()).MustHaveHappenedOnceExactly();
    }
}
