using System;
using System.Collections.Frozen;
using System.Collections.Generic;
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
    private readonly FrozenDictionary<int, IReadOnlyList<ItemStatEffect>> _effects;

    public ItemStatCatalog(IItemResourceRepository repository)
    {
        var resources = repository.GetEffectFields();
        var effects = new Dictionary<int, IReadOnlyList<ItemStatEffect>>(resources.Count);
        foreach (var resource in resources)
        {
            var resolved = BuildEffects(resource);
            if (resolved.Count > 0)
            {
                effects[resource.Id] = resolved;
            }
        }

        _effects = effects.ToFrozenDictionary();
        _logger.Debug("Loaded stat effects for {count} item resources", _effects.Count);
    }

    public IReadOnlyList<ItemStatEffect> GetEffects(int itemResourceId)
    {
        return _effects.TryGetValue(itemResourceId, out var effects) ? effects : Array.Empty<ItemStatEffect>();
    }

    public static IReadOnlyList<ItemStatEffect> BuildEffects(ItemEffectFields resource)
    {
        List<ItemStatEffect> effects = null;
        AppendSlots(resource.BaseTypes, resource.BaseVar1, resource.BaseVar2, ref effects);
        AppendSlots(resource.OptTypes, resource.OptVar1, resource.OptVar2, ref effects);
        return (IReadOnlyList<ItemStatEffect>)effects ?? Array.Empty<ItemStatEffect>();
    }

    private static void AppendSlots(short[] types, decimal[] var1, decimal[] var2,
        ref List<ItemStatEffect> effects)
    {
        if (types is null || var1 is null || var2 is null)
        {
            return;
        }

        for (var slot = 0; slot < SlotCount && slot < types.Length; slot++)
        {
            var type = types[slot];
            if (type == 0 || slot >= var1.Length || slot >= var2.Length)
            {
                continue;
            }

            if (type is IncParameterA or AmpParameterA)
            {
                AppendParameter(type == AmpParameterA, var1[slot], var2[slot], ref effects);
                continue;
            }

            if (type is IncParameterB or AmpParameterB)
            {
                continue;
            }

            var target = ResolvePassive(type);
            var value = (float)var1[slot];
            if (target != StatTarget.None && value != 0f)
            {
                effects ??= new List<ItemStatEffect>();
                effects.Add(new ItemStatEffect(target, value, false));
            }
        }
    }

    private static void AppendParameter(bool isPercent, decimal mask, decimal amount,
        ref List<ItemStatEffect> effects)
    {
        var value = (float)amount;
        if (mask <= 0 || mask > uint.MaxValue || value == 0f)
        {
            return;
        }

        foreach (var target in ParameterBitset.Decode((uint)mask))
        {
            effects ??= new List<ItemStatEffect>();
            effects.Add(new ItemStatEffect(target, value, isPercent));
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
