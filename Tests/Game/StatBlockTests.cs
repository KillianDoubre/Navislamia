using FluentAssertions;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

[TestFixture]
public class StatBlockTests
{
    [Test]
    public void Add_AccumulatesOntoTheTargetField()
    {
        var block = new StatBlock();
        block.Add(StatTarget.Strength, 5);
        block.Add(StatTarget.Strength, 3);
        block.Add(StatTarget.Critical, 2);

        block.Strength.Should().Be(8);
        block.Critical.Should().Be(2);
    }

    [Test]
    public void Add_IgnoresNone()
    {
        var block = new StatBlock();
        block.Add(StatTarget.None, 99);

        block.Strength.Should().Be(0);
        block.MaxHp.Should().Be(0);
    }

    [Test]
    public void Amplify_AppliesARatioOfTheCurrentValue()
    {
        var block = new StatBlock();
        block.Add(StatTarget.MaxHp, 1000);
        block.Amplify(StatTarget.MaxHp, 0.10f);

        block.MaxHp.Should().BeApproximately(1100f, 0.001f);
    }

    [Test]
    public void Amplify_OnAnEmptyFieldChangesNothing()
    {
        var block = new StatBlock();
        block.Amplify(StatTarget.MaxHp, 0.10f);

        block.MaxHp.Should().Be(0);
    }

    [Test]
    public void Add_ReachesEveryTargetTheEnumDeclares()
    {
        foreach (StatTarget target in System.Enum.GetValues(typeof(StatTarget)))
        {
            if (target == StatTarget.None)
            {
                continue;
            }

            var block = new StatBlock();
            block.Add(target, 7);

            block.Get(target).Should().Be(7, "StatBlock must map {0}", target);
        }
    }
}
