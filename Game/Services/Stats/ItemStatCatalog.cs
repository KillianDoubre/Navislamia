using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;

namespace Navislamia.Game.Services.Stats;

public class ItemStatCatalog : IItemStatCatalog
{
    private const int SlotCount = 4;
    private const short IncParameterA = (short)ItemEffectPassive.IncParameterA;
    private const short IncParameterB = (short)ItemEffectPassive.IncParameterB;
    private const short AmpParameterA = (short)ItemEffectPassive.AmpParameterA;
    private const short AmpParameterB = (short)ItemEffectPassive.AmpParameterB;

    private readonly ILogger _logger = Log.ForContext<ItemStatCatalog>();
    private readonly FrozenDictionary<int, IReadOnlyList<StatEffect>> _effects;
    private readonly FrozenDictionary<int, ItemType> _weaponTypes;
    private readonly FrozenSet<int> _shields;
    private readonly FrozenDictionary<int, float> _attackRanges = FrozenDictionary<int, float>.Empty;

    public ItemStatCatalog(IItemResourceRepository repository)
    {
        var resources = repository.GetEffectFields();
        var effects = new Dictionary<int, IReadOnlyList<StatEffect>>(resources.Count);
        var weaponTypes = new Dictionary<int, ItemType>();
        var shields = new HashSet<int>();
        foreach (var resource in resources)
        {
            var resolved = BuildEffects(resource);
            if (resolved.Count > 0)
            {
                effects[resource.Id] = resolved;
            }

            if (SkillWeaponGate.IsWeapon(resource.ItemType))
            {
                weaponTypes[resource.Id] = resource.ItemType;
            }

            if (resource.ItemType == ItemType.Shield)
            {
                shields.Add(resource.Id);
            }
        }

        _effects = effects.ToFrozenDictionary();
        _weaponTypes = weaponTypes.ToFrozenDictionary();
        _shields = shields.ToFrozenSet();
        var ranges = new Dictionary<int, float>();
        foreach (var (id, range) in repository.GetWeaponRanges() ?? new Dictionary<int, decimal>())
        {
            if (_weaponTypes.ContainsKey(id))
            {
                ranges[id] = (float)(range * 100m);
            }
        }

        _attackRanges = ranges.ToFrozenDictionary();
        _logger.Debug("Loaded stat effects for {count} item resources and {weapons} weapons", _effects.Count,
            _weaponTypes.Count);
    }

    public IReadOnlyList<StatEffect> GetEffects(int itemResourceId)
    {
        return _effects.TryGetValue(itemResourceId, out var effects) ? effects : Array.Empty<StatEffect>();
    }

    public float GetAttackRange(int itemResourceId) =>
        _attackRanges.TryGetValue(itemResourceId, out var range) ? range : 0f;

    public bool IsShield(int itemResourceId) => _shields.Contains(itemResourceId);

    public ItemType? GetWeaponType(int itemResourceId)
    {
        return _weaponTypes.TryGetValue(itemResourceId, out var itemType) ? itemType : null;
    }

    public static IReadOnlyList<StatEffect> BuildEffects(ItemEffectFields resource, int slotCount = SlotCount)
    {
        List<StatEffect> effects = null;
        AppendSlots(resource.BaseTypes, resource.BaseVar1, resource.BaseVar2, ref effects, slotCount);
        AppendSlots(resource.OptTypes, resource.OptVar1, resource.OptVar2, ref effects, slotCount);
        return (IReadOnlyList<StatEffect>)effects ?? Array.Empty<StatEffect>();
    }

    public static IReadOnlyList<StatEffect> RandomEffects(Navislamia.Game.DataAccess.Entities.Telecaster.ItemEntity item) =>
        BuildEffects(new ItemEffectFields((int)item.ItemResourceId, default,
            item.RandomOptionTypes?.Select(t => checked((short)t)).ToArray(),
            item.RandomOptionVars?.Select(v => (decimal)v).ToArray(), item.RandomOptionValues,
            null, null, null), 2);

    private static void AppendSlots(short[] types, decimal[] var1, decimal[] var2,
        ref List<StatEffect> effects, int slotCount)
    {
        if (types is null || var1 is null || var2 is null)
        {
            return;
        }

        for (var slot = 0; slot < slotCount && slot < types.Length && slot < var1.Length && slot < var2.Length; slot++)
        {
            var type = types[slot];
            if (type == 0 || slot >= var1.Length || slot >= var2.Length)
            {
                continue;
            }

            if (type is IncParameterA or AmpParameterA)
            {
                AppendParameter(false, type == AmpParameterA, var1[slot], var2[slot], ref effects);
                continue;
            }

            if (type is IncParameterB or AmpParameterB)
            {
                AppendParameter(true, type == AmpParameterB, var1[slot], var2[slot], ref effects);
                continue;
            }

            var target = ResolvePassive(type);
            var value = (float)var1[slot];
            if (target != StatTarget.None && value != 0f)
            {
                effects ??= new List<StatEffect>();
                effects.Add(new StatEffect(target, value, false));
            }
        }
    }

    private static void AppendParameter(bool resistance, bool isPercent, decimal mask, decimal amount,
        ref List<StatEffect> effects)
    {
        var value = (float)amount;
        if (mask <= 0 || mask > uint.MaxValue || value == 0f)
        {
            return;
        }

        foreach (var target in (resistance ? ParameterBitset.DecodeResistance((uint)mask) : ParameterBitset.Decode((uint)mask)))
        {
            effects ??= new List<StatEffect>();
            effects.Add(new StatEffect(target, value, isPercent));
        }
    }

    private static StatTarget ResolvePassive(short type) => (ItemEffectPassive)type switch
    {
        ItemEffectPassive.AttackPoint => StatTarget.AttackPointRight,
        ItemEffectPassive.MagicPoint => StatTarget.MagicPoint,
        ItemEffectPassive.Accuracy => StatTarget.AccuracyRight,
        ItemEffectPassive.AttackSpeed => StatTarget.AttackSpeed,
        ItemEffectPassive.Defence => StatTarget.Defence,
        ItemEffectPassive.MagicDefence => StatTarget.MagicDefence,
        ItemEffectPassive.Avoid => StatTarget.Avoid,
        ItemEffectPassive.MoveSpeed => StatTarget.MoveSpeed,
        ItemEffectPassive.BlockChange => StatTarget.BlockChance,
        ItemEffectPassive.CarryWeight => StatTarget.MaxWeight,
        ItemEffectPassive.BlockDefence => StatTarget.BlockDefence,
        ItemEffectPassive.CastingSpeed => StatTarget.CastingSpeed,
        ItemEffectPassive.MagicAccuracy => StatTarget.MagicAccuracy,
        ItemEffectPassive.MagicAvoid => StatTarget.MagicAvoid,
        ItemEffectPassive.CooltimeSpeed => StatTarget.CoolTimeSpeed,
        ItemEffectPassive.MaxChaos => StatTarget.MaxChaos,
        ItemEffectPassive.MaxHp => StatTarget.MaxHp,
        ItemEffectPassive.MaxMp => StatTarget.MaxMp,
        ItemEffectPassive.MpRegenPoint => StatTarget.MpRegenPoint,
        _ => StatTarget.None
    };
}
