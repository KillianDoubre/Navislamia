using System;
using System.Collections.Generic;

namespace Navislamia.Game.Services;

public static class DropRoll
{
    public static IReadOnlyList<DroppedItem> Roll(IReadOnlyList<DropEntry> entries, Random random,
        double chanceMultiplier = 1)
    {
        if (entries.Count == 0)
        {
            return Array.Empty<DroppedItem>();
        }

        List<DroppedItem> dropped = null;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var chance = Math.Min(1, entry.Chance * chanceMultiplier);
            if (random.NextDouble() >= chance)
            {
                continue;
            }

            var count = entry.MinCount >= entry.MaxCount
                ? entry.MinCount
                : random.Next(entry.MinCount, entry.MaxCount + 1);

            (dropped ??= new List<DroppedItem>()).Add(new DroppedItem(entry.ItemId, count));
        }

        return (IReadOnlyList<DroppedItem>)dropped ?? Array.Empty<DroppedItem>();
    }
}
