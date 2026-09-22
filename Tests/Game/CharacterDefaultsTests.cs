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

    [Test]
    public async Task CharacterService_AwaitsProgressPersistenceBeforeReturning()
    {
        var character = new CharacterEntity { CharacterName = "Character" };
        var repository = A.Fake<ICharacterRepository>();
        A.CallTo(() => repository.GetCharacterByName("Character")).Returns(character);

        var service = new CharacterService(
            A.Fake<IStarterItemsRepository>(),
            repository,
            A.Fake<ILogger<CharacterService>>());

        await service.SaveProgressAsync("Character", 1, 5, 100, 200, 300, 400, 0f, 0f, true);

        character.Lv.Should().Be(1);
        character.MaxReachedLv.Should().Be(1);
        character.Jlv.Should().Be(5);
        character.Exp.Should().Be(100);
        character.Jp.Should().Be(200);
        character.Gold.Should().Be(300);
        character.Chaos.Should().Be(400);
        character.PkMode.Should().BeTrue();
        A.CallTo(() => repository.SaveChangesAsync()).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task CharacterService_PersistsLearnedSkillAndRemainingJpTogether()
    {
        var character = new CharacterEntity
        {
            CharacterName = "Character",
            Jp = 100,
            Skills = new List<CharacterSkillEntity>()
        };
        var repository = A.Fake<ICharacterRepository>();
        A.CallTo(() => repository.GetCharacterByName("Character")).Returns(character);
        var service = new CharacterService(A.Fake<IStarterItemsRepository>(), repository,
            A.Fake<ILogger<CharacterService>>());

        (await service.SaveLearnedSkillAsync("Character", 1004, 1, 96)).Should().BeTrue();

        character.Jp.Should().Be(96);
        character.Skills.Should().ContainSingle(skill => skill.SkillId == 1004 && skill.Level == 1);
        A.CallTo(() => repository.SaveChangesAsync()).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task CharacterService_ConsumeItem_DecrementsAStack()
    {
        var item = new ItemEntity { Id = 7, Amount = 5, Idx = 1 };
        var (service, repository) = ServiceWithItems(item);

        (await service.ConsumeItemAsync("Character", 7, 1)).Should().Be(4);

        item.Amount.Should().Be(4);
        A.CallTo(() => repository.DeleteItem(A<ItemEntity>._)).MustNotHaveHappened();
        A.CallTo(() => repository.SaveChangesAsync()).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task CharacterService_ConsumeItem_DeletesTheLastUnit()
    {
        var item = new ItemEntity { Id = 7, Amount = 1, Idx = 1 };
        var other = new ItemEntity { Id = 8, Amount = 1, Idx = 2 };
        var (service, repository) = ServiceWithItems(item, other);

        (await service.ConsumeItemAsync("Character", 7, 1)).Should().Be(0);

        A.CallTo(() => repository.DeleteItem(item)).MustHaveHappenedOnceExactly();
        other.Idx.Should().Be(1);
        A.CallTo(() => repository.SaveChangesAsync()).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task CharacterService_ConsumeItem_ReportsAnUnknownHandle()
    {
        var (service, repository) = ServiceWithItems(new ItemEntity { Id = 7, Amount = 1, Idx = 1 });

        (await service.ConsumeItemAsync("Character", 99, 1)).Should().BeNull();

        A.CallTo(() => repository.SaveChangesAsync()).MustNotHaveHappened();
    }

    [Test]
    public async Task CharacterService_RemoveItem_RefusesWhatTheRuleRejectsWithoutTouchingTheStack()
    {
        var item = new ItemEntity { Id = 7, Amount = 5, Idx = 1, WearInfo = ItemWearType.Weapon };
        var (service, repository) = ServiceWithItems(item);

        var removal = await service.RemoveItemAsync("Character", 7,
            entry => entry.WearInfo == ItemWearType.None ? 1 : 0);

        removal.Item.Should().BeSameAs(item);
        removal.Removed.Should().Be(0);
        item.Amount.Should().Be(5);
        A.CallTo(() => repository.DeleteItem(A<ItemEntity>._)).MustNotHaveHappened();
        A.CallTo(() => repository.SaveChangesAsync()).MustNotHaveHappened();
    }

    [Test]
    public async Task CharacterService_RemoveItem_TakesTheResolvedUnitsAndDeletesAnEmptiedStack()
    {
        var partial = new ItemEntity { Id = 7, Amount = 5, Idx = 1 };
        var whole = new ItemEntity { Id = 8, Amount = 2, Idx = 2 };
        var (service, repository) = ServiceWithItems(partial, whole);

        (await service.RemoveItemAsync("Character", 7, _ => 3)).Removed.Should().Be(3);
        partial.Amount.Should().Be(2);

        (await service.RemoveItemAsync("Character", 8, _ => 9)).Removed.Should().Be(2);
        A.CallTo(() => repository.DeleteItem(whole)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task CharacterService_RemoveItem_ReportsAnUnknownHandle()
    {
        var (service, _) = ServiceWithItems(new ItemEntity { Id = 7, Amount = 1, Idx = 1 });

        var removal = await service.RemoveItemAsync("Character", 99, _ => 1);

        removal.Item.Should().BeNull();
        removal.Removed.Should().Be(0);
    }

    private static (CharacterService, ICharacterRepository) ServiceWithItems(params ItemEntity[] items)
    {
        var character = new CharacterEntity { CharacterName = "Character", Items = items.ToList() };
        var repository = A.Fake<ICharacterRepository>();
        A.CallTo(() => repository.GetCharacterByNameWithItems("Character")).Returns(character);
        return (new CharacterService(A.Fake<IStarterItemsRepository>(), repository,
            A.Fake<ILogger<CharacterService>>()), repository);
    }
}
