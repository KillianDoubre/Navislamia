using System;

namespace Navislamia.Game.Services.Stats;

public static class JobLevelBonusCurve
{
    public const int LevelsPerRange = 20;

    public static void FillLevelParts(int jobLevel, Span<int> parts, int partLevelLength = LevelsPerRange)
    {
        for (var i = 0; i < parts.Length; i++)
        {
            var part = jobLevel < partLevelLength || i == parts.Length - 1 ? jobLevel : partLevelLength;
            jobLevel -= part;
            parts[i] = part;
        }
    }

    public static float SumProduct(ReadOnlySpan<float> perLevel, ReadOnlySpan<int> parts)
    {
        var sum = 0f;
        for (var i = 0; i < parts.Length && i < perLevel.Length; i++)
        {
            sum += perLevel[i] * parts[i];
        }

        return sum;
    }
}
