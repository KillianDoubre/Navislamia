using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using NUnit.Framework;

namespace Tests.Game;

/// <summary>
/// The effects of a resolved mix (docs/packet-specs/socle-artisanat-ressources.md §14 and §15): NGemity's
/// five handled types with Killian's failure policy, the dice injected so every branch is decided here.
/// </summary>
[TestFixture]
public class CraftingEngineTests
{
    private const uint Target = 100;
    private const uint Cube = 200;
    private const uint Powder = 300;
    private const int CubeCode = 3620137;
    private const int PowderCode = 3620999;

    private static MixMaterial Item(uint handle, int code, long enhance = 0, int flag = 0, long count = 1) =>
        new(code, 0, 0, 0, 0, 1, enhance, flag, count, handle);

    private static EnhanceResourceEntity Enhance(int maxEnhance = 10, int failResult = 1, decimal chance = 0.5m) =>
        new()
        {
            Id = 5007,
            MaxEnhance = (short)maxEnhance,
            FailResult = (FailResultType)failResult,
            RequiredItemId = CubeCode,
            Percentage = Enumerable.Repeat(chance, 25).ToArray()
        };

    private static MixResolution Resolution(int mixType, int value2, int value3, params MixMaterial[] arranged) =>
        new(new MixResourceEntity { Id = 1, MixType = mixType, MixValue01 = 5007, MixValue02 = value2, MixValue03 = value3 },
            arranged.Select(material => material.Count).ToArray(), arranged);

    /// <summary>Dice that answer the gain draw with <paramref name="gain"/> and the success draw with <paramref name="success"/>.</summary>
    private static Func<int, int, int> Dice(int gain, bool success) =>
        (min, max) => max == CraftingEngine.RollScale ? (success ? 0 : CraftingEngine.RollScale) : Math.Clamp(gain, min, max);

    [Test]
    public void Enhance_Success_ConsumesTheCubeAndRaisesByTheDrawnGain()
    {
        var plan = CraftingEngine.Plan(Resolution(101, 1, 3, Item(Cube, CubeCode)), Item(Target, 0, enhance: 4),
            Enhance(), Dice(gain: 2, success: true));

        plan.Refusal.Should().Be(ResultCode.Success);
        plan.Consumed.Should().Equal(new CraftConsumption(Cube, 1));
        plan.Change.Should().Be(new CraftTargetChange(Target, 4, 0, 6, 0, false));
        plan.ResultHandles.Should().Equal(Target);
    }

    [Test]
    public void Enhance_GainIsCappedAtMaxEnhance_AndRefusedAtIt()
    {
        CraftingEngine.Plan(Resolution(101, 1, 5, Item(Cube, CubeCode)), Item(Target, 0, enhance: 9),
                Enhance(maxEnhance: 10), Dice(gain: 5, success: true))
            .Change!.Value.NewEnhance.Should().Be(10);

        CraftingEngine.Plan(Resolution(101, 1, 5, Item(Cube, CubeCode)), Item(Target, 0, enhance: 10),
                Enhance(maxEnhance: 10), Dice(gain: 1, success: true))
            .Refusal.Should().Be(ResultCode.InvalidArgument, "nothing is consumed for a craft that cannot raise anything");
    }

    [Test]
    public void Enhance_ChanceIsTheRateOfTheCurrentLevel()
    {
        var enhance = Enhance();
        enhance.Percentage = new decimal[25];
        enhance.Percentage[4] = 1m;

        var rolls = new List<(int Min, int Max)>();
        CraftingEngine.Plan(Resolution(101, 1, 1, Item(Cube, CubeCode)), Item(Target, 0, enhance: 4), enhance,
            (min, max) => { rolls.Add((min, max)); return max == CraftingEngine.RollScale ? CraftingEngine.RollScale : min; })
            .ResultHandles.Should().Equal(new[] { Target }, "percentage[4] = 1.000 succeeds even on the highest roll");
        rolls.Should().Contain((0, CraftingEngine.RollScale));
    }

    [TestCase(1)]
    [TestCase(0)]
    [TestCase(4)]
    public void Enhance_FailResultOneZeroOrUnknown_SetsTheFailedBitAndKeepsTheLevel(int failResult)
    {
        var plan = CraftingEngine.Plan(Resolution(101, 1, 1, Item(Cube, CubeCode)), Item(Target, 0, enhance: 5, flag: 1),
            Enhance(failResult: failResult), Dice(gain: 1, success: false));

        plan.Change.Should().Be(new CraftTargetChange(Target, 5, 1, 5, 1 | CraftingEngine.FailedFlagMask, false));
        plan.ResultHandles.Should().BeEmpty("a failure reports no handle");
        plan.Consumed.Should().Equal(new[] { new CraftConsumption(Cube, 1) }, "the cube is spent either way");
    }

    [Test]
    public void Enhance_SkillCardFailure_DestroysAtThreeOrLess_ElseTakesThree()
    {
        CraftingEngine.Plan(Resolution(101, 1, 1, Item(Cube, CubeCode)), Item(Target, 0, enhance: 3),
                Enhance(failResult: 2), Dice(1, success: false))
            .Change!.Value.Destroy.Should().BeTrue();

        var above = CraftingEngine.Plan(Resolution(101, 1, 1, Item(Cube, CubeCode)), Item(Target, 0, enhance: 7),
            Enhance(failResult: 2), Dice(1, success: false)).Change!.Value;
        above.Destroy.Should().BeFalse();
        above.NewEnhance.Should().Be(4);
    }

    [Test]
    public void Enhance_AccessoryFailure_TakesThreeFlooredAtZero()
    {
        CraftingEngine.Plan(Resolution(101, 1, 1, Item(Cube, CubeCode)), Item(Target, 0, enhance: 2),
                Enhance(failResult: 3), Dice(1, success: false))
            .Change!.Value.NewEnhance.Should().Be(0);
    }

    [Test]
    public void EnhanceWithoutFail_FindsTheCubeInEitherSlot_GainsOne_AndLosesOneOnFailure()
    {
        var success = CraftingEngine.Plan(Resolution(103, 0, 5000, Item(Powder, PowderCode), Item(Cube, CubeCode)),
            Item(Target, 0, enhance: 6), Enhance(), Dice(9, success: true));
        success.Consumed.Should().BeEquivalentTo(new[] { new CraftConsumption(Cube, 1), new CraftConsumption(Powder, 1) });
        success.Change!.Value.NewEnhance.Should().Be(7, "103 raises by one, whatever mix_value_03 holds");

        var failure = CraftingEngine.Plan(Resolution(103, 0, 5000, Item(Cube, CubeCode), Item(Powder, PowderCode)),
            Item(Target, 0, enhance: 6), Enhance(failResult: 2), Dice(1, success: false));
        failure.Change!.Value.NewEnhance.Should().Be(5, "the powder protects: one level off, never the fail_result");
        failure.Change!.Value.Destroy.Should().BeFalse();
    }

    [Test]
    public void Enhance_WithoutTheRightCube_OrWithoutTarget_IsRefused()
    {
        CraftingEngine.Plan(Resolution(101, 1, 1, Item(Cube, 999)), Item(Target, 0), Enhance(), Dice(1, true))
            .Refusal.Should().Be(ResultCode.InvalidArgument);
        CraftingEngine.Plan(Resolution(101, 1, 1, Item(Cube, CubeCode)), null, Enhance(), Dice(1, true))
            .Refusal.Should().Be(ResultCode.InvalidArgument);
        CraftingEngine.Plan(Resolution(101, 1, 1, Item(Cube, CubeCode)), Item(Target, 0), null, Dice(1, true))
            .Refusal.Should().Be(ResultCode.InvalidArgument, "a rule whose enhance row is missing does nothing");
    }

    [Test]
    public void SetFlag_GivesBitZeroTheValueOfMixValue03_AndConsumesTheMaterials()
    {
        var on = CraftingEngine.Plan(Resolution(311, 0, 1, Item(Cube, 11, count: 2)), Item(Target, 0, flag: 8),
            null, Dice(0, true));
        on.Change!.Value.NewFlag.Should().Be(8 | CraftingEngine.CardFlagMask);
        on.Consumed.Should().Equal(new CraftConsumption(Cube, 2));
        on.ResultHandles.Should().Equal(Target);

        CraftingEngine.Plan(Resolution(311, 0, 0, Item(Cube, 800000)), Item(Target, 0, flag: 9), null, Dice(0, true))
            .Change!.Value.NewFlag.Should().Be(8);
    }

    [Test]
    public void Repair_ClearsTheBitNamedByMixValue01()
    {
        var resolution = new MixResolution(
            new MixResourceEntity { Id = 3046, MixType = 501, MixValue01 = 3, MixValue03 = 5000 },
            new long[] { 1 }, new[] { Item(Cube, 950021) });

        var plan = CraftingEngine.Plan(resolution, Item(Target, 0, enhance: 5, flag: CraftingEngine.FailedFlagMask | 1),
            null, Dice(0, true));

        plan.Change.Should().Be(new CraftTargetChange(Target, 5, CraftingEngine.FailedFlagMask | 1, 5, 1, false));
        plan.ResultHandles.Should().Equal(Target);
    }

    [TestCase(102)]
    [TestCase(601)]
    [TestCase(202)]
    public void AnotherType_IsRefusedAsBefore(int mixType)
    {
        CraftingEngine.Plan(Resolution(mixType, 0, 0, Item(Cube, CubeCode)), Item(Target, 0), Enhance(), Dice(0, true))
            .Refusal.Should().Be(ResultCode.InvalidArgument);
    }

    [Test]
    public void MixResult_IsElevenPlusFourPerHandle()
    {
        var empty = GameCraftingPackets.BuildMixResult(Array.Empty<uint>());
        empty.Length.Should().Be(11);
        BinaryPrimitives.ReadUInt16LittleEndian(empty.AsSpan(4, 2)).Should().Be(257);
        BinaryPrimitives.ReadUInt32LittleEndian(empty.AsSpan(7, 4)).Should().Be(0);

        var one = GameCraftingPackets.BuildMixResult(new[] { 0x0A000001u });
        one.Length.Should().Be(15);
        BinaryPrimitives.ReadUInt32LittleEndian(one.AsSpan(7, 4)).Should().Be(1);
        BinaryPrimitives.ReadUInt32LittleEndian(one.AsSpan(11, 4)).Should().Be(0x0A000001u);
        one[6].Should().Be(StorageTestHarness.Checksum(one));
    }
}
