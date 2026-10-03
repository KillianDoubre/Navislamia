using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
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

    /// <summary>The bag item <paramref name="entity"/> after the plan's mutations of <paramref name="handle"/>.</summary>
    private static ItemEntity Apply(CraftPlan plan, uint handle, ItemEntity entity)
    {
        foreach (var mutation in plan.Mutations.Where(m => m.Handle == handle))
        {
            mutation.Apply(entity);
        }

        return entity;
    }

    [Test]
    public void Enhance_Success_ConsumesEveryMaterialAndRaisesByTheDrawnGain()
    {
        var plan = CraftingEngine.Plan(Resolution(101, 1, 3, Item(Cube, CubeCode)), Item(Target, 0, enhance: 4),
            Enhance(), Dice(gain: 2, success: true));

        plan.Refusal.Should().Be(ResultCode.Success);
        plan.Consumed.Select(c => (c.ItemHandle, c.Count)).Should().Equal((Cube, 1L));
        Apply(plan, Target, new ItemEntity { Enhance = 4 }).Enhance.Should().Be(6);
        plan.ResultHandles.Should().Equal(Target);
    }

    [Test]
    public void Enhance_GainIsCappedAtMaxEnhance_AndRefusedAtIt()
    {
        var capped = CraftingEngine.Plan(Resolution(101, 1, 5, Item(Cube, CubeCode)), Item(Target, 0, enhance: 9),
            Enhance(maxEnhance: 10), Dice(gain: 5, success: true));
        Apply(capped, Target, new ItemEntity { Enhance = 9 }).Enhance.Should().Be(10);

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

    [Test]
    public void Enhance_FailResultOne_SetsTheFailedBitAndEmptiesTheSockets_ButABeltKeepsThem()
    {
        var plan = CraftingEngine.Plan(Resolution(101, 1, 1, Item(Cube, CubeCode)), Item(Target, 0, enhance: 5, flag: 1),
            Enhance(failResult: 1), Dice(gain: 1, success: false));

        var item = Apply(plan, Target, new ItemEntity { Enhance = 5, Flag = (ItemFlag)1, SocketItemIds = new long[] { 7, 8, 0, 0 } });
        ((int)item.Flag).Should().Be(1 | CraftingEngine.FailedFlagMask);
        item.SocketItemIds.Should().Equal(0, 0, 0, 0);
        item.Enhance.Should().Be(5);
        plan.ResultHandles.Should().BeEmpty("a failure reports no handle");
        plan.Consumed.Select(c => c.ItemHandle).Should().Equal(new[] { Cube }, "the materials are spent either way");

        var belt = new MixMaterial(0, 7, 250, 0, 0, 1, 5, 0, 1, Target);
        var beltPlan = CraftingEngine.Plan(Resolution(101, 1, 1, Item(Cube, CubeCode)), belt, Enhance(failResult: 1),
            Dice(gain: 1, success: false));
        Apply(beltPlan, Target, new ItemEntity { SocketItemIds = new long[] { 3, 0, 0, 0 } }).SocketItemIds
            .Should().Equal(3, 0, 0, 0);
    }

    [TestCase(0)]
    [TestCase(4)]
    public void Enhance_FailResultZeroOrFour_ChangesNothing(int failResult)
    {
        var plan = CraftingEngine.Plan(Resolution(101, 1, 1, Item(Cube, CubeCode)), Item(Target, 0, enhance: 5),
            Enhance(failResult: failResult), Dice(gain: 1, success: false));

        plan.Mutations.Should().BeEmpty();
        plan.Created.Should().BeEmpty();
        plan.Consumed.Select(c => c.ItemHandle).Should().Equal(Cube);
    }

    [Test]
    public void Enhance_SkillCardFailure_TakesTheUnit_AndGivesBackACopyAtMinusThreeAboveThree()
    {
        var low = CraftingEngine.Plan(Resolution(101, 1, 1, Item(Cube, CubeCode)), Item(Target, 0, enhance: 3),
            Enhance(failResult: 2), Dice(1, success: false));
        low.Consumed.Select(c => (c.ItemHandle, c.Count)).Should().Contain((Target, 1L));
        low.Created.Should().BeEmpty();

        var above = CraftingEngine.Plan(Resolution(101, 1, 1, Item(Cube, CubeCode)), Item(Target, 0, enhance: 7),
            Enhance(failResult: 2), Dice(1, success: false));
        above.Created.Should().ContainSingle().Which.Should().Match<CraftCreation>(c => c.CopyOf == Target && c.Enhance == 4);
    }

    [Test]
    public void Enhance_AccessoryFailure_TakesThreeOrFallsToZero()
    {
        var low = CraftingEngine.Plan(Resolution(101, 1, 1, Item(Cube, CubeCode)), Item(Target, 0, enhance: 2),
            Enhance(failResult: 3), Dice(1, success: false));
        Apply(low, Target, new ItemEntity { Enhance = 2 }).Enhance.Should().Be(0);

        var high = CraftingEngine.Plan(Resolution(101, 1, 1, Item(Cube, CubeCode)), Item(Target, 0, enhance: 7),
            Enhance(failResult: 3), Dice(1, success: false));
        Apply(high, Target, new ItemEntity { Enhance = 7 }).Enhance.Should().Be(4);
    }

    private static MixResolution Without(int value3, int value4, params MixMaterial[] arranged) =>
        new(new MixResourceEntity { Id = 1, MixType = 103, MixValue01 = 5007, MixValue03 = value3, MixValue04 = value4 },
            arranged.Select(material => material.Count).ToArray(), arranged);

    [Test]
    public void EnhanceWithoutFail_GainsOne_AndAFailureRescalesTheLevelByMixValues03And04()
    {
        var success = CraftingEngine.Plan(Without(10000, 10000, Item(Cube, CubeCode), Item(Powder, PowderCode)),
            Item(Target, 0, enhance: 6), Enhance(), Dice(9, success: true));
        success.Consumed.Select(c => c.ItemHandle).Should().BeEquivalentTo(new[] { Cube, Powder });
        Apply(success, Target, new ItemEntity { Enhance = 6 }).Enhance.Should().Be(7);

        // ((rand(6 × 8000, 6 × 8000) / 1000) + 5) / 10 = (48 + 5) / 10 = 5.
        var failure = CraftingEngine.Plan(Without(8000, 8000, Item(Cube, CubeCode), Item(Powder, PowderCode)),
            Item(Target, 0, enhance: 6), Enhance(failResult: 2), (min, max) => max == CraftingEngine.RollScale ? max : min);
        Apply(failure, Target, new ItemEntity { Enhance = 6 }).Enhance.Should().Be(5);
        failure.Created.Should().BeEmpty("103 never applies the fail_result");
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
    public void AddLevelSetFlag_AddsTheLevelAndTurnsTheNamedBitOnOrOff()
    {
        var rule = new MixResourceEntity { Id = 2, MixType = 311, MixValue01 = 102, MixValue02 = 0, MixValue03 = 1 };
        var on = CraftingEngine.Plan(new MixResolution(rule, new long[] { 2 }, new[] { Item(Cube, 11, count: 2) }),
            Item(Target, 0, enhance: 1, flag: 8), null, Dice(0, true));
        var item = Apply(on, Target, new ItemEntity { Enhance = 1, Level = 1, Flag = (ItemFlag)8 });
        ((int)item.Flag).Should().Be(8 | CraftingEngine.CardFlagMask);
        item.Enhance.Should().Be(2, "102 = enhance +1, level +2");
        item.Level.Should().Be(3);
        on.Consumed.Select(c => (c.ItemHandle, c.Count)).Should().Equal((Cube, 2L));
        on.ResultHandles.Should().Equal(Target);

        var offRule = new MixResourceEntity { Id = 2, MixType = 311, MixValue01 = 0, MixValue02 = 0, MixValue03 = 0 };
        var off = CraftingEngine.Plan(new MixResolution(offRule, new long[] { 1 },
            new[] { Item(Cube, 800000) }), Item(Target, 0, flag: 9), null, Dice(0, true));
        ((int)Apply(off, Target, new ItemEntity { Flag = (ItemFlag)9 }).Flag).Should().Be(8);
    }

    [Test]
    public void Restore_SpendsOnePowder_RescalesTheLevel_AndClearsTheNamedBit()
    {
        var resolution = new MixResolution(
            new MixResourceEntity { Id = 3046, MixType = 501, MixValue01 = 3, MixValue02 = 0, MixValue03 = 10000, MixValue04 = 10000 },
            new long[] { 1 }, new[] { Item(Cube, 950021) });

        var plan = CraftingEngine.Plan(resolution, Item(Target, 0, enhance: 5, flag: CraftingEngine.FailedFlagMask | 1),
            null, Dice(0, true));

        var item = Apply(plan, Target, new ItemEntity { Enhance = 5, Flag = (ItemFlag)(CraftingEngine.FailedFlagMask | 1) });
        ((int)item.Flag).Should().Be(1);
        item.Enhance.Should().Be(5);
        plan.Consumed.Select(c => (c.ItemHandle, c.Count)).Should().Equal((Cube, 1L));
        plan.ResultHandles.Should().Equal(Target);
    }

    [TestCase(602)]
    [TestCase(999)]
    public void AnotherType_IsRefused(int mixType)
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
