using System;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Arcadia;
using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.Services;

/// <summary>
/// One stack of the inventory as a rule condition reads it. Every field comes from one of the two rows
/// the item is made of (docs/packet-specs/socle-artisanat-ressources.md §6.1):
/// <list type="bullet">
/// <item><c>ItemCode</c>, <c>Level</c>, <c>Enhance</c>, <c>Flag</c>, <c>Count</c> — the item instance
/// (<c>ItemEntity.ItemResourceId</c>, <c>Level</c>, <c>Enhance</c>, <c>Flag</c>, <c>Amount</c>);</item>
/// <item><c>ItemGroup</c>, <c>ItemClass</c>, <c>ItemRank</c>, <c>WearType</c> — the item resource
/// (<c>group</c>, <c>class</c>, <c>rank</c>, <c>wear_type</c>);</item>
/// <item><see cref="Mix"/> — the other resource columns the official <c>MixManager</c> reads, and
/// <see cref="Instance"/> the other instance fields (ethereal durability, element, sockets, summon).</item>
/// </list>
/// <c>Flag</c> is the retail bitset as stored: a condition names the <em>index</em> of a bit and the mask is
/// built from it.
/// </summary>
public readonly record struct MixMaterial(
    int ItemCode,
    int ItemGroup,
    int ItemClass,
    int ItemRank,
    int WearType,
    long Level,
    long Enhance,
    int Flag,
    long Count,
    uint Handle = 0,
    long SkillId = 0,
    long? AvailableCount = null,
    ItemMixFields Mix = null,
    MixInstance Instance = null)
{
    public ItemMixFields Resource => Mix ?? ItemMixFields.Empty;

    /// <summary>
    /// The current ethereal durability. Nothing in this server consumes it and no creation path sets it, so a
    /// stored 0 on an item that has a maximum is an item never initialised, read as full — what the official
    /// <c>AllocItem</c> would have given it (socle-artisanat-objets-officiel.md §2).
    /// </summary>
    public int CurrentEthereal => Instance is { } instance
        ? instance.EtherealDurability == 0 ? Resource.MaxEtherealDurability : instance.EtherealDurability
        : Resource.MaxEtherealDurability;

    public long Socket(int index) =>
        Instance?.Sockets is { } sockets && index < sockets.Length ? sockets[index] : 0;
}

/// <summary>The instance fields of a material that only some conditions and effects read.</summary>
public sealed record MixInstance(int EtherealDurability, int ElementalType, long[] Sockets, int RemainingTime,
    int SummonCode = 0, int SummonRate = 0, int SummonLevel = 1, bool Formed = false, int Endurance = 0,
    int JokerBonus = 0);

/// <summary>
/// What a <c>TM_CS_MIX</c> frame resolves to: the rule that accepts the combination, and the quantity of
/// each material the engine would consume, indexed by the group of the rule. Nothing is consumed here.
/// </summary>
public readonly record struct MixResolution(MixResourceEntity Rule, IReadOnlyList<long> ConsumedCounts,
    IReadOnlyList<MixMaterial> Arranged);

/// <summary>
/// The resolution of a <c>TM_CS_MIX</c> (256) frame, ported from the official server
/// (<c>MixManager::GetProperMixInfoAndArrangeSubMaterials</c>, <c>check_material_info</c>,
/// <c>post_arrange_check_material_info</c>, <c>MixManager.cpp:139-607</c>): the first rule of the table whose
/// <c>sub_material_count</c> matches, whose target accepts the main material, and for which every sub condition
/// finds a stack — <b>the first unclaimed stack, in frame order, that satisfies it</b> — wins. The stacks are then
/// rearranged in condition order and the cross conditions (same item, same summon, socket comparisons…) judged.
/// It replaces the positional pairing ported from NGemity (socle-artisanat-objets-officiel.md §1).
/// </summary>
public static class MixResourceMatcher
{
    public const int CheckItemGroup = 1;
    public const int CheckItemClass = 2;
    public const int CheckItemId = 3;
    public const int CheckItemRank = 4;
    public const int CheckItemLevel = 5;
    public const int CheckFlagOn = 6;
    public const int CheckFlagOff = 7;
    public const int CheckEnhanceMatch = 8;
    public const int CheckEnhanceDismatch = 9;
    public const int CheckItemCount = 10;
    public const int CheckElementalEffectMatch = 11;
    public const int CheckElementalEffectMismatch = 12;
    public const int CheckItemWearPositionMatch = 13;
    public const int CheckItemWearPositionMismatch = 14;
    public const int CheckItemCountGe = 15;
    public const int CheckItemEtherealDurabilityE = 16;
    public const int CheckItemEtherealDurabilityNe = 17;
    public const int CheckItemGrade = 18;
    public const int CheckSameItemId = 19;
    public const int CheckSameSummonCode = 20;
    public const int CheckItemExpiredTimeGe = 21;
    public const int CheckItemExpiredTimeLe = 22;
    public const int CheckFirstSocketCodeMatch = 23;
    public const int CheckSameItemEnhance = 24;
    public const int CheckSameSkillId = 25;
    public const int CheckMaxEtherealDurabilityE = 26;
    public const int CheckMaxEtherealDurabilityNe = 27;
    public const int CheckFirstSocketCodeG = 28;
    public const int CheckFirstSocketCodeL = 29;
    public const int CheckItemType = 30;
    public const int CheckSameItemClass = 31;
    public const int CheckItemGroupNe = 32;
    public const int CheckIncludeRaceLimit = 33;
    public const int CheckAwakenItem = 34;
    public const int CheckSameSummonRate = 35;
    public const int CheckBaseFlagOn = 36;
    public const int CheckDesignerDefinedType = 37;
    public const int CheckBaseFlagOff = 38;

    /// <summary><c>DESIGNER_TYPE_WEAPON</c>, <c>DESIGNER_TYPE_ARMOR</c> of <c>check_designer_defined_type</c>.</summary>
    private const int DesignerWeapon = 1;
    private const int DesignerArmor = 2;

    /// <summary>The repository's race bits (Deva, Asura, Gaia = 1, 2, 4).</summary>
    private const int RaceMask = 7;

    /// <summary>
    /// Walks <paramref name="rules"/> in table order and returns the first one that accepts the frame.
    /// <paramref name="mainMaterial"/> is null when the frame named no target.
    /// </summary>
    public static bool TryResolve(
        IReadOnlyList<MixResourceEntity> rules,
        MixMaterial? mainMaterial,
        IReadOnlyList<MixMaterial> subMaterials,
        out MixResolution resolution)
    {
        resolution = default;

        foreach (var rule in rules)
        {
            if (rule.SubMaterialCount != subMaterials.Count)
            {
                continue;
            }

            // The main slot is always one unit.
            if (!CheckMaterialInfo(MixResourceRules.MainMaterial(rule), mainMaterial, 1, out _))
            {
                continue;
            }

            if (!TryArrange(rule, subMaterials, out var arranged, out var counts))
            {
                continue;
            }

            if (!PostArrange(MixResourceRules.MainMaterial(rule), mainMaterial, arranged, mainMaterial))
            {
                continue;
            }

            var accepted = true;
            for (var group = 0; group < subMaterials.Count; group++)
            {
                if (!PostArrange(MixResourceRules.SubMaterial(rule, group), mainMaterial, arranged, arranged[group]))
                {
                    accepted = false;
                    break;
                }
            }

            if (!accepted)
            {
                continue;
            }

            resolution = new MixResolution(rule, counts, arranged);
            return true;
        }

        return false;
    }

    /// <summary>
    /// For each sub condition in order, the first stack of the frame not yet claimed that satisfies it
    /// (<c>abSubMaterialChecked</c>); the stacks come out in condition order with the count each was judged at.
    /// </summary>
    private static bool TryArrange(MixResourceEntity rule, IReadOnlyList<MixMaterial> subMaterials,
        out MixMaterial[] arranged, out long[] counts)
    {
        var count = subMaterials.Count;
        arranged = new MixMaterial[count];
        counts = new long[count];
        var claimed = new bool[count];

        for (var group = 0; group < count; group++)
        {
            var found = false;
            for (var stack = 0; stack < count; stack++)
            {
                if (claimed[stack])
                {
                    continue;
                }

                if (!CheckMaterialInfo(MixResourceRules.SubMaterial(rule, group), subMaterials[stack],
                        subMaterials[stack].Count, out var consumedCount))
                {
                    continue;
                }

                claimed[stack] = true;
                arranged[group] = subMaterials[stack];
                counts[group] = consumedCount;
                found = true;
                break;
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// <c>check_material_info</c>. A group without a single condition only accepts an absent stack. A count
    /// condition (10, 15) keeps the frame's count, otherwise one unit is taken. Expiry (21, 22), awakening (34)
    /// and the base flags (36, 38) read columns this repository does not have: they refuse rather than let a
    /// recipe through — no Epic 7 recipe uses them.
    /// </summary>
    private static bool CheckMaterialInfo(MixMaterialInfo info, MixMaterial? material,
        long declaredCount, out long consumedCount)
    {
        consumedCount = declaredCount;

        if (info.Types[0] == 0)
        {
            return material is null;
        }

        if (material is null)
        {
            return false;
        }

        var stack = material.Value;
        var countChecked = false;

        for (var i = 0; i < MixResourceRules.MaterialInfoCount; i++)
        {
            var code = info.Types[i];
            var value = info.Values[i];
            var passes = code switch
            {
                0 => true,
                CheckItemGroup => value == stack.ItemGroup,
                CheckItemClass => value == stack.ItemClass,
                CheckItemId => value == stack.ItemCode,
                CheckItemRank => value == stack.ItemRank,
                CheckItemLevel => value == stack.Level,
                CheckFlagOn => (FlagMask(value) & stack.Flag) != 0,
                CheckFlagOff => (FlagMask(value) & stack.Flag) == 0,
                CheckEnhanceMatch => value == stack.Enhance,
                CheckEnhanceDismatch => value != stack.Enhance,
                CheckItemCount => value == declaredCount,
                CheckElementalEffectMatch => ElementalMatches(value, stack),
                CheckElementalEffectMismatch => ElementalMismatches(value, stack),
                CheckItemWearPositionMatch => value == stack.WearType,
                CheckItemWearPositionMismatch => value != stack.WearType,
                CheckItemCountGe => declaredCount >= value,
                CheckItemEtherealDurabilityE => stack.CurrentEthereal == value,
                CheckItemEtherealDurabilityNe => stack.CurrentEthereal != value,
                CheckItemGrade => stack.Resource.Grade == value,
                CheckFirstSocketCodeMatch => stack.Socket(0) == value,
                CheckMaxEtherealDurabilityE => stack.Resource.MaxEtherealDurability == value,
                CheckMaxEtherealDurabilityNe => stack.Resource.MaxEtherealDurability != value,
                CheckItemType => stack.Resource.BaseType == value,
                CheckItemGroupNe => stack.ItemGroup != value,
                CheckSameSummonRate => stack.Instance is { SummonCode: not 0 } summon && summon.SummonRate == value,
                CheckDesignerDefinedType => DesignerType(value, stack.ItemGroup),
                // Decided once the stacks are arranged.
                CheckSameItemId or CheckSameSummonCode or CheckSameItemEnhance or CheckSameSkillId
                    or CheckFirstSocketCodeG or CheckFirstSocketCodeL or CheckSameItemClass or CheckIncludeRaceLimit => true,
                _ => false
            };

            if (!passes)
            {
                return false;
            }

            if (code is CheckItemCount or CheckItemCountGe)
            {
                countChecked = true;
            }
        }

        if (!countChecked)
        {
            consumedCount = 1;
        }

        return true;
    }

    /// <summary>
    /// <c>2^element &gt;&gt; 1</c>: no element is 0, element <c>n</c> the bit <c>n − 1</c>. No element passes only
    /// a 0 condition; an element passes when its bit is in the condition.
    /// </summary>
    private static int ElementBit(MixMaterial stack) =>
        stack.Instance is { ElementalType: > 0 and < 31 } instance ? 1 << (instance.ElementalType - 1) : 0;

    private static bool ElementalMatches(int value, MixMaterial stack)
    {
        var bit = ElementBit(stack);
        return bit == 0 ? value == 0 : (bit & value) == bit;
    }

    private static bool ElementalMismatches(int value, MixMaterial stack)
    {
        var bit = ElementBit(stack);
        return bit == 0 ? value != 0 : (bit & value) != bit;
    }

    private static bool DesignerType(int value, int group) => value switch
    {
        DesignerWeapon => group == 1,
        DesignerArmor => group is 2 or 3 or 4 or 5 or 6 or 7 or 8,
        _ => false
    };

    /// <summary>
    /// <c>post_arrange_check_material_info</c>: the conditions naming a slot of the arrangement (<c>0</c> the main
    /// material, <c>n</c> the arranged sub material <c>n − 1</c>).
    /// </summary>
    private static bool PostArrange(MixMaterialInfo info, MixMaterial? mainMaterial,
        IReadOnlyList<MixMaterial> arranged, MixMaterial? material)
    {
        for (var i = 0; i < MixResourceRules.MaterialInfoCount; i++)
        {
            var code = info.Types[i];
            if (code is not (CheckSameItemId or CheckSameSummonCode or CheckSameItemEnhance or CheckSameSkillId
                or CheckFirstSocketCodeG or CheckFirstSocketCodeL or CheckSameItemClass or CheckIncludeRaceLimit))
            {
                continue;
            }

            var slot = info.Values[i];
            // CHECK_SAME_SUMMON_CODE compares with the main material whatever its slot says.
            var reference = code == CheckSameSummonCode || slot == 0
                ? mainMaterial
                : slot > 0 && slot <= arranged.Count ? arranged[slot - 1] : null;
            if (reference is not { } other || material is not { } item)
            {
                return false;
            }

            var passes = code switch
            {
                CheckSameItemId => item.ItemCode == other.ItemCode,
                CheckSameItemEnhance => item.Enhance == other.Enhance,
                CheckSameSkillId => item.SkillId > 0 && item.SkillId == other.SkillId,
                CheckSameSummonCode => SummonCode(item) == SummonCode(other),
                CheckFirstSocketCodeG => item.Socket(0) > other.Socket(0),
                CheckFirstSocketCodeL => item.Socket(0) < other.Socket(0),
                CheckSameItemClass => item.ItemClass == other.ItemClass,
                _ => (item.Resource.RaceLimit & other.Resource.RaceLimit & RaceMask) == (item.Resource.RaceLimit & RaceMask)
            };

            if (!passes)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The summon code a card is compared on: the resource's own <c>summon_id</c> first, else the instance's.</summary>
    private static int SummonCode(MixMaterial material) =>
        material.Resource.SummonId != 0 ? material.Resource.SummonId : material.Instance?.SummonCode ?? 0;

    /// <summary><c>ItemInstance::IsOn(index)</c>: the condition carries the index of a flag bit.</summary>
    private static int FlagMask(int value) => 1 << (value & 0x1F);
}
