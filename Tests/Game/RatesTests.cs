using FakeItEasy;
using FluentAssertions;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services;
using Navislamia.Game.Services.GmCommands;
using Navislamia.Game.Services.Rates;

namespace Tests.Game;

[TestFixture]
public class RateMathTests
{
    private sealed class FixedRandom : Random
    {
        private readonly double _value;

        public FixedRandom(double value) => _value = value;

        public override double NextDouble() => _value;
    }

    [Test]
    public void ScaleRandom_RoundsTheFractionAtRandom()
    {
        RateMath.ScaleRandom(7, 1.5, new FixedRandom(0.49)).Should().Be(11, "a draw under the fraction rounds up");
        RateMath.ScaleRandom(7, 1.5, new FixedRandom(0.5)).Should().Be(10, "a draw at or over it rounds down");
    }

    [Test]
    public void ScaleRandom_IsExactOnAverage()
    {
        var random = new Random(1234);
        var total = 0L;
        for (var i = 0; i < 100_000; i++)
        {
            total += RateMath.ScaleRandom(7, 1.5, random);
        }

        (total / 100_000.0).Should().BeApproximately(10.5, 0.01,
            "NGemity's rounding always truncates; the intended one must not lose half a point per kill");
    }

    [Test]
    public void ScaleRandom_KeepsWholeProductsAndRefusesNonsense()
    {
        RateMath.ScaleRandom(30, 2, new FixedRandom(0)).Should().Be(60);
        RateMath.ScaleRandom(30, 0, new FixedRandom(0)).Should().Be(0);
        RateMath.ScaleRandom(-5, 2, new FixedRandom(0)).Should().Be(0);
        RateMath.ScaleRandom(30, double.NaN, new FixedRandom(0)).Should().Be(0);
        RateMath.ScaleRandom(long.MaxValue, 3, new FixedRandom(0)).Should().Be(long.MaxValue);
    }

    [Test]
    public void ScaleCost_RoundsUpSoOnlyAZeroRateIsFree()
    {
        RateMath.ScaleCost(7, 0.5).Should().Be(4);
        RateMath.ScaleCost(1, 0.01).Should().Be(1, "a reduced cost is never free by accident");
        RateMath.ScaleCost(40, 0).Should().Be(0);
        RateMath.ScaleCost(40, 2).Should().Be(80);
    }
}

[TestFixture]
public class RateEventBookTests
{
    private static readonly DateTime Now = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Reminder = TimeSpan.FromMinutes(5);

    [Test]
    public void Start_MultipliesOnlyItsTypesUntilItsEnd()
    {
        var book = new RateEventBook();
        book.Start(new[] { RateType.Exp }, 2, Now, TimeSpan.FromHours(1), "GM");

        book.Multiplier(RateType.Exp, Now.AddMinutes(59)).Should().Be(2);
        book.Multiplier(RateType.Gold, Now.AddMinutes(59)).Should().Be(1);
        book.Multiplier(RateType.Exp, Now.AddHours(1)).Should().Be(1,
            "an event stops counting at its end even before a tick removes it");
    }

    [Test]
    public void Start_ReplacesTheEventOnTheSameTypeInsteadOfStacking()
    {
        var book = new RateEventBook();
        book.Start(new[] { RateType.Exp }, 2, Now, TimeSpan.FromHours(1), "GM");
        book.Start(new[] { RateType.Exp }, 3, Now, TimeSpan.FromHours(1), "GM");

        book.Multiplier(RateType.Exp, Now).Should().Be(3, "x2 then x3 is x3, not x6");
        book.Events.Should().ContainSingle();
    }

    [Test]
    public void Start_TakesOneTypeOutOfAnAllEvent()
    {
        var book = new RateEventBook();
        book.Start(RateTypes.All.ToList(), 2, Now, TimeSpan.FromHours(2), "GM");
        book.Start(new[] { RateType.Exp }, 5, Now, TimeSpan.FromHours(1), "GM");

        book.Multiplier(RateType.Exp, Now).Should().Be(5);
        book.Multiplier(RateType.Gold, Now).Should().Be(2);
        book.Events.Should().HaveCount(2);
        book.Events[0].Types.Should().NotContain(RateType.Exp);
    }

    [Test]
    public void Reset_AnnouncesOnlyWhatItEnds()
    {
        var book = new RateEventBook();
        book.Start(RateTypes.All.ToList(), 2, Now, TimeSpan.FromHours(2), "GM");

        book.Reset(new[] { RateType.Exp }, Now).Should().Equal("The EXP x2 event has been ended.");
        book.Multiplier(RateType.Gold, Now).Should().Be(2, "the other rates keep running");
        book.Reset(new[] { RateType.Exp }, Now).Should().BeEmpty();
    }

    [Test]
    public void Tick_RemindsOnceThenAnnouncesTheEnd()
    {
        var book = new RateEventBook();
        book.Start(new[] { RateType.ItemDrop }, 3, Now, TimeSpan.FromHours(1), "GM");

        book.Tick(Now.AddMinutes(30), Reminder, out var changed).Should().BeEmpty();
        changed.Should().BeFalse();

        book.Tick(Now.AddMinutes(55), Reminder, out changed).Should().Equal("The Drop x3 event ends in 5min.");
        changed.Should().BeTrue("the reminder flag must be saved, or a restart repeats it");
        book.Tick(Now.AddMinutes(56), Reminder, out _).Should().BeEmpty();

        book.Tick(Now.AddHours(1), Reminder, out changed).Should().Equal("The Drop x3 event is over.");
        changed.Should().BeTrue();
        book.Events.Should().BeEmpty();
    }

    [Test]
    public void Tick_SkipsTheReminderOfAnEventShorterThanIt()
    {
        var book = new RateEventBook();
        book.Start(new[] { RateType.Gold }, 2, Now, TimeSpan.FromMinutes(3), "GM");

        book.Tick(Now.AddSeconds(1), Reminder, out _).Should().BeEmpty();
        book.Tick(Now.AddMinutes(3), Reminder, out _).Should().Equal("The Gold x2 event is over.");
    }

    [Test]
    public void Load_DropsWhatEndedWhileTheServerWasDown()
    {
        var book = new RateEventBook();
        var saved = new[]
        {
            new RateEvent { Types = { RateType.Exp }, Multiplier = 2, EndsAtUtc = Now.AddMinutes(-1) },
            new RateEvent { Types = { RateType.Gold }, Multiplier = 2, EndsAtUtc = Now.AddHours(1) },
            new RateEvent { Types = { }, Multiplier = 2, EndsAtUtc = Now.AddHours(1) },
            new RateEvent { Types = { RateType.Jp }, Multiplier = double.NaN, EndsAtUtc = Now.AddHours(1) }
        };

        book.Load(saved, Now).Should().Be(1);
        book.Multiplier(RateType.Gold, Now).Should().Be(2);
    }

    [TestCase(90, "1min 30s")]
    [TestCase(3_600, "1h")]
    [TestCase(5_400, "1h 30min")]
    [TestCase(93_600, "1d 2h")]
    [TestCase(299.4, "5min")]
    public void FormatDuration_IsCompact(double seconds, string expected) =>
        RateEventBook.FormatDuration(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
}

[TestFixture]
public class RateServiceTests
{
    private string _file = null!;
    private RatesOptions _options = null!;
    private DateTime _now;

    [SetUp]
    public void SetUp()
    {
        _file = Path.Combine(Path.GetTempPath(), $"navislamia-rates-{Guid.NewGuid():N}.json");
        _options = new RatesOptions { EventStatePath = _file };
        _now = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_file))
        {
            File.Delete(_file);
        }
    }

    private RateService NewService() =>
        new(new StaticOptionsMonitor<RatesOptions>(_options), () => _now);

    [Test]
    public void Jp_FollowsExpUntilItIsSet()
    {
        var service = NewService();
        _options.Exp = 3;
        service.Get(RateType.Jp).Should().Be(3, "NGemity's single EXPRate multiplies both");

        _options.Jp = 1.5;
        service.Get(RateType.Jp).Should().Be(1.5, "an edit of the settings applies without a restart");
    }

    [Test]
    public void NegativeOrBrokenSettings_CountAsZero()
    {
        _options.Gold = -2;
        _options.ItemDrop = double.PositiveInfinity;
        var service = NewService();

        service.Get(RateType.Gold).Should().Be(0);
        service.Get(RateType.ItemDrop).Should().Be(0);
    }

    [Test]
    public void AnEvent_SurvivesARestartWithItsRemainingTime()
    {
        NewService().StartEvent(new[] { RateType.Exp }, 2, TimeSpan.FromHours(2), "Freezeraid")
            .Should().Be("Event: EXP x2 for 2h!");

        _now = _now.AddMinutes(30);
        var restarted = NewService();

        restarted.Get(RateType.Exp).Should().Be(2);
        restarted.Status().Single(status => status.Type == RateType.Exp).Remaining
            .Should().Be(TimeSpan.FromMinutes(90));
    }

    [Test]
    public void AnEventThatEndedDuringTheDowntime_IsNotResumed()
    {
        NewService().StartEvent(new[] { RateType.Exp }, 2, TimeSpan.FromHours(1), "GM");

        _now = _now.AddHours(2);

        NewService().Get(RateType.Exp).Should().Be(1);
    }

    [Test]
    public void ABrokenStateFile_StartsWithoutEvents()
    {
        File.WriteAllText(_file, "{ not json");

        NewService().Get(RateType.Exp).Should().Be(1);
    }

    [Test]
    public void Tick_AnnouncesTheEndOnceAndPersistsIt()
    {
        var service = NewService();
        service.StartEvent(new[] { RateType.Gold }, 2, TimeSpan.FromMinutes(10), "GM");

        _now = _now.AddMinutes(10);
        service.Tick().Should().Equal("The Gold x2 event is over.");
        service.Tick().Should().BeEmpty();
        NewService().Status().Should().OnlyContain(status => status.Remaining == null);
    }

    [Test]
    public void TheNonMultiplierSettings_AreReadLive()
    {
        var service = NewService();
        _options.MonsterRespawnSeconds = 3;
        _options.GroundItemLifetimeSeconds = 600;

        service.MonsterRespawnDelay.Should().Be(TimeSpan.FromSeconds(3));
        service.GroundItemLifetime.Should().Be(TimeSpan.FromMinutes(10));
    }
}

[TestFixture]
public class RateCommandRulesTests
{
    [Test]
    public void TryParseRate_ReadsTheThreeForms()
    {
        GmCommandRules.TryParseRate(Array.Empty<string>(), 100, out var show).Should().BeTrue();
        show.Kind.Should().Be(RateCommandKind.Show);

        GmCommandRules.TryParseRate(new[] { "drop", "x2.5", "90m" }, 100, out var start).Should().BeTrue();
        start.Kind.Should().Be(RateCommandKind.Start);
        start.Types.Should().Equal(RateType.ItemDrop);
        start.Multiplier.Should().Be(2.5);
        start.Duration.Should().Be(TimeSpan.FromMinutes(90));

        GmCommandRules.TryParseRate(new[] { "reset" }, 100, out var resetAll).Should().BeTrue();
        resetAll.Types.Should().Equal(RateTypes.All);

        GmCommandRules.TryParseRate(new[] { "RESET", "card" }, 100, out var resetCard).Should().BeTrue();
        resetCard.Types.Should().Equal(RateType.CreatureCardDrop);
    }

    [TestCase("exp", "2")]
    [TestCase("exp", "2", "0")]
    [TestCase("exp", "-1", "1h")]
    [TestCase("exp", "101", "1h")]
    [TestCase("exp", "NaN", "1h")]
    [TestCase("stamina", "2", "1h")]
    [TestCase("exp", "2", "31d")]
    [TestCase("exp", "2", "1w")]
    [TestCase("reset", "exp", "gold")]
    public void TryParseRate_RefusesWhatItCannotHonour(params string[] args) =>
        GmCommandRules.TryParseRate(args, 100, out _).Should().BeFalse();

    [TestCase("3600", 3_600)]
    [TestCase("45s", 45)]
    [TestCase("30m", 1_800)]
    [TestCase("30min", 1_800)]
    [TestCase("2H", 7_200)]
    [TestCase("30d", 2_592_000)]
    public void TryParseDuration_AcceptsSecondsAndUnits(string text, int seconds)
    {
        GmCommandRules.TryParseDuration(text, out var duration).Should().BeTrue();
        duration.Should().Be(TimeSpan.FromSeconds(seconds));
    }

    [Test]
    public void TryParseMultiplier_AcceptsZeroAndTheCeiling()
    {
        GmCommandRules.TryParseMultiplier("0", 100, out _).Should().BeTrue();
        GmCommandRules.TryParseMultiplier("100", 100, out _).Should().BeTrue();
        GmCommandRules.TryParseMultiplier("1,5", 100, out _).Should().BeFalse("the invariant culture is the rule");
    }
}

[TestFixture]
public class RateDropAndCostTests
{
    private sealed class SequenceRandom : Random
    {
        private readonly Queue<double> _doubles;

        public SequenceRandom(params double[] values) => _doubles = new Queue<double>(values);

        public override double NextDouble() => _doubles.Dequeue();

        public override int Next(int minValue, int maxValue) => minValue;
    }

    [Test]
    public void CardRate_AppliesToADirectCardSlotOnly()
    {
        const int card = 700001;
        var entries = new[] { new DropEntry(card, 0.1, 1, 1), new DropEntry(500, 0.1, 1, 1) };
        Func<int, double> cardFactor = id => id == card ? 5 : 1;

        var dropped = DropRoll.Roll(entries, new Dictionary<int, DropGroupEntry[]>(),
            new SequenceRandom(0.3, 0.3), 1, cardFactor);

        dropped.Select(item => item.ItemId).Should().Equal(new[] { card },
            "0.1 × 5 beats a 0.3 draw for the card, 0.1 does not for the other item");
    }

    [Test]
    public void CardRate_DoesNotReachACardInsideADropGroup()
    {
        var entries = new[] { new DropEntry(-10, 0.1, 1, 1) };
        var groups = new Dictionary<int, DropGroupEntry[]> { [-10] = new[] { new DropGroupEntry(700001, 1, 1, 1) } };

        DropRoll.Roll(entries, groups, new SequenceRandom(0.3), 1, _ => 5).Should().BeEmpty(
            "NGemity's checkDrop judges the slot's own code, which is negative for a group");
    }

    [Test]
    public void ItemAndCardRates_StillCapTheChanceAtCertainty()
    {
        var entries = new[] { new DropEntry(700001, 0.5, 1, 1) };

        DropRoll.Roll(entries, new Dictionary<int, DropGroupEntry[]>(), new SequenceRandom(0.999999), 10,
            _ => 10).Should().ContainSingle();
    }

    [Test]
    public void JobLevelCost_ScalesButAZeroRateIsFreeNotCapped()
    {
        var repository = A.Fake<ILevelResourceRepository>();
        A.CallTo(() => repository.GetAll()).Returns(new[]
        {
            new LevelResourceEntity { Level = 1, NormalExp = 0, JLvs = new[] { 40 } },
            new LevelResourceEntity { Level = 2, NormalExp = 100, JLvs = new[] { 0 } }
        });
        var options = new RatesOptions { EventStatePath = string.Empty, JobLevelJpCost = 0.5 };
        var leveling = new LevelingService(repository, A.Fake<IStatService>(),
            new RateService(new StaticOptionsMonitor<RatesOptions>(options)));

        leveling.TryGetNextJobLevelCost(1, out var cost).Should().BeTrue();
        cost.Should().Be(20);

        options.JobLevelJpCost = 0;
        leveling.TryGetNextJobLevelCost(1, out cost).Should().BeTrue("a rate of 0 makes the step free");
        cost.Should().Be(0);

        leveling.TryGetNextJobLevelCost(2, out _).Should().BeFalse("a base cost of 0 is the capped tier");
    }
}
