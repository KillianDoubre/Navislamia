using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Packets;

namespace Navislamia.Game.Services;

/// <summary>One stack a craft consumes: an item of the crafter's bag, by handle, and how many units.</summary>
public readonly record struct CraftConsumption(uint ItemHandle, long Count)
{
    public MixMaterial? ExpectedMaterial { get; init; }
}

/// <summary>What a craft does to the target item: the state it must still have, and the state it gets.</summary>
public readonly record struct CraftTargetChange(
    uint Handle,
    long ExpectedEnhance,
    int ExpectedFlag,
    long NewEnhance,
    int NewFlag,
    bool Destroy)
{
    /// <summary>Consume one main-card unit; create a separate unit unless destroyed.</summary>
    public bool SplitOne { get; init; }
    public MixMaterial? ExpectedMaterial { get; init; }
    public long MinimumAmount { get; init; } = 1;
}

/// <summary>
/// An item a craft creates: its code, how many, the level and enhancement it is made at. <see cref="CopyOf"/>
/// names a bag item whose copy it is (<c>procEnhanceFail</c>'s −3 skill card), made before anything is consumed.
/// </summary>
public readonly record struct CraftCreation(int ItemCode, long Count, int Level)
{
    public int Enhance { get; init; }
    public uint CopyOf { get; init; }
}

/// <summary>
/// A change to an item of the bag that stays there: its handle, the state it must still have, and the change.
/// The commit applies it after the consumption, to an item still present.
/// </summary>
public sealed record CraftItemMutation(uint Handle, MixMaterial? Expected, Action<ItemEntity> Apply);

/// <summary>A creature card that changed enhancement (<c>EnhanceCreatureCard</c>): the session's card follows.</summary>
public readonly record struct CraftCardEnhance(uint CardHandle, int Enhance, bool Succeeded);

/// <summary>What the craft engine reads besides the materials: the crafter's ethereal stone and the resource table.</summary>
public sealed record MixContext(long EtherealStone, Func<int, ItemMixFields> ResourceOf)
{
    public static readonly MixContext Empty = new(0, _ => ItemMixFields.Empty);
}

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
    /// <summary>The items <c>MIX_CREATE_ITEM</c> makes, one entry per code (an empty list is a failed draw).</summary>
    public IReadOnlyList<CraftCreation> Created { get; init; } = Array.Empty<CraftCreation>();

    /// <summary>The changes to items that stay in the bag (the official effects other than enhancement).</summary>
    public IReadOnlyList<CraftItemMutation> Mutations { get; init; } = Array.Empty<CraftItemMutation>();

    /// <summary>What the crafter's ethereal stone gains or loses (<c>AddEtherealStoneDurability</c>).</summary>
    public long EtherealStoneDelta { get; init; }

    /// <summary>The 257 also reports the last item made.</summary>
    public bool ReportCreated { get; init; }

    /// <summary>No 257 at all: <c>SacrificeItemForEtherealStoneDurability</c> sends none on a success.</summary>
    public bool NoResult { get; init; }

    /// <summary>System lines on the item channel (<c>PrintfChatMessage(CHAT_ITEM, "@SYSTEM")</c>).</summary>
    public IReadOnlyList<string> ChatLines { get; init; } = Array.Empty<string>();

    public CraftCardEnhance? CardEnhance { get; init; }

    public static CraftPlan Refused(ResultCode code) =>
        new(code, Array.Empty<CraftConsumption>(), null, Array.Empty<uint>());
}

/// <summary>
/// The effects of a resolved <c>TM_CS_MIX</c> (256), ported from the official server's <c>MixManager</c>: every
/// type the Epic 7 data uses (<c>CraftingEngine.Official.cs</c>), 102 (<see cref="PlanSkillCard"/>) and 601
/// (<see cref="PlanCreate"/>). The decisions of 2026-09-29, taken on NGemity's defaults before the official server
/// was found, are superseded where it decides otherwise (docs/packet-specs/socle-artisanat-objets-officiel.md).
/// Every other type answers <see cref="ResultCode.InvalidArgument"/>.
/// </summary>
public static partial class CraftingEngine
{
    public const int MixEnhance = 101;
    public const int MixEnhanceSkillCard = 102;
    public const int MixEnhanceWithoutFail = 103;
    public const int MixAddLevelSetFlag = 311;
    public const int MixRestoreEnhanceSetFlag = 501;
    public const int MixCreateItem = 601;

    /// <summary>A drop group nests at most this deep before its draw is given up (the monster drops' bound).</summary>
    private const int MaxGroupDepth = 16;

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
        Func<int, int, int> roll, Func<int, (int ItemId, long Count)?> pickFromGroup = null, MixContext context = null)
    {
        var rule = resolution.Rule;
        context ??= MixContext.Empty;
        if (PlanOfficial(resolution, target, enhance, roll, pickFromGroup, context) is { } official)
        {
            return official;
        }

        switch (rule.MixType)
        {
            case MixCreateItem:
                return PlanCreate(resolution, target, roll, pickFromGroup);

            case MixEnhanceSkillCard:
                return PlanSkillCard(resolution, target, enhance, roll);

            default:
                return CraftPlan.Refused(ResultCode.InvalidArgument);
        }
    }

    /// <summary>
    /// <c>MixManager::CreateItem</c> for <c>MIX_CREATE_ITEM</c> (601): the main material goes whole, every sub
    /// material at its count; then <c>mix_value_03</c>% of the time a count is drawn in
    /// <c>[mix_value_04, mix_value_05]</c> and <c>mix_value_01</c> is made — that many of an item, or that many
    /// draws of a drop group when the id is negative (<c>SelectItemIDFromDropGroup</c>, each draw with its own
    /// member count) — at level <c>mix_value_02</c>. A failed draw still consumes everything.
    /// </summary>
    private static CraftPlan PlanCreate(MixResolution resolution, MixMaterial? target, Func<int, int, int> roll,
        Func<int, (int ItemId, long Count)?> pickFromGroup)
    {
        var rule = resolution.Rule;
        var consumed = new List<CraftConsumption>();
        if (target is { } main)
        {
            var whole = main.AvailableCount ?? main.Count;
            if (main.Handle == 0 || whole <= 0)
            {
                return CraftPlan.Refused(ResultCode.InvalidArgument);
            }

            consumed.Add(new CraftConsumption(main.Handle, whole) { ExpectedMaterial = main });
        }

        for (var group = 0; group < resolution.Arranged.Count; group++)
        {
            var material = resolution.Arranged[group];
            if (material.Handle == 0 || resolution.ConsumedCounts[group] <= 0)
            {
                return CraftPlan.Refused(ResultCode.InvalidArgument);
            }

            consumed.Add(new CraftConsumption(material.Handle, resolution.ConsumedCounts[group]) { ExpectedMaterial = material });
        }

        var created = new List<CraftCreation>();
        if (rule.MixValue03 > roll(0, 99))
        {
            var count = roll(Math.Min(rule.MixValue04, rule.MixValue05), Math.Max(rule.MixValue04, rule.MixValue05));
            var draws = rule.MixValue01 >= 0 ? 1 : count;
            for (var draw = 0; draw < draws; draw++)
            {
                var itemId = rule.MixValue01;
                long itemCount = count;
                for (var depth = 0; itemId < 0 && depth < MaxGroupDepth; depth++)
                {
                    if (pickFromGroup?.Invoke(itemId) is not { } picked)
                    {
                        itemId = 0;
                        break;
                    }

                    (itemId, itemCount) = picked;
                }

                if (itemId <= 0 || itemCount <= 0)
                {
                    continue;
                }

                var index = created.FindIndex(entry => entry.ItemCode == itemId);
                if (index >= 0)
                {
                    created[index] = created[index] with { Count = created[index].Count + itemCount };
                }
                else
                {
                    created.Add(new CraftCreation(itemId, itemCount, Math.Max(0, rule.MixValue02)));
                }
            }
        }

        return new CraftPlan(ResultCode.Success, consumed, null, Array.Empty<uint>()) { Created = created };
    }

    private static CraftPlan PlanSkillCard(MixResolution resolution, MixMaterial? target,
        EnhanceResourceEntity enhance, Func<int, int, int> roll)
    {
        if (target is not { } item || item.Handle == 0 || item.SkillId <= 0 || item.ItemGroup != 10
            || enhance?.RequiredItemId is null || item.Enhance < 0 || item.Enhance >= enhance.MaxEnhance
            || resolution.Arranged.Count != 2 || resolution.ConsumedCounts.Count != 2)
        {
            return CraftPlan.Refused(ResultCode.InvalidArgument);
        }

        var cube = resolution.Arranged[0];
        var second = resolution.Arranged[1];
        if (cube.Handle == 0 || second.Handle == 0 || cube.Handle == item.Handle || cube.Handle == second.Handle
            || cube.ItemCode != enhance.RequiredItemId || second.ItemGroup != 10
            || second.SkillId != item.SkillId || second.Enhance != item.Enhance
            || resolution.ConsumedCounts[0] != 1 || resolution.ConsumedCounts[1] != 1
            || item.AvailableCount < (second.Handle == item.Handle ? 2 : 1)
            || cube.AvailableCount < 1 || second.AvailableCount < 1)
        {
            return CraftPlan.Refused(ResultCode.InvalidArgument);
        }

        var consumed = new List<CraftConsumption>
        {
            new(cube.Handle, 1) { ExpectedMaterial = cube },
            new(second.Handle, 1) { ExpectedMaterial = second }
        };
        var chance = enhance.Percentage is { } rates && item.Enhance < rates.Length
            ? rates[(int)item.Enhance] : 0m;
        var success = roll(0, RollScale) <= (int)(chance * RollScale);
        var split = success || enhance.FailResult == FailResultType.SkillCard;
        var destroy = !success && enhance.FailResult == FailResultType.SkillCard && item.Enhance <= 3;
        var nextEnhance = success ? item.Enhance + 1
            : enhance.FailResult is FailResultType.SkillCard or FailResultType.Accessory
                ? Math.Max(0, item.Enhance - 3) : item.Enhance;
        var nextFlag = !success && enhance.FailResult is not (FailResultType.SkillCard or FailResultType.Accessory)
            ? item.Flag | FailedFlagMask : item.Flag;
        if (split)
        {
            consumed.Add(new CraftConsumption(item.Handle, 1) { ExpectedMaterial = item });
        }

        return new CraftPlan(ResultCode.Success, consumed,
            new CraftTargetChange(item.Handle, item.Enhance, item.Flag, nextEnhance, nextFlag, destroy)
            {
                SplitOne = split,
                ExpectedMaterial = item,
                MinimumAmount = second.Handle == item.Handle ? 2 : 1
            }, success ? new[] { item.Handle } : Array.Empty<uint>());
    }
}
