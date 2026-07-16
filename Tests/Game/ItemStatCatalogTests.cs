using FluentAssertions;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

[TestFixture]
public class ItemStatCatalogTests
{
    private static ItemEffectFields Resource(short[] baseTypes = null, decimal[] baseVar1 = null,
        short[] optTypes = null, decimal[] optVar1 = null, decimal[] optVar2 = null)
    {
        return new ItemEffectFields(
            Id: 1,
            BaseTypes: baseTypes ?? new short[4],
            BaseVar1: baseVar1 ?? new decimal[4],
            BaseVar2: new decimal[4],
            OptTypes: optTypes ?? new short[4],
            OptVar1: optVar1 ?? new decimal[4],
            OptVar2: optVar2 ?? new decimal[4]);
    }

    [Test]
    public void BuildEffects_ReadsPassiveBaseTypes()
    {
        var resource = Resource(baseTypes: new short[] { 15, 11, 0, 0 },
                                baseVar1: new decimal[] { 40, 12, 0, 0 });

        ItemStatCatalog.BuildEffects(resource).Should().Equal(
            new ItemStatEffect(StatTarget.Defence, 40f, false),
            new ItemStatEffect(StatTarget.AttackPointRight, 12f, false));
    }

    [Test]
    public void BuildEffects_ExpandsIncParameterAOverEveryBit()
    {
        var resource = Resource(optTypes: new short[] { 96, 0, 0, 0 },
                                optVar1: new decimal[] { 384, 0, 0, 0 },
                                optVar2: new decimal[] { 20, 0, 0, 0 });

        ItemStatCatalog.BuildEffects(resource).Should().Equal(
            new ItemStatEffect(StatTarget.AttackPointRight, 20f, false),
            new ItemStatEffect(StatTarget.MagicPoint, 20f, false));
    }

    [Test]
    public void BuildEffects_MarksAmpParameterAAsPercent()
    {
        var resource = Resource(optTypes: new short[] { 98, 0, 0, 0 },
                                optVar1: new decimal[] { 1048576, 0, 0, 0 },
                                optVar2: new decimal[] { 0.03m, 0, 0, 0 });

        ItemStatCatalog.BuildEffects(resource).Should().Equal(
            new ItemStatEffect(StatTarget.MagicAvoid, 0.03f, true));
    }

    [Test]
    public void BuildEffects_ReadsTheAllStatsMask()
    {
        var resource = Resource(optTypes: new short[] { 96, 0, 0, 0 },
                                optVar1: new decimal[] { 63, 0, 0, 0 },
                                optVar2: new decimal[] { 5, 0, 0, 0 });

        ItemStatCatalog.BuildEffects(resource).Should().HaveCount(6)
            .And.OnlyContain(effect => effect.Value == 5f && !effect.IsPercent);
    }

    [Test]
    public void BuildEffects_IgnoresParameterBAndNonStatEffects()
    {
        var resource = Resource(optTypes: new short[] { 97, 26, 6, 99 },
                                optVar1: new decimal[] { 268435456, 2, 1, 15360 },
                                optVar2: new decimal[] { 1, 0, 0, 0.05m });

        ItemStatCatalog.BuildEffects(resource).Should().BeEmpty();
    }

    [Test]
    public void BuildEffects_ReadsPassiveOptTypes()
    {
        var resource = Resource(optTypes: new short[] { 30, 31, 0, 0 },
                                optVar1: new decimal[] { 1500, 800, 0, 0 });

        ItemStatCatalog.BuildEffects(resource).Should().Equal(
            new ItemStatEffect(StatTarget.MaxHp, 1500f, false),
            new ItemStatEffect(StatTarget.MaxMp, 800f, false));
    }

    [Test]
    public void BuildEffects_SkipsZeroValuedSlots()
    {
        ItemStatCatalog.BuildEffects(Resource()).Should().BeEmpty();
        ItemStatCatalog.BuildEffects(Resource(baseTypes: new short[] { 15, 0, 0, 0 })).Should().BeEmpty();
    }

    [Test]
    public void BuildEffects_ToleratesNullArrays()
    {
        var resource = new ItemEffectFields(1, null, null, null, null, null, null);

        ItemStatCatalog.BuildEffects(resource).Should().BeEmpty();
    }
}
