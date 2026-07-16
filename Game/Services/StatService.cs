using System;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services;

public class StatService : IStatService
{
    private readonly StatCalculator _calculator;
    private readonly IItemStatCatalog _itemStats;

    public StatService(IStatCatalog catalog, IItemStatCatalog itemStats)
    {
        _calculator = new StatCalculator(catalog);
        _itemStats = itemStats;
    }

    public CharacterStatResult Compute(CharacterEntity character)
    {
        return _calculator.Compute(new StatCalculatorInput(
            (int)character.CurrentJob,
            BuildJobHistory(PreviousJobsOf(character), (int)character.CurrentJob, character.Jlv),
            character.Lv,
            ResolveItemEffects(character)));
    }

    public CharacterStatResult Compute(ConnectionInfo info)
    {
        return _calculator.Compute(new StatCalculatorInput(
            info.CharacterJob,
            BuildJobHistory(info.PreviousJobs, info.CharacterJob, info.CharacterJobLevel),
            info.CharacterLevel,
            info.ItemEffects));
    }

    public CharacterStatResult ComputeForNewCharacter(int race)
    {
        var job = (int)CharacterDefaults.GetStarterJob(race);
        return _calculator.Compute(new StatCalculatorInput(
            job,
            new[] { (job, 0) },
            1,
            Array.Empty<ItemStatEffect>()));
    }

    public void Seed(ConnectionInfo info, CharacterEntity character)
    {
        info.PreviousJobs.Clear();
        info.PreviousJobs.AddRange(PreviousJobsOf(character));
        info.ItemEffects = ResolveItemEffects(character);
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

    private IReadOnlyList<ItemStatEffect> ResolveItemEffects(CharacterEntity character)
    {
        if (character.Items is null)
        {
            return Array.Empty<ItemStatEffect>();
        }

        List<ItemStatEffect> effects = null;
        foreach (var item in character.Items)
        {
            if (item.WearInfo == ItemWearType.None)
            {
                continue;
            }

            var itemEffects = _itemStats.GetEffects((int)item.ItemResourceId);
            if (itemEffects.Count == 0)
            {
                continue;
            }

            effects ??= new List<ItemStatEffect>();
            effects.AddRange(itemEffects);
        }

        return (IReadOnlyList<ItemStatEffect>)effects ?? Array.Empty<ItemStatEffect>();
    }
}
