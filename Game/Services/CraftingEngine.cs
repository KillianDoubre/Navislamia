using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Network.Packets;

namespace Navislamia.Game.Services;

/// <summary>One stack a craft consumes: an item of the crafter's bag, by handle, and how many units.</summary>
public readonly record struct CraftConsumption(uint ItemHandle, long Count);

/// <summary>What a craft does to the target item: the state it must still have, and the state it gets.</summary>
public readonly record struct CraftTargetChange(
    uint Handle,
    long ExpectedEnhance,
    int ExpectedFlag,
    long NewEnhance,
    int NewFlag,
    bool Destroy);

/// <summary>
/// A decided craft: the stacks to consume, what happens to the target, and the handles
/// <c>TM_SC_MIX_RESULT</c> (257) reports — the target on success, none on failure (NGemity
/// <c>MixManager.cpp:99-116</c>). <see cref="Refusal"/> is not <see cref="ResultCode.Success"/> when nothing
/// may happen; the craft is then answered with that code on 256 and nothing is consumed.
/// </summary>
public sealed record CraftPlan(
    ResultCode Refusal,
    IReadOnlyList<CraftConsumption> Consumed,
    CraftTargetChange? Change,
    IReadOnlyList<uint> ResultHandles)
{
    public static CraftPlan Refused(ResultCode code) =>
        new(code, Array.Empty<CraftConsumption>(), null, Array.Empty<uint>());
}

/// <summary>
/// The effects of a resolved <c>TM_CS_MIX</c> (256), ported from NGemity's <c>MixManager</c> for the types
/// its handler really carries out (<c>WorldSession.cpp:1474-1492</c>), with Killian's decisions of
/// 2026-09-30 (docs/packet-specs/socle-artisanat-ressources.md §14) and the 9.4 data read as it is:
/// <list type="table">
/// <item><term>101 <c>MIX_ENHANCE</c></term><description>the cube (<c>need_item</c>) is consumed; gain drawn in
/// <c>[mix_value_02, mix_value_03]</c> and capped at <c>max_enhance</c>; success chance
/// <c>percentage[enhance]</c>; failure per <c>fail_result</c>: 2 destroys at +3 or less, else −3; 3 is −3
/// floored at 0; 1, 0 and the unknown 4 set the <c>FAILED</c> bit and keep the sockets.</description></item>
/// <item><term>103 <c>MIX_ENHANCE_WITHOUT_FAIL</c></term><description>cube and powder consumed; gain 1;
/// failure takes one level off, floored at 0 (<c>MixManager.cpp:108-111</c>).</description></item>
/// <item><term>311 <c>MIX_ADD_LEVEL_SET_FLAG</c></term><description>the materials are consumed and bit 0 — the
/// card flag — takes the value <c>mix_value_03</c>: the nine rules at 1 require it off, the nine at 0
/// require it on (their <c>CHECK_FLAG_OFF</c>/<c>ON 0</c> conditions).</description></item>
/// <item><term>501 <c>MIX_RESTORE_ENHANCE_SET_FLAG</c></term><description>the materials are consumed and the
/// bit <c>mix_value_01</c> is cleared — 3, <c>FAILED</c>, the bit every rule requires on: the repair.</description></item>
/// </list>
/// Every other type answers <see cref="ResultCode.InvalidArgument"/> as before: NGemity carries none of them
/// out, and 102 cannot resolve (its rules use condition codes 24 and 25, which no reference defines).
/// </summary>
public static class CraftingEngine
{
    public const int MixEnhance = 101;
    public const int MixEnhanceSkillCard = 102;
    public const int MixEnhanceWithoutFail = 103;
    public const int MixAddLevelSetFlag = 311;
    public const int MixRestoreEnhanceSetFlag = 501;

    /// <summary>The card flag, bit 0 of the stored bitset (NGemity <c>ITEM_FLAG_CARD</c>, tested as <c>flag % 2</c>).</summary>
    public const int CardFlagMask = 1;

    /// <summary>The failed flag, bit 3 (<c>ItemFlag.Failed</c> is its index; NGemity <c>ITEM_FLAG_FAILED = 0x08</c>).</summary>
    public const int FailedFlagMask = 1 << (int)ItemFlag.Failed;

    /// <summary>NGemity rolls <c>urand(0, 100000)</c> against <c>percentage × 100000</c> (<c>MixManager.cpp:96-99</c>).</summary>
    public const int RollScale = 100000;

    /// <summary>
    /// Decides a craft. <paramref name="target"/> is the main material (null when the frame named none),
    /// <paramref name="enhance"/> the <c>EnhanceResource</c> row of the rule when it names one (null
    /// otherwise), <paramref name="roll"/> returns an integer in <c>[min, max]</c> inclusive.
    /// </summary>
    public static CraftPlan Plan(MixResolution resolution, MixMaterial? target, EnhanceResourceEntity enhance,
        Func<int, int, int> roll)
    {
        var rule = resolution.Rule;
        switch (rule.MixType)
        {
            case MixEnhance:
            case MixEnhanceWithoutFail:
                return PlanEnhance(resolution, target, enhance, roll);

            case MixAddLevelSetFlag:
            {
                if (target is not { } item)
                {
                    return CraftPlan.Refused(ResultCode.InvalidArgument);
                }

                var flag = rule.MixValue03 != 0 ? item.Flag | CardFlagMask : item.Flag & ~CardFlagMask;
                return Change(resolution, item, item.Enhance, flag, destroy: false, success: true);
            }

            case MixRestoreEnhanceSetFlag:
            {
                if (target is not { } item || rule.MixValue01 is < 0 or > 31)
                {
                    return CraftPlan.Refused(ResultCode.InvalidArgument);
                }

                return Change(resolution, item, item.Enhance, item.Flag & ~(1 << rule.MixValue01), destroy: false,
                    success: true);
            }

            default:
                return CraftPlan.Refused(ResultCode.InvalidArgument);
        }
    }

    private static CraftPlan PlanEnhance(MixResolution resolution, MixMaterial? target,
        EnhanceResourceEntity enhance, Func<int, int, int> roll)
    {
        var rule = resolution.Rule;
        if (target is not { } item || enhance is null || enhance.RequiredItemId is null)
        {
            return CraftPlan.Refused(ResultCode.InvalidArgument);
        }

        // The cube is the material whose code is the row's need_item; with the powder of 103 it may come
        // first or second, as NGemity swaps them when the first is not a cube (MixManager.cpp:61-68).
        var cube = resolution.Arranged.FirstOrDefault(material => material.ItemCode == enhance.RequiredItemId);
        if (cube.Handle == 0)
        {
            return CraftPlan.Refused(ResultCode.InvalidArgument);
        }

        var consumed = new List<CraftConsumption> { new(cube.Handle, 1) };
        if (rule.MixType == MixEnhanceWithoutFail)
        {
            var powder = resolution.Arranged.FirstOrDefault(material => material.Handle != cube.Handle);
            if (powder.Handle == 0)
            {
                return CraftPlan.Refused(ResultCode.InvalidArgument);
            }

            consumed.Add(new CraftConsumption(powder.Handle, 1));
        }

        var current = item.Enhance;
        var gain = rule.MixType == MixEnhance
            ? roll(Math.Min(rule.MixValue02, rule.MixValue03), Math.Max(rule.MixValue02, rule.MixValue03))
            : 1;
        gain = (int)Math.Min(gain, enhance.MaxEnhance - current);
        if (gain <= 0)
        {
            return CraftPlan.Refused(ResultCode.InvalidArgument);
        }

        var chance = enhance.Percentage is { } rates && current < rates.Length ? rates[(int)current] : 0m;
        var succeeded = roll(0, RollScale) <= (int)(chance * RollScale);

        if (succeeded)
        {
            return Plan(consumed, item, current + gain, item.Flag, destroy: false, success: true);
        }

        if (rule.MixType == MixEnhanceWithoutFail)
        {
            return Plan(consumed, item, Math.Max(0, current - 1), item.Flag, destroy: false, success: false);
        }

        return (int)enhance.FailResult switch
        {
            (int)FailResultType.SkillCard => current <= 3
                ? Plan(consumed, item, current, item.Flag, destroy: true, success: false)
                : Plan(consumed, item, current - 3, item.Flag, destroy: false, success: false),
            (int)FailResultType.Accessory => Plan(consumed, item, Math.Max(0, current - 3), item.Flag, destroy: false,
                success: false),
            _ => Plan(consumed, item, current, item.Flag | FailedFlagMask, destroy: false, success: false)
        };
    }

    /// <summary>A craft that consumes every arranged material at the count the resolution settled.</summary>
    private static CraftPlan Change(MixResolution resolution, MixMaterial item, long enhance, int flag, bool destroy,
        bool success)
    {
        var consumed = resolution.Arranged
            .Select((material, group) => new CraftConsumption(material.Handle, resolution.ConsumedCounts[group]))
            .ToArray();
        return Plan(consumed, item, enhance, flag, destroy, success);
    }

    private static CraftPlan Plan(IReadOnlyList<CraftConsumption> consumed, MixMaterial item, long enhance, int flag,
        bool destroy, bool success)
    {
        var change = new CraftTargetChange(item.Handle, item.Enhance, item.Flag, enhance, flag, destroy);
        return new CraftPlan(ResultCode.Success, consumed, change,
            success ? new[] { item.Handle } : Array.Empty<uint>());
    }
}
