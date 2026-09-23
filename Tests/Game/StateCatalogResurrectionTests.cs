using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

[TestFixture]
public class StateCatalogResurrectionTests
{
    [Test]
    public void TryGetResurrection_ReadsTheFirstFourValuesOfTheResurrectionStates()
    {
        var repository = A.Fake<IStateResourceRepository>();
        A.CallTo(() => repository.GetStatStates()).Returns(Array.Empty<StateEffectFields>());
        A.CallTo(() => repository.GetStateIds()).Returns(new[] { 13472, 4001 });
        A.CallTo(() => repository.GetStatesWithEffect((int)StateEffectType.Resurrection)).Returns(new[]
        {
            new StateEffectFields(13472, 109, new[] { 0.050m, 0.000m, 0.030m, 0.000m, 0.000m, 0.000m })
        });

        var catalog = new StateCatalog(repository);

        catalog.TryGetResurrection(13472, out var values).Should().BeTrue();
        values.Should().Be(new ResurrectionStateValues(0.05m, 0m, 0.03m, 0m));
        catalog.TryGetResurrection(4001, out _).Should().BeFalse("4001 exists but is not a resurrection state");
        catalog.Exists(4001).Should().BeTrue();
    }

    [Test]
    public void ResurrectionStateValues_ToleratesAShortValueArray()
    {
        ResurrectionStateValues.From(new[] { 0.1m }).Should().Be(new ResurrectionStateValues(0.1m, 0m, 0m, 0m));
        ResurrectionStateValues.From(null).Should().Be(default(ResurrectionStateValues));
    }

    [Test]
    public void ResurrectionEffect_IsTheReferenceStateEffect109()
    {
        ((int)StateEffectType.Resurrection).Should().Be(109, "SEF_RESURRECTION, NGemity StateBase.h:317");
    }
}
