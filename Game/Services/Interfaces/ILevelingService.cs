using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

public interface ILevelingService
{
    void ApplyExperience(GameClient client);
    bool SetLevel(GameClient client, int level) => false;

    /// <summary>
    /// Gives back <paramref name="ratio"/> of the experience the last death took and forgets the rest
    /// (<c>Resurrect(…, exp)</c>): the experience update, and a level back up when the total crosses it again.
    /// Returns what was given back.
    /// </summary>
    long RestoreDeathExperience(GameClient client, decimal ratio) => 0;

    void ApplyJobLevelUp(GameClient client, uint targetHandle);

    /// <summary>
    /// A monster has just killed the character: takes the official death penalty off its experience and,
    /// when the total falls below its level's threshold, takes the level down with it (the official
    /// <c>onExpChange</c> recomputes the level from the total). Returns the experience lost.
    /// </summary>
    long ApplyDeathPenalty(GameClient client);

    /// <summary>The highest level the loaded curve describes, or 0 when leveling is disabled.</summary>
    int MaxLevel { get; }

    /// <summary>The cumulative experience needed to be <paramref name="level"/> (see <see cref="LevelCurve"/>).</summary>
    bool TryGetExperienceFor(int level, out long exp);

    /// <summary>
    /// The JP the next job level from <paramref name="currentJobLevel"/> really charges, the
    /// <c>JobLevelJpCost</c> rate included. False when the tier is capped or leveling is disabled
    /// (<see cref="JobLevelCurve.NextCost"/>) — never inferred from the cost, which a rate of 0 makes 0.
    /// </summary>
    bool TryGetNextJobLevelCost(int currentJobLevel, out long cost);

    /// <summary>
    /// The same at a job depth (0 base, 1 first job, 2 second job, 3 master class): the official
    /// <c>GetNeedJpForJobLevelUp(level, depth)</c> reads <c>jp_&lt;depth&gt;</c>. The depth of a character is the
    /// number of jobs it has left behind (<c>ConnectionInfo.PreviousJobs</c>).
    /// </summary>
    bool TryGetNextJobLevelCost(int jobDepth, int currentJobLevel, out long cost) =>
        jobDepth == 0 ? TryGetNextJobLevelCost(currentJobLevel, out cost) : NoCost(out cost);

    private static bool NoCost(out long cost)
    {
        cost = 0;
        return false;
    }
}
