using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;

namespace Navislamia.Game.Services;

/// <summary>
/// What using one resurrection item does: cast <see cref="SkillId"/> at <see cref="SkillLevel"/>, a skill of
/// effect <see cref="Effect"/> whose variables are <see cref="Vars"/> (<c>var1..var20</c>).
/// </summary>
public readonly record struct ResurrectionItem(int ItemResourceId, int SkillId, int SkillLevel,
    SkillEffectType Effect, decimal[] Vars);

/// <summary>
/// A resurrection potion of <c>StructPlayer::ResurrectByPotion</c>: effect 114, <c>var1</c> the share of the max HP,
/// <c>var2</c> the share of the death's experience given back.
/// </summary>
public readonly record struct ResurrectionPotion(int ItemResourceId, decimal HpRatio, decimal ExpRatio);

public interface IResurrectionItemCatalog
{
    int Count { get; }

    bool TryGet(int itemResourceId, out ResurrectionItem item);

    /// <summary>The potions a dead character may use on itself, in the order the reference looks for them.</summary>
    IReadOnlyList<ResurrectionPotion> Potions => Array.Empty<ResurrectionPotion>();
}

/// <summary>
/// The items that resurrect a character: those whose instant effect is <c>ItemEffectInstant.Skill</c> (5)
/// pointing at a resurrection skill that targets a character. Frozen at startup like every catalog.
/// </summary>
/// <remarks>
/// The Resurrection Scroll (603002) is <c>opt_type_0 = 5</c>, <c>opt_var1_0 = 6001</c>,
/// <c>opt_var2_0 = 1</c>: skill 6001 (<c>EF_RESURRECTION</c>, <c>var1 = 0.1</c>) at level 1. The link is
/// the effect slot, not <c>ItemResource.skill_id</c> (null on every consumable) nor
/// <c>ItemEffectInstant.Resurrection</c> (4, on no item) — the two places the first analysis looked
/// before concluding, wrongly, that the data was missing. The Creature Resurrection Scrolls point at
/// 6013, which targets summons only (<c>tf_avatar = 0</c>) and is therefore not listed.
/// </remarks>
public class ResurrectionItemCatalog : IResurrectionItemCatalog
{
    private readonly ILogger _logger = Log.ForContext<ResurrectionItemCatalog>();
    private readonly FrozenDictionary<int, ResurrectionItem> _items;
    private readonly IReadOnlyList<ResurrectionPotion> _potions = Array.Empty<ResurrectionPotion>();

    /// <summary>
    /// The four codes <c>ResurrectByPotion</c> looks for, in its order (2012-11 <c>0x1400e5650</c>).
    /// </summary>
    public static readonly int[] PotionCodes = { 2010454, 2902042, 910005, 910004 };

    /// <summary>The instant effect a resurrection potion carries.</summary>
    public const short PotionEffect = 114;

    public ResurrectionItemCatalog(IItemResourceRepository items, ISkillResourceRepository skills)
    {
        try
        {
            _items = Build(items.GetInstantSkillItems(), skills.GetResurrectionSkills());
            _potions = BuildPotions(items.GetItemsWithInstantEffect(PotionEffect));
            _logger.Information("Loaded {count} resurrection items and {potions} resurrection potions", _items.Count,
                _potions.Count);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not load the resurrection items; item resurrection is disabled");
            _items = FrozenDictionary<int, ResurrectionItem>.Empty;
        }
    }

    public int Count => _items.Count;

    public IReadOnlyList<ResurrectionPotion> Potions => _potions;

    /// <summary>The potions among <see cref="PotionCodes"/> whose effect 114 resolves, in that order.</summary>
    public static IReadOnlyList<ResurrectionPotion> BuildPotions(IEnumerable<ItemEffectFields> items)
    {
        var byId = (items ?? Array.Empty<ItemEffectFields>()).ToDictionary(item => item.Id);
        var potions = new List<ResurrectionPotion>();
        foreach (var code in PotionCodes)
        {
            if (!byId.TryGetValue(code, out var item))
            {
                continue;
            }

            if (TryPotionSlot(item.BaseTypes, item.BaseVar1, item.BaseVar2, out var hp, out var exp)
                || TryPotionSlot(item.OptTypes, item.OptVar1, item.OptVar2, out hp, out exp))
            {
                potions.Add(new ResurrectionPotion(code, hp, exp));
            }
        }

        return potions;
    }

    private static bool TryPotionSlot(short[] types, decimal[] var1, decimal[] var2, out decimal hp, out decimal exp)
    {
        hp = 0m;
        exp = 0m;
        if (types is null)
        {
            return false;
        }

        for (var slot = 0; slot < types.Length; slot++)
        {
            if (types[slot] != PotionEffect)
            {
                continue;
            }

            hp = var1 is not null && slot < var1.Length ? var1[slot] : 0m;
            exp = var2 is not null && slot < var2.Length ? var2[slot] : 0m;
            return true;
        }

        return false;
    }

    public bool TryGet(int itemResourceId, out ResurrectionItem item) => _items.TryGetValue(itemResourceId, out item);

    /// <summary>
    /// Pairs each item's skill effect with a known resurrection skill. The slots are visited in
    /// NGemity's order (<c>Player::UseItem</c>: <c>base_type[i]</c> then <c>opt_type[i]</c>, for i = 0..3),
    /// and the first slot that resolves wins.
    /// </summary>
    public static FrozenDictionary<int, ResurrectionItem> Build(IEnumerable<ItemEffectFields> items,
        IEnumerable<ResurrectionSkillRow> skills)
    {
        var bySkill = new Dictionary<int, ResurrectionSkillRow>();
        foreach (var skill in skills ?? Array.Empty<ResurrectionSkillRow>())
        {
            bySkill[skill.SkillId] = skill;
        }

        var result = new Dictionary<int, ResurrectionItem>();
        foreach (var item in items ?? Array.Empty<ItemEffectFields>())
        {
            for (var slot = 0; slot < 4; slot++)
            {
                if (TryResolve(item.Id, item.BaseTypes, item.BaseVar1, item.BaseVar2, slot, bySkill, out var found)
                    || TryResolve(item.Id, item.OptTypes, item.OptVar1, item.OptVar2, slot, bySkill, out found))
                {
                    result[item.Id] = found;
                    break;
                }
            }
        }

        return result.ToFrozenDictionary();
    }

    private static bool TryResolve(int itemId, short[] types, decimal[] var1, decimal[] var2, int slot,
        IReadOnlyDictionary<int, ResurrectionSkillRow> bySkill, out ResurrectionItem item)
    {
        item = default;
        if (types is null || slot >= types.Length || types[slot] != (short)ItemEffectInstant.Skill
            || var1 is null || slot >= var1.Length)
        {
            return false;
        }

        if (!bySkill.TryGetValue((int)var1[slot], out var skill))
        {
            return false;
        }

        var level = var2 is not null && slot < var2.Length ? (int)var2[slot] : 0;
        item = new ResurrectionItem(itemId, skill.SkillId, level, (SkillEffectType)skill.EffectType,
            skill.Vars ?? Array.Empty<decimal>());
        return true;
    }
}
