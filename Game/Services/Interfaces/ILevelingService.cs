using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

public interface ILevelingService
{
    void ApplyExperience(GameClient client);

    void ApplyJobLevelUp(GameClient client, uint targetHandle);

    /// <summary>The highest level the loaded curve describes, or 0 when leveling is disabled.</summary>
    int MaxLevel { get; }

    /// <summary>The cumulative experience needed to be <paramref name="level"/> (see <see cref="LevelCurve"/>).</summary>
    bool TryGetExperienceFor(int level, out long exp);

    /// <summary>
    /// The JP the next job level costs from <paramref name="currentJobLevel"/>, or 0 when the tier is
    /// capped or leveling is disabled (<see cref="JobLevelCurve.NextCost"/>).
    /// </summary>
    int NextJobLevelCost(int currentJobLevel);
}
