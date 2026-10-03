using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Packets;

namespace Navislamia.Game.Services;

/// <summary>
/// The mix types ported from the official server's <c>MixManager</c> (<c>MixManager.cpp</c>, Epic 7 Part 4):
/// enhancement (101/103) and its <c>procEnhanceFail</c>, creature cards (104/105), item level (2xx/3xx),
/// recycling (401/402), restoration (501), appearance (603), elements (701/702), sockets (703/704) and ethereal
/// durability (801-806). <c>value[k]</c> of the source is <c>mix_value_0(k+1)</c>.
/// docs/packet-specs/socle-artisanat-objets-officiel.md.
/// </summary>
public static partial class CraftingEngine
{
    public const int MixEnhanceCreatureCard = 104;
    public const int MixEnhanceCreatureCardWithJoker = 105;
    public const int MixSetLevel = 201;
    public const int MixSetLevelCreateItem = 202;
    public const int MixSetLevelSetFlag = 211;
    public const int MixSetLevelSetFlagCreateItem = 212;
    public const int MixSetLevelSetFlagCreateItemWithMainLevel = 213;
    public const int MixSetLevelOnSubMaterialLevelSetFlag = 214;
    public const int MixSetLevelSetFlagCreateItemWithMainLevelSetZero = 215;
    public const int MixAddLevel = 301;
    public const int MixAddLevelCreateItem = 302;
    public const int MixAddLevelSetFlagCreateItem = 312;
    public const int MixRecycle = 401;
    public const int MixRecycleEnhance = 402;
    public const int MixChangeAppearanceCode = 603;
    public const int MixSetElementalEffect = 701;
    public const int MixSetElementalEffectParameter = 702;
    public const int MixSetSocket = 703;
    public const int MixReplaceSocketWith = 704;
    public const int MixSacrificeForEtherealWithMessage = 801;
    public const int MixTransmitEthereal = 802;
    public const int MixRecoverExhaustedEthereal = 803;
    public const int MixSacrificeForEthereal = 804;
    public const int MixSacrificeForEtherealStone = 805;
    public const int MixTransmitEtherealFromStone = 806;

    /// <summary><c>ItemBase::ITEM_CODE_JOKER</c>: the joker card, which cannot be the enhanced card.</summary>
    public const int JokerCardCode = 540070;

    /// <summary><c>SKILL_FRIENDSHIP_OF_CROWN</c>, the joker's passive that raises a creature card's chance.</summary>
    public const int FriendshipOfCrownSkill = 47027;

    /// <summary><c>GameRule::MAX_ETHEREAL_STONE_DURABILITY</c>.</summary>
    public const long MaxEtherealStone = 100_000_000;

    /// <summary><c>ItemBase::MAX_SOCKET_NUMBER</c>.</summary>
    public const int MaxSockets = 4;

    /// <summary><c>MixBase::RESULT_SOCKET_ERASE</c> / <c>RESULT_SOCKET_RESET</c>.</summary>
    private const int SocketErase = 1;
    private const int SocketReset = 2;

    private const int FailResultDoNothing = 4;
    private const int BeltClass = 250;

    private static int V(MixResourceEntity rule, int index) => index switch
    {
        0 => rule.MixValue01, 1 => rule.MixValue02, 2 => rule.MixValue03,
        3 => rule.MixValue04, 4 => rule.MixValue05, _ => rule.MixValue06
    };

    private static int Roll(Func<int, int, int> roll, long min, long max) =>
        roll((int)Math.Min(min, max), (int)Math.Max(min, max));

    /// <summary>The official plan of a type it ports, or null for a type handled elsewhere (102, 601) or unknown.</summary>
    private static CraftPlan PlanOfficial(MixResolution resolution, MixMaterial? target, EnhanceResourceEntity enhance,
        Func<int, int, int> roll, Func<int, (int ItemId, long Count)?> pickFromGroup, MixContext context)
    {
        return resolution.Rule.MixType switch
        {
            MixEnhance or MixEnhanceWithoutFail => PlanEnhanceItem(resolution, target, enhance, roll),
            MixEnhanceCreatureCard or MixEnhanceCreatureCardWithJoker =>
                PlanCreatureCard(resolution, target, enhance, roll, pickFromGroup),
            MixSetLevel or MixSetLevelCreateItem or MixSetLevelSetFlag or MixSetLevelSetFlagCreateItem
                or MixSetLevelSetFlagCreateItemWithMainLevel or MixSetLevelOnSubMaterialLevelSetFlag
                or MixSetLevelSetFlagCreateItemWithMainLevelSetZero or MixAddLevel or MixAddLevelCreateItem
                or MixAddLevelSetFlag or MixAddLevelSetFlagCreateItem =>
                PlanItemLevel(resolution, target, roll, pickFromGroup),
            MixRecycle or MixRecycleEnhance => PlanRecycle(resolution, roll),
            MixRestoreEnhanceSetFlag => PlanRestoreEnhance(resolution, target, roll),
            MixChangeAppearanceCode => PlanAppearance(resolution, target),
            MixSetElementalEffect => PlanElementalEffect(resolution, target),
            MixSetElementalEffectParameter => PlanElementalParameter(resolution, target, roll),
            MixSetSocket => PlanSetSocket(resolution, target, roll, pickFromGroup, context),
            MixReplaceSocketWith => PlanReplaceSocket(resolution, target, roll),
            MixSacrificeForEtherealWithMessage or MixSacrificeForEthereal => PlanSacrifice(resolution, target, roll),
            MixSacrificeForEtherealStone => PlanStoneSacrifice(resolution, roll, context),
            MixTransmitEthereal => PlanTransmit(resolution, target),
            MixTransmitEtherealFromStone => PlanTransmitFromStone(resolution, target, context),
            MixRecoverExhaustedEthereal => PlanRecoverExhausted(resolution, target, roll),
            _ => null
        };
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    private static List<CraftConsumption> ConsumeAll(MixResolution resolution) =>
        resolution.Arranged.Select((material, group) =>
            new CraftConsumption(material.Handle, resolution.ConsumedCounts[group]) { ExpectedMaterial = material })
            .ToList();

    private static CraftItemMutation Mutate(MixMaterial item, Action<ItemEntity> apply) => new(item.Handle, item, apply);

    private static uint[] Report(MixMaterial item) => new[] { item.Handle };

    private static CraftPlan Done(List<CraftConsumption> consumed, IReadOnlyList<uint> result,
        params CraftItemMutation[] mutations) =>
        new(ResultCode.Success, consumed, null, result) { Mutations = mutations };

    private static int? ResolveItem(int itemId, ref long count, Func<int, (int ItemId, long Count)?> pickFromGroup,
        bool nested)
    {
        for (var depth = 0; itemId < 0 && depth < MaxGroupDepth; depth++)
        {
            if (pickFromGroup?.Invoke(itemId) is not { } picked)
            {
                return null;
            }

            (itemId, count) = picked;
            if (!nested)
            {
                break;
            }
        }

        return itemId > 0 && count > 0 ? itemId : null;
    }

    private static string EtherealLine(int message, long amount) =>
        $"@{message}\v#@Ethereal_Durability@#\v{amount / 10000}";

    // ---- enhancement ---------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>EnhanceItem</c>: the cube is the first arranged material; every material goes at its count; the gain
    /// is drawn in <c>[value[1], value[2]]</c> for 101 (1 for 103) and capped at <c>max_enhance</c>. A failure of
    /// 101 is <c>procEnhanceFail</c>; of 103, the enhancement becomes
    /// <c>((rand(e × value[2], e × value[3]) / 1000) + 5) / 10</c> (<c>game.enhance_fail_type</c> 0, the default).
    /// </summary>
    private static CraftPlan PlanEnhanceItem(MixResolution resolution, MixMaterial? target,
        EnhanceResourceEntity enhance, Func<int, int, int> roll)
    {
        var rule = resolution.Rule;
        if (target is not { } item || enhance?.RequiredItemId is null || resolution.Arranged.Count == 0
            || resolution.Arranged[0].ItemCode != enhance.RequiredItemId)
        {
            return CraftPlan.Refused(ResultCode.InvalidArgument);
        }

        var current = item.Enhance;
        var gain = rule.MixType == MixEnhance ? Roll(roll, V(rule, 1), V(rule, 2)) : 1;
        if (enhance.MaxEnhance < current + gain)
        {
            gain = (int)(enhance.MaxEnhance - current);
        }

        if (gain <= 0)
        {
            return CraftPlan.Refused(ResultCode.InvalidArgument);
        }

        var consumed = ConsumeAll(resolution);
        var chance = enhance.Percentage is { } rates && current < rates.Length ? rates[(int)current] : 0m;
        if (roll(0, RollScale) <= (int)(chance * RollScale))
        {
            return Done(consumed, Report(item), Mutate(item, entry => entry.Enhance = (uint)(current + gain)));
        }

        if (rule.MixType == MixEnhanceWithoutFail)
        {
            var fallen = (Roll(roll, current * V(rule, 2), current * V(rule, 3)) / 1000 + 5) / 10;
            return Done(consumed, Array.Empty<uint>(), Mutate(item, entry => entry.Enhance = (uint)Math.Max(0, fallen)));
        }

        return EnhanceFailure(consumed, item, (int)enhance.FailResult, Array.Empty<CraftCreation>());
    }

    /// <summary>
    /// <c>procEnhanceFail</c>: 2 takes one unit and gives back a copy at −3 above +3; 1 sets <c>FAILED</c> and empties
    /// the sockets (a belt keeps them); 3 takes 3 levels off, down to 0 at +3 or less; 4 and the rest change nothing.
    /// </summary>
    private static CraftPlan EnhanceFailure(List<CraftConsumption> consumed, MixMaterial item, int failResult,
        IReadOnlyList<CraftCreation> alsoCreated, CraftCardEnhance? card = null)
    {
        var created = alsoCreated.ToList();
        var mutations = new List<CraftItemMutation>();
        var enhance = item.Enhance;
        switch (failResult)
        {
            case (int)FailResultType.SkillCard:
                consumed.Add(new CraftConsumption(item.Handle, 1) { ExpectedMaterial = item });
                if (enhance > 3)
                {
                    created.Add(new CraftCreation(item.ItemCode, 1, (int)item.Level)
                        { Enhance = (int)enhance - 3, CopyOf = item.Handle });
                }

                enhance = -1;
                break;

            case (int)FailResultType.Fail:
                var keepSockets = item.ItemClass == BeltClass;
                mutations.Add(Mutate(item, entry =>
                {
                    entry.Flag = (ItemFlag)unchecked((int)(Raw(entry.Flag) | FailedFlagMask));
                    if (!keepSockets && entry.SocketItemIds is { } sockets)
                    {
                        Array.Clear(sockets);
                    }
                }));
                break;

            case (int)FailResultType.Accessory:
                enhance = enhance <= 3 ? 0 : enhance - 3;
                var next = enhance;
                mutations.Add(Mutate(item, entry => entry.Enhance = (uint)next));
                break;
        }

        return new CraftPlan(ResultCode.Success, consumed, null, Array.Empty<uint>())
        {
            Mutations = mutations,
            Created = created,
            CardEnhance = card is { } c ? c with { Enhance = (int)enhance } : null
        };
    }

    private static uint Raw(ItemFlag flag) => flag == ItemFlag.None ? 0u : unchecked((uint)flag);

    /// <summary>
    /// <c>EnhanceCreatureCard</c>: the main card, the second card and the soul mixer (and the joker for 105). Both
    /// cards at the same enhancement, neither formed, the main one not the joker, neither spent; chance
    /// <c>percentage[e] × 100000 + (level₁ + level₂) × 125</c> (+ the joker's Friendship of Crown). A success raises
    /// the card by one and fills its ethereal durability; a failure of a +3 card of rate 1 or more may give
    /// <c>value[3]</c> (<c>value[5]</c>%), then <c>procEnhanceFail</c>.
    /// </summary>
    private static CraftPlan PlanCreatureCard(MixResolution resolution, MixMaterial? target,
        EnhanceResourceEntity enhance, Func<int, int, int> roll, Func<int, (int ItemId, long Count)?> pickFromGroup)
    {
        var rule = resolution.Rule;
        var joker = rule.MixType == MixEnhanceCreatureCardWithJoker;
        if (target is not { } main || resolution.Arranged.Count < (joker ? 3 : 2) || enhance is null)
        {
            return CraftPlan.Refused(ResultCode.InvalidArgument);
        }

        var sub = resolution.Arranged[0];
        var spent = Spent(main) || Spent(sub);
        var formed = main.Instance?.Formed == true || sub.Instance?.Formed == true
                     || joker && resolution.Arranged[2].Instance?.Formed == true;
        if (main.ItemCode == JokerCardCode || spent || main.Enhance != sub.Enhance || enhance.MaxEnhance <= main.Enhance
            || formed)
        {
            return CraftPlan.Refused(ResultCode.InvalidArgument);
        }

        var chance = enhance.Percentage is { } rates && main.Enhance < rates.Length ? rates[(int)main.Enhance] : 0m;
        var rate = (int)(chance * RollScale);
        var addition = ((main.Instance?.SummonLevel ?? 1) + (sub.Instance?.SummonLevel ?? 1)) * 125
                       + (joker ? resolution.Arranged[2].Instance?.JokerBonus ?? 0 : 0);
        var consumed = ConsumeAll(resolution);

        if (roll(0, RollScale) <= rate + addition)
        {
            var next = main.Enhance + 1;
            var max = main.Resource.MaxEtherealDurability;
            return Done(consumed, Report(main), Mutate(main, entry =>
            {
                entry.Enhance = (uint)next;
                entry.EtherealDurability = max;
            })) with { CardEnhance = new CraftCardEnhance(main.Handle, (int)next, true) };
        }

        var created = new List<CraftCreation>();
        if (main.Enhance >= 3 && (main.Instance?.SummonRate ?? 0) >= 1 && roll(0, 100) <= V(rule, 5))
        {
            long count = 1;
            if (ResolveItem(V(rule, 3), ref count, pickFromGroup, nested: true) is { } itemId)
            {
                created.Add(new CraftCreation(itemId, count, 0));
            }
        }

        return EnhanceFailure(consumed, main, (int)enhance.FailResult, created,
            new CraftCardEnhance(main.Handle, (int)main.Enhance, false));
    }

    private static bool Spent(MixMaterial card) => card.Resource.MaxEtherealDurability > 0 && card.CurrentEthereal == 0;

    // ---- item level ----------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>MixItemLevel</c>: the level and enhancement the main material takes (<c>value[0]</c> as enhance × 100 +
    /// level, set or added; 214 copies the sub material <c>value[0]</c>), the flag bit <c>value[1]</c> turned on or
    /// off by <c>value[2]</c>, and for the creating types <c>value[3]</c> made <c>value[5]</c>% of the time at
    /// <c>value[4]</c> (or the main material's level for 213/215). Every material is consumed; a failed creation
    /// still changes the main material.
    /// </summary>
    private static CraftPlan PlanItemLevel(MixResolution resolution, MixMaterial? target, Func<int, int, int> roll,
        Func<int, (int ItemId, long Count)?> pickFromGroup)
    {
        var rule = resolution.Rule;
        var type = rule.MixType;
        var value = V(rule, 0);
        long enhance = 0, level = 0;
        switch (type)
        {
            case MixSetLevel or MixSetLevelCreateItem or MixSetLevelSetFlag or MixSetLevelSetFlagCreateItem
                or MixSetLevelSetFlagCreateItemWithMainLevel:
                enhance = value / 100;
                level = value % 100;
                if (enhance == 0 && target is { } a) enhance = a.Enhance;
                if (level == 0 && target is { } b) level = b.Level;
                break;
            case MixSetLevelSetFlagCreateItemWithMainLevelSetZero:
                enhance = value / 100;
                level = value % 100;
                break;
            case MixSetLevelOnSubMaterialLevelSetFlag:
                if (value < 1 || value > resolution.Arranged.Count)
                {
                    return CraftPlan.Refused(ResultCode.InvalidArgument);
                }

                enhance = resolution.Arranged[value - 1].Enhance;
                level = resolution.Arranged[value - 1].Level;
                break;
            default:
                if (target is not { } added)
                {
                    return CraftPlan.Refused(ResultCode.InvalidArgument);
                }

                enhance = added.Enhance + value / 100;
                level = added.Level + value % 100;
                break;
        }

        var consumed = ConsumeAll(resolution);
        var success = true;
        var created = new List<CraftCreation>();
        if (type is MixSetLevelCreateItem or MixSetLevelSetFlagCreateItem or MixSetLevelSetFlagCreateItemWithMainLevel
            or MixSetLevelSetFlagCreateItemWithMainLevelSetZero or MixAddLevelCreateItem or MixAddLevelSetFlagCreateItem)
        {
            if (roll(0, 100) <= V(rule, 5))
            {
                long count = 1;
                // MixItemLevel draws a group once (an `if`, not the `while` of the other types).
                if (ResolveItem(V(rule, 3), ref count, pickFromGroup, nested: false) is { } itemId)
                {
                    var madeEnhance = V(rule, 4) / 100;
                    var madeLevel = V(rule, 4) % 100;
                    if (type is MixSetLevelSetFlagCreateItemWithMainLevel or MixSetLevelSetFlagCreateItemWithMainLevelSetZero
                        && target is { } main)
                    {
                        madeEnhance = (int)main.Enhance;
                        madeLevel = (int)main.Level;
                    }

                    created.Add(new CraftCreation(itemId, count, madeLevel) { Enhance = madeEnhance });
                }
            }
            else
            {
                success = false;
            }
        }

        var mutations = new List<CraftItemMutation>();
        if (target is { } item)
        {
            var setsFlag = type is MixSetLevelSetFlag or MixSetLevelSetFlagCreateItem
                or MixSetLevelSetFlagCreateItemWithMainLevel or MixSetLevelSetFlagCreateItemWithMainLevelSetZero
                or MixSetLevelOnSubMaterialLevelSetFlag or MixAddLevelSetFlag or MixAddLevelSetFlagCreateItem;
            var bit = 1u << (V(rule, 1) & 0x1F);
            var on = V(rule, 2) != 0;
            mutations.Add(Mutate(item, entry =>
            {
                entry.Enhance = (uint)Math.Max(0, enhance);
                entry.Level = (uint)Math.Max(0, level);
                if (setsFlag)
                {
                    entry.Flag = (ItemFlag)unchecked((int)(on ? Raw(entry.Flag) | bit : Raw(entry.Flag) & ~bit));
                }
            }));
        }

        return new CraftPlan(ResultCode.Success, consumed, null,
            success ? new[] { target?.Handle ?? 0u } : Array.Empty<uint>())
        {
            Mutations = mutations,
            Created = created
        };
    }

    // ---- recycling and restoration -------------------------------------------------------------------------------

    /// <summary>
    /// <c>RecycleItem</c>: an equipment (group 1..9) and a cube, in either order. The cube goes in any case; with
    /// <c>value[4]</c>% the equipment becomes <c>rand(e × value[2], e × value[3])</c> of <c>value[0]</c> at level
    /// <c>value[1]</c> (402 reads it in 1/10 000, rounded), otherwise it is lost.
    /// </summary>
    private static CraftPlan PlanRecycle(MixResolution resolution, Func<int, int, int> roll)
    {
        var rule = resolution.Rule;
        if (resolution.Arranged.Count < 2)
        {
            return CraftPlan.Refused(ResultCode.InvalidArgument);
        }

        var equipment = resolution.Arranged[0];
        var cube = resolution.Arranged[1];
        if (equipment.ItemGroup is > 9 or < 1)
        {
            (equipment, cube) = (cube, equipment);
        }

        var consumed = new List<CraftConsumption>
        {
            new(cube.Handle, 1) { ExpectedMaterial = cube },
            new(equipment.Handle, 1) { ExpectedMaterial = equipment }
        };
        if (roll(1, 10000) > V(rule, 4) * 100)
        {
            return Done(consumed, Array.Empty<uint>());
        }

        var enhance = equipment.Enhance;
        long count = rule.MixType == MixRecycleEnhance
            ? (Roll(roll, enhance * V(rule, 2), enhance * V(rule, 3)) / 1000 + 5) / 10
            : Roll(roll, enhance * V(rule, 2), enhance * V(rule, 3));
        if (count <= 0)
        {
            return Done(consumed, Array.Empty<uint>());
        }

        return new CraftPlan(ResultCode.Success, consumed, null, Array.Empty<uint>())
        {
            Created = new[] { new CraftCreation(V(rule, 0), count, V(rule, 1)) },
            ReportCreated = true
        };
    }

    /// <summary>
    /// <c>RestoreEnhance</c>: one unit of the powder (first arranged material); the enhancement becomes
    /// <c>((rand(e × value[2], e × value[3]) / 1000) + 5) / 10</c> and the flag bit <c>value[0]</c> goes on or off by
    /// <c>value[1]</c>.
    /// </summary>
    private static CraftPlan PlanRestoreEnhance(MixResolution resolution, MixMaterial? target, Func<int, int, int> roll)
    {
        var rule = resolution.Rule;
        if (target is not { } item || resolution.Arranged.Count == 0 || V(rule, 0) is < 0 or > 31)
        {
            return CraftPlan.Refused(ResultCode.InvalidArgument);
        }

        var powder = resolution.Arranged[0];
        var current = item.Enhance;
        var enhance = (Roll(roll, current * V(rule, 2), current * V(rule, 3)) / 1000 + 5) / 10;
        var bit = 1u << V(rule, 0);
        var on = V(rule, 1) != 0;
        return Done(new List<CraftConsumption> { new(powder.Handle, 1) { ExpectedMaterial = powder } }, Report(item),
            Mutate(item, entry =>
            {
                entry.Enhance = (uint)Math.Max(0, enhance);
                entry.Flag = (ItemFlag)unchecked((int)(on ? Raw(entry.Flag) | bit : Raw(entry.Flag) & ~bit));
            }));
    }

    // ---- appearance and elements ---------------------------------------------------------------------------------

    /// <summary><c>ChangeAppearanceCode</c>: the main material takes the look of the first arranged material.</summary>
    private static CraftPlan PlanAppearance(MixResolution resolution, MixMaterial? target)
    {
        if (target is not { } item || resolution.Arranged.Count == 0)
        {
            return CraftPlan.Refused(ResultCode.InvalidArgument);
        }

        var look = resolution.Arranged[0].ItemCode;
        return Done(ConsumeAll(resolution), Report(item), Mutate(item, entry => entry.AppearanceCode = look));
    }

    /// <summary>
    /// <c>SetElementalEffect</c>: the effector's first option gives the element (<c>fOptVar1[0]</c>, 0 clears it) and
    /// its duration in seconds (<c>fOptVar2[0]</c>, 0 forever). One effector is consumed.
    /// </summary>
    private static CraftPlan PlanElementalEffect(MixResolution resolution, MixMaterial? target)
    {
        if (target is not { } item || resolution.Arranged.Count == 0)
        {
            return CraftPlan.Refused(ResultCode.InvalidArgument);
        }

        var effector = resolution.Arranged[0];
        var element = (int)effector.Resource.OptVar1First;
        var seconds = (long)effector.Resource.OptVar2First;
        return Done(new List<CraftConsumption> { new(effector.Handle, 1) { ExpectedMaterial = effector } }, Report(item),
            Mutate(item, entry =>
            {
                if (element == 0)
                {
                    entry.ElementalEffectType = ElementalType.None;
                    entry.ElementalEffectExpireTime = null;
                    entry.ElementalEffectAttackPoint = 0;
                    entry.ElementalEffectMagicPoint = 0;
                    return;
                }

                entry.ElementalEffectType = (ElementalType)element;
                entry.ElementalEffectExpireTime = seconds > 0 ? DateTime.UtcNow.AddSeconds(seconds) : null;
            }));
    }

    /// <summary>
    /// <c>SetElementalEffectParameter</c>: the enhancer's first two options are the ranges of the element's attack
    /// and magic points; a range of two zeros leaves its value. One enhancer is consumed.
    /// </summary>
    private static CraftPlan PlanElementalParameter(MixResolution resolution, MixMaterial? target,
        Func<int, int, int> roll)
    {
        if (target is not { } item || resolution.Arranged.Count == 0)
        {
            return CraftPlan.Refused(ResultCode.InvalidArgument);
        }

        var enhancer = resolution.Arranged[0];
        var fields = enhancer.Resource;
        int? attack = fields.OptVar1First != 0 || fields.OptVar2First != 0
            ? Roll(roll, (long)fields.OptVar1First, (long)fields.OptVar2First) : null;
        int? magic = fields.OptVar1Second != 0 || fields.OptVar2Second != 0
            ? Roll(roll, (long)fields.OptVar1Second, (long)fields.OptVar2Second) : null;
        return Done(new List<CraftConsumption> { new(enhancer.Handle, 1) { ExpectedMaterial = enhancer } }, Report(item),
            Mutate(item, entry =>
            {
                if (attack is { } a) entry.ElementalEffectAttackPoint = a;
                if (magic is { } m) entry.ElementalEffectMagicPoint = m;
            }));
    }

    // ---- sockets -------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>SetSocket</c>: the first socket holding <c>value[0]</c> takes <c>value[1]</c> (<c>value[2]</c>%) or
    /// <c>value[3]</c>; filling an empty socket adds the stone's endurance when <c>value[4]</c> is set; a success
    /// also gives <c>value[5]</c>. Every material is consumed.
    /// </summary>
    private static CraftPlan PlanSetSocket(MixResolution resolution, MixMaterial? target, Func<int, int, int> roll,
        Func<int, (int ItemId, long Count)?> pickFromGroup, MixContext context)
    {
        var rule = resolution.Rule;
        if (target is not { } item)
        {
            return CraftPlan.Refused(ResultCode.InvalidArgument);
        }

        var index = -1;
        for (var i = 0; i < MaxSockets; i++)
        {
            if (item.Socket(i) == V(rule, 0))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return CraftPlan.Refused(ResultCode.InvalidArgument);
        }

        var success = roll(0, 99) < V(rule, 2);
        var code = success ? V(rule, 1) : V(rule, 3);
        var endurance = V(rule, 4) != 0 && V(rule, 0) == 0 && code != 0 ? context.ResourceOf(code).Endurance : 0;
        var created = new List<CraftCreation>();
        if (success && V(rule, 5) != 0)
        {
            long count = 1;
            if (ResolveItem(V(rule, 5), ref count, pickFromGroup, nested: true) is { } itemId)
            {
                created.Add(new CraftCreation(itemId, count, 0));
            }
        }

        return new CraftPlan(ResultCode.Success, ConsumeAll(resolution), null,
            success ? Report(item) : Array.Empty<uint>())
        {
            Mutations = new[]
            {
                Mutate(item, entry =>
                {
                    entry.SocketItemIds = Sockets(entry.SocketItemIds);
                    entry.SocketItemIds[index] = code;
                    entry.Endurance += endurance;
                })
            },
            Created = created
        };
    }

    private static long[] Sockets(long[] sockets)
    {
        if (sockets is { Length: >= MaxSockets })
        {
            return sockets;
        }

        var resized = new long[MaxSockets];
        if (sockets is not null)
        {
            Array.Copy(sockets, resized, sockets.Length);
        }

        return resized;
    }

    /// <summary>
    /// <c>ReplaceSocketWith</c>: the sub material <c>value[0]</c> lends its sockets to the main material
    /// (<c>value[1]</c>%); it is then erased (1) or emptied (2) per <c>value[2]</c> on a success, <c>value[3]</c> on
    /// a failure. The other materials are consumed.
    /// </summary>
    private static CraftPlan PlanReplaceSocket(MixResolution resolution, MixMaterial? target, Func<int, int, int> roll)
    {
        var rule = resolution.Rule;
        var slot = V(rule, 0);
        if (target is not { } item || slot < 1 || slot > resolution.Arranged.Count)
        {
            return CraftPlan.Refused(ResultCode.InvalidArgument);
        }

        var donor = resolution.Arranged[slot - 1];
        var consumed = ConsumeAll(resolution).Where((_, group) => group != slot - 1).ToList();
        var success = roll(0, 99) < V(rule, 1);
        var policy = success ? V(rule, 2) : V(rule, 3);
        var mutations = new List<CraftItemMutation>();
        if (success)
        {
            var sockets = Enumerable.Range(0, MaxSockets).Select(donor.Socket).ToArray();
            mutations.Add(Mutate(item, entry => entry.SocketItemIds = sockets.ToArray()));
        }

        if (policy == SocketErase)
        {
            consumed.Add(new CraftConsumption(donor.Handle, resolution.ConsumedCounts[slot - 1]) { ExpectedMaterial = donor });
        }
        else if (policy == SocketReset)
        {
            mutations.Add(Mutate(donor, entry => entry.SocketItemIds = new long[MaxSockets]));
        }

        return new CraftPlan(ResultCode.Success, consumed, null, success ? Report(item) : Array.Empty<uint>())
        {
            Mutations = mutations
        };
    }

    // ---- ethereal durability -------------------------------------------------------------------------------------

    /// <summary>
    /// <c>SacrificeItemForEtherealDurability</c>: the first material is only spent; each other one gives, with
    /// <c>value[3]</c>%, <c>price × value[0] / 100 × count + value[1]</c> (a quarter for a spent item), until the main
    /// material is full. A material whose bit is set in <c>value[2]</c> is kept.
    /// </summary>
    private static CraftPlan PlanSacrifice(MixResolution resolution, MixMaterial? target, Func<int, int, int> roll)
    {
        var rule = resolution.Rule;
        if (target is not { } item)
        {
            return CraftPlan.Refused(ResultCode.InvalidArgument);
        }

        var max = item.Resource.MaxEtherealDurability;
        long current = item.CurrentEthereal;
        var consumed = new List<CraftConsumption>();
        var lines = new List<string>();
        var success = false;
        for (var i = 0; i < resolution.Arranged.Count; i++)
        {
            if (current >= max)
            {
                break;
            }

            var sacrifice = resolution.Arranged[i];
            var count = resolution.ConsumedCounts[i];
            if (i != 0 && roll(0, 99) < V(rule, 3))
            {
                success = true;
                var recover = sacrifice.Resource.Price * V(rule, 0) / 100 * count + V(rule, 1);
                if (Spent(sacrifice))
                {
                    recover /= 4;
                }

                current = Math.Min(max, current + recover);
                if (rule.MixType == MixSacrificeForEtherealWithMessage)
                {
                    lines.Add(EtherealLine(7900, recover));
                }
            }

            if ((V(rule, 2) & (1 << i)) == 0)
            {
                consumed.Add(new CraftConsumption(sacrifice.Handle, count) { ExpectedMaterial = sacrifice });
            }
        }

        var result = (int)current;
        return new CraftPlan(ResultCode.Success, consumed, null, success ? Report(item) : Array.Empty<uint>())
        {
            Mutations = success ? new[] { Mutate(item, entry => entry.EtherealDurability = result) } : Array.Empty<CraftItemMutation>(),
            ChatLines = lines
        };
    }

    /// <summary>
    /// <c>SacrificeItemForEtherealStoneDurability</c>: the same gift, poured into the crafter's ethereal stone up to
    /// <see cref="MaxEtherealStone"/>; a success answers by one line and no 257.
    /// </summary>
    private static CraftPlan PlanStoneSacrifice(MixResolution resolution, Func<int, int, int> roll, MixContext context)
    {
        var rule = resolution.Rule;
        var stone = context.EtherealStone;
        var gained = 0L;
        var consumed = new List<CraftConsumption>();
        for (var i = 0; i < resolution.Arranged.Count; i++)
        {
            if (stone >= MaxEtherealStone)
            {
                break;
            }

            var sacrifice = resolution.Arranged[i];
            var count = resolution.ConsumedCounts[i];
            if (roll(0, 99) < V(rule, 3))
            {
                var recover = sacrifice.Resource.Price * V(rule, 0) / 100 * count + V(rule, 1);
                if (Spent(sacrifice))
                {
                    recover /= 4;
                }

                var next = Math.Min(MaxEtherealStone, stone + recover);
                gained += next - stone;
                stone = next;
            }

            if ((V(rule, 2) & (1 << i)) == 0)
            {
                consumed.Add(new CraftConsumption(sacrifice.Handle, count) { ExpectedMaterial = sacrifice });
            }
        }

        return new CraftPlan(ResultCode.Success, consumed, null, Array.Empty<uint>())
        {
            EtherealStoneDelta = gained,
            NoResult = gained > 0,
            ChatLines = gained > 0 ? new[] { EtherealLine(7900, gained) } : Array.Empty<string>()
        };
    }

    /// <summary>
    /// <c>TransmitEtherealDurability</c>: a battery (first material) and a catalyst (second, one per point) refill the
    /// main material: <c>min((used − value[1]) × 100 / value[0], battery points − 1, catalysts)</c> points, each worth
    /// <c>10000 × value[0] / 100</c> plus <c>value[1]</c>.
    /// </summary>
    private static CraftPlan PlanTransmit(MixResolution resolution, MixMaterial? target)
    {
        var rule = resolution.Rule;
        if (target is not { } item || resolution.Arranged.Count < 2 || V(rule, 0) == 0)
        {
            return CraftPlan.Refused(ResultCode.InvalidArgument);
        }

        var battery = resolution.Arranged[0];
        var catalyst = resolution.Arranged[1];
        var used = (item.Resource.MaxEtherealDurability - item.CurrentEthereal) / 10000;
        if (used == 0 || item.CurrentEthereal == 0)
        {
            return new CraftPlan(ResultCode.Success, Array.Empty<CraftConsumption>(), null, Array.Empty<uint>());
        }

        var required = (int)((used - V(rule, 1)) * 100m / V(rule, 0));
        var extract = Math.Min(required, (battery.CurrentEthereal - 1) / 10000);
        extract = (int)Math.Min(extract, resolution.ConsumedCounts[1]);
        if (extract <= 0 || catalyst.Count < extract || battery.CurrentEthereal / 10000 < extract)
        {
            return new CraftPlan(ResultCode.Success, Array.Empty<CraftConsumption>(), null, Array.Empty<uint>());
        }

        var amount = (long)extract * 10000 * V(rule, 0) / 100 + V(rule, 1);
        var batteryLeft = battery.CurrentEthereal - extract * 10000;
        var itemNext = (int)Math.Min(item.Resource.MaxEtherealDurability, item.CurrentEthereal + amount);
        var consumed = new List<CraftConsumption>();
        for (var i = 0; i < resolution.Arranged.Count; i++)
        {
            if ((V(rule, 2) & (1 << i)) == 0)
            {
                var material = resolution.Arranged[i];
                consumed.Add(new CraftConsumption(material.Handle, i == 1 ? extract : resolution.ConsumedCounts[i])
                    { ExpectedMaterial = material });
            }
        }

        return new CraftPlan(ResultCode.Success, consumed, null, new[] { battery.Handle, item.Handle })
        {
            Mutations = new[]
            {
                Mutate(battery, entry => entry.EtherealDurability = batteryLeft),
                Mutate(item, entry => entry.EtherealDurability = itemNext)
            },
            ChatLines = new[] { EtherealLine(7901, amount) }
        };
    }

    /// <summary>
    /// <c>TransmitEtherealDurabilityFromEtherealStone</c>: the crafter's ethereal stone refills the main material,
    /// <c>min((used − value[1]) × 100 / value[0], stone points − 1)</c> points.
    /// </summary>
    private static CraftPlan PlanTransmitFromStone(MixResolution resolution, MixMaterial? target, MixContext context)
    {
        var rule = resolution.Rule;
        if (target is not { } item || V(rule, 0) == 0)
        {
            return CraftPlan.Refused(ResultCode.InvalidArgument);
        }

        var used = (item.Resource.MaxEtherealDurability - item.CurrentEthereal) / 10000;
        if (used == 0 || item.CurrentEthereal == 0)
        {
            return new CraftPlan(ResultCode.Success, Array.Empty<CraftConsumption>(), null, Array.Empty<uint>());
        }

        var required = (int)((used - V(rule, 1)) * 100m / V(rule, 0));
        var extract = (int)Math.Min(required, (context.EtherealStone - 1) / 10000);
        if (extract <= 0 || context.EtherealStone / 10000 < extract)
        {
            return new CraftPlan(ResultCode.Success, Array.Empty<CraftConsumption>(), null, Array.Empty<uint>());
        }

        var amount = (long)extract * 10000 * V(rule, 0) / 100 + V(rule, 1);
        var itemNext = (int)Math.Min(item.Resource.MaxEtherealDurability, item.CurrentEthereal + amount);
        var consumed = new List<CraftConsumption>();
        for (var i = 0; i < resolution.Arranged.Count; i++)
        {
            if ((V(rule, 2) & (1 << i)) == 0)
            {
                var material = resolution.Arranged[i];
                consumed.Add(new CraftConsumption(material.Handle, resolution.ConsumedCounts[i]) { ExpectedMaterial = material });
            }
        }

        return new CraftPlan(ResultCode.Success, consumed, null, Report(item))
        {
            Mutations = new[] { Mutate(item, entry => entry.EtherealDurability = itemNext) },
            EtherealStoneDelta = -(long)extract * 10000,
            ChatLines = new[] { EtherealLine(7901, amount) }
        };
    }

    /// <summary>
    /// <c>RecoverExhaustedEtherealDurability</c>: the materials go (those whose bit is set in <c>value[1]</c> stay);
    /// with <c>value[2]</c>% the main material recovers <c>value[2]</c>% of its maximum plus <c>value[0]</c>.
    /// </summary>
    private static CraftPlan PlanRecoverExhausted(MixResolution resolution, MixMaterial? target, Func<int, int, int> roll)
    {
        var rule = resolution.Rule;
        if (target is not { } item)
        {
            return CraftPlan.Refused(ResultCode.InvalidArgument);
        }

        var consumed = new List<CraftConsumption>();
        for (var i = 0; i < resolution.Arranged.Count; i++)
        {
            if ((V(rule, 1) & (1 << i)) == 0)
            {
                var material = resolution.Arranged[i];
                consumed.Add(new CraftConsumption(material.Handle, resolution.ConsumedCounts[i]) { ExpectedMaterial = material });
            }
        }

        if (roll(0, 99) >= V(rule, 2))
        {
            return Done(consumed, Array.Empty<uint>());
        }

        var max = item.Resource.MaxEtherealDurability;
        var recover = Math.Min(int.MaxValue, (long)max * V(rule, 2) / 100 + V(rule, 0));
        var next = (int)Math.Min(max, item.CurrentEthereal + recover);
        return new CraftPlan(ResultCode.Success, consumed, null, Report(item))
        {
            Mutations = new[] { Mutate(item, entry => entry.EtherealDurability = next) },
            ChatLines = new[] { EtherealLine(7901, recover) }
        };
    }
}
