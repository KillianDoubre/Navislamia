using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Navislamia.Game.Services.Creatures;

/// <summary>
/// Where a summon wears an item, <c>StructSummon::TranslateWearPosition</c> of the official server: only an item in
/// card form (<c>ITEM_FLAG_CARD</c>, bit 0) goes on a summon, judged against the summon's level; its slots are
/// <c>0..slot_amount-1</c> of the card's <c>CreatureEnhance</c> row — 0 and 1 for equipment, 2 for an accessory,
/// 3 and above for an artifact (docs/packet-specs/socle-equipement-invocation.md).
/// </summary>
public static class SummonWearRules
{
    /// <summary><c>ItemBase::MAX_ITEM_WEAR</c>: the position that asks the server to choose the slot.</summary>
    public const int AutoPosition = 24;

    /// <summary><c>SUMMON_MAX_NON_ARTIFACT_ITEM_WEAR</c>.</summary>
    public const int NonArtifactSlots = 2;

    /// <summary><c>SUMMON_MAX_ACCESSORY_ITEM_WEAR</c>.</summary>
    public const int AccessorySlots = 3;

    /// <summary>The card flag, bit 0 of the stored bitset (<c>ITEM_FLAG_CARD</c>).</summary>
    public const uint CardFlagMask = 1;

    public readonly record struct Worn(int Slot, int Group);

    /// <summary>An item a summon may wear: in card form, wearable, and allowed at the summon's level.</summary>
    public static bool IsCardForm(ItemFlag flag) => flag != ItemFlag.None && (unchecked((uint)flag) & CardFlagMask) != 0;

    /// <summary>
    /// The slot an item goes to, or null when it cannot. At <see cref="AutoPosition"/> the first free slot of its
    /// range, refused when an equipment or accessory of the same group is already worn there; otherwise the asked
    /// slot below <paramref name="maxSlots"/>, whatever it holds (that item comes off).
    /// </summary>
    public static int? Resolve(int position, ItemGroup group, IReadOnlyCollection<Worn> worn, int maxSlots)
    {
        if (position < 0 || position > AutoPosition)
        {
            return null;
        }

        if (position != AutoPosition)
        {
            return position < maxSlots ? position : null;
        }

        var artifact = group == ItemGroup.Artifact;
        var (start, end) = group switch
        {
            ItemGroup.Accessory => (NonArtifactSlots, Math.Min(maxSlots, AccessorySlots)),
            ItemGroup.Artifact => (AccessorySlots, maxSlots),
            _ => (0, NonArtifactSlots)
        };

        int? free = null;
        for (var slot = start; slot < end; slot++)
        {
            var taken = worn.Where(item => item.Slot == slot).ToArray();
            if (!artifact && taken.Any(item => item.Group == (int)group))
            {
                return null;
            }

            if (taken.Length == 0 && free is null)
            {
                free = slot;
            }
        }

        return free;
    }

    /// <summary>The worn items of one summon.</summary>
    public static bool IsWornBy(ItemEntity item, long summonId) =>
        item.EquippedBySummonId is { } owner && owner == summonId && item.WearInfo != ItemWearType.None;
}
