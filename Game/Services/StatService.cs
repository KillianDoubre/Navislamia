using System;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services;

public class StatService : IStatService
{
    private readonly StatCalculator _calculator;
    private readonly IItemStatCatalog _itemStats;
    private readonly ISkillPassiveCatalog _passives;
    private readonly IStateCatalog _states;
    private readonly Progression.TitleCatalog _titles;
    private readonly IItemMatchCatalog _itemTemplates;

    public StatService(IStatCatalog catalog, IItemStatCatalog itemStats, ISkillPassiveCatalog passives,
        IStateCatalog states, Progression.TitleCatalog titles = null, IItemMatchCatalog itemTemplates = null)
    {
        _itemTemplates = itemTemplates;
        _calculator = new StatCalculator(catalog);
        _itemStats = itemStats;
        _passives = passives;
        _states = states;
        _titles = titles ?? new Progression.TitleCatalog();
    }

    public void RefreshEquipment(ConnectionInfo info, IReadOnlyList<ItemEntity> items)
    {
        var character = new CharacterEntity { Items = items?.ToArray() ?? Array.Empty<ItemEntity>() };
        info.ItemEffects = ResolveItemEffects(character);
        info.EquippedWeapon = ResolveEquippedWeapon(character);
        SeedHands(info, character);
        RefreshPassives(info);
        info.EtherealGear = EtherealWearRules.PlayerCandidates(character.Items, info.BeltItemIds, _itemTemplates);
    }

    public CharacterStatResult Compute(CharacterEntity character)
    {
        return _calculator.Compute(new StatCalculatorInput(
            (int)character.CurrentJob,
            BuildJobHistory(PreviousJobsOf(character), (int)character.CurrentJob, character.Jlv),
            character.Lv,
            ResolveItemEffects(character),
            ResolvePassiveEffects(character, ResolveEquippedWeapon(character)),
            TitleEffects: _titles.GetEffects(character.MainTitleId, character.SubTitleIds)));
    }

    public CharacterStatResult Compute(ConnectionInfo info)
    {
        var result = _calculator.Compute(new StatCalculatorInput(
            info.CharacterJob,
            BuildJobHistory(info.PreviousJobs, info.CharacterJob, info.CharacterJobLevel),
            info.CharacterLevel,
            info.ItemEffects,
            info.PassiveEffects,
            info.BuffEffects,
            _titles.GetEffects(info.MainTitleId, info.SubTitleIds)));

        // applyItemEffect: the worn weapon's range sets the attack range (50 bare-handed, the calculator's default).
        if (info.WeaponAttackRange > 0f && result.Total is not null)
        {
            result.Total.AttackRange = info.WeaponAttackRange;
        }

        // The GM command /speed replaces the move speed outright (load and mount still apply on top).
        if (info.MoveSpeedOverride is { } speed && result.Total is not null)
        {
            result.Total.MoveSpeed = speed;
        }

        return result;
    }

    public CharacterStatResult ComputeForNewCharacter(int race)
    {
        var job = (int)CharacterDefaults.GetStarterJob(race);
        return _calculator.Compute(new StatCalculatorInput(
            job,
            new[] { (job, 0) },
            1,
            Array.Empty<StatEffect>()));
    }

    public void Seed(ConnectionInfo info, CharacterEntity character)
    {
        info.BeltItemIds = character.BeltItemIds?.ToArray() ?? Array.Empty<long>();
        info.EtherealGear = EtherealWearRules.PlayerCandidates(character.Items, info.BeltItemIds, _itemTemplates);
        info.PreviousJobs.Clear();
        info.PreviousJobs.AddRange(PreviousJobsOf(character));
        info.EquippedWeapon = ResolveEquippedWeapon(character);
        info.ItemEffects = ResolveItemEffects(character);
        info.MainTitleId = character.MainTitleId;
        info.SubTitleIds = SubTitlesOf(character);
        info.TitleEffects = _titles.GetEffects(character.MainTitleId, info.SubTitleIds);
        SeedHands(info, character);
        info.PassiveEffects = ResolvePassiveEffects(character, info.EquippedWeapon);
        RefreshBuffs(info);
    }

    private static int[] SubTitlesOf(CharacterEntity character)
    {
        var subs = new int[Progression.TitleCatalog.SubTitleCount];
        if (character.SubTitleIds is { } stored)
        {
            Array.Copy(stored, subs, Math.Min(stored.Length, subs.Length));
        }

        return subs;
    }

    public void RefreshPassives(ConnectionInfo info)
    {
        info.PassiveEffects = ResolveEffects(info.LearnedSkills, info.EquippedWeapon, info.WearsShield);
    }

    public void RefreshBuffs(ConnectionInfo info)
    {
        ActiveBuff[] active;
        lock (info.BuffLock)
        {
            if (info.ActiveBuffs.Count == 0)
            {
                info.BuffEffects = Array.Empty<StatEffect>();
                return;
            }

            // Snapshot and get out: the expiry tick wants this lock, and resolving is pure work.
            active = info.ActiveBuffs.ToArray();
        }

        List<StatEffect> effects = null;
        foreach (var buff in active)
        {
            Append(_states.Resolve(buff.StateId, buff.StateLevel), ref effects);
        }

        info.BuffEffects = (IReadOnlyList<StatEffect>)effects ?? Array.Empty<StatEffect>();
    }

    /// <summary>The main-hand weapon's own effects and what the shield slot holds if it is not a shield.</summary>
    private void SeedHands(ConnectionInfo info, CharacterEntity character)
    {
        info.RightWeaponEffects = Array.Empty<StatEffect>();
        info.LeftHand = null;
        info.WearsShield = WearsShield(character);
        info.WeaponAttackRange = 0f;
        if (character.Items is null)
        {
            return;
        }

        foreach (var item in character.Items)
        {
            if (item.EquippedBySummonId is not null || EtherealWearRules.Exhausted(item, _itemTemplates))
            {
                continue;
            }

            if (item.WearInfo == ItemWearType.Weapon)
            {
                info.RightWeaponEffects = _itemStats.GetEffects((int)item.ItemResourceId).Concat(ItemStatCatalog.RandomEffects(item)).ToArray();
                info.WeaponAttackRange = _itemStats.GetAttackRange((int)item.ItemResourceId);
            }
            else if (item.WearInfo == ItemWearType.Shield)
            {
                var weapon = _itemStats.GetWeaponType((int)item.ItemResourceId);
                if (weapon is not null || Combat.AttackMechanics.IsRanged(info.EquippedWeapon))
                {
                    info.LeftHand = new Combat.LeftHandItem((uint)item.Id, (int)item.ItemResourceId, weapon,
                        item.Amount, _itemStats.GetEffects((int)item.ItemResourceId).Concat(ItemStatCatalog.RandomEffects(item)).ToArray());
                }
            }
        }
    }

    private bool WearsShield(CharacterEntity character) => character.Items is { } items && items.Any(item =>
        !EtherealWearRules.Exhausted(item, _itemTemplates) && ItemWearRules.IsWornByPlayerAt(item, ItemWearType.Shield)
        && _itemStats.IsShield((int)item.ItemResourceId));

    private ItemType? ResolveEquippedWeapon(CharacterEntity character)
    {
        if (character.Items is null)
        {
            return null;
        }

        foreach (var item in character.Items)
        {
            if (!EtherealWearRules.Exhausted(item, _itemTemplates) && ItemWearRules.IsWornByPlayerAt(item, ItemWearType.Weapon))
            {
                return _itemStats.GetWeaponType((int)item.ItemResourceId);
            }
        }

        return null;
    }

    private IReadOnlyList<StatEffect> ResolvePassiveEffects(CharacterEntity character, ItemType? equippedWeapon)
    {
        if (character.Skills is null)
        {
            return Array.Empty<StatEffect>();
        }

        List<StatEffect> effects = null;
        foreach (var skill in character.Skills)
        {
            Append(_passives.Resolve(skill.SkillId, skill.Level, equippedWeapon, WearsShield(character)), ref effects);
        }

        return (IReadOnlyList<StatEffect>)effects ?? Array.Empty<StatEffect>();
    }

    private IReadOnlyList<StatEffect> ResolveEffects(IReadOnlyDictionary<int, byte> learnedSkills,
        ItemType? equippedWeapon, bool wearsShield)
    {
        List<StatEffect> effects = null;
        foreach (var (skillId, level) in learnedSkills)
        {
            Append(_passives.Resolve(skillId, level, equippedWeapon, wearsShield), ref effects);
        }

        return (IReadOnlyList<StatEffect>)effects ?? Array.Empty<StatEffect>();
    }

    private static void Append(IReadOnlyList<StatEffect> resolved, ref List<StatEffect> effects)
    {
        if (resolved.Count == 0)
        {
            return;
        }

        effects ??= new List<StatEffect>();
        effects.AddRange(resolved);
    }

    private static List<(int Job, int JobLevel)> PreviousJobsOf(CharacterEntity character)
    {
        var previous = new List<(int Job, int JobLevel)>();
        if (character.PreviousJobs is null || character.JobLvs is null)
        {
            return previous;
        }

        for (var i = 0; i < character.PreviousJobs.Length && i < character.JobLvs.Length; i++)
        {
            var job = (int)character.PreviousJobs[i];
            if (job == 0 || character.JobLvs[i] == 0)
            {
                break;
            }

            previous.Add((job, character.JobLvs[i]));
        }

        return previous;
    }

    private static IReadOnlyList<(int Job, int JobLevel)> BuildJobHistory(
        IReadOnlyList<(int Job, int JobLevel)> previous, int currentJob, int currentJobLevel)
    {
        var history = new List<(int Job, int JobLevel)>(previous.Count + 1);
        history.AddRange(previous);
        history.Add((currentJob, currentJobLevel));
        return history;
    }

    private IReadOnlyList<StatEffect> ResolveItemEffects(CharacterEntity character)
    {
        if (character.Items is null)
        {
            return Array.Empty<StatEffect>();
        }

        List<StatEffect> effects = null;
        foreach (var item in character.Items)
        {
            // A spare-set item (24..27) is worn but gives nothing until the swap brings it to its main slot
            // (StructPlayer::TranslateWearPosition: "spare items do not apply their performance").
            if (EtherealWearRules.Exhausted(item, _itemTemplates) || !ItemWearRules.IsWornByPlayer(item) || item.WearInfo >= ItemWearType.SpareWeapon
                && item.WearInfo <= ItemWearType.SpareDecoShield)
            {
                continue;
            }

            var itemEffects = _itemStats.GetEffects((int)item.ItemResourceId).Concat(ItemStatCatalog.RandomEffects(item)).ToArray();
            if (itemEffects.Length == 0)
            {
                continue;
            }

            effects ??= new List<StatEffect>();
            effects.AddRange(itemEffects);
        }

        return (IReadOnlyList<StatEffect>)effects ?? Array.Empty<StatEffect>();
    }
}
