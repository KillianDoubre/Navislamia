using System.Linq;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services;

namespace Tests.Game;

[TestFixture]
public class MonsterStateTests
{
    private const long Goblin = 42;
    private const long Orc = 43;
    private const int Poison = 500;
    private const int Slow = 501;

    private static MonsterWorldState Create()
    {
        var repository = A.Fake<IMonsterResourceRepository>();
        return new MonsterWorldState(repository, Options.Create(new MonsterSpawnOptions()));
    }

    [Test]
    public void AddState_TracksTheStateAgainstItsMonster()
    {
        var world = Create();

        var applied = world.AddState(Goblin, Poison, skillId: 3005, stateLevel: 3, startTick: 1000,
            endTick: 2000);

        applied.StateId.Should().Be(Poison);
        applied.StateLevel.Should().Be(3);
        applied.StateHandle.Should().NotBe(0);
        world.GetStates(Goblin).Should().ContainSingle();
        world.GetStates(Orc).Should().BeEmpty("states are sparse: an untouched monster carries none");
    }

    [Test]
    public void AddState_ReplacesTheSameStateAndReusesItsHandle()
    {
        var world = Create();

        var first = world.AddState(Goblin, Poison, 3005, 3, 1000, 2000);
        var recast = world.AddState(Goblin, Poison, 3005, 5, 1500, 4000);

        world.GetStates(Goblin).Should().ContainSingle("a recast replaces the active instance");
        recast.StateHandle.Should().Be(first.StateHandle, "the client tracks the state by its handle");
        world.GetStates(Goblin).Single().StateLevel.Should().Be(5);
        world.GetStates(Goblin).Single().EndTick.Should().Be(4000);
    }

    [Test]
    public void AddState_KeepsDifferentStatesSideBySide()
    {
        var world = Create();

        var poison = world.AddState(Goblin, Poison, 3005, 1, 1000, 2000);
        var slow = world.AddState(Goblin, Slow, 3006, 1, 1000, 2000);

        world.GetStates(Goblin).Should().HaveCount(2);
        slow.StateHandle.Should().NotBe(poison.StateHandle);
    }

    [Test]
    public void RemoveExpiredStates_OnlyDropsThoseWhoseDeadlineHasPassed()
    {
        var world = Create();
        world.AddState(Goblin, Poison, 3005, 1, 1000, 2000);
        world.AddState(Goblin, Slow, 3006, 1, 1000, 5000);

        world.RemoveExpiredStates(1500).Should().BeEmpty("neither deadline has passed yet");
        world.GetStates(Goblin).Should().HaveCount(2);

        var expired = world.RemoveExpiredStates(2500);
        expired.Should().ContainSingle();
        expired.Single().InstanceId.Should().Be(Goblin);
        expired.Single().State.StateId.Should().Be(Poison);
        world.GetStates(Goblin).Should().ContainSingle();
    }

    [Test]
    public void RemoveExpiredStates_SweepsEveryMonster()
    {
        var world = Create();
        world.AddState(Goblin, Poison, 3005, 1, 1000, 2000);
        world.AddState(Orc, Poison, 3005, 1, 1000, 2000);

        world.RemoveExpiredStates(3000).Should().HaveCount(2);
        world.GetStates(Goblin).Should().BeEmpty();
        world.GetStates(Orc).Should().BeEmpty();
    }

    [Test]
    public void ClearStates_DropsEverythingOnDeathAndReturnsWhatWasRemoved()
    {
        var world = Create();
        world.AddState(Goblin, Poison, 3005, 1, 1000, 9000);
        world.AddState(Goblin, Slow, 3006, 1, 1000, 9000);

        var cleared = world.ClearStates(Goblin);

        cleared.Should().HaveCount(2, "a corpse keeps no debuff, and a respawn inherits none");
        world.GetStates(Goblin).Should().BeEmpty();
        world.ClearStates(Goblin).Should().BeEmpty("clearing twice is harmless");
    }
}
