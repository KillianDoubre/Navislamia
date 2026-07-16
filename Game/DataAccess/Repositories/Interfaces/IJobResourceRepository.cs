using System.Collections.Generic;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

public readonly record struct JobStatFields(int Job, int StatId);

public interface IJobResourceRepository
{
    IReadOnlyList<JobStatFields> GetJobStatIds();
}
