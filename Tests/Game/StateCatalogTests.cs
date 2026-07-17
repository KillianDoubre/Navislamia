using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

[TestFixture]
public class StateCatalogTests
{
    private const int QuickPace = 2622;
    private const uint MoveSpeedMask = 8192;
    private const uint DefenceMask = 512;
    private const uint BothDefencesMask = 1536;

    private static decimal[] Values(params decimal[] values)
    {
        var full = new decimal[20];
        values.CopyTo(full, 0);
        return full;
    }

    private static StateEffectFields State(int id, int effectType, decimal[] values)
    {
        return new StateEffectFields(id, effectType, values);
    }

    private static StateCatalog Create(params StateEffectFields[] states)
    {
        var repository = A.Fake<IStateResourceRepository>();
        A.CallTo(() => repository.GetStatStates()).Returns(states);
        return new StateCatalog(repository);
    }

    [Test]
    public void Resolve_ReadsTheFirstTripletAsMaskBaseAndPerLevel()
    {
        var catalog = Create(State(QuickPace, StateCatalog.ParameterInc, Values(MoveSpeedMask, 0, 1)));

        catalog.Resolve(QuickPace, 5).Should()
            .Equal(new[] { new StatEffect(StatTarget.MoveSpeed, 5f, false) },
                "Quick Pace is mask 8192 = Move Speed");
        catalog.Resolve(QuickPace, 1).Should().Equal(new StatEffect(StatTarget.MoveSpeed, 1f, false));
    }

    [Test]
    public void Resolve_AppliesBasePlusPerLevel()
    {
        var catalog = Create(State(1, StateCatalog.ParameterInc, Values(MoveSpeedMask, 200, 10)));

        catalog.Resolve(1, 3).Should().Equal(new StatEffect(StatTarget.MoveSpeed, 230f, false));
    }

    [Test]
    public void Resolve_ExpandsAMultiBitMaskIntoOneEffectPerParameter()
    {
        var catalog = Create(State(1, StateCatalog.ParameterInc, Values(BothDefencesMask, 0, -3)));

        catalog.Resolve(1, 2).Should().Equal(
            new StatEffect(StatTarget.Defence, -6f, false),
            new StatEffect(StatTarget.MagicDefence, -6f, false));
    }

    [Test]
    public void Resolve_ReadsAllFourParameterATriplets()
    {
        var catalog = Create(State(1, StateCatalog.ParameterInc,
            Values(DefenceMask, 1, 0, MoveSpeedMask, 2, 0, 0, 0, 0, 0, 0, 0, 65536, 3, 0, 2, 4, 0)));

        catalog.Resolve(1, 1).Should().Equal(
            new StatEffect(StatTarget.Defence, 1f, false),
            new StatEffect(StatTarget.MoveSpeed, 2f, false),
            new StatEffect(StatTarget.Critical, 3f, false),
            new StatEffect(StatTarget.Vitality, 4f, false));
    }

    [Test]
    public void Resolve_SkipsTheParameterBTriplets()
    {
        var catalog = Create(State(1, StateCatalog.ParameterInc,
            Values(0, 0, 0, 0, 0, 0, DefenceMask, 50, 0, MoveSpeedMask, 60, 0)));

        catalog.Resolve(1, 1).Should()
            .BeEmpty("triplets 2 and 3 address ParameterB, which is not decoded");
    }

    [Test]
    public void Resolve_MarksAnAmplifyStateAsAPercentage()
    {
        var catalog = Create(State(1, StateCatalog.ParameterAmp, Values(MoveSpeedMask, 0, 5)));

        catalog.Resolve(1, 2).Should().Equal(new StatEffect(StatTarget.MoveSpeed, 10f, true));
    }

    [Test]
    public void Resolve_IgnoresUnsupportedEffectTypesAndEmptyStates()
    {
        StateCatalog.BuildTemplates(State(1, 46, Values(MoveSpeedMask, 0, 1)))
            .Should().BeEmpty("only ParameterInc and ParameterAmp are decoded");
        StateCatalog.BuildTemplates(State(1, StateCatalog.ParameterInc, Values()))
            .Should().BeEmpty();
        StateCatalog.BuildTemplates(State(1, StateCatalog.ParameterInc, null))
            .Should().BeEmpty();
        StateCatalog.BuildTemplates(State(1, StateCatalog.ParameterInc, Values(0, 5, 5)))
            .Should().BeEmpty("a zero mask addresses nothing");
    }

    [Test]
    public void Resolve_ReturnsNothingForAnUnknownStateOrZeroLevel()
    {
        var catalog = Create(State(QuickPace, StateCatalog.ParameterInc, Values(MoveSpeedMask, 0, 1)));

        catalog.Resolve(9999, 1).Should().BeEmpty();
        catalog.Resolve(QuickPace, 0).Should().BeEmpty();
    }
}
