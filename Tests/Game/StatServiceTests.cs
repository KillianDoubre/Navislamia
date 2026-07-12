using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services;

namespace Tests.Game;

[TestFixture]
public class StatServiceTests
{
    private const int DevaRace = 4;

    [Test]
    public void Compute_UsesDbStats_WhenRowPresent()
    {
        var repo = A.Fake<IStatResourceRepository>();
        A.CallTo(() => repo.GetById(A<int>._)).Returns(new StatResourceEntity
        {
            Strength = 20, Vitality = 21, Dexterity = 22, Agility = 23,
            Intelligence = 24, Wisdom = 25, Luck = 26
        });
        var service = new StatService(repo);

        var stats = service.Compute(DevaRace, level: 1);

        stats.Strength.Should().Be(20);
        stats.Vitality.Should().Be(21);
        stats.Mentality.Should().Be(25);
        stats.MaxHp.Should().Be(50 + 21 * 10 + 1 * 20);
        stats.MaxMp.Should().Be(30 + 25 * 8 + 1 * 10);
    }

    [Test]
    public void Compute_FallsBackToStaticBaseline_WhenRowMissing()
    {
        var repo = A.Fake<IStatResourceRepository>();
        A.CallTo(() => repo.GetById(A<int>._)).Returns(null);
        var service = new StatService(repo);

        var stats = service.Compute(DevaRace, level: 10);

        stats.Strength.Should().BeGreaterThan(0);
        stats.Vitality.Should().BeGreaterThan(0);
        stats.MaxHp.Should().Be(50 + stats.Vitality * 10 + 10 * 20);
        stats.MaxMp.Should().Be(30 + stats.Mentality * 8 + 10 * 10);
    }
}
