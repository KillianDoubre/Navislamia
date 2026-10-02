namespace Navislamia.Configuration.Options;

/// <summary>
/// The JP a job level costs at each job depth (base, first job, second job, master class), indexed by the current
/// job level, 0 once the tier is capped: <c>DevConsole/job-level-costs.73.json</c>, exported from LevelResource
/// <c>jp_0..jp_3</c> by <c>tools/export_job_level_costs.py</c>. 64-bit, because <c>jp_3</c> overflows an int.
/// </summary>
public class JobLevelCostOptions
{
    public long[][] Depths { get; set; } = System.Array.Empty<long[]>();
}
