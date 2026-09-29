using System;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Arcadia;

namespace Navislamia.Game.Services;

/// <summary>
/// One stack of the inventory as a rule condition reads it. Every field comes from one of the two rows
/// the item is made of, exactly as NGemity's accessors do (docs/packet-specs/socle-artisanat-ressources.md
/// §6.1):
/// <list type="bullet">
/// <item><c>ItemCode</c>, <c>Level</c>, <c>Enhance</c>, <c>Flag</c>, <c>Count</c> — the item instance
/// (<c>ItemEntity.ItemResourceId</c>, <c>Level</c>, <c>Enhance</c>, <c>Flag</c>, <c>Amount</c>,
/// <c>Item.h:48-56</c>);</item>
/// <item><c>ItemGroup</c>, <c>ItemClass</c>, <c>ItemRank</c>, <c>WearType</c> — the item resource
/// (<c>group</c>, <c>class</c>, <c>rank</c>, <c>wear_type</c>, <c>ObjectMgr.cpp:122-128</c>).</item>
/// </list>
/// <c>Flag</c> is the retail bitset as stored, the convention the drop path already documents
/// (<see cref="GroundItemDropRules.SummonFlagMask"/>): a condition names the <em>index</em> of a bit and
/// the mask is built from it.
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
    long Count);

/// <summary>
/// What a <c>TM_CS_MIX</c> frame resolves to: the rule that accepts the combination, and the quantity of
/// each material the engine would consume, indexed by the group of the rule (the arrangement of the
/// reference, <c>MixManager.cpp:280-284</c>). Nothing is consumed here: the resolution only reports.
/// </summary>
public readonly record struct MixResolution(MixResourceEntity Rule, IReadOnlyList<long> ConsumedCounts);

/// <summary>
/// The resolution of a <c>TM_CS_MIX</c> (256) frame against the <c>MixResource</c> rules, a static and
/// pure port of <c>MixManager</c> (<c>Chihiro/src/Crafting/MixManager.cpp:242-298</c> and
/// <c>:345-452</c>, pinned in the fiche): the first rule of the table whose declared <c>sub_material_count</c>
/// matches the frame, whose target group and material groups all accept the stacks the frame names, wins.
///
/// The materials are paired <em>by permutation</em> — a group takes any free stack it accepts — which is
/// <b>not</b> what the executed reference loop does: see <see cref="TryArrange"/> and the fiche §7, §9 and
/// §10. The choice is assumed and labelled, not presented as a fact of the reference.
///
/// It decides <em>match or no match</em> and nothing else: no rate is rolled, no item is removed, no
/// <c>TM_SC_MIX_RESULT</c> (257) is written. What a matched type does is the next lobe
/// (docs/packet-specs/socle-artisanat-ressources.md §8, L2).
/// </summary>
public static class MixResourceMatcher
{
    /// <summary>The twenty condition codes of the reference (<c>MixManager.h:64-85</c>).</summary>
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

    /// <summary>
    /// Walks <paramref name="rules"/> in the order it is given (the order of the table, <c>MixManager.cpp:244</c>)
    /// and returns the first one that accepts the frame. <paramref name="mainMaterial"/> is null when the
    /// frame named no target (<c>main_item.handle == 0</c>, <c>WorldSession.cpp:1450</c>): a rule whose
    /// target group carries no condition is the only one that can accept such a frame.
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

            if (!CheckMaterialInfo(MixResourceRules.MainMaterial(rule), mainMaterial, 1, out var mainCount))
            {
                continue;
            }

            if (!TryArrange(rule, subMaterials, out var arranged, out var counts))
            {
                continue;
            }

            if (!PostArrange(MixResourceRules.MainMaterial(rule), mainMaterial, arranged, counts, mainMaterial, mainCount))
            {
                continue;
            }

            var accepted = true;
            for (var group = 0; group < subMaterials.Count; group++)
            {
                if (PostArrange(MixResourceRules.SubMaterial(rule, group), mainMaterial, arranged, counts,
                        arranged[group], counts[group]))
                {
                    continue;
                }

                accepted = false;
                break;
            }

            if (!accepted)
            {
                continue;
            }

            resolution = new MixResolution(rule, counts);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Assigns one material group of the rule to each stack of the frame, by <b>permutation</b>: the groups
    /// are walked in order and each takes the first stack left free that it accepts, without backtracking
    /// (<c>getProperMixInfoSub</c>, <c>MixManager.cpp:300-318</c>).
    ///
    /// This is <b>not</b> what the reference executes. The loop that runs (<c>:257-276</c>) calls
    /// <c>check_material_info((*it).sub_material[idx], pSubItem[idx], pCountList[idx])</c> with the
    /// <em>same</em> index on both sides — it therefore requires every group <c>j</c> to accept the stack
    /// <c>j</c>, produces the identity arrangement, and refuses a frame that only a different arrangement
    /// would satisfy. The permuting function it names, <c>getProperMixInfoSub</c>
    /// (<c>MixManager.h:162</c>, <c>MixManager.cpp:300</c>), has no caller: it is dead code, like
    /// <c>CreateItem</c> (<c>:192-240</c>).
    ///
    /// The choice is kept because it is the more permissive of the two readings, but it is <b>not
    /// established</b> for 7.3: it is assumed as a labelled divergence from the executed reference
    /// (fiche §7) and carried as an open question in §9 and §10, where the client test of §4 (step 7 —
    /// the same frame with its materials in reverse order) is what decides between the two. Today the two
    /// readings differ only in which frames the log reports as resolved: the answer is the same refusal.
    /// </summary>
    private static bool TryArrange(MixResourceEntity rule, IReadOnlyList<MixMaterial> subMaterials,
        out MixMaterial[] arranged, out long[] counts)
    {
        var count = subMaterials.Count;
        arranged = new MixMaterial[count];
        counts = new long[count];

        var consumed = new bool[count];
        for (var stack = 0; stack < count; stack++)
        {
            counts[stack] = subMaterials[stack].Count;
        }

        for (var group = 0; group < count; group++)
        {
            var found = false;

            for (var stack = 0; stack < count; stack++)
            {
                if (consumed[stack])
                {
                    continue;
                }

                if (!CheckMaterialInfo(MixResourceRules.SubMaterial(rule, group), subMaterials[stack],
                        subMaterials[stack].Count, out var consumedCount))
                {
                    continue;
                }

                consumed[stack] = true;
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
    /// The conditions each group has to satisfy. A group without a single condition only accepts an absent
    /// stack (<c>return pItem == nullptr</c>, <c>MixManager.cpp:347-348</c>) — a frame that named an item
    /// there is refused.
    /// </summary>
    /// <remarks>
    /// Codes 11, 12 and 15-18 are refused rather than satisfied: the reference leaves them inert (the body
    /// of 11 and 12 is commented out, 15-18 have no case at all and fall into <c>default: break</c>). A
    /// silent <c>default</c> would let a condition nobody established validate a recipe, so the port closes
    /// instead — an unmatched frame is refused with <c>InvalidArgument</c>, the answer NGemity sends for a
    /// frame no rule accepts (<c>WorldSession.cpp:1463-1466</c>). Codes 19 and 20 are decided in the
    /// post-arrangement, as in the reference: the reference's <c>default: break</c> makes them inert here
    /// too (<c>MixManager.cpp:443-444</c>), and code 20 answers <c>false</c> there (<c>:574-576</c>).
    /// </remarks>
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

            if (code == 0)
            {
                continue;
            }

            switch (code)
            {
                case CheckItemGroup:
                    if (value != stack.ItemGroup)
                    {
                        return false;
                    }

                    break;

                case CheckItemClass:
                    if (value != stack.ItemClass)
                    {
                        return false;
                    }

                    break;

                case CheckItemId:
                    if (value != stack.ItemCode)
                    {
                        return false;
                    }

                    break;

                case CheckItemRank:
                    if (value != stack.ItemRank)
                    {
                        return false;
                    }

                    break;

                case CheckItemLevel:
                    if (value != stack.Level)
                    {
                        return false;
                    }

                    break;

                case CheckFlagOn:
                    if ((FlagMask(value) & stack.Flag) == 0)
                    {
                        return false;
                    }

                    break;

                case CheckFlagOff:
                    if ((FlagMask(value) & stack.Flag) != 0)
                    {
                        return false;
                    }

                    break;

                case CheckEnhanceMatch:
                    if (value != stack.Enhance)
                    {
                        return false;
                    }

                    break;

                case CheckEnhanceDismatch:
                    if (value == stack.Enhance)
                    {
                        return false;
                    }

                    break;

                case CheckItemWearPositionMatch:
                    if (value != stack.WearType)
                    {
                        return false;
                    }

                    break;

                case CheckItemWearPositionMismatch:
                    if (value == stack.WearType)
                    {
                        return false;
                    }

                    break;

                case CheckItemCount:
                    if (value != declaredCount)
                    {
                        return false;
                    }

                    countChecked = true;
                    break;

                case CheckSameItemId:
                case CheckSameSummonCode:
                    // Both are decided on the arranged stacks, not here: 19 by the post-arrangement
                    // (MixManager.cpp:558-570), 20 by the same function answering false (:574-576). The
                    // reference's `default: break` (:443-444) leaves them inert at this stage, so the
                    // refusal stays where the reference puts it — and stays reachable, which is what makes
                    // it testable.
                    break;

                default:
                    // 11, 12, 15-18 and anything outside the table: refused, never satisfied.
                    return false;
            }
        }

        if (!countChecked)
        {
            consumedCount = 1;
        }

        return true;
    }

    /// <summary>
    /// The conditions that only make sense once the materials are arranged: <c>CHECK_SAME_ITEM_ID</c> names
    /// a slot of the arrangement (<c>value == 0</c> is the target, <c>n</c> the rearranged material
    /// <c>n - 1</c>) and demands the same item code. The reference rejects a slot index outside
    /// <c>[0, nSubMaterialCount]</c> (<c>MixManager.cpp:559-563</c>, where the bound is written with an
    /// <c>&amp;&amp;</c> that makes it dead) — the port keeps the index inside the arrangement instead of
    /// duplicating a condition it cannot honour.
    /// </summary>
    private static bool PostArrange(MixMaterialInfo info, MixMaterial? mainMaterial,
        IReadOnlyList<MixMaterial> arranged, IReadOnlyList<long> counts, MixMaterial? material, long count)
    {
        for (var i = 0; i < MixResourceRules.MaterialInfoCount; i++)
        {
            if (info.Types[i] == CheckSameItemId)
            {
                var slot = info.Values[i];
                var reference = slot == 0
                    ? mainMaterial
                    : slot - 1 < arranged.Count ? arranged[slot - 1] : null;

                if (reference is null || material is null || material.Value.ItemCode != reference.Value.ItemCode)
                {
                    return false;
                }
            }
            else if (info.Types[i] == CheckSameSummonCode)
            {
                // The reference answers false here: the condition is not implemented (MixManager.cpp:574-576).
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// <c>1 &lt;&lt; (value &amp; 0x1F)</c> of the reference (<c>MixManager.cpp:380-389</c>): the condition
    /// carries the <em>index</em> of a flag bit, the item carries the bitset.
    /// </summary>
    private static int FlagMask(int value)
    {
        return 1 << (value & 0x1F);
    }
}
