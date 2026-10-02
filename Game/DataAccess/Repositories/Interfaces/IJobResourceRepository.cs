using System.Collections.Generic;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

public readonly record struct JobStatFields(int Job, int StatId);

/// <summary>The class and depth whitelist bit a job carries in JobResource.</summary>
public readonly record struct JobWearFields(int Job, int JobClass, short JobDepth);

public interface IJobResourceRepository
{
    IReadOnlyList<JobStatFields> GetJobStatIds();
    IReadOnlyList<JobWearFields> GetWearFields();
}
