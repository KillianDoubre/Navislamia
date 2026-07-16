using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Serilog;

namespace Navislamia.Game.Services.Stats;

public class StatCatalog : IStatCatalog
{
    private readonly ILogger _logger = Log.ForContext<StatCatalog>();
    private readonly FrozenDictionary<int, StatBaseStats> _baseStats;
    private readonly FrozenDictionary<int, JobStatBonus> _jobBonuses;

    public StatCatalog(IJobResourceRepository jobs, IStatResourceRepository stats, IJobLevelBonusRepository bonuses)
    {
        _baseStats = BuildBaseStats(jobs, stats);
        _jobBonuses = bonuses.GetAll()
            .ToDictionary(bonus => bonus.Job, BuildBonus)
            .ToFrozenDictionary();

        _logger.Debug("Loaded base stats for {jobs} jobs and level bonuses for {bonuses} jobs",
            _baseStats.Count, _jobBonuses.Count);
    }

    public bool TryGetBaseStats(int job, out StatBaseStats stats)
    {
        return _baseStats.TryGetValue(job, out stats);
    }

    public bool TryGetJobBonus(int job, out JobStatBonus bonus)
    {
        return _jobBonuses.TryGetValue(job, out bonus);
    }

    private FrozenDictionary<int, StatBaseStats> BuildBaseStats(IJobResourceRepository jobs,
        IStatResourceRepository stats)
    {
        var resolved = new Dictionary<int, StatBaseStats>();
        foreach (var job in jobs.GetJobStatIds())
        {
            var row = stats.GetById(job.StatId);
            if (row is null)
            {
                _logger.Warning("Job {job} references stat id {statId} which has no StatResource row", job.Job,
                    job.StatId);
                continue;
            }

            resolved[job.Job] = new StatBaseStats(job.StatId, row.Strength, row.Vitality, row.Dexterity,
                row.Agility, row.Intelligence, row.Wisdom, row.Luck);
        }

        return resolved.ToFrozenDictionary();
    }

    private static JobStatBonus BuildBonus(JobLevelBonusFields fields)
    {
        return new JobStatBonus(new[]
        {
            (StatTarget.Strength, ToFloats(fields.Strength), (float)fields.DefaultStrength),
            (StatTarget.Vitality, ToFloats(fields.Vitality), (float)fields.DefaultVitality),
            (StatTarget.Dexterity, ToFloats(fields.Dexterity), (float)fields.DefaultDexterity),
            (StatTarget.Agility, ToFloats(fields.Agility), (float)fields.DefaultAgility),
            (StatTarget.Intelligence, ToFloats(fields.Intelligence), (float)fields.DefaultIntelligence),
            (StatTarget.Wisdom, ToFloats(fields.Wisdom), (float)fields.DefaultWisdom),
            (StatTarget.Luck, ToFloats(fields.Luck), (float)fields.DefaultLuck)
        });
    }

    private static float[] ToFloats(decimal[] values)
    {
        if (values is null)
        {
            return System.Array.Empty<float>();
        }

        var floats = new float[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            floats[i] = (float)values[i];
        }

        return floats;
    }
}
